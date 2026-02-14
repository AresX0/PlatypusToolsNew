using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for syncing media libraries between multiple locations.
    /// Supports two-way sync with conflict detection.
    /// </summary>
    public class LibrarySyncService
    {
        private static readonly Lazy<LibrarySyncService> _instance = new(() => new LibrarySyncService());
        public static LibrarySyncService Instance => _instance.Value;

        public event EventHandler<LibrarySyncProgress>? ProgressChanged;
        public event EventHandler<LibrarySyncConflict>? ConflictDetected;
        public event EventHandler<string>? StatusChanged;

        #region Models

        public class LibraryLocation
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public bool IsPrimary { get; set; }
            public bool IsOnline { get; set; }
            public DateTime? LastSync { get; set; }
            public long TotalSize { get; set; }
            public int FileCount { get; set; }
        }

        public class SyncProfile
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = string.Empty;
            public List<LibraryLocation> Locations { get; set; } = new();
            public SyncMode Mode { get; set; } = SyncMode.Mirror;
            public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.KeepNewest;
            public bool DeleteOrphans { get; set; }
            public bool SyncMetadata { get; set; } = true;
            public bool SyncPlaylists { get; set; } = true;
            public List<string> IncludeExtensions { get; set; } = new() { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".wma", ".mp4", ".mkv", ".avi", ".mov" };
            public List<string> ExcludeFolders { get; set; } = new() { ".git", "node_modules", "Thumbs.db", ".DS_Store" };
            public int MaxFileSizeMB { get; set; } = 0; // 0 = no limit
            public DateTime? LastSync { get; set; }
        }

        public enum SyncMode
        {
            Mirror,         // Primary -> Secondary (one-way)
            Bidirectional,  // Both ways, merge changes
            Backup          // Primary -> Secondary, never delete from secondary
        }

        public enum ConflictPolicy
        {
            KeepNewest,
            KeepLargest,
            KeepPrimary,
            KeepBoth,
            AskUser
        }

        public class LibrarySyncProgress
        {
            public string CurrentFile { get; set; } = string.Empty;
            public string CurrentAction { get; set; } = string.Empty;
            public int FilesProcessed { get; set; }
            public int TotalFiles { get; set; }
            public long BytesTransferred { get; set; }
            public long TotalBytes { get; set; }
            public double PercentComplete => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;
            public TimeSpan Elapsed { get; set; }
            public TimeSpan Remaining { get; set; }
        }

        public class LibrarySyncConflict
        {
            public string RelativePath { get; set; } = string.Empty;
            public FileInfo SourceFile { get; set; } = null!;
            public FileInfo TargetFile { get; set; } = null!;
            public ConflictPolicy? Resolution { get; set; }
        }

        public class SyncItem
        {
            public string RelativePath { get; set; } = string.Empty;
            public string SourcePath { get; set; } = string.Empty;
            public string TargetPath { get; set; } = string.Empty;
            public SyncAction Action { get; set; }
            public long Size { get; set; }
            public bool HasConflict { get; set; }
        }

        public enum SyncAction
        {
            None,
            Copy,
            Update,
            Delete,
            Conflict
        }

        public class SyncResult
        {
            public bool Success { get; set; }
            public int FilesCopied { get; set; }
            public int FilesUpdated { get; set; }
            public int FilesDeleted { get; set; }
            public int FilesSkipped { get; set; }
            public int ConflictsResolved { get; set; }
            public int Errors { get; set; }
            public long BytesTransferred { get; set; }
            public TimeSpan Duration { get; set; }
            public List<string> ErrorMessages { get; set; } = new();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks if a location is available.
        /// </summary>
        public bool IsLocationOnline(LibraryLocation location)
        {
            try
            {
                return Directory.Exists(location.Path);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets file statistics for a location.
        /// </summary>
        public async Task<(int FileCount, long TotalSize)> GetLocationStatsAsync(LibraryLocation location, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                if (!Directory.Exists(location.Path))
                    return (0, 0L);

                var files = Utilities.SafeFileEnumerator.EnumerateFiles(location.Path, "*", recurse: true).ToArray();
                var totalSize = files.Sum(f => { try { return new FileInfo(f).Length; } catch { return 0L; } });
                return (files.Length, totalSize);
            }, cancellationToken);
        }

        /// <summary>
        /// Analyzes differences between two locations.
        /// </summary>
        public async Task<List<SyncItem>> AnalyzeDifferencesAsync(SyncProfile profile, CancellationToken cancellationToken = default)
        {
            var items = new List<SyncItem>();
            var primary = profile.Locations.FirstOrDefault(l => l.IsPrimary);
            
            if (primary == null || !Directory.Exists(primary.Path))
                return items;

            foreach (var secondary in profile.Locations.Where(l => !l.IsPrimary && Directory.Exists(l.Path)))
            {
                var analysis = await CompareLocationsAsync(primary.Path, secondary.Path, profile, cancellationToken);
                items.AddRange(analysis);
            }

            return items;
        }

        /// <summary>
        /// Synchronizes libraries according to the profile.
        /// </summary>
        public async Task<SyncResult> SyncAsync(SyncProfile profile, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var result = new SyncResult { Success = true };

            var primary = profile.Locations.FirstOrDefault(l => l.IsPrimary);
            if (primary == null || !Directory.Exists(primary.Path))
            {
                result.Success = false;
                result.ErrorMessages.Add("Primary library location not found");
                return result;
            }

            var onlineSecondaries = profile.Locations
                .Where(l => !l.IsPrimary && Directory.Exists(l.Path))
                .ToList();

            if (!onlineSecondaries.Any())
            {
                result.Success = false;
                result.ErrorMessages.Add("No secondary locations available");
                return result;
            }

            foreach (var secondary in onlineSecondaries)
            {
                StatusChanged?.Invoke(this, $"Syncing to {secondary.Name}...");

                var syncItems = await CompareLocationsAsync(primary.Path, secondary.Path, profile, cancellationToken);
                var progress = new LibrarySyncProgress
                {
                    TotalFiles = syncItems.Count,
                    TotalBytes = syncItems.Sum(i => i.Size)
                };

                foreach (var item in syncItems)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        progress.CurrentFile = item.RelativePath;
                        progress.CurrentAction = item.Action.ToString();
                        ProgressChanged?.Invoke(this, progress);

                        switch (item.Action)
                        {
                            case SyncAction.Copy:
                                await CopyFileAsync(item.SourcePath, item.TargetPath, cancellationToken);
                                result.FilesCopied++;
                                result.BytesTransferred += item.Size;
                                break;

                            case SyncAction.Update:
                                await CopyFileAsync(item.SourcePath, item.TargetPath, cancellationToken);
                                result.FilesUpdated++;
                                result.BytesTransferred += item.Size;
                                break;

                            case SyncAction.Delete:
                                if (profile.DeleteOrphans && profile.Mode != SyncMode.Backup)
                                {
                                    File.Delete(item.TargetPath);
                                    result.FilesDeleted++;
                                }
                                else
                                {
                                    result.FilesSkipped++;
                                }
                                break;

                            case SyncAction.Conflict:
                                var resolved = await ResolveConflictAsync(item, profile.ConflictPolicy, cancellationToken);
                                if (resolved)
                                    result.ConflictsResolved++;
                                break;

                            case SyncAction.None:
                                result.FilesSkipped++;
                                break;
                        }

                        progress.FilesProcessed++;
                        progress.BytesTransferred += item.Size;
                        progress.Elapsed = DateTime.UtcNow - startTime;

                        if (progress.FilesProcessed > 0)
                        {
                            var avgTime = progress.Elapsed.TotalSeconds / progress.FilesProcessed;
                            progress.Remaining = TimeSpan.FromSeconds(avgTime * (progress.TotalFiles - progress.FilesProcessed));
                        }

                        ProgressChanged?.Invoke(this, progress);
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        result.ErrorMessages.Add($"{item.RelativePath}: {ex.Message}");
                    }
                }

                secondary.LastSync = DateTime.UtcNow;

                // Bidirectional sync - copy from secondary to primary
                if (profile.Mode == SyncMode.Bidirectional)
                {
                    var reverseItems = await CompareLocationsAsync(secondary.Path, primary.Path, profile, cancellationToken);
                    foreach (var item in reverseItems.Where(i => i.Action == SyncAction.Copy))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        try
                        {
                            await CopyFileAsync(item.SourcePath, item.TargetPath, cancellationToken);
                            result.FilesCopied++;
                            result.BytesTransferred += item.Size;
                        }
                        catch (Exception ex)
                        {
                            result.Errors++;
                            result.ErrorMessages.Add($"{item.RelativePath}: {ex.Message}");
                        }
                    }
                }
            }

            profile.LastSync = DateTime.UtcNow;
            result.Duration = DateTime.UtcNow - startTime;

            return result;
        }

        /// <summary>
        /// Verifies sync integrity using file hashes.
        /// </summary>
        public async Task<List<string>> VerifySyncIntegrityAsync(SyncProfile profile, CancellationToken cancellationToken = default)
        {
            var mismatches = new List<string>();
            var primary = profile.Locations.FirstOrDefault(l => l.IsPrimary);

            if (primary == null)
                return mismatches;

            foreach (var secondary in profile.Locations.Where(l => !l.IsPrimary && Directory.Exists(l.Path)))
            {
                var primaryFiles = GetFilteredFiles(primary.Path, profile);

                foreach (var file in primaryFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var relativePath = GetRelativePath(file, primary.Path);
                    var secondaryFile = Path.Combine(secondary.Path, relativePath);

                    if (File.Exists(secondaryFile))
                    {
                        var primaryHash = await ComputeFileHashAsync(file, cancellationToken);
                        var secondaryHash = await ComputeFileHashAsync(secondaryFile, cancellationToken);

                        if (primaryHash != secondaryHash)
                        {
                            mismatches.Add($"{relativePath} (hash mismatch in {secondary.Name})");
                        }
                    }
                    else
                    {
                        mismatches.Add($"{relativePath} (missing in {secondary.Name})");
                    }
                }
            }

            return mismatches;
        }

        #endregion

        #region Private Methods

        private async Task<List<SyncItem>> CompareLocationsAsync(string sourcePath, string targetPath, SyncProfile profile, CancellationToken cancellationToken)
        {
            var items = new List<SyncItem>();

            await Task.Run(() =>
            {
                var sourceFiles = GetFilteredFiles(sourcePath, profile);
                var targetFiles = GetFilteredFiles(targetPath, profile)
                    .ToDictionary(f => GetRelativePath(f, targetPath), f => f);

                foreach (var sourceFile in sourceFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var relativePath = GetRelativePath(sourceFile, sourcePath);
                    var targetFile = Path.Combine(targetPath, relativePath);

                    var item = new SyncItem
                    {
                        RelativePath = relativePath,
                        SourcePath = sourceFile,
                        TargetPath = targetFile,
                        Size = new FileInfo(sourceFile).Length
                    };

                    if (!targetFiles.ContainsKey(relativePath))
                    {
                        item.Action = SyncAction.Copy;
                    }
                    else
                    {
                        var sourceInfo = new FileInfo(sourceFile);
                        var targetInfo = new FileInfo(targetFile);

                        if (sourceInfo.Length != targetInfo.Length)
                        {
                            // Size differs - check for conflict
                            if (profile.Mode == SyncMode.Bidirectional && targetInfo.LastWriteTimeUtc > sourceInfo.LastWriteTimeUtc)
                            {
                                item.Action = SyncAction.Conflict;
                                item.HasConflict = true;
                            }
                            else
                            {
                                item.Action = SyncAction.Update;
                            }
                        }
                        else if (Math.Abs((sourceInfo.LastWriteTimeUtc - targetInfo.LastWriteTimeUtc).TotalSeconds) > 2)
                        {
                            if (sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc)
                            {
                                item.Action = SyncAction.Update;
                            }
                            else if (profile.Mode == SyncMode.Bidirectional)
                            {
                                item.Action = SyncAction.Conflict;
                                item.HasConflict = true;
                            }
                            else
                            {
                                item.Action = SyncAction.None;
                            }
                        }
                        else
                        {
                            item.Action = SyncAction.None;
                        }

                        targetFiles.Remove(relativePath);
                    }

                    items.Add(item);
                }

                // Files only in target (orphans)
                foreach (var orphan in targetFiles)
                {
                    items.Add(new SyncItem
                    {
                        RelativePath = orphan.Key,
                        SourcePath = Path.Combine(sourcePath, orphan.Key),
                        TargetPath = orphan.Value,
                        Size = new FileInfo(orphan.Value).Length,
                        Action = SyncAction.Delete
                    });
                }
            }, cancellationToken);

            return items;
        }

        private List<string> GetFilteredFiles(string path, SyncProfile profile)
        {
            if (!Directory.Exists(path))
                return new List<string>();

            var files = new List<string>();
            var extensions = new HashSet<string>(profile.IncludeExtensions, StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (var file in Utilities.SafeFileEnumerator.EnumerateFiles(path, "*", recurse: true))
                {
                    var ext = Path.GetExtension(file);
                    if (extensions.Count == 0 || extensions.Contains(ext))
                    {
                        // Check excluded folders
                        var skip = false;
                        foreach (var exclude in profile.ExcludeFolders)
                        {
                            if (file.Contains(Path.DirectorySeparatorChar + exclude + Path.DirectorySeparatorChar) ||
                                file.EndsWith(Path.DirectorySeparatorChar + exclude))
                            {
                                skip = true;
                                break;
                            }
                        }

                        if (!skip)
                        {
                            // Check max file size
                            if (profile.MaxFileSizeMB > 0)
                            {
                                var size = new FileInfo(file).Length;
                                if (size > profile.MaxFileSizeMB * 1024 * 1024)
                                    continue;
                            }

                            files.Add(file);
                        }
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            return files;
        }

        private async Task CopyFileAsync(string source, string target, CancellationToken cancellationToken)
        {
            var dir = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            const int bufferSize = 81920; // 80 KB
            using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, true);
            using var targetStream = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, true);
            await sourceStream.CopyToAsync(targetStream, bufferSize, cancellationToken);

            // Preserve timestamps
            var sourceInfo = new FileInfo(source);
            File.SetLastWriteTimeUtc(target, sourceInfo.LastWriteTimeUtc);
            File.SetCreationTimeUtc(target, sourceInfo.CreationTimeUtc);
        }

        private async Task<bool> ResolveConflictAsync(SyncItem item, ConflictPolicy policy, CancellationToken cancellationToken)
        {
            if (policy == ConflictPolicy.AskUser)
            {
                var conflict = new LibrarySyncConflict
                {
                    RelativePath = item.RelativePath,
                    SourceFile = new FileInfo(item.SourcePath),
                    TargetFile = new FileInfo(item.TargetPath)
                };

                ConflictDetected?.Invoke(this, conflict);

                if (conflict.Resolution.HasValue)
                {
                    policy = conflict.Resolution.Value;
                }
                else
                {
                    return false;
                }
            }

            var sourceInfo = new FileInfo(item.SourcePath);
            var targetInfo = new FileInfo(item.TargetPath);

            switch (policy)
            {
                case ConflictPolicy.KeepNewest:
                    if (sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc)
                        await CopyFileAsync(item.SourcePath, item.TargetPath, cancellationToken);
                    break;

                case ConflictPolicy.KeepLargest:
                    if (sourceInfo.Length > targetInfo.Length)
                        await CopyFileAsync(item.SourcePath, item.TargetPath, cancellationToken);
                    break;

                case ConflictPolicy.KeepPrimary:
                    await CopyFileAsync(item.SourcePath, item.TargetPath, cancellationToken);
                    break;

                case ConflictPolicy.KeepBoth:
                    var ext = Path.GetExtension(item.TargetPath);
                    var name = Path.GetFileNameWithoutExtension(item.TargetPath);
                    var dir = Path.GetDirectoryName(item.TargetPath) ?? "";
                    var conflictPath = Path.Combine(dir, $"{name}_conflict_{DateTime.Now:yyyyMMddHHmmss}{ext}");
                    File.Move(item.TargetPath, conflictPath);
                    await CopyFileAsync(item.SourcePath, item.TargetPath, cancellationToken);
                    break;
            }

            return true;
        }

        private async Task<string> ComputeFileHashAsync(string filePath, CancellationToken cancellationToken)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            using var md5 = MD5.Create();
            var hash = await md5.ComputeHashAsync(stream, cancellationToken);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            return fullPath.Substring(basePath.Length);
        }

        #endregion
    }
}
