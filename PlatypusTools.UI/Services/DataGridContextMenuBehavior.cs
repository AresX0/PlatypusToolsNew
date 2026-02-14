using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// IDEA-011: Attached behavior that automatically applies a standard context menu
    /// to DataGrids that don't already have one. Applied globally via App.xaml style.
    /// </summary>
    public static class DataGridContextMenuBehavior
    {
        public static readonly DependencyProperty AutoContextMenuProperty =
            DependencyProperty.RegisterAttached(
                "AutoContextMenu",
                typeof(bool),
                typeof(DataGridContextMenuBehavior),
                new PropertyMetadata(false, OnAutoContextMenuChanged));

        public static bool GetAutoContextMenu(DependencyObject obj) => (bool)obj.GetValue(AutoContextMenuProperty);
        public static void SetAutoContextMenu(DependencyObject obj, bool value) => obj.SetValue(AutoContextMenuProperty, value);

        /// <summary>
        /// Optional: specify which property contains the file path. Defaults to auto-detect.
        /// </summary>
        public static readonly DependencyProperty PathPropertyProperty =
            DependencyProperty.RegisterAttached(
                "PathProperty",
                typeof(string),
                typeof(DataGridContextMenuBehavior),
                new PropertyMetadata(null));

        public static string? GetPathProperty(DependencyObject obj) => (string?)obj.GetValue(PathPropertyProperty);
        public static void SetPathProperty(DependencyObject obj, string? value) => obj.SetValue(PathPropertyProperty, value);

        private static void OnAutoContextMenuChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is DataGrid dg && (bool)e.NewValue)
            {
                dg.Loaded += DataGrid_Loaded;
            }
        }

        private static void DataGrid_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is DataGrid dg && dg.ContextMenu == null)
            {
                // Auto-detect path property from item type
                var pathProp = GetPathProperty(dg);
                if (string.IsNullOrEmpty(pathProp))
                    pathProp = DetectPathProperty(dg);

                if (!string.IsNullOrEmpty(pathProp))
                {
                    dg.ContextMenu = StandardContextMenuService.CreateFileContextMenu(pathProp);
                }
                else
                {
                    dg.ContextMenu = StandardContextMenuService.CreateTextContextMenu();
                }
            }
        }

        /// <summary>
        /// Tries to detect the file path property by inspecting column bindings or item type.
        /// </summary>
        private static string? DetectPathProperty(DataGrid dg)
        {
            // Check if items have common path properties
            if (dg.Items.Count > 0 && dg.Items[0] != null)
            {
                var type = dg.Items[0].GetType();
                var pathNames = new[] { "Path", "FilePath", "FullPath", "FileName", "SourcePath", "OutputPath", "KeyPath", "DumpPath", "FolderPath" };
                foreach (var name in pathNames)
                {
                    if (type.GetProperty(name) != null)
                        return name;
                }
            }

            // Fallback: check column bindings
            foreach (var col in dg.Columns)
            {
                if (col is DataGridTextColumn tc && tc.Binding is System.Windows.Data.Binding b)
                {
                    var path = b.Path?.Path;
                    if (path != null && (path.Contains("Path") || path.Contains("File") || path.Contains("Folder")))
                        return path;
                }
            }

            return null;
        }
    }
}
