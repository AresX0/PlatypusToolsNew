using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// IDEA-007: Batch Job Queue panel ViewModel.
    /// Shows all queued, running, completed, and failed jobs with controls.
    /// </summary>
    public class BatchJobQueueViewModel : BindableBase
    {
        private readonly BatchJobQueueService _queue;

        public ObservableCollection<BatchJob> Jobs => _queue.AllJobs;

        private string _summaryText = "No jobs";
        public string SummaryText
        {
            get => _summaryText;
            set { _summaryText = value; RaisePropertyChanged(); }
        }

        private bool _isPaused;
        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                RaisePropertyChanged();
                RaisePropertyChanged(nameof(PauseResumeLabel));
                _queue.IsPaused = value;
            }
        }

        public string PauseResumeLabel => IsPaused ? "▶ Resume" : "⏸ Pause";

        private int _maxConcurrent;
        public int MaxConcurrent
        {
            get => _maxConcurrent;
            set
            {
                _maxConcurrent = value;
                RaisePropertyChanged();
                _queue.MaxConcurrentJobs = value;
            }
        }

        public ICommand PauseResumeCommand { get; }
        public ICommand CancelAllCommand { get; }
        public ICommand ClearFinishedCommand { get; }
        public ICommand CancelJobCommand { get; }
        public ICommand RetryJobCommand { get; }

        public BatchJobQueueViewModel()
        {
            _queue = BatchJobQueueService.Instance;
            _maxConcurrent = _queue.MaxConcurrentJobs;

            PauseResumeCommand = new RelayCommand(_ => IsPaused = !IsPaused);
            CancelAllCommand = new RelayCommand(_ => _queue.CancelAll());
            ClearFinishedCommand = new RelayCommand(_ => _queue.ClearFinished());
            CancelJobCommand = new RelayCommand(param =>
            {
                if (param is BatchJob job) _queue.Cancel(job);
            });
            RetryJobCommand = new RelayCommand(param =>
            {
                if (param is BatchJob job) _queue.Retry(job);
            });

            _queue.QueueChanged += (_, __) => UpdateSummary();
            _queue.JobStarted += (_, __) => UpdateSummary();
            _queue.JobCompleted += (_, __) => UpdateSummary();
            _queue.JobFailed += (_, __) => UpdateSummary();
            _queue.JobCancelled += (_, __) => UpdateSummary();

            UpdateSummary();
        }

        private void UpdateSummary()
        {
            var pending = _queue.PendingCount;
            var running = _queue.RunningCount;
            var completed = _queue.CompletedCount;
            var failed = _queue.FailedCount;
            var total = Jobs.Count;

            if (total == 0)
            {
                SummaryText = "No jobs in queue";
            }
            else
            {
                SummaryText = $"{running} running, {pending} queued, {completed} completed, {failed} failed — {total} total";
            }
        }
    }
}
