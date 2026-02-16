using System;
using System.Windows;
using PlatypusTools.Core.Services.Abstractions;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Bridges IProgressReporter events from Core services to the UI StatusBarViewModel.
    /// Automatically dispatches to the UI thread.
    /// </summary>
    public class ProgressReporterStatusBarBridge : IDisposable
    {
        private readonly IProgressReporter _reporter;
        private readonly StatusBarViewModel _statusBar;
        private readonly string _operationName;
        private bool _disposed;

        /// <summary>
        /// Creates a bridge that forwards IProgressReporter events to the StatusBarViewModel.
        /// </summary>
        /// <param name="reporter">The progress reporter to listen to.</param>
        /// <param name="operationName">The operation name shown in the status bar.</param>
        /// <param name="statusBar">Optional StatusBarViewModel override; uses Instance if null.</param>
        public ProgressReporterStatusBarBridge(IProgressReporter reporter, string operationName, StatusBarViewModel? statusBar = null)
        {
            _reporter = reporter ?? throw new ArgumentNullException(nameof(reporter));
            _operationName = operationName;
            _statusBar = statusBar ?? StatusBarViewModel.Instance;

            _reporter.ProgressChanged += OnProgressChanged;
        }

        /// <summary>
        /// Creates a new ProgressReporter and its bridge in one step.
        /// </summary>
        public static (ProgressReporter reporter, ProgressReporterStatusBarBridge bridge) Create(string operationName, StatusBarViewModel? statusBar = null)
        {
            var reporter = new ProgressReporter();
            var bridge = new ProgressReporterStatusBarBridge(reporter, operationName, statusBar);
            return (reporter, bridge);
        }

        private void OnProgressChanged(object? sender, ProgressInfo info)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(() => ApplyProgress(info)));
                return;
            }
            ApplyProgress(info);
        }

        private void ApplyProgress(ProgressInfo info)
        {
            if (info.IsComplete)
            {
                _statusBar.CompleteOperation(info.Message);
                return;
            }

            if (info.IsError)
            {
                _statusBar.ReportError(info.Message);
                return;
            }

            if (info.IsIndeterminate)
            {
                _statusBar.StatusMessage = info.Message;
                return;
            }

            if (info.TotalItems > 0)
            {
                _statusBar.UpdateProgress(info.CurrentItem, info.Message);
            }
            else if (info.Percent.HasValue)
            {
                _statusBar.UpdateProgress(info.Percent.Value, info.Message);
            }
            else
            {
                _statusBar.StatusMessage = info.Message;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _reporter.ProgressChanged -= OnProgressChanged;
                _disposed = true;
            }
        }
    }
}
