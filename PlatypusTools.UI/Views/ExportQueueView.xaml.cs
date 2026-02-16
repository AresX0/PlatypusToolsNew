using System.Windows;
using System.Windows.Controls;
using PlatypusTools.Core.Services.Video;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Export Queue with batch rendering and scheduling support.
    /// </summary>
    public partial class ExportQueueView : UserControl
    {
        private ExportQueueViewModel ViewModel => (ExportQueueViewModel)DataContext;

        public ExportQueueView()
        {
            InitializeComponent();
        }

        private void StartProcessing_Click(object sender, RoutedEventArgs e) => ViewModel.StartProcessing();
        private void StopProcessing_Click(object sender, RoutedEventArgs e) => ViewModel.StopProcessing();
        private void ClearFinished_Click(object sender, RoutedEventArgs e) => ViewModel.ClearFinished();

        private void AddFiles_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.webm;*.wmv;*.flv|All Files|*.*",
                Multiselect = true,
                Title = "Select video files to export"
            };

            if (dlg.ShowDialog() == true)
            {
                foreach (var file in dlg.FileNames)
                {
                    SourcePathBox.Text = file;
                    var ext = System.IO.Path.GetExtension(file);
                    var name = System.IO.Path.GetFileNameWithoutExtension(file);
                    OutputPathBox.Text = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(file) ?? "",
                        $"{name}_export.mp4");
                }
            }
        }

        private void ScheduleExport_Click(object sender, RoutedEventArgs e)
        {
            if (System.DateTime.TryParse(ScheduleTimeBox.Text, out var scheduledTime))
            {
                ViewModel.ScheduledTime = scheduledTime;
                ViewModel.StartProcessing();
            }
            else
            {
                MessageBox.Show("Invalid date/time format. Use: yyyy-MM-dd HH:mm", "Schedule Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var source = SourcePathBox.Text?.Trim();
            var output = OutputPathBox.Text?.Trim();

            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(output))
            {
                MessageBox.Show("Source and output paths are required.", "Add Job",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var preset = PresetCombo.SelectedItem as ExportPreset;
            var priorityIndex = PriorityCombo.SelectedIndex;
            var priority = priorityIndex switch
            {
                0 => ExportPriority.Low,
                2 => ExportPriority.High,
                3 => ExportPriority.Critical,
                _ => ExportPriority.Normal
            };

            ExportQueueService.Instance.AddJob(source, output, preset, priority);
            ViewModel.RefreshCounts();
            ViewModel.StatusMessage = $"Added: {System.IO.Path.GetFileName(source)}";
        }

        private void CancelJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ExportJob job)
                ViewModel.CancelJob(job);
        }

        private void RemoveJob_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ExportJob job)
                ViewModel.RemoveJob(job);
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ExportJob job)
                ViewModel.MoveJobUp(job);
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is ExportJob job)
                ViewModel.MoveJobDown(job);
        }
    }
}
