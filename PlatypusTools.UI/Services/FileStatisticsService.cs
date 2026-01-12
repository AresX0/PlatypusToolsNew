using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for gathering file and folder statistics
    /// </summary>
    public sealed class FileStatisticsService
    {
        private static readonly Lazy<FileStatisticsService> _instance = new(() => new FileStatisticsService());
        public static FileStatisticsService Instance => _instance.Value;

        private FileStatisticsService() { }

        public async Task<FolderStatistics> AnalyzeFolderAsync(
            string path, 
            bool recursive = true,
            IProgress<AnalysisProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var stats = new FolderStatistics { Path = path };
            var extensionStats = new Dictionary<string, ExtensionStatistics>(StringComparer.OrdinalIgnoreCase);

            await Task.Run(() =>
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                
                try
                {
                    // Count directories
                    if (recursive)
                    {
                        stats.FolderCount = Directory.EnumerateDirectories(path, "*", searchOption).Count();
                    }

                    // Analyze files
                    var files = Directory.EnumerateFiles(path, "*.*", searchOption);
                    int processedFiles = 0;

                    foreach (var file in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var info = new FileInfo(file);
                            stats.FileCount++;
                            stats.TotalSize += info.Length;

                            if (info.Length > stats.LargestFileSize)
                            {
                                stats.LargestFileSize = info.Length;
                                stats.LargestFileName = info.Name;
                            }

                            if (stats.SmallestFileSize == 0 || info.Length < stats.SmallestFileSize)
                            {
                                stats.SmallestFileSize = info.Length;
                                stats.SmallestFileName = info.Name;
                            }

                            if (info.LastWriteTime > stats.NewestFileDate)
                            {
                                stats.NewestFileDate = info.LastWriteTime;
                                stats.NewestFileName = info.Name;
                            }

                            if (stats.OldestFileDate == DateTime.MinValue || info.LastWriteTime < stats.OldestFileDate)
                            {
                                stats.OldestFileDate = info.LastWriteTime;
                                stats.OldestFileName = info.Name;
                            }

                            // Track extension statistics
                            var ext = info.Extension.ToLowerInvariant();
                            if (string.IsNullOrEmpty(ext)) ext = "(no extension)";

                            if (!extensionStats.TryGetValue(ext, out var extStats))
                            {
                                extStats = new ExtensionStatistics { Extension = ext };
                                extensionStats[ext] = extStats;
                            }
                            extStats.FileCount++;
                            extStats.TotalSize += info.Length;

                            processedFiles++;
                            if (processedFiles % 100 == 0)
                            {
                                progress?.Report(new AnalysisProgress
                                {
                                    ProcessedFiles = processedFiles,
                                    CurrentFile = info.Name
                                });
                            }
                        }
                        catch { /* Skip inaccessible files */ }
                    }

                    stats.AverageFileSize = stats.FileCount > 0 ? stats.TotalSize / stats.FileCount : 0;
                    stats.ExtensionStats = extensionStats.Values
                        .OrderByDescending(e => e.TotalSize)
                        .ToList();
                }
                catch (Exception ex)
                {
                    stats.Error = ex.Message;
                }
            }, cancellationToken);

            return stats;
        }

        public async Task<List<FileInfo>> FindLargestFilesAsync(
            string path,
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Select(f =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { return new FileInfo(f); }
                        catch { return null; }
                    })
                    .Where(f => f != null)
                    .Cast<FileInfo>()
                    .OrderByDescending(f => f.Length)
                    .Take(count)
                    .ToList();

                return files;
            }, cancellationToken);
        }

        public async Task<List<FileInfo>> FindOldestFilesAsync(
            string path,
            int count = 10,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Select(f =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { return new FileInfo(f); }
                        catch { return null; }
                    })
                    .Where(f => f != null)
                    .Cast<FileInfo>()
                    .OrderBy(f => f.LastWriteTime)
                    .Take(count)
                    .ToList();
            }, cancellationToken);
        }

        public async Task<List<FileInfo>> FindRecentFilesAsync(
            string path,
            TimeSpan age,
            CancellationToken cancellationToken = default)
        {
            var cutoff = DateTime.Now - age;
            
            return await Task.Run(() =>
            {
                return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                    .Select(f =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try { return new FileInfo(f); }
                        catch { return null; }
                    })
                    .Where(f => f != null && f.LastWriteTime > cutoff)
                    .Cast<FileInfo>()
                    .OrderByDescending(f => f.LastWriteTime)
                    .ToList();
            }, cancellationToken);
        }

        public async Task<Dictionary<string, long>> GetFolderSizesAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            var sizes = new Dictionary<string, long>();

            await Task.Run(() =>
            {
                foreach (var dir in Directory.EnumerateDirectories(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var size = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                            .Sum(f =>
                            {
                                try { return new FileInfo(f).Length; }
                                catch { return 0; }
                            });
                        sizes[Path.GetFileName(dir)] = size;
                    }
                    catch { /* Skip inaccessible folders */ }
                }
            }, cancellationToken);

            return sizes.OrderByDescending(kvp => kvp.Value)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }

    public class FolderStatistics
    {
        public string Path { get; set; } = "";
        public int FileCount { get; set; }
        public int FolderCount { get; set; }
        public long TotalSize { get; set; }
        public long AverageFileSize { get; set; }
        public long LargestFileSize { get; set; }
        public string? LargestFileName { get; set; }
        public long SmallestFileSize { get; set; }
        public string? SmallestFileName { get; set; }
        public DateTime NewestFileDate { get; set; }
        public string? NewestFileName { get; set; }
        public DateTime OldestFileDate { get; set; }
        public string? OldestFileName { get; set; }
        public List<ExtensionStatistics> ExtensionStats { get; set; } = new();
        public string? Error { get; set; }
    }

    public class ExtensionStatistics
    {
        public string Extension { get; set; } = "";
        public int FileCount { get; set; }
        public long TotalSize { get; set; }
    }

    public class AnalysisProgress
    {
        public int ProcessedFiles { get; set; }
        public string? CurrentFile { get; set; }
    }
}
