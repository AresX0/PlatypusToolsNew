using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PlatypusTools.UI.Controls
{
    public partial class StatusBar : UserControl
    {
        private readonly DispatcherTimer _memoryTimer;

        public StatusBar()
        {
            InitializeComponent();
            
            _memoryTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _memoryTimer.Tick += UpdateMemoryUsage;
            _memoryTimer.Start();
            
            Unloaded += (s, e) => _memoryTimer.Stop();
            UpdateMemoryUsage(null, EventArgs.Empty);
        }

        public void SetStatus(string message)
        {
            StatusMessage.Text = message;
        }

        public void SetStatusTemporary(string message, int durationMs = 3000)
        {
            StatusMessage.Text = message;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                StatusMessage.Text = "Ready";
            };
            timer.Start();
        }

        public void ShowProgress(bool visible = true)
        {
            ProgressBar.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetProgress(double value, double maximum = 100)
        {
            ProgressBar.Maximum = maximum;
            ProgressBar.Value = value;
        }

        public void SetProgressIndeterminate(bool indeterminate = true)
        {
            ProgressBar.IsIndeterminate = indeterminate;
        }

        public void UpdateFileCount(int count)
        {
            FileCount.Text = count.ToString("N0");
        }

        public void UpdateSelectedCount(int count)
        {
            SelectedCount.Text = count.ToString("N0");
        }

        public void UpdateTotalSize(long bytes)
        {
            TotalSize.Text = FormatFileSize(bytes);
        }

        private void UpdateMemoryUsage(object? sender, EventArgs e)
        {
            var process = System.Diagnostics.Process.GetCurrentProcess();
            var memoryMB = process.WorkingSet64 / (1024.0 * 1024.0);
            MemoryUsage.Text = $"{memoryMB:F1} MB";
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;
            
            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }
            
            return suffixIndex == 0 ? $"{size:F0} {suffixes[suffixIndex]}" : $"{size:F2} {suffixes[suffixIndex]}";
        }
    }
}
