using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// A modal dialog for showing progress during long-running operations.
    /// Supports cancellation, determinate and indeterminate progress.
    /// </summary>
    public partial class ProgressDialogWindow : Window
    {
        private readonly CancellationTokenSource _cts;
        private bool _canCancel = true;
        
        public CancellationToken CancellationToken => _cts.Token;
        public bool WasCancelled { get; private set; }
        
        public ProgressDialogWindow(string title = "Processing...", string message = "Please wait...", bool canCancel = true)
        {
            InitializeComponent();
            _cts = new CancellationTokenSource();
            _canCancel = canCancel;
            
            TitleText.Text = title;
            MessageText.Text = message;
            CancelButton.Visibility = canCancel ? Visibility.Visible : Visibility.Collapsed;
        }
        
        /// <summary>
        /// Updates the progress display.
        /// </summary>
        /// <param name="percent">Progress percentage (0-100), or -1 for indeterminate.</param>
        /// <param name="message">Optional status message.</param>
        /// <param name="currentItem">Current item number.</param>
        /// <param name="totalItems">Total items count.</param>
        public void UpdateProgress(double percent, string? message = null, int currentItem = 0, int totalItems = 0)
        {
            Dispatcher.Invoke(() =>
            {
                if (percent < 0)
                {
                    MainProgressBar.IsIndeterminate = true;
                    PercentText.Text = "";
                }
                else
                {
                    MainProgressBar.IsIndeterminate = false;
                    MainProgressBar.Value = percent;
                    PercentText.Text = $"{percent:F0}%";
                }
                
                if (!string.IsNullOrEmpty(message))
                {
                    MessageText.Text = message;
                }
                
                if (totalItems > 0)
                {
                    ItemsText.Text = $"Item {currentItem} of {totalItems}";
                }
            });
        }
        
        /// <summary>
        /// Sets the title text.
        /// </summary>
        public void SetTitle(string title)
        {
            Dispatcher.Invoke(() => TitleText.Text = title);
        }
        
        /// <summary>
        /// Sets the message text.
        /// </summary>
        public void SetMessage(string message)
        {
            Dispatcher.Invoke(() => MessageText.Text = message);
        }
        
        /// <summary>
        /// Completes the dialog, closing it automatically.
        /// </summary>
        public void Complete()
        {
            Dispatcher.Invoke(() =>
            {
                DialogResult = !WasCancelled;
                Close();
            });
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_canCancel)
            {
                WasCancelled = true;
                _cts.Cancel();
                CancelButton.IsEnabled = false;
                CancelButton.Content = "Cancelling...";
                MessageText.Text = "Cancelling operation...";
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _cts.Dispose();
            base.OnClosed(e);
        }
        
        /// <summary>
        /// Helper method to run an async operation with a progress dialog.
        /// </summary>
        public static async Task<T?> RunWithProgressAsync<T>(
            Window owner, 
            Func<ProgressDialogWindow, CancellationToken, Task<T>> operation,
            string title = "Processing...",
            string message = "Please wait...",
            bool canCancel = true)
        {
            var dialog = new ProgressDialogWindow(title, message, canCancel);
            dialog.Owner = owner;
            
            T? result = default;
            
            // Start the operation on a background thread
            var task = Task.Run(async () =>
            {
                try
                {
                    result = await operation(dialog, dialog.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    dialog.WasCancelled = true;
                }
                finally
                {
                    dialog.Complete();
                }
            });
            
            dialog.ShowDialog();
            
            await task;
            
            return result;
        }
        
        /// <summary>
        /// Helper method to run an async operation without a return value.
        /// </summary>
        public static async Task RunWithProgressAsync(
            Window owner,
            Func<ProgressDialogWindow, CancellationToken, Task> operation,
            string title = "Processing...",
            string message = "Please wait...",
            bool canCancel = true)
        {
            await RunWithProgressAsync<object?>(owner, async (dialog, ct) =>
            {
                await operation(dialog, ct);
                return null;
            }, title, message, canCancel);
        }
    }
}
