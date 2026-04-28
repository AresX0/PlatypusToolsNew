using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class ScheduledTasksView : UserControl
    {
        public ScheduledTasksView()
        {
            InitializeComponent();
            Loaded += ScheduledTasksView_Loaded;
        }

        private async void ScheduledTasksView_Loaded(object sender, RoutedEventArgs e)
        {
            this.UpdateLayout();
            if (DataContext is ViewModels.ScheduledTasksViewModel vm)
            {
                await vm.RefreshAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScheduledTasksViewModel vm)
            {
                await vm.RefreshAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnEnableClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScheduledTasksViewModel vm)
            {
                await vm.EnableTaskAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnDisableClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScheduledTasksViewModel vm)
            {
                await vm.DisableTaskAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnRunClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScheduledTasksViewModel vm)
            {
                await vm.RunTaskAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScheduledTasksViewModel vm)
            {
                await vm.DeleteTaskAsync();
                RefreshUI(vm);
            }
        }
        
        private void OnCreateClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ScheduledTasksViewModel vm)
            {
                vm.CreateCommand.Execute(null);
            }
        }

        /// <summary>Phase 2.4 — pop a context menu of built-in scheduled job templates.</summary>
        private void OnTemplatesClick(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            if (btn == null) return;

            var menu = new ContextMenu();
            foreach (var t in Services.Scheduling.ScheduledJobTemplates.All)
            {
                var item = new MenuItem { Header = $"{t.DisplayName} — {t.Description}", Tag = t };
                item.Click += (_, _) => SaveTemplate(t);
                menu.Items.Add(item);
            }
            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }

        private void SaveTemplate(Services.Scheduling.ScheduledJobTemplate t)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Title = $"Save '{t.DisplayName}' template",
                Filter = "Task Scheduler XML (*.xml)|*.xml|All files (*.*)|*.*",
                FileName = t.SuggestedFileName
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                System.IO.File.WriteAllText(dlg.FileName, t.TaskXml, System.Text.Encoding.Unicode);
                var result = MessageBox.Show(
                    $"Template saved to:\n{dlg.FileName}\n\n" +
                    "To register it run:\n" +
                    $"schtasks /create /xml \"{dlg.FileName}\" /tn \"{t.SuggestedTaskName}\"\n\n" +
                    "Open the containing folder?",
                    "Templates", MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (result == MessageBoxResult.Yes)
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{dlg.FileName}\"");
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Failed to save template: {ex.Message}",
                    "Templates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        
        private void RefreshUI(ViewModels.ScheduledTasksViewModel vm)
        {
            TotalTasksText.Text = vm.TotalTasks.ToString();
            EnabledTasksText.Text = vm.EnabledTasks.ToString();
            TasksDataGrid.ItemsSource = null;
            TasksDataGrid.ItemsSource = vm.Tasks;
        }
    }
}
