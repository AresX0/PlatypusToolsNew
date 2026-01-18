using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class ProgressDialog : Window
    {
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly StringBuilder _details = new();

        public bool IsCancellationRequested => _cancellationTokenSource?.IsCancellationRequested ?? false;
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        public ProgressDialog()
        {
            InitializeComponent();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public ProgressDialog(string title) : this()
        {
            TitleText.Text = title;
            Title = title;
        }

        public void SetTitle(string title)
        {
            Dispatcher.Invoke(() =>
            {
                TitleText.Text = title;
                Title = title;
            });
        }

        public void SetStatus(string status)
        {
            Dispatcher.Invoke(() => StatusText.Text = status);
        }

        public void SetProgress(double value, double maximum = 100)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.Maximum = maximum;
                ProgressBar.Value = value;
                ProgressBar.IsIndeterminate = false;
                
                var percent = maximum > 0 ? (value / maximum) * 100 : 0;
                ProgressPercentText.Text = $"{percent:F0}%";
            });
        }

        public void SetIndeterminate(bool indeterminate = true)
        {
            Dispatcher.Invoke(() =>
            {
                ProgressBar.IsIndeterminate = indeterminate;
                ProgressPercentText.Text = indeterminate ? "" : "0%";
            });
        }

        public void AddDetail(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _details.AppendLine($"[{DateTime.Now:HH:mm:ss}] {message}");
                DetailsText.Text = _details.ToString();
                DetailsText.ScrollToEnd();
            });
        }

        public void EnableCancel(bool enable = true)
        {
            Dispatcher.Invoke(() => CancelButton.IsEnabled = enable);
        }

        public void Complete(string? message = null)
        {
            Dispatcher.Invoke(() =>
            {
                SetProgress(100);
                StatusText.Text = message ?? "Operation completed.";
                CancelButton.Content = "Close";
                _cancellationTokenSource = null;
            });
        }

        public void ShowError(string error)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"Error: {error}";
                CancelButton.Content = "Close";
                _cancellationTokenSource = null;
            });
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                StatusText.Text = "Cancelling...";
                CancelButton.IsEnabled = false;
            }
            else
            {
                Close();
            }
        }

        public static async Task<T?> RunWithProgressAsync<T>(
            Window owner,
            string title,
            Func<ProgressDialog, CancellationToken, Task<T>> operation)
        {
            var dialog = new ProgressDialog(title) { Owner = owner };
            T? result = default;
            Exception? error = null;

            dialog.Loaded += async (s, e) =>
            {
                try
                {
                    result = await operation(dialog, dialog.CancellationToken);
                    dialog.Complete();
                }
                catch (OperationCanceledException)
                {
                    dialog.SetStatus("Operation cancelled.");
                }
                catch (Exception ex)
                {
                    error = ex;
                    dialog.ShowError(ex.Message);
                }
            };

            dialog.ShowDialog();

            if (error != null)
                throw error;

            return result;
        }

        public static async Task RunWithProgressAsync(
            Window owner,
            string title,
            Func<ProgressDialog, CancellationToken, Task> operation)
        {
            await RunWithProgressAsync<object?>(owner, title, async (dialog, token) =>
            {
                await operation(dialog, token);
                return null;
            });
        }
    }
}
