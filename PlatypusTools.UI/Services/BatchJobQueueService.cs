using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// IDEA-007: Unified batch job queue for video conversions, image upscales, file copies, and more.
    /// Supports priority ordering, concurrent execution, pause/resume/cancel.
    /// </summary>
    public class BatchJobQueueService
    {
        private static readonly Lazy<BatchJobQueueService> _instance = new(() => new BatchJobQueueService());
        public static BatchJobQueueService Instance => _instance.Value;

        private readonly SemaphoreSlim _queueLock = new(1, 1);
        private readonly List<BatchJob> _pendingJobs = new();
        private readonly List<Task> _runningTasks = new();
        private int _maxConcurrent = 2;
        private bool _isPaused;
        private bool _isProcessing;

        /// <summary>All jobs (active + completed + pending) for UI binding.</summary>
        public ObservableCollection<BatchJob> AllJobs { get; } = new();

        public int MaxConcurrentJobs
        {
            get => _maxConcurrent;
            set => _maxConcurrent = Math.Max(1, Math.Min(8, value));
        }

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                PausedChanged?.Invoke(this, value);
                if (!value) _ = ProcessQueueAsync();
            }
        }

        public event EventHandler<BatchJob>? JobAdded;
        public event EventHandler<BatchJob>? JobStarted;
        public event EventHandler<BatchJob>? JobCompleted;
        public event EventHandler<BatchJob>? JobFailed;
        public event EventHandler<BatchJob>? JobCancelled;
        public event EventHandler<bool>? PausedChanged;
        public event EventHandler? QueueChanged;

        public int PendingCount => AllJobs.Count(j => j.Status == BatchJobStatus.Queued);
        public int RunningCount => AllJobs.Count(j => j.Status == BatchJobStatus.Running);
        public int CompletedCount => AllJobs.Count(j => j.Status == BatchJobStatus.Completed);
        public int FailedCount => AllJobs.Count(j => j.Status == BatchJobStatus.Failed);

        /// <summary>
        /// Enqueue a new job. Jobs are processed by priority (lower number = higher priority), then FIFO.
        /// </summary>
        public void Enqueue(BatchJob job)
        {
            job.Status = BatchJobStatus.Queued;
            job.QueuedAt = DateTime.Now;

            Application.Current?.Dispatcher?.Invoke(() => AllJobs.Add(job));

            lock (_pendingJobs)
            {
                _pendingJobs.Add(job);
                _pendingJobs.Sort((a, b) =>
                {
                    var p = a.Priority.CompareTo(b.Priority);
                    return p != 0 ? p : a.QueuedAt.CompareTo(b.QueuedAt);
                });
            }

            JobAdded?.Invoke(this, job);
            QueueChanged?.Invoke(this, EventArgs.Empty);

            _ = ProcessQueueAsync();
        }

        /// <summary>
        /// Enqueue a simple action as a batch job.
        /// </summary>
        public BatchJob Enqueue(string name, BatchJobType type, Func<BatchJob, CancellationToken, Task> work, int priority = 5)
        {
            var job = new BatchJob
            {
                Name = name,
                JobType = type,
                Priority = priority,
                WorkAction = work
            };
            Enqueue(job);
            return job;
        }

        /// <summary>Cancel a specific job.</summary>
        public void Cancel(BatchJob job)
        {
            if (job.Status == BatchJobStatus.Queued)
            {
                job.Status = BatchJobStatus.Cancelled;
                lock (_pendingJobs) _pendingJobs.Remove(job);
                JobCancelled?.Invoke(this, job);
                QueueChanged?.Invoke(this, EventArgs.Empty);
            }
            else if (job.Status == BatchJobStatus.Running)
            {
                job.CancellationSource?.Cancel();
            }
        }

        /// <summary>Cancel all pending and running jobs.</summary>
        public void CancelAll()
        {
            lock (_pendingJobs)
            {
                foreach (var job in _pendingJobs.ToList())
                {
                    job.Status = BatchJobStatus.Cancelled;
                    JobCancelled?.Invoke(this, job);
                }
                _pendingJobs.Clear();
            }

            foreach (var job in AllJobs.Where(j => j.Status == BatchJobStatus.Running).ToList())
            {
                job.CancellationSource?.Cancel();
            }
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Remove completed/failed/cancelled jobs from the list.</summary>
        public void ClearFinished()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var toRemove = AllJobs.Where(j =>
                    j.Status == BatchJobStatus.Completed ||
                    j.Status == BatchJobStatus.Failed ||
                    j.Status == BatchJobStatus.Cancelled).ToList();
                foreach (var job in toRemove)
                    AllJobs.Remove(job);
            });
            QueueChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Retry a failed job.</summary>
        public void Retry(BatchJob job)
        {
            if (job.Status != BatchJobStatus.Failed && job.Status != BatchJobStatus.Cancelled) return;
            job.Status = BatchJobStatus.Queued;
            job.Progress = 0;
            job.StatusMessage = "Re-queued";
            job.ErrorMessage = null;
            job.QueuedAt = DateTime.Now;
            job.CancellationSource = null;

            lock (_pendingJobs)
            {
                _pendingJobs.Add(job);
                _pendingJobs.Sort((a, b) =>
                {
                    var p = a.Priority.CompareTo(b.Priority);
                    return p != 0 ? p : a.QueuedAt.CompareTo(b.QueuedAt);
                });
            }
            QueueChanged?.Invoke(this, EventArgs.Empty);
            _ = ProcessQueueAsync();
        }

        private async Task ProcessQueueAsync()
        {
            if (_isPaused || _isProcessing) return;

            await _queueLock.WaitAsync();
            try
            {
                _isProcessing = true;

                while (!_isPaused)
                {
                    // Clean up completed tasks
                    _runningTasks.RemoveAll(t => t.IsCompleted);

                    if (_runningTasks.Count >= _maxConcurrent) break;

                    BatchJob? nextJob;
                    lock (_pendingJobs)
                    {
                        nextJob = _pendingJobs.FirstOrDefault();
                        if (nextJob != null) _pendingJobs.RemoveAt(0);
                    }

                    if (nextJob == null) break;

                    var task = ExecuteJobAsync(nextJob);
                    _runningTasks.Add(task);
                }
            }
            finally
            {
                _isProcessing = false;
                _queueLock.Release();
            }
        }

        private async Task ExecuteJobAsync(BatchJob job)
        {
            job.CancellationSource = new CancellationTokenSource();
            job.Status = BatchJobStatus.Running;
            job.StartedAt = DateTime.Now;
            job.StatusMessage = "Running...";
            JobStarted?.Invoke(this, job);
            QueueChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                if (job.WorkAction != null)
                {
                    await job.WorkAction(job, job.CancellationSource.Token);
                }

                if (job.CancellationSource.IsCancellationRequested)
                {
                    job.Status = BatchJobStatus.Cancelled;
                    job.StatusMessage = "Cancelled";
                    JobCancelled?.Invoke(this, job);
                }
                else
                {
                    job.Status = BatchJobStatus.Completed;
                    job.Progress = 100;
                    job.StatusMessage = "Completed";
                    job.CompletedAt = DateTime.Now;
                    JobCompleted?.Invoke(this, job);
                }
            }
            catch (OperationCanceledException)
            {
                job.Status = BatchJobStatus.Cancelled;
                job.StatusMessage = "Cancelled";
                JobCancelled?.Invoke(this, job);
            }
            catch (Exception ex)
            {
                job.Status = BatchJobStatus.Failed;
                job.StatusMessage = "Failed";
                job.ErrorMessage = ex.Message;
                JobFailed?.Invoke(this, job);
            }
            finally
            {
                QueueChanged?.Invoke(this, EventArgs.Empty);

                // Process next job
                _ = ProcessQueueAsync();
            }
        }
    }

    public enum BatchJobStatus
    {
        Queued,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public enum BatchJobType
    {
        VideoConvert,
        ImageUpscale,
        ImageConvert,
        ImageResize,
        FileCopy,
        FileMove,
        Backup,
        SecureWipe,
        Generic
    }

    /// <summary>
    /// Represents a single job in the batch queue.
    /// </summary>
    public class BatchJob : System.ComponentModel.INotifyPropertyChanged
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = string.Empty;
        public BatchJobType JobType { get; set; } = BatchJobType.Generic;
        public int Priority { get; set; } = 5; // 1 (highest) to 10 (lowest)

        private BatchJobStatus _status;
        public BatchJobStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusIcon)); }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        private string _statusMessage = "Queued";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(nameof(StatusMessage)); }
        }

        public string? ErrorMessage { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        /// <summary>The actual work to perform. Receives the job and a cancellation token.</summary>
        public Func<BatchJob, CancellationToken, Task>? WorkAction { get; set; }

        public CancellationTokenSource? CancellationSource { get; set; }

        public string StatusIcon => Status switch
        {
            BatchJobStatus.Queued => "⏳",
            BatchJobStatus.Running => "▶️",
            BatchJobStatus.Completed => "✅",
            BatchJobStatus.Failed => "❌",
            BatchJobStatus.Cancelled => "🚫",
            _ => "❓"
        };

        public string TypeIcon => JobType switch
        {
            BatchJobType.VideoConvert => "🎬",
            BatchJobType.ImageUpscale => "🔍",
            BatchJobType.ImageConvert => "🖼️",
            BatchJobType.ImageResize => "📐",
            BatchJobType.FileCopy => "📋",
            BatchJobType.FileMove => "📦",
            BatchJobType.Backup => "💾",
            BatchJobType.SecureWipe => "🔒",
            BatchJobType.Generic => "⚙️",
            _ => "⚙️"
        };

        public string Duration
        {
            get
            {
                if (StartedAt == null) return "--";
                var end = CompletedAt ?? DateTime.Now;
                var span = end - StartedAt.Value;
                return span.TotalHours >= 1
                    ? $"{span:hh\\:mm\\:ss}"
                    : $"{span:mm\\:ss}";
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) =>
            Application.Current?.Dispatcher?.Invoke(() =>
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(name)));
    }
}
