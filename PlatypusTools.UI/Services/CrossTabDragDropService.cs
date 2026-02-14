using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// IDEA-001: Cross-tab drag-and-drop service.
    /// Enables dragging files between tabs (e.g., File Cleaner → Secure Wipe, Duplicates → Metadata Editor).
    /// Uses WPF drag-drop with a custom data format.
    /// </summary>
    public static class CrossTabDragDropService
    {
        /// <summary>
        /// Custom data format for PlatypusTools cross-tab file transfers.
        /// </summary>
        public const string PlatypusFileFormat = "PlatypusTools.Files";

        /// <summary>
        /// Event raised when files are dropped on a target tab.
        /// </summary>
        public static event EventHandler<CrossTabDropEventArgs>? FilesDropped;

        /// <summary>
        /// Registered drop targets (tab name → handler).
        /// </summary>
        private static readonly Dictionary<string, Action<string[]>> _dropTargets = new();

        /// <summary>
        /// Initiates a drag operation with the given file paths.
        /// Call this from DataGrid MouseMove or DragStart handlers.
        /// </summary>
        public static void StartDrag(DependencyObject source, IEnumerable<string> filePaths, string sourceTab)
        {
            var paths = filePaths.Where(p => !string.IsNullOrEmpty(p)).ToArray();
            if (paths.Length == 0) return;

            var data = new DataObject();
            data.SetData(PlatypusFileFormat, new CrossTabDragData
            {
                SourceTab = sourceTab,
                FilePaths = paths
            });
            // Also set standard file drop format for external app compatibility
            data.SetFileDropList(new System.Collections.Specialized.StringCollection());
            foreach (var p in paths)
            {
                var sc = data.GetFileDropList();
                sc.Add(p);
                data.SetFileDropList(sc);
            }

            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy | DragDropEffects.Move);
        }

        /// <summary>
        /// Registers a target tab to accept dropped files.
        /// </summary>
        public static void RegisterDropTarget(string tabName, Action<string[]> handler)
        {
            _dropTargets[tabName] = handler;
        }

        /// <summary>
        /// Unregisters a drop target.
        /// </summary>
        public static void UnregisterDropTarget(string tabName)
        {
            _dropTargets.Remove(tabName);
        }

        /// <summary>
        /// Handles a drop event. Call from UIElement.Drop handler.
        /// Returns true if the drop was handled.
        /// </summary>
        public static bool HandleDrop(DragEventArgs e, string targetTab)
        {
            string[]? paths = null;
            string? sourceTab = null;

            // Check for PlatypusTools internal format first
            if (e.Data.GetDataPresent(PlatypusFileFormat))
            {
                var dragData = e.Data.GetData(PlatypusFileFormat) as CrossTabDragData;
                if (dragData != null)
                {
                    paths = dragData.FilePaths;
                    sourceTab = dragData.SourceTab;
                }
            }
            // Fall back to standard file drop
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                sourceTab = "External";
            }

            if (paths == null || paths.Length == 0) return false;

            // Invoke registered handler
            if (_dropTargets.TryGetValue(targetTab, out var handler))
            {
                handler(paths);
            }

            // Raise event
            FilesDropped?.Invoke(null, new CrossTabDropEventArgs
            {
                SourceTab = sourceTab ?? "Unknown",
                TargetTab = targetTab,
                FilePaths = paths
            });

            var toast = ToastNotificationService.Instance;
            toast.ShowInfo($"{paths.Length} file(s) received from {sourceTab}.", $"Drop on {targetTab}");

            e.Handled = true;
            return true;
        }

        /// <summary>
        /// Helper to set up a UIElement as a drop target.
        /// Adds AllowDrop, DragOver, and Drop handlers.
        /// </summary>
        public static void MakeDropTarget(UIElement element, string tabName, Action<string[]>? customHandler = null)
        {
            element.AllowDrop = true;
            element.DragOver += (s, e) =>
            {
                if (e.Data.GetDataPresent(PlatypusFileFormat) || e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
                e.Handled = true;
            };
            element.Drop += (s, e) =>
            {
                if (customHandler != null)
                {
                    string[]? paths = null;
                    if (e.Data.GetDataPresent(PlatypusFileFormat))
                        paths = (e.Data.GetData(PlatypusFileFormat) as CrossTabDragData)?.FilePaths;
                    else if (e.Data.GetDataPresent(DataFormats.FileDrop))
                        paths = e.Data.GetData(DataFormats.FileDrop) as string[];

                    if (paths != null && paths.Length > 0)
                        customHandler(paths);
                    e.Handled = true;
                }
                else
                {
                    HandleDrop(e, tabName);
                }
            };

            if (customHandler != null)
                RegisterDropTarget(tabName, customHandler);
        }
    }

    /// <summary>
    /// Internal data carried during a cross-tab drag operation.
    /// </summary>
    [Serializable]
    public class CrossTabDragData
    {
        public string SourceTab { get; set; } = string.Empty;
        public string[] FilePaths { get; set; } = Array.Empty<string>();
    }

    /// <summary>
    /// Event args for cross-tab file drops.
    /// </summary>
    public class CrossTabDropEventArgs : EventArgs
    {
        public string SourceTab { get; set; } = string.Empty;
        public string TargetTab { get; set; } = string.Empty;
        public string[] FilePaths { get; set; } = Array.Empty<string>();
    }
}
