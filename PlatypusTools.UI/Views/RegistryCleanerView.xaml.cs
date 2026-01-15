using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class RegistryCleanerView : UserControl
    {
        public RegistryCleanerView()
        {
            InitializeComponent();
            Loaded += RegistryCleanerView_Loaded;
        }

        private void RegistryCleanerView_Loaded(object sender, RoutedEventArgs e)
        {
            this.UpdateLayout();
            
            // Make all columns auto-size first to establish proper geometry
            if (IssuesDataGrid != null)
            {
                IssuesDataGrid.UpdateLayout();
                foreach (var column in IssuesDataGrid.Columns)
                {
                    column.Width = double.NaN; // Auto-size
                }
                IssuesDataGrid.UpdateLayout();
            }
        }

        private async void OnScanClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RegistryCleanerViewModel vm)
            {
                await vm.ScanAsync();
                RefreshUI();
            }
        }

        private async void OnFixSelectedClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RegistryCleanerViewModel vm)
            {
                await vm.FixSelectedAsync();
                RefreshUI();
            }
        }

        private async void OnFixAllClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RegistryCleanerViewModel vm)
            {
                await vm.FixAllAsync();
                RefreshUI();
            }
        }

        private async void OnBackupClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RegistryCleanerViewModel vm)
            {
                await vm.BackupAsync();
            }
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RegistryCleanerViewModel vm)
            {
                vm.SelectAll();
                RefreshUI();
            }
        }

        private void OnDeselectAllClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.RegistryCleanerViewModel vm)
            {
                vm.DeselectAll();
                RefreshUI();
            }
        }

        private void RefreshUI()
        {
            if (DataContext is ViewModels.RegistryCleanerViewModel vm)
            {
                IssuesDataGrid.ItemsSource = null;
                IssuesDataGrid.ItemsSource = vm.Issues;
                TotalIssuesText.Text = $"Issues: {vm.TotalIssues}";
                FixedIssuesText.Text = $"Fixed: {vm.FixedIssues}";
            }
        }
    }
}
