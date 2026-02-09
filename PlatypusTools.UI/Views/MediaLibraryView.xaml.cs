using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class MediaLibraryView : UserControl
    {
        private static readonly string[] AudioExtensions = { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma" };
        private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".vob" };

        public MediaLibraryView()
        {
            InitializeComponent();
            Loaded += MediaLibraryView_Loaded;
        }

        private void MediaLibraryView_Loaded(object sender, RoutedEventArgs e)
        {
            // Force layout measurement so column resizers are interactive even when empty
            this.UpdateLayout();
            
            // Make all columns auto-size first to establish proper geometry
            if (this.FindName("DataGrid") is DataGrid grid)
            {
                grid.UpdateLayout();
                foreach (var column in grid.Columns)
                {
                    column.Width = double.NaN; // Auto-size
                }
                grid.UpdateLayout();
            }
        }

        private MediaItemViewModel? GetSelectedItem(object sender)
        {
            if (sender is MenuItem menuItem && 
                menuItem.Parent is ContextMenu ctx && 
                ctx.PlacementTarget is DataGrid dg &&
                dg.SelectedItem is MediaItemViewModel item)
            {
                return item;
            }
            return null;
        }

        private MainWindowViewModel? GetMainViewModel()
        {
            var mainWindow = Window.GetWindow(this);
            return mainWindow?.DataContext as MainWindowViewModel;
        }

        /// <summary>
        /// Navigate to a nested tab by selecting TabItems at each level.
        /// Uses LogicalTreeHelper which can find XAML-defined TabControls even
        /// inside unrendered TabItems (unlike VisualTreeHelper which requires
        /// the content to be rendered first).
        /// Tab hierarchy: MainTabControl > Multimedia (1) > Audio (0) / Video (2)
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

        /// <summary>
        /// Finds the first descendant of type T in the logical tree.
        /// The logical tree includes XAML-defined children even when they
        /// haven't been rendered yet (unlike the visual tree).
        /// </summary>
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

        /// <summary>
        /// Finds the first descendant of type T in the visual tree.
        /// Used after tab navigation to find rendered view instances.
        /// </summary>
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

        private void OpenFileLocation_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem(sender);
            if (item == null || string.IsNullOrEmpty(item.FilePath)) return;

            try
            {
                if (File.Exists(item.FilePath))
                {
                    Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
                }
                else
                {
                    var dir = Path.GetDirectoryName(item.FilePath);
                    if (dir != null && Directory.Exists(dir))
                        Process.Start("explorer.exe", dir);
                }
            }
            catch { }
        }

        private async void PlayInAudioPlayer_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem(sender);
            if (item == null || string.IsNullOrEmpty(item.FilePath)) return;

            var mainVm = GetMainViewModel();
            if (mainVm == null) return;

            // Navigate to Multimedia > Audio > Audio Player
            NavigateToTab(1, 0, 0);

            await mainVm.EnhancedAudioPlayer.PlayFileAsync(item.FilePath);
        }

        private async void PlayInVideoPlayer_Click(object sender, RoutedEventArgs e)
        {
            var item = GetSelectedItem(sender);
            if (item == null || string.IsNullOrEmpty(item.FilePath)) return;

            // Navigate to Multimedia > Video > Video Player
            NavigateToTab(1, 2, 0);

            // Wait for LazyTabContent to load the video player view.
            // Poll with small delays since the view is created asynchronously.
            var mainWindow = Window.GetWindow(this);
            if (mainWindow == null) return;

            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(100);
                var videoView = FindVisualDescendant<NativeVideoPlayerView>(mainWindow);
                if (videoView != null)
                {
                    videoView.PlayFromMediaLibrary(item.FilePath);
                    return;
                }
            }
        }
    }
}
