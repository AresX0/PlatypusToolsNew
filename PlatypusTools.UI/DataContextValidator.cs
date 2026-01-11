using System.Windows;
using System.Text;

namespace PlatypusTools.UI
{
    public static class DataContextValidator
    {
        public static string ValidateMainWindow(MainWindow window)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== DataContext Validation ===\n");
            
            // Check MainWindow DataContext
            sb.AppendLine($"MainWindow.DataContext type: {window.DataContext?.GetType().Name ?? "NULL"}");
            
            if (window.DataContext is ViewModels.MainWindowViewModel vm)
            {
                sb.AppendLine($"  FileCleaner: {vm.FileCleaner?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  VideoConverter: {vm.VideoConverter?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  VideoCombiner: {vm.VideoCombiner?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  ImageConverter: {vm.ImageConverter?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  Upscaler: {vm.Upscaler?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  Duplicates: {vm.Duplicates?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  Hider: {vm.Hider?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  Recent: {vm.Recent?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  DiskCleanup: {vm.DiskCleanup?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  PrivacyCleaner: {vm.PrivacyCleaner?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  MetadataEditor: {vm.MetadataEditor?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  SystemAudit: {vm.SystemAudit?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  StartupManager: {vm.StartupManager?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  ImageResizer: {vm.ImageResizer?.GetType().Name ?? "NULL"}");
                sb.AppendLine($"  IconConverter: {vm.IconConverter?.GetType().Name ?? "NULL"}");
            }
            else
            {
                sb.AppendLine("ERROR: MainWindow.DataContext is not MainWindowViewModel!");
            }
            
            // Find TabControl and check selected tab
            var tabControl = FindVisualChild<System.Windows.Controls.TabControl>(window);
            if (tabControl != null)
            {
                sb.AppendLine($"\nTabControl found. Selected Index: {tabControl.SelectedIndex}");
                if (tabControl.SelectedItem is System.Windows.Controls.TabItem selectedTab)
                {
                    sb.AppendLine($"Selected Tab Header: {selectedTab.Header}");
                    sb.AppendLine($"Selected Tab Content type: {selectedTab.Content?.GetType().Name ?? "NULL"}");
                    
                    if (selectedTab.Content is FrameworkElement content)
                    {
                        sb.AppendLine($"Content.DataContext type: {content.DataContext?.GetType().Name ?? "NULL"}");
                    }
                }
            }
            else
            {
                sb.AppendLine("\nERROR: TabControl not found!");
            }
            
            return sb.ToString();
        }
        
        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T result)
                    return result;
                    
                var childResult = FindVisualChild<T>(child);
                if (childResult != null)
                    return childResult;
            }
            return null;
        }
    }
}
