using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class WebsiteDownloaderView : UserControl
    {
        public WebsiteDownloaderView()
        {
            InitializeComponent();
        }

        private WebsiteDownloaderViewModel? VM => DataContext as WebsiteDownloaderViewModel;

        private async void OnScanClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;

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

        private void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            vm.BrowseOutput();
            OutputDirectoryTextBox.Text = vm.OutputDirectory;
        }

        private async void OnDownloadClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            await vm.StartDownloadAsync();
            RefreshUI();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e) => VM?.Cancel();

        private void OnSelectAllClick(object sender, RoutedEventArgs e)
        {
            VM?.SelectAll();
            RefreshUI();
        }

        private void OnSelectNoneClick(object sender, RoutedEventArgs e)
        {
            VM?.SelectNone();
            RefreshUI();
        }

        private void OnPauseResumeClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            if (vm.IsPaused)
            {
                vm.ResumeDownloads();
                PauseResumeButton.Content = "Pause";
            }
            else
            {
                vm.PauseDownloads();
                PauseResumeButton.Content = "Resume";
            }
        }

        private async void OnTestLinkClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            if (string.IsNullOrWhiteSpace(vm.Url))
            {
                MessageBox.Show("Enter a URL first.", "Test Link", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            await vm.TestLinkAsync(vm.Url);
        }

        private async void OnRetryFailedClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            var failedItems = vm.DownloadItems.Where(i => i.Status == "Failed").ToList();
            if (failedItems.Count == 0)
            {
                MessageBox.Show("No failed items to retry.", "Retry", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            foreach (var item in failedItems)
            {
                await vm.RetryItemAsync(item);
            }
        }

        private async void OnRetryItemClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            if (sender is Button btn && btn.DataContext is DownloadItemViewModel item)
            {
                await vm.RetryItemAsync(item);
            }
        }

        private async void OnTestItemLinkClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            if (sender is Button btn && btn.DataContext is DownloadItemViewModel item)
            {
                await vm.TestLinkAsync(item.Url);
            }
        }

        private void OnShowSkippedClick(object sender, RoutedEventArgs e) => VM?.ShowSkippedFiles();

        private void OnClearLogClick(object sender, RoutedEventArgs e)
        {
            if (VM is not { } vm) return;
            vm.LogText = string.Empty;
        }

        private void OnSaveProfileClick(object sender, RoutedEventArgs e) => VM?.SaveCurrentAsProfile();

        private void OnEditProfileClick(object sender, RoutedEventArgs e) => VM?.EditSelectedProfile();

        private void OnDeleteProfileClick(object sender, RoutedEventArgs e) => VM?.DeleteSelectedProfile();

        private void RefreshUI()
        {
            if (VM is not { } vm) return;
            DownloadItemsDataGrid.ItemsSource = null;
            DownloadItemsDataGrid.ItemsSource = vm.DownloadItems;
        }
    }
}
