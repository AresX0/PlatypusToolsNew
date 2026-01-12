using System;
using System.Threading;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the application status bar.
    /// Displays current operation status, progress, and elapsed time.
    /// </summary>
    public class StatusBarViewModel : BindableBase
    {
        private static StatusBarViewModel? _instance;
        private readonly System.Diagnostics.Stopwatch _stopwatch = new();
        private Timer? _elapsedTimer;

        /// <summary>
        /// Singleton instance for global status bar access.
        /// </summary>
        public static StatusBarViewModel Instance => _instance ??= new StatusBarViewModel();

        public StatusBarViewModel()
        {
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsOperationRunning && _cancellationTokenSource != null);
        }

        #region Properties

        private string _statusMessage = "Ready";
        /// <summary>
        /// Current status message displayed in the status bar.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _operationName = string.Empty;
        /// <summary>
        /// Name of the current operation (e.g., "Scanning files...", "Converting video...").
        /// </summary>
        public string OperationName
        {
            get => _operationName;
            set => SetProperty(ref _operationName, value);
        }

        private double _progress;
        /// <summary>
        /// Progress value from 0 to 100.
        /// </summary>
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, Math.Clamp(value, 0, 100));
        }

        private bool _isIndeterminate;
        /// <summary>
        /// Whether the progress bar should show indeterminate progress.
        /// </summary>
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value);
        }

        private bool _isOperationRunning;
        /// <summary>
        /// Whether an operation is currently in progress.
        /// </summary>
        public bool IsOperationRunning
        {
            get => _isOperationRunning;
            set => SetProperty(ref _isOperationRunning, value);
        }

        private string _elapsedTime = "00:00";
        /// <summary>
        /// Elapsed time since operation started.
        /// </summary>
        public string ElapsedTime
        {
            get => _elapsedTime;
            set => SetProperty(ref _elapsedTime, value);
        }

        private int _itemsProcessed;
        /// <summary>
        /// Number of items processed so far.
        /// </summary>
        public int ItemsProcessed
        {
            get => _itemsProcessed;
            set => SetProperty(ref _itemsProcessed, value);
        }

        private int _totalItems;
        /// <summary>
        /// Total number of items to process.
        /// </summary>
        public int TotalItems
        {
            get => _totalItems;
            set => SetProperty(ref _totalItems, value);
        }

        private string _itemsDisplay = string.Empty;
        /// <summary>
        /// Display string for items (e.g., "5 of 100").
        /// </summary>
        public string ItemsDisplay
        {
            get => _itemsDisplay;
            set => SetProperty(ref _itemsDisplay, value);
        }

        private bool _isCancellable = true;
        /// <summary>
        /// Whether the current operation can be cancelled.
        /// </summary>
        public bool IsCancellable
        {
            get => _isCancellable;
            set => SetProperty(ref _isCancellable, value);
        }

        #endregion

        #region Commands

        public ICommand CancelCommand { get; }

        #endregion

        #region Cancellation

        private CancellationTokenSource? _cancellationTokenSource;

        /// <summary>
        /// Gets a CancellationToken for the current operation.
        /// </summary>
        public CancellationToken GetCancellationToken()
        {
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            return _cancellationTokenSource.Token;
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts a new operation with the specified name.
        /// </summary>
        /// <param name="operationName">Name of the operation to display.</param>
        /// <param name="totalItems">Total items to process (0 for indeterminate).</param>
        /// <param name="isCancellable">Whether the operation can be cancelled.</param>
        public void StartOperation(string operationName, int totalItems = 0, bool isCancellable = true)
        {
            OperationName = operationName;
            StatusMessage = operationName;
            Progress = 0;
            ItemsProcessed = 0;
            TotalItems = totalItems;
            IsIndeterminate = totalItems == 0;
            IsOperationRunning = true;
            IsCancellable = isCancellable;
            UpdateItemsDisplay();

            _stopwatch.Restart();
            _elapsedTimer?.Dispose();
            _elapsedTimer = new Timer(UpdateElapsedTime, null, 0, 1000);
        }

        /// <summary>
        /// Updates the progress of the current operation.
        /// </summary>
        /// <param name="itemsProcessed">Number of items processed.</param>
        /// <param name="statusMessage">Optional status message update.</param>
        public void UpdateProgress(int itemsProcessed, string? statusMessage = null)
        {
            ItemsProcessed = itemsProcessed;
            if (TotalItems > 0)
            {
                Progress = (itemsProcessed * 100.0) / TotalItems;
            }
            if (statusMessage != null)
            {
                StatusMessage = statusMessage;
            }
            UpdateItemsDisplay();
        }

        /// <summary>
        /// Updates the progress percentage directly.
        /// </summary>
        /// <param name="progressPercent">Progress from 0 to 100.</param>
        /// <param name="statusMessage">Optional status message update.</param>
        public void UpdateProgress(double progressPercent, string? statusMessage = null)
        {
            Progress = progressPercent;
            if (statusMessage != null)
            {
                StatusMessage = statusMessage;
            }
        }

        /// <summary>
        /// Completes the current operation.
        /// </summary>
        /// <param name="message">Completion message to display.</param>
        public void CompleteOperation(string message = "Complete")
        {
            _stopwatch.Stop();
            _elapsedTimer?.Dispose();
            _elapsedTimer = null;

            IsOperationRunning = false;
            IsIndeterminate = false;
            Progress = 100;
            StatusMessage = $"{message} ({ElapsedTime})";
            OperationName = string.Empty;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        /// <summary>
        /// Resets the status bar to ready state.
        /// </summary>
        public void Reset()
        {
            _stopwatch.Stop();
            _elapsedTimer?.Dispose();
            _elapsedTimer = null;

            StatusMessage = "Ready";
            OperationName = string.Empty;
            Progress = 0;
            IsIndeterminate = false;
            IsOperationRunning = false;
            ItemsProcessed = 0;
            TotalItems = 0;
            ItemsDisplay = string.Empty;
            ElapsedTime = "00:00";

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        /// <summary>
        /// Reports an error and stops the operation.
        /// </summary>
        /// <param name="errorMessage">Error message to display.</param>
        public void ReportError(string errorMessage)
        {
            _stopwatch.Stop();
            _elapsedTimer?.Dispose();
            _elapsedTimer = null;

            IsOperationRunning = false;
            IsIndeterminate = false;
            StatusMessage = $"Error: {errorMessage}";

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }

        #endregion

        #region Private Methods

        private void UpdateElapsedTime(object? state)
        {
            var elapsed = _stopwatch.Elapsed;
            ElapsedTime = elapsed.TotalHours >= 1
                ? $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}"
                : $"{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
        }

        private void UpdateItemsDisplay()
        {
            ItemsDisplay = TotalItems > 0
                ? $"{ItemsProcessed} of {TotalItems}"
                : ItemsProcessed > 0
                    ? $"{ItemsProcessed} items"
                    : string.Empty;
        }

        #endregion
    }

    /// <summary>
    /// Helper class for reporting progress to the status bar.
    /// Implements IProgress&lt;T&gt; for use with async operations.
    /// </summary>
    public class StatusBarProgress : IProgress<double>
    {
        private readonly StatusBarViewModel _statusBar;
        private readonly string? _messageFormat;

        public StatusBarProgress(StatusBarViewModel? statusBar = null, string? messageFormat = null)
        {
            _statusBar = statusBar ?? StatusBarViewModel.Instance;
            _messageFormat = messageFormat;
        }

        public void Report(double value)
        {
            var message = _messageFormat != null
                ? string.Format(_messageFormat, value)
                : null;
            _statusBar.UpdateProgress(value, message);
        }
    }

    /// <summary>
    /// Helper class for reporting item-based progress to the status bar.
    /// </summary>
    public class StatusBarItemProgress : IProgress<int>
    {
        private readonly StatusBarViewModel _statusBar;

        public StatusBarItemProgress(StatusBarViewModel? statusBar = null)
        {
            _statusBar = statusBar ?? StatusBarViewModel.Instance;
        }

        public void Report(int value)
        {
            _statusBar.UpdateProgress(value);
        }
    }
}
