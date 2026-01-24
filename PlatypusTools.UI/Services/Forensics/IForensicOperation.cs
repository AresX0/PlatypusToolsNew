using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Base interface for all forensic operations.
    /// Provides consistent patterns for progress reporting, cancellation, and logging.
    /// </summary>
    public interface IForensicOperation
    {
        /// <summary>
        /// Gets the name of the operation for display purposes.
        /// </summary>
        string OperationName { get; }

        /// <summary>
        /// Gets whether the operation is currently running.
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Gets the current progress (0-100).
        /// </summary>
        double Progress { get; }

        /// <summary>
        /// Gets the current status message.
        /// </summary>
        string StatusMessage { get; }

        /// <summary>
        /// Event raised when a log message is generated.
        /// </summary>
        event Action<string>? LogMessage;

        /// <summary>
        /// Event raised when progress changes.
        /// </summary>
        event Action<double>? ProgressChanged;

        /// <summary>
        /// Event raised when status changes.
        /// </summary>
        event Action<string>? StatusChanged;

        /// <summary>
        /// Cancels the current operation.
        /// </summary>
        void Cancel();
    }

    /// <summary>
    /// Base class providing reusable patterns for forensic operations.
    /// Eliminates duplicate code across DFIR features.
    /// </summary>
    public abstract class ForensicOperationBase : IForensicOperation, IDisposable
    {
        protected CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed;

        public abstract string OperationName { get; }
        
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            protected set
            {
                _isRunning = value;
                StatusChanged?.Invoke(value ? $"{OperationName} running..." : $"{OperationName} idle");
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            protected set
            {
                _progress = value;
                ProgressChanged?.Invoke(value);
            }
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            protected set
            {
                _statusMessage = value;
                StatusChanged?.Invoke(value);
            }
        }

        public event Action<string>? LogMessage;
        public event Action<double>? ProgressChanged;
        public event Action<string>? StatusChanged;

        /// <summary>
        /// Logs a message to subscribers.
        /// </summary>
        protected void Log(string message)
        {
            LogMessage?.Invoke(message);
        }

        /// <summary>
        /// Reports progress with a message - convenience alias for Log + Progress update.
        /// </summary>
        protected void ReportProgress(string message, double? progressPercent = null)
        {
            Log(message);
            if (progressPercent.HasValue)
            {
                Progress = progressPercent.Value;
            }
        }

        /// <summary>
        /// Reports an error - convenience alias for LogError.
        /// </summary>
        protected void ReportError(string message)
        {
            LogError(message);
            StatusMessage = message;
        }

        /// <summary>
        /// Logs a header section.
        /// </summary>
        protected void LogHeader(string title)
        {
            Log("========================================");
            Log(title);
            Log("========================================");
            Log("");
        }

        /// <summary>
        /// Logs a success message.
        /// </summary>
        protected void LogSuccess(string message)
        {
            Log($"✓ {message}");
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        protected void LogError(string message)
        {
            Log($"✗ {message}");
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        protected void LogWarning(string message)
        {
            Log($"⚠ {message}");
        }

        /// <summary>
        /// Logs an info message.
        /// </summary>
        protected void LogInfo(string message)
        {
            Log($"ℹ {message}");
        }

        /// <summary>
        /// Gets or creates a cancellation token for the current operation.
        /// </summary>
        protected CancellationToken GetCancellationToken()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            return _cancellationTokenSource.Token;
        }

        /// <summary>
        /// Cancels the current operation.
        /// </summary>
        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            IsRunning = false;
            StatusMessage = $"{OperationName} cancelled";
            Log($"Operation cancelled by user");
        }

        /// <summary>
        /// Executes an operation with standard error handling and progress tracking.
        /// </summary>
        protected async Task ExecuteWithHandlingAsync(Func<CancellationToken, Task> operation)
        {
            if (IsRunning)
            {
                Log("Operation already in progress");
                return;
            }

            IsRunning = true;
            Progress = 0;
            var token = GetCancellationToken();

            try
            {
                await operation(token);
            }
            catch (OperationCanceledException)
            {
                LogWarning("Operation was cancelled");
                StatusMessage = "Cancelled";
            }
            catch (Exception ex)
            {
                LogError($"Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        /// <summary>
        /// Executes an operation with standard error handling and an external cancellation token.
        /// </summary>
        protected async Task ExecuteWithHandlingAsync(Func<Task> operation, CancellationToken externalToken)
        {
            if (IsRunning)
            {
                Log("Operation already in progress");
                return;
            }

            IsRunning = true;
            Progress = 0;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            var token = _cancellationTokenSource.Token;

            try
            {
                await operation();
            }
            catch (OperationCanceledException)
            {
                LogWarning("Operation was cancelled");
                StatusMessage = "Cancelled";
            }
            catch (Exception ex)
            {
                LogError($"Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        /// <summary>
        /// Updates progress based on current item and total count.
        /// </summary>
        protected void UpdateProgress(int current, int total)
        {
            if (total > 0)
            {
                Progress = (current * 100.0) / total;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;

            if (disposing)
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }

            _disposed = true;
        }
    }
}
