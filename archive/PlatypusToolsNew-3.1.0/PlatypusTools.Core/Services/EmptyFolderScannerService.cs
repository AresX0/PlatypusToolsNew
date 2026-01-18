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
                    return false;

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

        public async Task<int> DeleteFoldersAsync(
            IEnumerable<string> folderPaths,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            int deleted = 0;

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
                        if (Directory.Exists(path))
                        {
                            // Double-check it's still empty
                            if (IsEmptyFolder(path))
                            {
                                progress?.Report($"Deleting: {path}");
                                Directory.Delete(path, false);
                                deleted++;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Error($"Failed to delete folder '{path}': {ex.Message}");
                    }
                }
            }, cancellationToken);

            return deleted;
        }
    }
}
