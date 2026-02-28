using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class MediaHubView : UserControl
    {
        public MediaHubView()
        {
            InitializeComponent();
            Loaded += MediaHubView_Loaded;
            Unloaded += MediaHubView_Unloaded;
        }

        private void MediaHubView_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MediaHubViewModel vm)
            {
                vm.PlayFileRequested += OnPlayFileRequested;
            }
        }

        private void MediaHubView_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MediaHubViewModel vm)
            {
                vm.PlayFileRequested -= OnPlayFileRequested;
            }
        }

        private void OnPlayFileRequested(string filePath)
        {
            _ = PlayInVideoPlayerAsync(filePath);
        }

        private void ContinueWatching_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is ContinueWatchingItem item)
            {
                _ = PlayInVideoPlayerAsync(item.FilePath);
            }
        }

        private void Series_DoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid && grid.SelectedItem is TvSeriesDisplayItem series)
            {
                var vm = DataContext as MediaHubViewModel;
                vm?.DrillIntoSeriesCommand.Execute(series);
            }
        }

        /// <summary>
        /// Navigate to the in-app Video Player and play the file there,
        /// using the same approach as MediaLibraryView.
        /// </summary>
        public async Task PlayInVideoPlayerAsync(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return;

            // Navigate to Multimedia (2) > Video (3) > Video Player (0)
            NavigateToTab(2, 3, 0);

            // Wait for LazyTabContent to create the NativeVideoPlayerView
            var mainWindow = Window.GetWindow(this);
            if (mainWindow == null) return;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                await Task.Delay(150);
                var videoView = FindVisualDescendant<NativeVideoPlayerView>(mainWindow);
                if (videoView != null)
                {
                    // PlayFromMediaLibrary handles the case where LibVLC is still
                    // initializing by storing the file as pending, so we can call
                    // it immediately without waiting for full init.
                    videoView.PlayFromMediaLibrary(filePath);
                    return;
                }
            }
        }

        /// <summary>
        /// Navigate to a nested tab by selecting TabItems at each level.
        /// </summary>
        private void NavigateToTab(params int[] indices)
        {
            var mainWindow = Window.GetWindow(this);
            if (mainWindow == null) return;

            TabControl? current = FindLogicalDescendant<TabControl>(mainWindow);
            foreach (int idx in indices)
            {
                if (current == null || idx >= current.Items.Count) return;
                current.SelectedIndex = idx;
                var tabItem = current.Items[idx] as TabItem;
                if (tabItem == null) return;
                current = FindLogicalDescendant<TabControl>(tabItem);
            }
        }

        private static T? FindLogicalDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            foreach (var child in LogicalTreeHelper.GetChildren(parent))
            {
                if (child is T found) return found;
                if (child is DependencyObject depChild)
                {
                    var result = FindLogicalDescendant<T>(depChild);
                    if (result != null) return result;
                }
            }
            return null;
        }

        private static T? FindVisualDescendant<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindVisualDescendant<T>(child);
                if (result != null) return result;
            }
            return null;
        }
    }
}
