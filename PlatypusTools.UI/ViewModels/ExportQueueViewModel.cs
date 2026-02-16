using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using PlatypusTools.Core.Services.Video;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Export Queue with scheduling support.
    /// Bridges ExportQueueService and AppTaskSchedulerService for batch export operations.
    /// </summary>
    public class ExportQueueViewModel : BindableBase
    {
        private readonly ExportQueueService _exportQueue = ExportQueueService.Instance;
        private string _statusMessage = "Ready";
        private bool _isSchedulingEnabled;
        private DateTime _scheduledTime = DateTime.Now.AddHours(1);
        private string _selectedPresetCategory = "All";

        public ExportQueueViewModel()
        {
            _exportQueue.JobStarted += (s, j) => StatusMessage = $"Started: {j.Name}";
            _exportQueue.JobCompleted += (s, j) => StatusMessage = $"Completed: {j.Name}";
            _exportQueue.JobFailed += (s, j) => StatusMessage = $"Failed: {j.Name} - {j.ErrorMessage}";
            _exportQueue.QueueCompleted += (s, e) => StatusMessage = "All exports completed!";
        }

        #region Properties

        public ObservableCollection<ExportJob> Jobs => _exportQueue.Queue;

        public bool IsProcessing => _exportQueue.IsProcessing;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsSchedulingEnabled
        {
            get => _isSchedulingEnabled;
            set => SetProperty(ref _isSchedulingEnabled, value);
        }

        public DateTime ScheduledTime
        {
            get => _scheduledTime;
            set => SetProperty(ref _scheduledTime, value);
        }

        public string SelectedPresetCategory
        {
            get => _selectedPresetCategory;
            set
            {
                if (SetProperty(ref _selectedPresetCategory, value))
                    OnPropertyChanged(nameof(FilteredPresets));
            }
        }

        public System.Collections.Generic.IReadOnlyList<ExportPreset> AllPresets => _exportQueue.Presets;

        public System.Collections.Generic.IEnumerable<ExportPreset> FilteredPresets =>
            _selectedPresetCategory == "All"
                ? AllPresets
                : AllPresets.Where(p => p.Category.ToString() == _selectedPresetCategory);

        public System.Collections.Generic.IEnumerable<string> PresetCategories =>
            new[] { "All" }.Concat(AllPresets.Select(p => p.Category.ToString()).Distinct().OrderBy(c => c));

        public int QueuedCount => Jobs.Count(j => j.Status == ExportJobStatus.Queued);
        public int ActiveCount => Jobs.Count(j => j.IsActive);
        public int CompletedCount => Jobs.Count(j => j.Status == ExportJobStatus.Completed);
        public int FailedCount => Jobs.Count(j => j.Status == ExportJobStatus.Failed);

        public string SummaryText =>
            $"Queued: {QueuedCount} | Active: {ActiveCount} | Completed: {CompletedCount} | Failed: {FailedCount}";

        #endregion

        #region Public Methods

        public void StartProcessing()
        {
            if (IsSchedulingEnabled && ScheduledTime > DateTime.Now)
            {
                ScheduleExport();
                return;
            }

            _ = _exportQueue.StartProcessingAsync();
            StatusMessage = "Processing started...";
            RefreshCounts();
        }

        public void StopProcessing()
        {
            _exportQueue.StopProcessing();
            StatusMessage = "Processing stopped.";
            RefreshCounts();
        }

        public void CancelJob(ExportJob job)
        {
            _exportQueue.CancelJob(job.Id);
            StatusMessage = $"Cancelled: {job.Name}";
            RefreshCounts();
        }

        public void MoveJobUp(ExportJob job)
        {
            _exportQueue.MoveJobUp(job.Id);
        }

        public void MoveJobDown(ExportJob job)
        {
            _exportQueue.MoveJobDown(job.Id);
        }

        public void ClearFinished()
        {
            _exportQueue.ClearFinishedJobs();
            StatusMessage = "Cleared finished jobs.";
            RefreshCounts();
        }

        public void RemoveJob(ExportJob job)
        {
            _exportQueue.RemoveJob(job.Id);
            RefreshCounts();
        }

        public void RefreshCounts()
        {
            OnPropertyChanged(nameof(QueuedCount));
            OnPropertyChanged(nameof(ActiveCount));
            OnPropertyChanged(nameof(CompletedCount));
            OnPropertyChanged(nameof(FailedCount));
            OnPropertyChanged(nameof(SummaryText));
            OnPropertyChanged(nameof(IsProcessing));
        }

        #endregion

        #region Scheduling

        private System.Threading.Timer? _scheduleTimer;

        private void ScheduleExport()
        {
            var delay = ScheduledTime - DateTime.Now;
            if (delay <= TimeSpan.Zero)
            {
                _ = _exportQueue.StartProcessingAsync();
                StatusMessage = "Processing started immediately (scheduled time already passed).";
                return;
            }

            _scheduleTimer?.Dispose();
            _scheduleTimer = new System.Threading.Timer(_ =>
            {
                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    _ = _exportQueue.StartProcessingAsync();
                    StatusMessage = "Scheduled export started!";
                    RefreshCounts();
                });
            }, null, delay, System.Threading.Timeout.InfiniteTimeSpan);

            StatusMessage = $"Export scheduled for {ScheduledTime:yyyy-MM-dd HH:mm}";
        }

        #endregion
    }
}
