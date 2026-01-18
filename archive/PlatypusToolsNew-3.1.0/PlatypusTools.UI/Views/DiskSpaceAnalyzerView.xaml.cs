using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class DiskSpaceAnalyzerView : UserControl
    {
        public DiskSpaceAnalyzerView()
        {
            InitializeComponent();
        }
        
        private void OnBrowseClick(object sender, System.Windows.RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select root path to analyze"
            };
            
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                RootPathTextBox.Text = dialog.SelectedPath;
                if (DataContext is ViewModels.DiskSpaceAnalyzerViewModel vm)
                {
                    vm.RootPath = dialog.SelectedPath;
                }
            }
        }
        
        private async void OnAnalyzeClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is ViewModels.DiskSpaceAnalyzerViewModel vm)
            {
                vm.RootPath = RootPathTextBox.Text;
                
                if (string.IsNullOrWhiteSpace(vm.RootPath))
                {
                    System.Windows.MessageBox.Show("Please select a root path first.", "Disk Space Analyzer", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }
                
                await vm.AnalyzeAsync();
                
                // Force UI update
                TotalSizeDisplayText.Text = vm.TotalSizeDisplay;
                DirectoryTreeView.ItemsSource = null;
                DirectoryTreeView.ItemsSource = vm.DirectoryTree;
            }
        }
    }
}
