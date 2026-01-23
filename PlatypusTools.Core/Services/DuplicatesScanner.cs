using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public class DuplicateGroup
    {
        public string Hash { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new List<string>();
    }

    public static class DuplicatesScanner
    {
        /// <summary>
        /// Batch size for processing files - report progress after this many files.
        /// </summary>
        private const int BatchSize = 300;

        /// <summary>
        /// Find duplicate files (synchronous version for backward compatibility).
        /// </summary>
        public static IEnumerable<DuplicateGroup> FindDuplicates(IEnumerable<string> paths, bool recurse = true)
        {
            return FindDuplicatesAsync(paths, recurse, null, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Find duplicate files asynchronously with progress reporting and batch processing.
        /// Optimized for large directories (50000+ files) with deep subdirectories.
        /// </summary>
        /// <param name="paths">Paths to scan (files or directories).</param>
        /// <param name="recurse">Whether to scan subdirectories.</param>
        /// <param name="onProgress">Progress callback (current, total, message).</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <param name="onBatchProcessed">Callback when a batch of duplicates is found.</param>
        public static async Task<List<DuplicateGroup>> FindDuplicatesAsync(
            IEnumerable<string> paths,
            bool recurse = true,
            Action<int, int, string>? onProgress = null,
            CancellationToken cancellationToken = default,
            Action<List<DuplicateGroup>>? onBatchProcessed = null)
        {
            return await Task.Run(async () =>
            {
                // Use robust enumeration for deep directories
                var fileList = new List<string>();

                onProgress?.Invoke(0, 0, "Discovering files...");

                foreach (var p in paths)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (Directory.Exists(p))
                    {
                        foreach (var file in EnumerateFilesRobust(p, recurse))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            fileList.Add(file);

                            if (fileList.Count % 1000 == 0)
                                onProgress?.Invoke(fileList.Count, 0, $"Discovered {fileList.Count} files...");
                        }
                    }
                    else if (File.Exists(p))
                    {
                        fileList.Add(p);
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                    return new List<DuplicateGroup>();

                var totalFiles = fileList.Count;
                onProgress?.Invoke(0, totalFiles, $"Grouping {totalFiles} files by size...");

                // First pass: Group by file size (much faster than hashing)
                var sizeGroups = new Dictionary<long, List<string>>();
                var processed = 0;

                foreach (var f in fileList)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    try
                    {
                        var fi = new FileInfo(f);
                        if (!sizeGroups.TryGetValue(fi.Length, out var list))
                        {
                            list = new List<string>();
                            sizeGroups[fi.Length] = list;
                        }
                        list.Add(f);
                    }
                    catch { }

                    processed++;
                    if (processed % BatchSize == 0)
                    {
                        onProgress?.Invoke(processed, totalFiles, $"Size grouping: {processed}/{totalFiles}");
                        await Task.Delay(1); // Yield to UI
                    }
                }

                // Filter to groups with potential duplicates
                var potentialDuplicates = sizeGroups.Where(g => g.Value.Count > 1).ToList();
                var filesToHash = potentialDuplicates.Sum(g => g.Value.Count);

                onProgress?.Invoke(0, filesToHash, $"Hashing {filesToHash} potential duplicates...");

                // Second pass: Hash files in batches
                var hashDict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var hashed = 0;
                var batchDuplicates = new List<DuplicateGroup>();

                foreach (var sizeGroup in potentialDuplicates)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    foreach (var f in sizeGroup.Value)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        try
                        {
                            using var sha = SHA256.Create();
                            using var s = File.OpenRead(f);
                            var hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", string.Empty);

                            if (!hashDict.TryGetValue(hash, out var list))
                            {
                                list = new List<string>();
                                hashDict[hash] = list;
                            }
                            list.Add(f);
                        }
                        catch { }

                        hashed++;
                        if (hashed % BatchSize == 0)
                        {
                            onProgress?.Invoke(hashed, filesToHash, $"Hashing: {hashed}/{filesToHash}");

                            // Check for new duplicates in this batch
                            var newDuplicates = hashDict
                                .Where(kv => kv.Value.Count > 1)
                                .Select(kv => new DuplicateGroup { Hash = kv.Key, Files = new List<string>(kv.Value) })
                                .ToList();

                            if (newDuplicates.Count > batchDuplicates.Count)
                            {
                                onBatchProcessed?.Invoke(newDuplicates);
                                batchDuplicates = newDuplicates;
                            }

                            await Task.Delay(1); // Yield to UI
                        }
                    }
                }

                var result = hashDict
                    .Where(kv => kv.Value.Count > 1)
                    .Select(kv => new DuplicateGroup { Hash = kv.Key, Files = kv.Value })
                    .ToList();

                onProgress?.Invoke(filesToHash, filesToHash, $"Found {result.Count} duplicate groups");
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Enumerate files robustly, handling access denied and deep directories.
        /// </summary>
        private static IEnumerable<string> EnumerateFilesRobust(string directory, bool recursive)
        {
            var dirsToProcess = new Queue<string>();
            dirsToProcess.Enqueue(directory);

            while (dirsToProcess.Count > 0)
            {
                var currentDir = dirsToProcess.Dequeue();

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(currentDir);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (PathTooLongException)
                {
                    continue;
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }

                if (recursive)
                {
                    IEnumerable<string> subdirs;
                    try
                    {
                        subdirs = Directory.EnumerateDirectories(currentDir);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var subdir in subdirs)
                    {
                        dirsToProcess.Enqueue(subdir);
                    }
                }
            }
        }
    }
}