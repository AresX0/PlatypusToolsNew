using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Services.Video
{
    #region Models

    /// <summary>
    /// Status of an export job.
    /// </summary>
    public enum ExportJobStatus
    {
        Queued,
        Preparing,
        Encoding,
        Completed,
        Failed,
        Cancelled,
        Paused
    }

    /// <summary>
    /// Priority level for export jobs.
    /// </summary>
    public enum ExportPriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Critical = 3
    }

    /// <summary>
    /// Represents a single export job in the queue.
    /// </summary>
    public class ExportJob : BindableModel
    {
        private string _id = Guid.NewGuid().ToString();
        private string _name = string.Empty;
        private string _projectPath = string.Empty;
        private string _outputPath = string.Empty;
        private ExportJobStatus _status = ExportJobStatus.Queued;
        private double _progress;
        private string _statusMessage = "Queued";
        private DateTime _createdAt = DateTime.Now;
        private DateTime? _startedAt;
        private DateTime? _completedAt;
        private TimeSpan _estimatedTimeRemaining;
        private long _outputFileSize;
        private ExportPriority _priority = ExportPriority.Normal;
        private string? _errorMessage;
        private ExportPreset? _preset;
        private int _passNumber;
        private int _totalPasses = 1;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string ProjectPath
        {
            get => _projectPath;
            set { _projectPath = value; OnPropertyChanged(); }
        }

        public string OutputPath
        {
            get => _outputPath;
            set { _outputPath = value; OnPropertyChanged(); }
        }

        public ExportJobStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsActive)); OnPropertyChanged(nameof(CanCancel)); }
        }

        public double Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressText)); }
        }

        public string ProgressText => $"{Progress:F1}%";

        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public DateTime CreatedAt
        {
            get => _createdAt;
            set { _createdAt = value; OnPropertyChanged(); }
        }

        public DateTime? StartedAt
        {
            get => _startedAt;
            set { _startedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(Duration)); }
        }

        public DateTime? CompletedAt
        {
            get => _completedAt;
            set { _completedAt = value; OnPropertyChanged(); OnPropertyChanged(nameof(Duration)); }
        }

        public TimeSpan? Duration => StartedAt.HasValue 
            ? (CompletedAt ?? DateTime.Now) - StartedAt.Value 
            : null;

        public TimeSpan EstimatedTimeRemaining
        {
            get => _estimatedTimeRemaining;
            set { _estimatedTimeRemaining = value; OnPropertyChanged(); OnPropertyChanged(nameof(ETAText)); }
        }

        public string ETAText => EstimatedTimeRemaining.TotalSeconds > 0 
            ? $"ETA: {EstimatedTimeRemaining:hh\\:mm\\:ss}" 
            : "";

        public long OutputFileSize
        {
            get => _outputFileSize;
            set { _outputFileSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(OutputFileSizeText)); }
        }

        public string OutputFileSizeText => OutputFileSize > 0 
            ? FormatFileSize(OutputFileSize) 
            : "-";

        public ExportPriority Priority
        {
            get => _priority;
            set { _priority = value; OnPropertyChanged(); }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set { _errorMessage = value; OnPropertyChanged(); }
        }

        public ExportPreset? Preset
        {
            get => _preset;
            set { _preset = value; OnPropertyChanged(); OnPropertyChanged(nameof(PresetName)); }
        }

        public string PresetName => Preset?.Name ?? "Custom";

        public int PassNumber
        {
            get => _passNumber;
            set { _passNumber = value; OnPropertyChanged(); OnPropertyChanged(nameof(PassText)); }
        }

        public int TotalPasses
        {
            get => _totalPasses;
            set { _totalPasses = value; OnPropertyChanged(); OnPropertyChanged(nameof(PassText)); }
        }

        public string PassText => TotalPasses > 1 ? $"Pass {PassNumber}/{TotalPasses}" : "";

        public bool IsActive => Status == ExportJobStatus.Preparing || Status == ExportJobStatus.Encoding;
        public bool CanCancel => Status == ExportJobStatus.Queued || Status == ExportJobStatus.Preparing || Status == ExportJobStatus.Encoding;

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    #endregion

    /// <summary>
    /// Service for managing export queue with batch rendering support.
    /// </summary>
    public class ExportQueueService : BindableModel
    {
        private static readonly Lazy<ExportQueueService> _instance = new(() => new ExportQueueService());
        public static ExportQueueService Instance => _instance.Value;

        private readonly ObservableCollection<ExportJob> _queue = new();
        private readonly List<ExportPreset> _presets = new();
        private CancellationTokenSource? _cts;
        private bool _isProcessing;
        private int _maxConcurrentJobs = 1;
        private readonly object _lock = new();

        public event EventHandler<ExportJob>? JobStarted;
        public event EventHandler<ExportJob>? JobCompleted;
        public event EventHandler<ExportJob>? JobFailed;
        public event EventHandler<ExportJob>? JobCancelled;
        public event EventHandler<double>? ProgressChanged;
        public event EventHandler? QueueCompleted;

        private ExportQueueService()
        {
            LoadBuiltInPresets();
        }

        #region Properties

        public ObservableCollection<ExportJob> Queue => _queue;

        public IReadOnlyList<ExportPreset> Presets => _presets.AsReadOnly();

        public bool IsProcessing
        {
            get => _isProcessing;
            private set
            {
                if (_isProcessing != value)
                {
                    _isProcessing = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CanStartProcessing));
                    OnPropertyChanged(nameof(CanPauseProcessing));
                }
            }
        }

        public int MaxConcurrentJobs
        {
            get => _maxConcurrentJobs;
            set
            {
                if (_maxConcurrentJobs != value && value >= 1)
                {
                    _maxConcurrentJobs = value;
                    OnPropertyChanged();
                }
            }
        }

        public int TotalJobs => _queue.Count;
        public int CompletedJobs => _queue.Count(j => j.Status == ExportJobStatus.Completed);
        public int FailedJobs => _queue.Count(j => j.Status == ExportJobStatus.Failed);
        public int PendingJobs => _queue.Count(j => j.Status == ExportJobStatus.Queued);
        public int ActiveJobs => _queue.Count(j => j.IsActive);

        public double OverallProgress
        {
            get
            {
                if (TotalJobs == 0) return 0;
                var completed = _queue.Where(j => j.Status == ExportJobStatus.Completed).Count();
                var active = _queue.Where(j => j.IsActive).Sum(j => j.Progress / 100.0);
                return ((completed + active) / TotalJobs) * 100;
            }
        }

        public bool CanStartProcessing => !IsProcessing && PendingJobs > 0;
        public bool CanPauseProcessing => IsProcessing;

        #endregion

        #region Queue Management

        /// <summary>
        /// Adds a job to the export queue.
        /// </summary>
        public ExportJob AddJob(string projectPath, string outputPath, ExportPreset? preset = null, ExportPriority priority = ExportPriority.Normal)
        {
            var job = new ExportJob
            {
                Name = Path.GetFileNameWithoutExtension(outputPath),
                ProjectPath = projectPath,
                OutputPath = outputPath,
                Preset = preset,
                Priority = priority,
                TotalPasses = preset?.TwoPass == true ? 2 : 1
            };

            lock (_lock)
            {
                // Insert based on priority
                var insertIndex = _queue.Count;
                for (int i = 0; i < _queue.Count; i++)
                {
                    if (_queue[i].Status == ExportJobStatus.Queued && _queue[i].Priority < priority)
                    {
                        insertIndex = i;
                        break;
                    }
                }
                _queue.Insert(insertIndex, job);
            }

            OnPropertyChanged(nameof(TotalJobs));
            OnPropertyChanged(nameof(PendingJobs));
            return job;
        }

        /// <summary>
        /// Removes a job from the queue.
        /// </summary>
        public bool RemoveJob(string jobId)
        {
            lock (_lock)
            {
                var job = _queue.FirstOrDefault(j => j.Id == jobId);
                if (job != null && !job.IsActive)
                {
                    _queue.Remove(job);
                    OnPropertyChanged(nameof(TotalJobs));
                    OnPropertyChanged(nameof(PendingJobs));
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Clears completed and failed jobs from the queue.
        /// </summary>
        public void ClearFinishedJobs()
        {
            lock (_lock)
            {
                var toRemove = _queue.Where(j => 
                    j.Status == ExportJobStatus.Completed || 
                    j.Status == ExportJobStatus.Failed || 
                    j.Status == ExportJobStatus.Cancelled).ToList();
                
                foreach (var job in toRemove)
                    _queue.Remove(job);
            }

            OnPropertyChanged(nameof(TotalJobs));
            OnPropertyChanged(nameof(CompletedJobs));
            OnPropertyChanged(nameof(FailedJobs));
        }

        /// <summary>
        /// Moves a job up in the queue.
        /// </summary>
        public void MoveJobUp(string jobId)
        {
            lock (_lock)
            {
                var index = _queue.ToList().FindIndex(j => j.Id == jobId);
                if (index > 0 && _queue[index].Status == ExportJobStatus.Queued)
                {
                    var job = _queue[index];
                    _queue.RemoveAt(index);
                    _queue.Insert(index - 1, job);
                }
            }
        }

        /// <summary>
        /// Moves a job down in the queue.
        /// </summary>
        public void MoveJobDown(string jobId)
        {
            lock (_lock)
            {
                var index = _queue.ToList().FindIndex(j => j.Id == jobId);
                if (index >= 0 && index < _queue.Count - 1 && _queue[index].Status == ExportJobStatus.Queued)
                {
                    var job = _queue[index];
                    _queue.RemoveAt(index);
                    _queue.Insert(index + 1, job);
                }
            }
        }

        #endregion

        #region Processing

        /// <summary>
        /// Starts processing the export queue.
        /// </summary>
        public async Task StartProcessingAsync()
        {
            if (IsProcessing) return;

            IsProcessing = true;
            _cts = new CancellationTokenSource();

            try
            {
                while (PendingJobs > 0 && !_cts.Token.IsCancellationRequested)
                {
                    // Get next job (highest priority first)
                    ExportJob? nextJob;
                    lock (_lock)
                    {
                        nextJob = _queue
                            .Where(j => j.Status == ExportJobStatus.Queued)
                            .OrderByDescending(j => j.Priority)
                            .ThenBy(j => j.CreatedAt)
                            .FirstOrDefault();
                    }

                    if (nextJob == null) break;

                    await ProcessJobAsync(nextJob, _cts.Token);
                }

                QueueCompleted?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                // Processing was cancelled
            }
            finally
            {
                IsProcessing = false;
            }
        }

        /// <summary>
        /// Stops processing the queue.
        /// </summary>
        public void StopProcessing()
        {
            _cts?.Cancel();
        }

        /// <summary>
        /// Cancels a specific job.
        /// </summary>
        public void CancelJob(string jobId)
        {
            lock (_lock)
            {
                var job = _queue.FirstOrDefault(j => j.Id == jobId);
                if (job != null && job.CanCancel)
                {
                    job.Status = ExportJobStatus.Cancelled;
                    job.StatusMessage = "Cancelled by user";
                    JobCancelled?.Invoke(this, job);
                }
            }
        }

        private async Task ProcessJobAsync(ExportJob job, CancellationToken cancellationToken)
        {
            job.Status = ExportJobStatus.Preparing;
            job.StatusMessage = "Preparing...";
            job.StartedAt = DateTime.Now;
            JobStarted?.Invoke(this, job);

            try
            {
                // Simulate export process (in real implementation, call FFmpegService)
                job.Status = ExportJobStatus.Encoding;
                
                for (int pass = 1; pass <= job.TotalPasses; pass++)
                {
                    job.PassNumber = pass;
                    job.StatusMessage = job.TotalPasses > 1 
                        ? $"Encoding (Pass {pass}/{job.TotalPasses})..."
                        : "Encoding...";

                    // Simulate progress
                    for (int i = 0; i <= 100; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        if (job.Status == ExportJobStatus.Cancelled)
                            throw new OperationCanceledException();

                        job.Progress = i;
                        var elapsed = DateTime.Now - job.StartedAt!.Value;
                        if (i > 0)
                        {
                            var estimatedTotal = TimeSpan.FromTicks((long)(elapsed.Ticks / (i / 100.0)));
                            job.EstimatedTimeRemaining = estimatedTotal - elapsed;
                        }

                        OnPropertyChanged(nameof(OverallProgress));
                        ProgressChanged?.Invoke(this, OverallProgress);
                        
                        await Task.Delay(50, cancellationToken); // Simulate work
                    }
                }

                // Check output file
                if (File.Exists(job.OutputPath))
                {
                    job.OutputFileSize = new FileInfo(job.OutputPath).Length;
                }

                job.Status = ExportJobStatus.Completed;
                job.StatusMessage = "Completed";
                job.CompletedAt = DateTime.Now;
                job.Progress = 100;
                JobCompleted?.Invoke(this, job);
            }
            catch (OperationCanceledException)
            {
                job.Status = ExportJobStatus.Cancelled;
                job.StatusMessage = "Cancelled";
                job.CompletedAt = DateTime.Now;
                JobCancelled?.Invoke(this, job);
            }
            catch (Exception ex)
            {
                job.Status = ExportJobStatus.Failed;
                job.StatusMessage = "Failed";
                job.ErrorMessage = ex.Message;
                job.CompletedAt = DateTime.Now;
                JobFailed?.Invoke(this, job);
            }

            OnPropertyChanged(nameof(CompletedJobs));
            OnPropertyChanged(nameof(FailedJobs));
            OnPropertyChanged(nameof(PendingJobs));
            OnPropertyChanged(nameof(ActiveJobs));
        }

        #endregion

        #region Presets

        private void LoadBuiltInPresets()
        {
            _presets.AddRange(ExportPresets.GetAll());
        }

        /// <summary>
        /// Adds a custom preset.
        /// </summary>
        public void AddPreset(ExportPreset preset)
        {
            _presets.Add(preset);
        }

        /// <summary>
        /// Removes a custom preset.
        /// </summary>
        public bool RemovePreset(string presetName)
        {
            var preset = _presets.FirstOrDefault(p => p.Name == presetName);
            if (preset != null)
            {
                _presets.Remove(preset);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets presets by category.
        /// </summary>
        public IEnumerable<ExportPreset> GetPresetsByCategory(ExportCategory category)
        {
            return _presets.Where(p => p.Category == category);
        }

        /// <summary>
        /// Gets all preset categories.
        /// </summary>
        public IEnumerable<ExportCategory> GetPresetCategories()
        {
            return _presets.Select(p => p.Category).Distinct().OrderBy(c => c);
        }

        #endregion
    }
}
