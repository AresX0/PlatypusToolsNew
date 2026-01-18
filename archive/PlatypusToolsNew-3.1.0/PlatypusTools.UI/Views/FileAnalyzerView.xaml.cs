using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class FileAnalyzerView : UserControl
    {
        public FileAnalyzerView()
        {
            InitializeComponent();
        }
        
        private void OnBrowseClick(object sender, System.Windows.RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select directory to analyze"
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                DirectoryPathTextBox.Text = dialog.SelectedPath;
                if (DataContext is ViewModels.FileAnalyzerViewModel vm)
                {
                    vm.DirectoryPath = dialog.SelectedPath;
                }
            }
        }
        
        private async void OnAnalyzeClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FileAnalyzerViewModel vm)
            {
                vm.DirectoryPath = DirectoryPathTextBox.Text;
                
                if (string.IsNullOrWhiteSpace(vm.DirectoryPath))
                {
                    System.Windows.MessageBox.Show("Please select a directory first.", "File Analyzer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                try
                {
                    await vm.AnalyzeAsync();
                    RefreshAllGrids(vm);
                }
                catch (System.Exception ex)
                {
                    System.Windows.MessageBox.Show($"Error: {ex.Message}", "Error");
                }
            }
        }
        
        private void RefreshAllGrids(ViewModels.FileAnalyzerViewModel vm)
        {
            // Force UI update for all controls
            TotalFilesText.Text = vm.TotalFiles.ToString();
            TotalSizeText.Text = vm.TotalSizeFormatted;
            TotalDirsText.Text = vm.TotalDirectories.ToString();
            
            FileTypesDataGrid.ItemsSource = null;
            FileTypesDataGrid.ItemsSource = vm.FileTypeStats;
            
            LargestFilesDataGrid.ItemsSource = null;
            LargestFilesDataGrid.ItemsSource = vm.LargestFiles;
            
            OldestFilesDataGrid.ItemsSource = null;
            OldestFilesDataGrid.ItemsSource = vm.OldestFiles;
            
            NewestFilesDataGrid.ItemsSource = null;
            NewestFilesDataGrid.ItemsSource = vm.NewestFiles;
            
            AgeDistributionDataGrid.ItemsSource = null;
            AgeDistributionDataGrid.ItemsSource = vm.FilesByAge;
            
            DuplicatesDataGrid.ItemsSource = null;
            DuplicatesDataGrid.ItemsSource = vm.DuplicateGroups;
            
            DirectoryTreeView.ItemsSource = null;
            DirectoryTreeView.ItemsSource = vm.TreeNodes;
        }
        
        private async void OnFindDuplicatesClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FileAnalyzerViewModel vm)
            {
                vm.DirectoryPath = DirectoryPathTextBox.Text;
                
                if (string.IsNullOrWhiteSpace(vm.DirectoryPath))
                {
                    System.Windows.MessageBox.Show("Please select a directory first.", "File Analyzer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                await vm.FindDuplicatesAsync();
                DuplicatesDataGrid.ItemsSource = null;
                DuplicatesDataGrid.ItemsSource = vm.DuplicateGroups;
            }
        }
        
        private async void OnBuildTreeClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FileAnalyzerViewModel vm)
            {
                vm.DirectoryPath = DirectoryPathTextBox.Text;
                
                if (string.IsNullOrWhiteSpace(vm.DirectoryPath))
                {
                    System.Windows.MessageBox.Show("Please select a directory first.", "File Analyzer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                await vm.BuildTreeAsync();
                DirectoryTreeView.ItemsSource = null;
                DirectoryTreeView.ItemsSource = vm.TreeNodes;
            }
        }
        
        private void OnExportClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FileAnalyzerViewModel vm)
            {
                vm.ExportCommand.Execute(null);
            }
        }
        
        private void OnClearClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.FileAnalyzerViewModel vm)
            {
                vm.ClearCommand.Execute(null);
            }
        }
    }
}
