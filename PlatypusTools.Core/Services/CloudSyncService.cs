using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for syncing files with cloud storage providers (OneDrive, Google Drive, Dropbox).
    /// </summary>
    public class CloudSyncService
    {
        private static readonly Lazy<CloudSyncService> _instance = new(() => new CloudSyncService());
        public static CloudSyncService Instance => _instance.Value;

        private readonly Dictionary<string, CloudProvider> _configuredProviders = new();
        private FileSystemWatcher? _localWatcher;
        private bool _isSyncing;

        public event EventHandler<SyncProgressEventArgs>? SyncProgressChanged;
        public event EventHandler<SyncConflictEventArgs>? ConflictDetected;
        public event EventHandler<SyncErrorEventArgs>? SyncError;
        public event EventHandler<string>? SyncCompleted;

        #region Models

        public enum CloudProviderType
        {
            OneDrive,
            GoogleDrive,
            Dropbox,
            LocalFolder
        }

        public enum SyncDirection
        {
            Upload,
            Download,
            Bidirectional
        }

        public enum ConflictResolution
        {
            KeepLocal,
            KeepRemote,
            KeepBoth,
            AskUser
        }

        public class CloudProvider
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = string.Empty;
            public CloudProviderType Type { get; set; }
            public string RootPath { get; set; } = string.Empty;
            public bool IsConnected { get; set; }
            public DateTime? LastSync { get; set; }
            public string? AccessToken { get; set; }
            public string? RefreshToken { get; set; }
            public DateTime? TokenExpiry { get; set; }
        }

        public class SyncRule
        {
            public string Id { get; set; } = Guid.NewGuid().ToString();
            public string Name { get; set; } = string.Empty;
            public string LocalPath { get; set; } = string.Empty;
            public string RemotePath { get; set; } = string.Empty;
            public string ProviderId { get; set; } = string.Empty;
            public SyncDirection Direction { get; set; } = SyncDirection.Bidirectional;
            public ConflictResolution ConflictPolicy { get; set; } = ConflictResolution.AskUser;
            public bool IsEnabled { get; set; } = true;
            public bool SyncSubfolders { get; set; } = true;
            public List<string> ExcludePatterns { get; set; } = new() { "*.tmp", "*.bak", "~*", ".git", "node_modules" };
            public int SyncIntervalMinutes { get; set; } = 15;
            public DateTime? LastSync { get; set; }
        }

        public class SyncItem
        {
            public string LocalPath { get; set; } = string.Empty;
            public string RemotePath { get; set; } = string.Empty;
            public DateTime LocalModified { get; set; }
            public DateTime RemoteModified { get; set; }
            public long LocalSize { get; set; }
            public long RemoteSize { get; set; }
            public string LocalHash { get; set; } = string.Empty;
            public string RemoteHash { get; set; } = string.Empty;
            public SyncAction RequiredAction { get; set; }
            public bool HasConflict { get; set; }
        }

        public enum SyncAction
        {
            None,
            Upload,
            Download,
            Delete,
            Conflict
        }

        public class SyncProgressEventArgs : EventArgs
        {
            public string CurrentFile { get; set; } = string.Empty;
            public int FilesProcessed { get; set; }
            public int TotalFiles { get; set; }
            public long BytesTransferred { get; set; }
            public long TotalBytes { get; set; }
            public SyncAction CurrentAction { get; set; }
            public double PercentComplete => TotalFiles > 0 ? (double)FilesProcessed / TotalFiles * 100 : 0;
        }

        public class SyncConflictEventArgs : EventArgs
        {
            public SyncItem Item { get; set; } = new();
            public string RuleName { get; set; } = string.Empty;
            public ConflictResolution? UserResolution { get; set; }
        }

        public class SyncErrorEventArgs : EventArgs
        {
            public string FilePath { get; set; } = string.Empty;
            public string ErrorMessage { get; set; } = string.Empty;
            public Exception? Exception { get; set; }
        }

        public class SyncResult
        {
            public bool Success { get; set; }
            public int FilesUploaded { get; set; }
            public int FilesDownloaded { get; set; }
            public int FilesDeleted { get; set; }
            public int ConflictsResolved { get; set; }
            public int Errors { get; set; }
            public long BytesTransferred { get; set; }
            public TimeSpan Duration { get; set; }
            public List<string> ErrorMessages { get; set; } = new();
        }

        #endregion

        #region Provider Management

        /// <summary>
        /// Detects cloud storage folders on the system.
        /// </summary>
        public List<CloudProvider> DetectCloudProviders()
        {
            var providers = new List<CloudProvider>();

            // OneDrive
            var oneDrivePath = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(oneDrivePath) && Directory.Exists(oneDrivePath))
            {
                providers.Add(new CloudProvider
                {
                    Name = "OneDrive",
                    Type = CloudProviderType.OneDrive,
                    RootPath = oneDrivePath,
                    IsConnected = true
                });
            }

            // OneDrive for Business
            var oneDriveBusinessPath = Environment.GetEnvironmentVariable("OneDriveCommercial");
            if (!string.IsNullOrEmpty(oneDriveBusinessPath) && Directory.Exists(oneDriveBusinessPath))
            {
                providers.Add(new CloudProvider
                {
                    Name = "OneDrive for Business",
                    Type = CloudProviderType.OneDrive,
                    RootPath = oneDriveBusinessPath,
                    IsConnected = true
                });
            }

            // Google Drive (common installation paths)
            var googleDrivePaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Google Drive"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "My Drive"),
                @"G:\My Drive"
            };
            foreach (var path in googleDrivePaths)
            {
                if (Directory.Exists(path))
                {
                    providers.Add(new CloudProvider
                    {
                        Name = "Google Drive",
                        Type = CloudProviderType.GoogleDrive,
                        RootPath = path,
                        IsConnected = true
                    });
                    break;
                }
            }

            // Dropbox
            var dropboxInfoPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dropbox", "info.json");
            if (File.Exists(dropboxInfoPath))
            {
                try
                {
                    var json = File.ReadAllText(dropboxInfoPath);
                    var match = System.Text.RegularExpressions.Regex.Match(json, @"""path"":\s*""([^""]+)""");
                    if (match.Success)
                    {
                        var dropboxPath = match.Groups[1].Value.Replace("\\\\", "\\");
                        if (Directory.Exists(dropboxPath))
                        {
                            providers.Add(new CloudProvider
                            {
                                Name = "Dropbox",
                                Type = CloudProviderType.Dropbox,
                                RootPath = dropboxPath,
                                IsConnected = true
                            });
                        }
                    }
                }
                catch { }
            }

            return providers;
        }

        /// <summary>
        /// Adds a cloud provider configuration.
        /// </summary>
        public void AddProvider(CloudProvider provider)
        {
            _configuredProviders[provider.Id] = provider;
        }

        /// <summary>
        /// Removes a cloud provider.
        /// </summary>
        public bool RemoveProvider(string providerId)
        {
            return _configuredProviders.Remove(providerId);
        }

        /// <summary>
        /// Gets all configured providers.
        /// </summary>
        public IReadOnlyList<CloudProvider> GetProviders()
        {
            return _configuredProviders.Values.ToList();
        }

        #endregion

        #region Sync Operations

        /// <summary>
        /// Synchronizes files according to a sync rule.
        /// </summary>
        public async Task<SyncResult> SyncAsync(SyncRule rule, CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var result = new SyncResult { Success = true };

            if (_isSyncing)
            {
                result.Success = false;
                result.ErrorMessages.Add("Sync already in progress");
                return result;
            }

            _isSyncing = true;

            try
            {
                if (!_configuredProviders.TryGetValue(rule.ProviderId, out var provider))
                {
                    result.Success = false;
                    result.ErrorMessages.Add($"Provider not found: {rule.ProviderId}");
                    return result;
                }

                // Build sync plan
                var syncItems = await BuildSyncPlanAsync(rule, provider, cancellationToken);

                var progress = new SyncProgressEventArgs
                {
                    TotalFiles = syncItems.Count
                };

                foreach (var item in syncItems)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        progress.CurrentFile = item.LocalPath;
                        progress.CurrentAction = item.RequiredAction;
                        SyncProgressChanged?.Invoke(this, progress);

                        switch (item.RequiredAction)
                        {
                            case SyncAction.Upload:
                                await UploadFileAsync(item, provider, cancellationToken);
                                result.FilesUploaded++;
                                result.BytesTransferred += item.LocalSize;
                                break;

                            case SyncAction.Download:
                                await DownloadFileAsync(item, provider, cancellationToken);
                                result.FilesDownloaded++;
                                result.BytesTransferred += item.RemoteSize;
                                break;

                            case SyncAction.Delete:
                                await DeleteFileAsync(item, rule.Direction, cancellationToken);
                                result.FilesDeleted++;
                                break;

                            case SyncAction.Conflict:
                                var resolution = await ResolveConflictAsync(item, rule, cancellationToken);
                                if (resolution != ConflictResolution.AskUser)
                                    result.ConflictsResolved++;
                                break;
                        }

                        progress.FilesProcessed++;
                    }
                    catch (Exception ex)
                    {
                        result.Errors++;
                        result.ErrorMessages.Add($"{item.LocalPath}: {ex.Message}");
                        SyncError?.Invoke(this, new SyncErrorEventArgs
                        {
                            FilePath = item.LocalPath,
                            ErrorMessage = ex.Message,
                            Exception = ex
                        });
                    }
                }

                rule.LastSync = DateTime.UtcNow;
                provider.LastSync = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessages.Add(ex.Message);
            }
            finally
            {
                _isSyncing = false;
                result.Duration = DateTime.UtcNow - startTime;
                SyncCompleted?.Invoke(this, $"Sync completed: {result.FilesUploaded} uploaded, {result.FilesDownloaded} downloaded");
            }

            return result;
        }

        /// <summary>
        /// Builds a sync plan comparing local and remote files.
        /// </summary>
        private async Task<List<SyncItem>> BuildSyncPlanAsync(SyncRule rule, CloudProvider provider, CancellationToken cancellationToken)
        {
            var items = new List<SyncItem>();

            if (!Directory.Exists(rule.LocalPath))
            {
                Directory.CreateDirectory(rule.LocalPath);
            }

            var remotePath = Path.Combine(provider.RootPath, rule.RemotePath);
            if (!Directory.Exists(remotePath))
            {
                Directory.CreateDirectory(remotePath);
            }

            // Get local files
            var localFiles = GetFilesRecursive(rule.LocalPath, rule.SyncSubfolders, rule.ExcludePatterns);

            // Get remote files (in cloud folder)
            var remoteFiles = GetFilesRecursive(remotePath, rule.SyncSubfolders, rule.ExcludePatterns);

            // Build relative path mappings
            var localRelative = localFiles.ToDictionary(f => GetRelativePath(f, rule.LocalPath), f => f);
            var remoteRelative = remoteFiles.ToDictionary(f => GetRelativePath(f, remotePath), f => f);

            // Compare and create sync items
            var allPaths = localRelative.Keys.Union(remoteRelative.Keys).Distinct();

            foreach (var relativePath in allPaths)
            {
                var item = new SyncItem
                {
                    LocalPath = Path.Combine(rule.LocalPath, relativePath),
                    RemotePath = Path.Combine(remotePath, relativePath)
                };

                var hasLocal = localRelative.ContainsKey(relativePath);
                var hasRemote = remoteRelative.ContainsKey(relativePath);

                if (hasLocal)
                {
                    var localInfo = new FileInfo(localRelative[relativePath]);
                    item.LocalModified = localInfo.LastWriteTimeUtc;
                    item.LocalSize = localInfo.Length;
                }

                if (hasRemote)
                {
                    var remoteInfo = new FileInfo(remoteRelative[relativePath]);
                    item.RemoteModified = remoteInfo.LastWriteTimeUtc;
                    item.RemoteSize = remoteInfo.Length;
                }

                // Determine action
                item.RequiredAction = DetermineAction(item, hasLocal, hasRemote, rule);
                items.Add(item);
            }

            await Task.CompletedTask;
            return items;
        }

        private SyncAction DetermineAction(SyncItem item, bool hasLocal, bool hasRemote, SyncRule rule)
        {
            if (hasLocal && !hasRemote)
            {
                return rule.Direction == SyncDirection.Download ? SyncAction.Delete : SyncAction.Upload;
            }

            if (!hasLocal && hasRemote)
            {
                return rule.Direction == SyncDirection.Upload ? SyncAction.Delete : SyncAction.Download;
            }

            if (hasLocal && hasRemote)
            {
                // Check if files are identical
                if (item.LocalSize == item.RemoteSize && 
                    Math.Abs((item.LocalModified - item.RemoteModified).TotalSeconds) < 2)
                {
                    return SyncAction.None;
                }

                // Check for conflict
                if (rule.Direction == SyncDirection.Bidirectional)
                {
                    // Both modified since last sync
                    if (rule.LastSync.HasValue && 
                        item.LocalModified > rule.LastSync && 
                        item.RemoteModified > rule.LastSync)
                    {
                        item.HasConflict = true;
                        return SyncAction.Conflict;
                    }

                    // Sync newer version
                    return item.LocalModified > item.RemoteModified ? SyncAction.Upload : SyncAction.Download;
                }

                return rule.Direction == SyncDirection.Upload ? SyncAction.Upload : SyncAction.Download;
            }

            return SyncAction.None;
        }

        private async Task UploadFileAsync(SyncItem item, CloudProvider provider, CancellationToken cancellationToken)
        {
            // For local cloud folders (OneDrive, Google Drive, Dropbox), just copy the file
            var dir = Path.GetDirectoryName(item.RemotePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await Task.Run(() => File.Copy(item.LocalPath, item.RemotePath, true), cancellationToken);
        }

        private async Task DownloadFileAsync(SyncItem item, CloudProvider provider, CancellationToken cancellationToken)
        {
            var dir = Path.GetDirectoryName(item.LocalPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await Task.Run(() => File.Copy(item.RemotePath, item.LocalPath, true), cancellationToken);
        }

        private async Task DeleteFileAsync(SyncItem item, SyncDirection direction, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                if (direction == SyncDirection.Upload && File.Exists(item.RemotePath))
                {
                    File.Delete(item.RemotePath);
                }
                else if (direction == SyncDirection.Download && File.Exists(item.LocalPath))
                {
                    File.Delete(item.LocalPath);
                }
            }, cancellationToken);
        }

        private async Task<ConflictResolution> ResolveConflictAsync(SyncItem item, SyncRule rule, CancellationToken cancellationToken)
        {
            if (rule.ConflictPolicy != ConflictResolution.AskUser)
            {
                await ApplyConflictResolutionAsync(item, rule.ConflictPolicy, cancellationToken);
                return rule.ConflictPolicy;
            }

            // Raise event for user decision
            var args = new SyncConflictEventArgs
            {
                Item = item,
                RuleName = rule.Name
            };
            ConflictDetected?.Invoke(this, args);

            if (args.UserResolution.HasValue)
            {
                await ApplyConflictResolutionAsync(item, args.UserResolution.Value, cancellationToken);
                return args.UserResolution.Value;
            }

            return ConflictResolution.AskUser;
        }

        private async Task ApplyConflictResolutionAsync(SyncItem item, ConflictResolution resolution, CancellationToken cancellationToken)
        {
            switch (resolution)
            {
                case ConflictResolution.KeepLocal:
                    await Task.Run(() => File.Copy(item.LocalPath, item.RemotePath, true), cancellationToken);
                    break;

                case ConflictResolution.KeepRemote:
                    await Task.Run(() => File.Copy(item.RemotePath, item.LocalPath, true), cancellationToken);
                    break;

                case ConflictResolution.KeepBoth:
                    var localExt = Path.GetExtension(item.LocalPath);
                    var localName = Path.GetFileNameWithoutExtension(item.LocalPath);
                    var localDir = Path.GetDirectoryName(item.LocalPath) ?? "";
                    var conflictPath = Path.Combine(localDir, $"{localName}_conflict_{DateTime.Now:yyyyMMddHHmmss}{localExt}");
                    await Task.Run(() =>
                    {
                        File.Copy(item.RemotePath, conflictPath);
                        File.Copy(item.LocalPath, item.RemotePath, true);
                    }, cancellationToken);
                    break;
            }
        }

        #endregion

        #region Watch for Changes

        /// <summary>
        /// Starts watching a local folder for changes.
        /// </summary>
        public void StartWatching(SyncRule rule)
        {
            StopWatching();

            _localWatcher = new FileSystemWatcher(rule.LocalPath)
            {
                IncludeSubdirectories = rule.SyncSubfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName
            };

            _localWatcher.Created += (s, e) => OnFileChanged(e.FullPath, rule);
            _localWatcher.Changed += (s, e) => OnFileChanged(e.FullPath, rule);
            _localWatcher.Deleted += (s, e) => OnFileChanged(e.FullPath, rule);
            _localWatcher.Renamed += (s, e) => OnFileChanged(e.FullPath, rule);

            _localWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Stops watching for changes.
        /// </summary>
        public void StopWatching()
        {
            _localWatcher?.Dispose();
            _localWatcher = null;
        }

        private void OnFileChanged(string path, SyncRule rule)
        {
            // Check exclude patterns
            foreach (var pattern in rule.ExcludePatterns)
            {
                if (MatchesPattern(path, pattern))
                    return;
            }

            // Queue sync (debounced)
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000); // Debounce
                await SyncAsync(rule);
            });
        }

        #endregion

        #region Helpers

        private List<string> GetFilesRecursive(string path, bool includeSubfolders, List<string> excludePatterns)
        {
            var files = new List<string>();

            if (!Directory.Exists(path))
                return files;

            var option = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                foreach (var file in Directory.GetFiles(path, "*", option))
                {
                    var exclude = false;
                    foreach (var pattern in excludePatterns)
                    {
                        if (MatchesPattern(file, pattern))
                        {
                            exclude = true;
                            break;
                        }
                    }

                    if (!exclude)
                        files.Add(file);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }

            return files;
        }

        private string GetRelativePath(string fullPath, string basePath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            return fullPath.Substring(basePath.Length);
        }

        private bool MatchesPattern(string path, string pattern)
        {
            var fileName = Path.GetFileName(path);

            if (pattern.StartsWith("*"))
            {
                return fileName.EndsWith(pattern.Substring(1), StringComparison.OrdinalIgnoreCase);
            }

            if (pattern.EndsWith("*"))
            {
                return fileName.StartsWith(pattern.Substring(0, pattern.Length - 1), StringComparison.OrdinalIgnoreCase);
            }

            return fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase) ||
                   path.Contains(Path.DirectorySeparatorChar + pattern + Path.DirectorySeparatorChar);
        }

        #endregion
    }
}
