using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class DuplicatesView : UserControl
    {
        public DuplicatesView()
        {
            InitializeComponent();
        }

        private void OpenInFileCleaner_Click(object sender, RoutedEventArgs e)
        {
            var mainWindow = Window.GetWindow(this);
            if (mainWindow?.DataContext is not MainWindowViewModel mainVm) return;

            // Get the folder path from the DuplicatesViewModel
            var dupVm = DataContext as DuplicatesViewModel;
            var folderPath = dupVm?.FolderPath;

            if (string.IsNullOrEmpty(folderPath))
            {
                MessageBox.Show("No folder path set. Run a scan first.", "No Folder",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pre-load the folder path in File Cleaner
            mainVm.FileCleaner.FolderPath = folderPath;

            // Navigate to File Management (0) > File Cleaner (0)
            NavigateToTab(mainWindow, 0, 0);
        }

        /// <summary>
        /// Navigate to a nested tab using logical tree traversal.
        /// </summary>
        private static void NavigateToTab(DependencyObject root, params int[] indices)
        {
            TabControl? current = FindLogicalDescendant<TabControl>(root);
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
    }
}
