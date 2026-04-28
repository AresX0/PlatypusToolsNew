using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PlatypusTools.UI.Services.Diagnostics;

namespace PlatypusTools.UI.Views
{
    public partial class ActivityLogWindow : Window
    {
        private readonly CollectionViewSource _view = new();

        public ActivityLogWindow()
        {
            InitializeComponent();
            _view.Source = ActivityLogService.Instance.Recent;
            _view.Filter += View_Filter;
            LogGrid.ItemsSource = _view.View;
            ActivityLogService.Instance.EntryAdded += OnEntryAdded;
            Closed += (_, _) => ActivityLogService.Instance.EntryAdded -= OnEntryAdded;
            UpdateStatus();
        }

        private void OnEntryAdded(object? sender, ActivityLogService.Entry e)
        {
            // Recent is observable; just keep auto-scroll behaviour and stats.
            Dispatcher.BeginInvoke(new Action(() =>
            {
                UpdateStatus();
                if (AutoScrollBox.IsChecked == true && LogGrid.Items.Count > 0)
                {
                    var last = LogGrid.Items[^1];
                    if (last != null) LogGrid.ScrollIntoView(last);
                }
            }));
        }

        private void View_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is not ActivityLogService.Entry entry) { e.Accepted = false; return; }
            var minLevel = (LevelBox?.SelectedIndex ?? 0) - 1; // 0 = All -> -1
            if (minLevel >= 0 && (int)entry.Level < minLevel) { e.Accepted = false; return; }
            var f = (FilterBox?.Text ?? "").Trim();
            if (string.IsNullOrEmpty(f)) { e.Accepted = true; return; }
            e.Accepted = (entry.Category?.IndexOf(f, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                      || (entry.Message?.IndexOf(f, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0;
        }

        private void Filter_Changed(object sender, EventArgs e)
        {
            try { _view.View?.Refresh(); UpdateStatus(); } catch { }
        }

        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var fromDisk = ActivityLogService.Instance.ReadFromDisk(2000).ToList();
                ActivityLogService.Instance.Recent.Clear();
                foreach (var entry in fromDisk) ActivityLogService.Instance.Recent.Add(entry);
                UpdateStatus();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to read log: {ex.Message}", "Activity Log", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(ActivityLogService.Instance.LogFilePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
            catch { }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            ActivityLogService.Instance.Clear();
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            try
            {
                var total = ActivityLogService.Instance.Recent.Count;
                StatusText.Text = $"{total} entries in memory · log file: {ActivityLogService.Instance.LogFilePath}";
            }
            catch { }
        }
    }
}
