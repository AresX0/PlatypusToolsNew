using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for creating consistent context menus
    /// </summary>
    public sealed class ContextMenuService
    {
        private static readonly Lazy<ContextMenuService> _instance = new(() => new ContextMenuService());
        public static ContextMenuService Instance => _instance.Value;

        private ContextMenuService() { }

        /// <summary>
        /// Creates a context menu for file operations
        /// </summary>
        public ContextMenu CreateFileContextMenu(string filePath, Action? onRefresh = null)
        {
            var menu = new ContextMenu();

            // Open
            AddMenuItem(menu, "Open", "📂", () => OpenFile(filePath));
            AddMenuItem(menu, "Open Containing Folder", "📁", () => OpenContainingFolder(filePath));
            AddSeparator(menu);

            // Edit
            AddMenuItem(menu, "Copy", "📋", () => CopyToClipboard(filePath), "Ctrl+C");
            AddMenuItem(menu, "Copy Path", "📝", () => CopyPathToClipboard(filePath));
            AddMenuItem(menu, "Rename", "✏️", null, "F2");
            AddMenuItem(menu, "Delete", "🗑️", null, "Del");
            AddSeparator(menu);

            // Properties
            AddMenuItem(menu, "Properties", "ℹ️", () => ShowProperties(filePath));
            
            if (onRefresh != null)
            {
                AddSeparator(menu);
                AddMenuItem(menu, "Refresh", "🔄", onRefresh, "F5");
            }

            return menu;
        }

        /// <summary>
        /// Creates a context menu for folder operations
        /// </summary>
        public ContextMenu CreateFolderContextMenu(string folderPath, Action? onRefresh = null)
        {
            var menu = new ContextMenu();

            AddMenuItem(menu, "Open in Explorer", "📁", () => OpenFolder(folderPath));
            AddMenuItem(menu, "Open in Terminal", "💻", () => OpenInTerminal(folderPath));
            AddSeparator(menu);

            AddMenuItem(menu, "Copy Path", "📝", () => CopyPathToClipboard(folderPath));
            AddMenuItem(menu, "New Folder", "📂", null);
            AddSeparator(menu);

            AddMenuItem(menu, "Properties", "ℹ️", () => ShowFolderProperties(folderPath));
            
            if (onRefresh != null)
            {
                AddSeparator(menu);
                AddMenuItem(menu, "Refresh", "🔄", onRefresh, "F5");
            }

            return menu;
        }

        /// <summary>
        /// Creates a context menu for multiple selected files
        /// </summary>
        public ContextMenu CreateMultiFileContextMenu(IEnumerable<string> filePaths, Action? onRefresh = null)
        {
            var menu = new ContextMenu();
            var paths = new List<string>(filePaths);

            AddMenuItem(menu, $"Open {paths.Count} Files", "📂", () =>
            {
                foreach (var path in paths)
                    OpenFile(path);
            });
            AddSeparator(menu);

            AddMenuItem(menu, "Copy All", "📋", () =>
            {
                var files = new System.Collections.Specialized.StringCollection();
                files.AddRange(paths.ToArray());
                Clipboard.SetFileDropList(files);
            });
            AddMenuItem(menu, "Delete All", "🗑️", null);
            AddSeparator(menu);

            AddMenuItem(menu, "Select All", "☑️", null, "Ctrl+A");
            AddMenuItem(menu, "Invert Selection", "🔄", null);
            
            if (onRefresh != null)
            {
                AddSeparator(menu);
                AddMenuItem(menu, "Refresh", "🔄", onRefresh, "F5");
            }

            return menu;
        }

        private void AddMenuItem(ContextMenu menu, string header, string? icon, Action? action, string? shortcut = null)
        {
            var item = new MenuItem
            {
                Header = icon != null ? $"{icon}  {header}" : header,
                InputGestureText = shortcut,
                IsEnabled = action != null
            };
            
            if (action != null)
                item.Click += (s, e) => action();

            menu.Items.Add(item);
        }

        private void AddSeparator(ContextMenu menu)
        {
            menu.Items.Add(new Separator());
        }

        #region File Operations

        private void OpenFile(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenContainingFolder(string path)
        {
            try
            {
                var folder = Path.GetDirectoryName(path);
                if (folder != null)
                {
                    Process.Start("explorer.exe", $"/select,\"{path}\"");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                Process.Start("explorer.exe", path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpenInTerminal(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    WorkingDirectory = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open terminal: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyToClipboard(string path)
        {
            try
            {
                var files = new System.Collections.Specialized.StringCollection { path };
                Clipboard.SetFileDropList(files);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy to clipboard: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyPathToClipboard(string path)
        {
            try
            {
                Clipboard.SetText(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy path: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowProperties(string path)
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true
                };
                Process.Start(info);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not show properties: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ShowFolderProperties(string path)
        {
            ShowProperties(path);
        }

        #endregion
    }
}
