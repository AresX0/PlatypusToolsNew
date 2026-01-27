using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public class EmptyFolderInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ParentPath { get; set; } = string.Empty;
        public int Depth { get; set; }
        public bool IsSelected { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    public class EmptyFolderScannerService
    {
        private static readonly HashSet<string> SystemFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "System Volume Information",
            "$Recycle.Bin",
            "$RECYCLE.BIN",
            "Windows",
            "Program Files",
            "Program Files (x86)",
            "ProgramData",
            "Recovery",
            "Boot",
            "Config.Msi",
            "Documents and Settings",
            "MSOCache",
            "PerfLogs",
            "Intel",
            "AMD",
            "NVIDIA"
        };

        /// <summary>
        /// Files that are typically auto-generated and can be ignored when determining if a folder is "empty".
        /// </summary>
        private static readonly HashSet<string> JunkFiles = new(StringComparer.OrdinalIgnoreCase)
        {
            "Thumbs.db",
            "desktop.ini",
            ".DS_Store",
            ".localized",
            "Icon\r",
            ".directory",
            ".Spotlight-V100",
            ".fseventsd",
            ".Trashes",
            ".TemporaryItems",
            "ehthumbs.db",
            "ehthumbs_vista.db",
            "folder.jpg",
            "folder.gif",
            "AlbumArtSmall.jpg",
            "AlbumArt_{*}.jpg"
        };

        /// <summary>
        /// Gets or sets whether to treat folders containing only junk files (Thumbs.db, desktop.ini, etc.) as empty.
        /// Default is true.
        /// </summary>
        public bool IgnoreJunkFiles { get; set; } = true;

        public event Action<string>? ProgressChanged;
        public event Action<int>? FolderScanned;

        public async Task<List<EmptyFolderInfo>> ScanForEmptyFoldersAsync(
            string rootPath,
            CancellationToken cancellationToken = default)
        {
            var emptyFolders = new List<EmptyFolderInfo>();

            if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                return emptyFolders;

            await Task.Run(() =>
            {
                try
                {
                    var allDirs = GetAllDirectoriesSafe(rootPath, cancellationToken);
                    int scanned = 0;

                    // Process from deepest to shallowest (so children are checked before parents)
                    var sortedDirs = allDirs
                        .OrderByDescending(d => d.Split(Path.DirectorySeparatorChar).Length)
                        .ToList();

                    foreach (var dir in sortedDirs)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        scanned++;
                        if (scanned % 100 == 0)
                        {
                            ProgressChanged?.Invoke($"Scanning: {dir}");
                            FolderScanned?.Invoke(scanned);
                        }

                        if (IsEmptyFolder(dir))
                        {
                            var info = new DirectoryInfo(dir);
                            emptyFolders.Add(new EmptyFolderInfo
                            {
                                Path = dir,
                                Name = info.Name,
                                ParentPath = info.Parent?.FullName ?? string.Empty,
                                Depth = dir.Count(c => c == Path.DirectorySeparatorChar) - 
                                        rootPath.Count(c => c == Path.DirectorySeparatorChar),
                                IsSelected = true,
                                CreatedDate = info.CreationTime
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Error($"EmptyFolderScanner.Scan: {ex.Message}");
                }
            }, cancellationToken);

            return emptyFolders;
        }

        private List<string> GetAllDirectoriesSafe(string rootPath, CancellationToken ct)
        {
            var dirs = new List<string>();
            var stack = new Stack<string>();
            stack.Push(rootPath);

            while (stack.Count > 0)
            {
                if (ct.IsCancellationRequested)
                    break;

                var currentDir = stack.Pop();
                try
                {
                    // Skip system folders
                    var dirName = Path.GetFileName(currentDir);
                    if (IsSystemFolder(dirName))
                        continue;

                    var subDirs = Directory.GetDirectories(currentDir);
                    foreach (var subDir in subDirs)
                    {
                        if (ct.IsCancellationRequested)
                            break;

                        var subDirName = Path.GetFileName(subDir);
                        if (!IsSystemFolder(subDirName))
                        {
                            dirs.Add(subDir);
                            stack.Push(subDir);
                        }
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (PathTooLongException) { }
                catch (DirectoryNotFoundException) { }
            }

            return dirs;
        }

        private bool IsSystemFolder(string folderName)
        {
            if (string.IsNullOrEmpty(folderName))
                return false;

            // Check if it's a system folder
            if (SystemFolders.Contains(folderName))
                return true;

            // Skip hidden/system folders starting with $
            if (folderName.StartsWith("$", StringComparison.Ordinal))
                return true;

            return false;
        }

        private bool IsEmptyFolder(string path)
        {
            try
            {
                // Check for any files (including hidden and temp files)
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                
                if (files.Length > 0)
                {
                    // If we're ignoring junk files, check if ALL files are junk
                    if (IgnoreJunkFiles)
                    {
                        bool allFilesAreJunk = files.All(f => IsJunkFile(Path.GetFileName(f)));
                        if (!allFilesAreJunk)
                            return false;
                        // All files are junk, continue to check subdirectories
                    }
                    else
                    {
                        return false;
                    }
                }

                // Check for any subdirectories (including hidden)
                var subDirs = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
                if (subDirs.Length > 0)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool IsJunkFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            // Direct match
            if (JunkFiles.Contains(fileName))
                return true;

            // Check for pattern matches (e.g., AlbumArt_{*}.jpg)
            if (fileName.StartsWith("AlbumArt_", StringComparison.OrdinalIgnoreCase) &&
                fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        public async Task<int> DeleteFoldersAsync(
            IEnumerable<string> folderPaths,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int deleted = 0;
            int failed = 0;

            await Task.Run(() =>
            {
                // Sort by depth descending so we delete children before parents
                var sorted = folderPaths
                    .OrderByDescending(p => p.Count(c => c == Path.DirectorySeparatorChar))
                    .ToList();

                foreach (var path in sorted)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        if (!Directory.Exists(path))
                        {
                            // Already deleted (maybe by a previous iteration or externally)
                            deleted++;
                            continue;
                        }

                        progress?.Report($"Deleting: {path}");
                        
                        // Use recursive force delete - this handles nested empty folders and junk files
                        if (ForceDeleteEmptyFolder(path))
                        {
                            deleted++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Error($"Failed to delete folder '{path}': {ex.Message}");
                        failed++;
                    }
                }
                
                if (failed > 0)
                {
                    progress?.Report($"Deleted {deleted} folder(s), {failed} failed");
                }
            }, cancellationToken);

            return deleted;
        }

        /// <summary>
        /// Recursively deletes an "empty" folder - handles junk files and nested empty subfolders.
        /// Returns true if the folder was successfully deleted.
        /// </summary>
        private bool ForceDeleteEmptyFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return true; // Already gone

                // First, recursively process all subdirectories
                var subDirs = Directory.GetDirectories(path, "*", SearchOption.TopDirectoryOnly);
                foreach (var subDir in subDirs)
                {
                    // Recursively try to delete the subdirectory
                    if (!ForceDeleteEmptyFolder(subDir))
                    {
                        // Subdirectory couldn't be deleted - it has real content
                        SimpleLogger.Error($"Cannot delete folder '{path}': subdirectory '{subDir}' contains non-empty content");
                        return false;
                    }
                }

                // Now handle files in this folder
                var files = Directory.GetFiles(path, "*", SearchOption.TopDirectoryOnly);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    
                    // If IgnoreJunkFiles is enabled, we can delete junk files
                    if (IgnoreJunkFiles && IsJunkFile(fileName))
                    {
                        try
                        {
                            // Remove read-only, hidden, system attributes if set
                            var attrs = File.GetAttributes(file);
                            if ((attrs & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                                File.SetAttributes(file, FileAttributes.Normal);
                            File.Delete(file);
                        }
                        catch (Exception ex)
                        {
                            SimpleLogger.Error($"Failed to delete junk file '{file}': {ex.Message}");
                            return false;
                        }
                    }
                    else
                    {
                        // Non-junk file found - folder is not truly empty
                        SimpleLogger.Error($"Cannot delete folder '{path}': contains non-junk file '{fileName}'");
                        return false;
                    }
                }

                // Now the folder should be completely empty - delete it
                // Remove any special attributes from the folder itself
                try
                {
                    var dirInfo = new DirectoryInfo(path);
                    if ((dirInfo.Attributes & (FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System)) != 0)
                    {
                        dirInfo.Attributes = FileAttributes.Normal;
                    }
                }
                catch { }

                Directory.Delete(path, false);
                return true;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"ForceDeleteEmptyFolder failed for '{path}': {ex.Message}");
                return false;
            }
        }
    }
}
