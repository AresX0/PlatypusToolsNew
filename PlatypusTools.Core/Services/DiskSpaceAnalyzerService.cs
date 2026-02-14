using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for analyzing disk space usage and directory structures
    /// </summary>
    public interface IDiskSpaceAnalyzerService
    {
        Task<DirectoryNode> GetDirectoryTree(string path, int maxDepth = 3);
        Task<long> CalculateDirectorySize(string path);
        Task<List<DirectoryNode>> GetLargestDirectories(string path, int count = 10);
    }

    /// <summary>
    /// Represents a directory node in the disk space tree
    /// </summary>
    public class DirectoryNode
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public List<DirectoryNode> Children { get; set; } = new List<DirectoryNode>();
        public int FileCount { get; set; }
        public int SubDirectoryCount { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsFile { get; set; } = false;
        public bool IsHidden { get; set; } = false;
        public bool IsSystem { get; set; } = false;

        public string FormattedSize => FormatBytes(Size);

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// Implementation of disk space analyzer service
    /// </summary>
    public class DiskSpaceAnalyzerService : IDiskSpaceAnalyzerService
    {
        /// <summary>
        /// Gets a tree structure of directories with their sizes
        /// </summary>
        /// <param name="path">Root path to analyze</param>
        /// <param name="maxDepth">Maximum depth to traverse</param>
        /// <returns>Directory tree with size information</returns>
        public async Task<DirectoryNode> GetDirectoryTree(string path, int maxDepth = 3)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        throw new DirectoryNotFoundException($"Directory not found: {path}");
                    }

                    return BuildDirectoryTree(path, 0, maxDepth);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to build directory tree: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Calculates the total size of a directory including all subdirectories
        /// </summary>
        /// <param name="path">Directory path</param>
        /// <returns>Total size in bytes</returns>
        public async Task<long> CalculateDirectorySize(string path)
        {
            return await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        return 0;
                    }

                    long size = 0;
                    var dirInfo = new DirectoryInfo(path);

                    // Add file sizes
                    try
                    {
                        var files = dirInfo.GetFiles();
                        size += files.Sum(f => f.Length);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip directories we can't access
                    }
                    catch (PathTooLongException)
                    {
                        // Skip paths that exceed max length
                    }

                    // Recursively add subdirectory sizes (synchronously to avoid deadlock)
                    try
                    {
                        var subDirs = dirInfo.GetDirectories();
                        foreach (var dir in subDirs)
                        {
                            size += CalculateDirectorySizeSync(dir.FullName);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip directories we can't access
                    }
                    catch (PathTooLongException)
                    {
                        // Skip paths that exceed max length
                    }

                    return size;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to calculate directory size: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Synchronously calculates the total size of a directory (used internally to avoid blocking on async).
        /// </summary>
        private long CalculateDirectorySizeSync(string path)
        {
            if (!Directory.Exists(path))
                return 0;

            long size = 0;
            var dirInfo = new DirectoryInfo(path);

            try
            {
                var files = dirInfo.GetFiles();
                size += files.Sum(f => f.Length);
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }

            try
            {
                var subDirs = dirInfo.GetDirectories();
                foreach (var dir in subDirs)
                {
                    size += CalculateDirectorySizeSync(dir.FullName);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (PathTooLongException) { }

            return size;
        }

        /// <summary>
        /// Gets the largest directories under a given path
        /// </summary>
        /// <param name="path">Root path to analyze</param>
        /// <param name="count">Number of directories to return</param>
        /// <returns>List of largest directories</returns>
        public async Task<List<DirectoryNode>> GetLargestDirectories(string path, int count = 10)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    if (!Directory.Exists(path))
                    {
                        throw new DirectoryNotFoundException($"Directory not found: {path}");
                    }

                    var directories = new List<DirectoryNode>();
                    var dirInfo = new DirectoryInfo(path);

                    try
                    {
                        var subDirs = dirInfo.GetDirectories();
                        foreach (var dir in subDirs)
                        {
                            try
                            {
                                var size = await CalculateDirectorySize(dir.FullName);
                                directories.Add(new DirectoryNode
                                {
                                    Name = dir.Name,
                                    Path = dir.FullName,
                                    Size = size,
                                    LastModified = dir.LastWriteTime
                                });
                            }
                            catch
                            {
                                // Skip directories we can't access
                            }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip if we can't access the directory
                    }

                    return directories
                        .OrderByDescending(d => d.Size)
                        .Take(count)
                        .ToList();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to get largest directories: {ex.Message}", ex);
                }
            });
        }

        private DirectoryNode BuildDirectoryTree(string path, int currentDepth, int maxDepth)
        {
            var dirInfo = new DirectoryInfo(path);
            var node = new DirectoryNode
            {
                Name = dirInfo.Name,
                Path = dirInfo.FullName,
                LastModified = dirInfo.LastWriteTime,
                IsFile = false,
                IsHidden = (dirInfo.Attributes & FileAttributes.Hidden) != 0,
                IsSystem = (dirInfo.Attributes & FileAttributes.System) != 0
            };

            try
            {
                // Add files as children
                FileInfo[] files = Array.Empty<FileInfo>();
                try {
                    var tempFiles = dirInfo.GetFiles();
                    if (tempFiles != null) files = tempFiles;
                } catch { files = Array.Empty<FileInfo>(); }
                node.FileCount = files != null ? files.Length : 0;
                node.Size = files != null ? files.Sum(f => f?.Length ?? 0) : 0;
                if (files != null)
                {
                    foreach (var file in files)
                    {
                        if (file == null) continue;
                        try {
                            node.Children.Add(new DirectoryNode
                            {
                                Name = file.Name,
                                Path = file.FullName,
                                Size = file.Length,
                                LastModified = file.LastWriteTime,
                                IsFile = true,
                                IsHidden = (file.Attributes & FileAttributes.Hidden) != 0,
                                IsSystem = (file.Attributes & FileAttributes.System) != 0,
                                FileCount = 1,
                                SubDirectoryCount = 0
                            });
                        } catch { /* skip files we can't access */ }
                    }
                }

                // Process subdirectories
                if (currentDepth < maxDepth)
                {
                    DirectoryInfo[] subDirs = Array.Empty<DirectoryInfo>();
                    try {
                        var tempDirs = dirInfo.GetDirectories();
                        if (tempDirs != null) subDirs = tempDirs;
                    } catch { subDirs = Array.Empty<DirectoryInfo>(); }
                    node.SubDirectoryCount = subDirs != null ? subDirs.Length : 0;

                    if (subDirs != null)
                    {
                        foreach (var dir in subDirs)
                        {
                            if (dir == null) continue;
                            try
                            {
                                var childNode = BuildDirectoryTree(dir.FullName, currentDepth + 1, maxDepth);
                                node.Children.Add(childNode);
                                node.Size += childNode.Size;
                            }
                            catch
                            {
                                // Skip directories we can't access
                            }
                        }
                    }
                }
                else
                {
                    // Just count subdirectories at max depth
                    node.SubDirectoryCount = dirInfo.GetDirectories().Length;
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }
            catch (PathTooLongException)
            {
                // Skip paths that exceed max length
            }

            return node;
        }
    }
}
