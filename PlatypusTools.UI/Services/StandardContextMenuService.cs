using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// IDEA-011: Provides standardized context menus for DataGrids across the application.
    /// Ensures every DataGrid has consistent Copy, Open, Open Folder, Properties options.
    /// </summary>
    public static class StandardContextMenuService
    {
        /// <summary>
        /// Creates a standard context menu for a DataGrid that works with file-path items.
        /// Expects items that have a common file path property (Path, FilePath, FullPath, FileName).
        /// </summary>
        /// <param name="pathPropertyName">The name of the property containing the file path.</param>
        /// <param name="additionalItems">Extra MenuItems to prepend before standard items.</param>
        public static ContextMenu CreateFileContextMenu(string pathPropertyName = "Path", MenuItem[]? additionalItems = null)
        {
            var menu = new ContextMenu();

            // Add custom items first
            if (additionalItems != null)
            {
                foreach (var item in additionalItems)
                    menu.Items.Add(item);
                menu.Items.Add(new Separator());
            }

            // Copy Path
            var copyPath = new MenuItem { Header = "Copy Path", InputGestureText = "Ctrl+C" };
            copyPath.Click += (s, e) =>
            {
                var path = GetPathFromContext(menu, pathPropertyName);
                if (!string.IsNullOrEmpty(path))
                    Clipboard.SetText(path);
            };
            menu.Items.Add(copyPath);

            // Copy File Name
            var copyName = new MenuItem { Header = "Copy File Name" };
            copyName.Click += (s, e) =>
            {
                var path = GetPathFromContext(menu, pathPropertyName);
                if (!string.IsNullOrEmpty(path))
                    Clipboard.SetText(System.IO.Path.GetFileName(path));
            };
            menu.Items.Add(copyName);

            menu.Items.Add(new Separator());

            // Open File
            var openFile = new MenuItem { Header = "Open File", InputGestureText = "Enter" };
            openFile.Click += (s, e) =>
            {
                var path = GetPathFromContext(menu, pathPropertyName);
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                    catch { /* ignore */ }
                }
            };
            menu.Items.Add(openFile);

            // Open Containing Folder
            var openFolder = new MenuItem { Header = "Open Containing Folder" };
            openFolder.Click += (s, e) =>
            {
                var path = GetPathFromContext(menu, pathPropertyName);
                if (!string.IsNullOrEmpty(path))
                {
                    var dir = File.Exists(path) ? System.IO.Path.GetDirectoryName(path) : 
                              Directory.Exists(path) ? path : null;
                    if (dir != null)
                    {
                        try
                        {
                            if (File.Exists(path))
                                Process.Start("explorer.exe", $"/select,\"{path}\"");
                            else
                                Process.Start("explorer.exe", $"\"{dir}\"");
                        }
                        catch { /* ignore */ }
                    }
                }
            };
            menu.Items.Add(openFolder);

            menu.Items.Add(new Separator());

            // Properties (Shell)
            var properties = new MenuItem { Header = "Properties" };
            properties.Click += (s, e) =>
            {
                var path = GetPathFromContext(menu, pathPropertyName);
                if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                {
                    try { ShowFileProperties(path); }
                    catch { /* ignore */ }
                }
            };
            menu.Items.Add(properties);

            return menu;
        }

        /// <summary>
        /// Creates a simple text context menu with Copy/Select All.
        /// </summary>
        public static ContextMenu CreateTextContextMenu()
        {
            var menu = new ContextMenu();

            var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C" };
            copy.Click += (s, e) =>
            {
                if (menu.PlacementTarget is DataGrid dg && dg.SelectedItem != null)
                {
                    try { Clipboard.SetText(dg.SelectedItem.ToString() ?? ""); }
                    catch { /* ignore */ }
                }
            };
            menu.Items.Add(copy);

            var selectAll = new MenuItem { Header = "Select All", InputGestureText = "Ctrl+A" };
            selectAll.Click += (s, e) =>
            {
                if (menu.PlacementTarget is DataGrid dg)
                    dg.SelectAll();
            };
            menu.Items.Add(selectAll);

            return menu;
        }

        /// <summary>
        /// Applies a standard file context menu to an existing DataGrid.
        /// </summary>
        public static void ApplyTo(DataGrid dataGrid, string pathPropertyName = "Path", MenuItem[]? additionalItems = null)
        {
            dataGrid.ContextMenu = CreateFileContextMenu(pathPropertyName, additionalItems);
        }

        private static string? GetPathFromContext(ContextMenu menu, string propertyName)
        {
            if (menu.PlacementTarget is DataGrid dg && dg.SelectedItem != null)
            {
                var prop = dg.SelectedItem.GetType().GetProperty(propertyName);
                return prop?.GetValue(dg.SelectedItem)?.ToString();
            }
            return null;
        }

        /// <summary>
        /// Shows Windows file properties dialog for a given path.
        /// </summary>
        private static void ShowFileProperties(string path)
        {
            var info = new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true
            };
            Process.Start(info);
        }
    }
}
