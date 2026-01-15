using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class WebsiteDownloaderView : UserControl
    {
        public WebsiteDownloaderView()
        {
            InitializeComponent();
        }

        private async void OnScanClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.WebsiteDownloaderViewModel vm)
            {
                // Ensure URL is set from TextBox
                vm.Url = UrlTextBox.Text;
                
                if (string.IsNullOrWhiteSpace(vm.Url))
                {
                    MessageBox.Show("Please enter a website URL.", "Website Downloader", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await vm.ScanUrlAsync();
                RefreshUI();
            }
        }

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.WebsiteDownloaderViewModel vm)
            {
                vm.BrowseOutput();
                OutputDirectoryTextBox.Text = vm.OutputDirectory;
            }
        }

        private async void OnDownloadClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.WebsiteDownloaderViewModel vm)
            {
                await vm.StartDownloadAsync();
                RefreshUI();
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.WebsiteDownloaderViewModel vm)
            {
                vm.Cancel();
            }
        }

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.WebsiteDownloaderViewModel vm)
            {
                vm.SelectAll();
                RefreshUI();
            }
        }

        private void OnSelectNoneClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.WebsiteDownloaderViewModel vm)
            {
                vm.SelectNone();
                RefreshUI();
            }
        }

        private void RefreshUI()
        {
            if (DataContext is ViewModels.WebsiteDownloaderViewModel vm)
            {
                DownloadItemsDataGrid.ItemsSource = null;
                DownloadItemsDataGrid.ItemsSource = vm.DownloadItems;
                ItemCountText.Text = vm.DownloadItems.Count.ToString();
            }
        }
    }
}
