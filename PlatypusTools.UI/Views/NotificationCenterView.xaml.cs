using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Phase 1.2 — Notification Center: dedicated tab listing the persisted notification history.
    /// </summary>
    public partial class NotificationCenterView : UserControl
    {
        public NotificationCenterView()
        {
            InitializeComponent();
            Loaded += (s, e) => Refresh();
            ToastNotificationService.Instance.NotificationHistory.CollectionChanged += (_, __) =>
                Dispatcher.BeginInvoke(new Action(Refresh));
        }

        private void Refresh()
        {
            var src = ToastNotificationService.Instance.NotificationHistory.AsEnumerable();

            // Severity filter
            if (FilterCombo?.SelectedIndex > 0)
            {
                var severity = (ToastType)(FilterCombo.SelectedIndex - 1);
                src = src.Where(n => n.Type == severity);
            }

            // Text filter
            var query = SearchBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(query))
            {
                src = src.Where(n =>
                    (n.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (n.Message?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false));
            }

            var items = src.ToList();
            if (HistoryGrid != null) HistoryGrid.ItemsSource = items;
            if (CountText != null) CountText.Text = $"({items.Count} entries)";
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

        private void MarkAllRead_Click(object sender, RoutedEventArgs e)
        {
            ToastNotificationService.Instance.MarkAllRead();
            ToastNotificationService.Instance.ShowSuccess("All notifications marked as read.");
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Clear all notification history?\nThis cannot be undone.",
                "Clear History", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                ToastNotificationService.Instance.ClearHistory();
                Refresh();
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ToastNotificationService.Instance.ShowError($"Could not open folder: {ex.Message}");
            }
        }

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => Refresh();
        private void Search_TextChanged(object sender, TextChangedEventArgs e) => Refresh();
    }
}
