using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Centralized service for handling drag and drop operations across the application.
    /// </summary>
    public class DragDropService
    {
        private static DragDropService? _instance;
        public static DragDropService Instance => _instance ??= new DragDropService();

        public event EventHandler<DragDropEventArgs>? FilesDropped;
        public event EventHandler<DragDropEventArgs>? FoldersDropped;
        public event EventHandler<DragDropEventArgs>? ItemsDropped;

        /// <summary>
        /// Handles a drop event and extracts file/folder paths.
        /// </summary>
        public DragDropResult HandleDrop(DragEventArgs e)
        {
            var result = new DragDropResult();

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null)
                {
                    foreach (var path in paths)
                    {
                        if (Directory.Exists(path))
                        {
                            result.Folders.Add(path);
                        }
                        else if (File.Exists(path))
                        {
                            result.Files.Add(path);
                        }
                    }
                }
            }

            // Raise events
            if (result.Files.Count > 0)
            {
                FilesDropped?.Invoke(this, new DragDropEventArgs { Paths = result.Files });
            }

            if (result.Folders.Count > 0)
            {
                FoldersDropped?.Invoke(this, new DragDropEventArgs { Paths = result.Folders });
            }

            if (result.AllPaths.Count > 0)
            {
                ItemsDropped?.Invoke(this, new DragDropEventArgs { Paths = result.AllPaths });
            }

            return result;
        }

        /// <summary>
        /// Handles a drop event with file type filtering.
        /// </summary>
        public DragDropResult HandleDrop(DragEventArgs e, IEnumerable<string>? allowedExtensions)
        {
            var result = HandleDrop(e);

            if (allowedExtensions != null)
            {
                var extensions = allowedExtensions.Select(ext => ext.ToLowerInvariant()).ToHashSet();
                result.Files = result.Files
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();
            }

            return result;
        }

        /// <summary>
        /// Gets files from dropped folders recursively.
        /// </summary>
        public IEnumerable<string> GetFilesFromDroppedFolders(DragDropResult result, string searchPattern = "*.*", bool recursive = true)
        {
            var files = new List<string>(result.Files);

            foreach (var folder in result.Folders)
            {
                try
                {
                    var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    files.AddRange(Directory.GetFiles(folder, searchPattern, searchOption));
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error accessing folder {folder}: {ex.Message}");
                }
            }

            return files;
        }

        /// <summary>
        /// Checks if the drag data contains valid file/folder data.
        /// </summary>
        public static bool CanDrop(DragEventArgs e)
        {
            return e.Data.GetDataPresent(DataFormats.FileDrop);
        }

        /// <summary>
        /// Validates that dropped files have allowed extensions.
        /// </summary>
        public static bool ValidateFileTypes(DragEventArgs e, IEnumerable<string> allowedExtensions)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return false;

            var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0)
                return false;

            var extensions = allowedExtensions.Select(ext => ext.ToLowerInvariant()).ToHashSet();

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (extensions.Contains(ext))
                        return true;
                }
                else if (Directory.Exists(path))
                {
                    // Folders are allowed
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Creates drag data from a list of file paths.
        /// </summary>
        public static DataObject CreateDragData(IEnumerable<string> paths)
        {
            var dataObject = new DataObject();
            dataObject.SetData(DataFormats.FileDrop, paths.ToArray());
            return dataObject;
        }

        /// <summary>
        /// Starts a drag operation with the specified files.
        /// </summary>
        public static DragDropEffects StartDrag(UIElement source, IEnumerable<string> paths, DragDropEffects allowedEffects = DragDropEffects.Copy | DragDropEffects.Move)
        {
            var data = CreateDragData(paths);
            return DragDrop.DoDragDrop(source, data, allowedEffects);
        }
    }

    public class DragDropResult
    {
        public List<string> Files { get; set; } = new();
        public List<string> Folders { get; set; } = new();
        
        public List<string> AllPaths => Files.Concat(Folders).ToList();
        public int TotalCount => Files.Count + Folders.Count;
        public bool HasFiles => Files.Count > 0;
        public bool HasFolders => Folders.Count > 0;
    }

    public class DragDropEventArgs : EventArgs
    {
        public List<string> Paths { get; set; } = new();
    }
}
