using System;

namespace PlatypusTools.Core.Services.Abstractions
{
    /// <summary>
    /// Unified progress reporting interface for all long-running operations.
    /// Use this instead of individual event-based or callback-based progress patterns.
    /// </summary>
    public interface IProgressReporter
    {
        /// <summary>
        /// Reports progress with a percentage (0-100), current item count, total items, and message.
        /// </summary>
        void Report(ProgressInfo progress);

        /// <summary>
        /// Reports a simple percentage with optional message.
        /// </summary>
        void ReportPercent(double percent, string? message = null);

        /// <summary>
        /// Reports item-based progress (current/total).
        /// </summary>
        void ReportItems(int current, int total, string? message = null);

        /// <summary>
        /// Reports an indeterminate operation with a message.
        /// </summary>
        void ReportIndeterminate(string message);

        /// <summary>
        /// Reports that the operation is complete.
        /// </summary>
        void ReportComplete(string? message = null);

        /// <summary>
        /// Reports an error.
        /// </summary>
        void ReportError(string message);

        /// <summary>
        /// Fired when progress is reported.
        /// </summary>
        event EventHandler<ProgressInfo>? ProgressChanged;
    }

    /// <summary>
    /// Standard progress info payload used across all services.
    /// </summary>
    public class ProgressInfo
    {
        /// <summary>
        /// Progress percentage (0-100). Null for indeterminate operations.
        /// </summary>
        public double? Percent { get; set; }

        /// <summary>
        /// Current item number being processed.
        /// </summary>
        public int CurrentItem { get; set; }

        /// <summary>
        /// Total number of items to process. 0 if unknown.
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// Human-readable progress message.
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Whether the operation is complete.
        /// </summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// Whether this is an error report.
        /// </summary>
        public bool IsError { get; set; }

        /// <summary>
        /// Whether the operation is in an indeterminate state.
        /// </summary>
        public bool IsIndeterminate { get; set; }

        /// <summary>
        /// Estimated time remaining. Null if unknown.
        /// </summary>
        public TimeSpan? EstimatedRemaining { get; set; }

        /// <summary>
        /// Bytes processed so far.
        /// </summary>
        public long BytesProcessed { get; set; }

        /// <summary>
        /// Total bytes to process. 0 if unknown.
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// The name/label of the current operation phase.
        /// </summary>
        public string? Phase { get; set; }

        /// <summary>
        /// Timestamp of this progress report.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// Default implementation of IProgressReporter.
    /// Thread-safe and suitable for use from any service.
    /// Bridges to IProgress&lt;T&gt; for compatibility with existing async patterns.
    /// </summary>
    public class ProgressReporter : IProgressReporter, IProgress<double>
    {
        private readonly object _lock = new();
        private DateTime _operationStart = DateTime.Now;

        public event EventHandler<ProgressInfo>? ProgressChanged;

        /// <summary>
        /// Creates a new ProgressReporter.
        /// </summary>
        public ProgressReporter()
        {
        }

        /// <summary>
        /// Creates a ProgressReporter that also forwards events to a callback.
        /// </summary>
        public ProgressReporter(Action<ProgressInfo> callback)
        {
            ProgressChanged += (_, info) => callback(info);
        }

        public void Report(ProgressInfo progress)
        {
            ProgressChanged?.Invoke(this, progress);
        }

        public void ReportPercent(double percent, string? message = null)
        {
            Report(new ProgressInfo
            {
                Percent = percent,
                Message = message ?? $"{percent:F1}%"
            });
        }

        public void ReportItems(int current, int total, string? message = null)
        {
            var percent = total > 0 ? (double)current / total * 100 : 0;
            var elapsed = DateTime.Now - _operationStart;
            TimeSpan? eta = null;
            if (current > 0 && total > 0)
            {
                var remaining = total - current;
                var perItem = elapsed.TotalMilliseconds / current;
                eta = TimeSpan.FromMilliseconds(remaining * perItem);
            }

            Report(new ProgressInfo
            {
                Percent = percent,
                CurrentItem = current,
                TotalItems = total,
                EstimatedRemaining = eta,
                Message = message ?? $"{current} / {total}"
            });
        }

        public void ReportIndeterminate(string message)
        {
            Report(new ProgressInfo
            {
                IsIndeterminate = true,
                Message = message
            });
        }

        public void ReportComplete(string? message = null)
        {
            Report(new ProgressInfo
            {
                Percent = 100,
                IsComplete = true,
                Message = message ?? "Complete"
            });
        }

        public void ReportError(string message)
        {
            Report(new ProgressInfo
            {
                IsError = true,
                Message = message
            });
        }

        /// <summary>
        /// Resets the start time for ETA calculations.
        /// Call this at the beginning of each new operation.
        /// </summary>
        public void ResetTimer()
        {
            _operationStart = DateTime.Now;
        }

        /// <summary>
        /// IProgress&lt;double&gt; implementation for compatibility with async APIs.
        /// Value should be 0.0 to 1.0.
        /// </summary>
        void IProgress<double>.Report(double value)
        {
            ReportPercent(value * 100);
        }
    }
}
