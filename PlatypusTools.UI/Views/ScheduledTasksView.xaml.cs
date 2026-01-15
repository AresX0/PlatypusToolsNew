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
        
        private void RefreshUI(ViewModels.ScheduledTasksViewModel vm)
        {
            TotalTasksText.Text = vm.TotalTasks.ToString();
            EnabledTasksText.Text = vm.EnabledTasks.ToString();
            TasksDataGrid.ItemsSource = null;
            TasksDataGrid.ItemsSource = vm.Tasks;
        }
    }
}
