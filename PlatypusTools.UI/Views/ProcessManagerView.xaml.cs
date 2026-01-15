using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class ProcessManagerView : UserControl
    {
        public ProcessManagerView()
        {
            InitializeComponent();
            Loaded += ProcessManagerView_Loaded;
        }

        private async void ProcessManagerView_Loaded(object sender, RoutedEventArgs e)
        {
            this.UpdateLayout();
            
            // Auto-refresh on load
            if (DataContext is ViewModels.ProcessManagerViewModel vm)
            {
                await vm.RefreshAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ProcessManagerViewModel vm)
            {
                await vm.RefreshAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnKillProcessClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ProcessManagerViewModel vm)
            {
                var selectedProcesses = ProcessesDataGrid.SelectedItems
                    .Cast<ViewModels.ProcessInfoViewModel>()
                    .ToList();

                if (selectedProcesses.Count == 0)
                {
                    MessageBox.Show("Please select one or more processes to kill.", "Kill Process", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await vm.KillProcessesAsync(selectedProcesses);
                RefreshUI(vm);
            }
        }

        private void ProcessesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is ViewModels.ProcessManagerViewModel vm)
            {
                vm.SelectedProcesses.Clear();
                foreach (var item in ProcessesDataGrid.SelectedItems.Cast<ViewModels.ProcessInfoViewModel>())
                {
                    vm.SelectedProcesses.Add(item);
                }
            }
        }
        
        private void RefreshUI(ViewModels.ProcessManagerViewModel vm)
        {
            TotalProcessesText.Text = vm.TotalProcesses.ToString();
            ProcessesDataGrid.ItemsSource = null;
            ProcessesDataGrid.ItemsSource = vm.Processes;
        }
    }
}
