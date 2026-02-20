using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for scheduled folder backups with versioning, compression, and retention policies.
    /// </summary>
    public class ScheduledBackupService
    {
        public class BackupProfile
        {
            public string Id { get; set; } = Guid.NewGuid().ToString("N");
            public string Name { get; set; } = "";
            public List<string> SourceFolders { get; set; } = new();
            public string DestinationFolder { get; set; } = "";
            public bool CompressBackup { get; set; } = true;
            public bool Incremental { get; set; }
            public int MaxVersions { get; set; } = 5;
            public string Schedule { get; set; } = "Manual"; // Manual, Daily, Weekly, Monthly
            public DateTime? LastRun { get; set; }
            public bool IsEnabled { get; set; } = true;
            public List<string> ExcludePatterns { get; set; } = new() { "*.tmp", "*.log", "Thumbs.db", "desktop.ini" };
        }

        public class BackupResult
        {
            public string ProfileName { get; set; } = "";
            public DateTime StartTime { get; set; }
            public DateTime EndTime { get; set; }
            public int FilesCopied { get; set; }
            public int FilesSkipped { get; set; }
            public int Errors { get; set; }
            public long TotalSize { get; set; }
            public string BackupPath { get; set; } = "";
            public bool Success { get; set; }
            public List<string> ErrorMessages { get; set; } = new();
        }

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        private static readonly string ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "BackupProfiles");

        /// <summary>
        /// Run a backup profile.
        /// </summary>
        public async Task<BackupResult> RunBackupAsync(BackupProfile profile, CancellationToken ct = default)
        {
            var result = new BackupResult
            {
                ProfileName = profile.Name,
                StartTime = DateTime.Now
            };

            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupName = $"{profile.Name}_{timestamp}";
                var targetDir = Path.Combine(profile.DestinationFolder, backupName);

                LogMessage?.Invoke(this, $"[START] Backup '{profile.Name}' to {targetDir}");

                // Collect all files from source folders
                var allFiles = new List<(string source, string relative)>();

                foreach (var sourceFolder in profile.SourceFolders)
                {
                    if (!Directory.Exists(sourceFolder)) continue;

                    var files = Directory.GetFiles(sourceFolder, "*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        if (IsExcluded(file, profile.ExcludePatterns)) continue;
                        var relative = Path.GetRelativePath(sourceFolder, file);
                        allFiles.Add((file, Path.Combine(Path.GetFileName(sourceFolder), relative)));
                    }
                }

                LogMessage?.Invoke(this, $"  Found {allFiles.Count} files to back up");

                if (profile.CompressBackup)
                {
                    // Create a ZIP backup
                    var zipPath = targetDir + ".zip";
                    result.BackupPath = zipPath;

                    Directory.CreateDirectory(profile.DestinationFolder);

                    using var zipStream = File.Create(zipPath);
                    using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);

                    for (int i = 0; i < allFiles.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (source, relative) = allFiles[i];

                        try
                        {
                            var entry = archive.CreateEntry(relative, CompressionLevel.Optimal);
                            using var entryStream = entry.Open();
                            using var fileStream = File.OpenRead(source);
                            await fileStream.CopyToAsync(entryStream, ct);
                            result.FilesCopied++;
                            result.TotalSize += new FileInfo(source).Length;
                        }
                        catch (Exception ex)
                        {
                            result.Errors++;
                            result.ErrorMessages.Add($"{relative}: {ex.Message}");
                        }

                        ProgressChanged?.Invoke(this, (int)((i + 1.0) / allFiles.Count * 100));
                    }
                }
                else
                {
                    // Copy files directly
                    result.BackupPath = targetDir;
                    Directory.CreateDirectory(targetDir);

                    for (int i = 0; i < allFiles.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var (source, relative) = allFiles[i];
                        var destPath = Path.Combine(targetDir, relative);

                        try
                        {
                            var destDir = Path.GetDirectoryName(destPath);
                            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                                Directory.CreateDirectory(destDir);

                            File.Copy(source, destPath, true);
                            result.FilesCopied++;
                            result.TotalSize += new FileInfo(source).Length;
                        }
                        catch (Exception ex)
                        {
                            result.Errors++;
                            result.ErrorMessages.Add($"{relative}: {ex.Message}");
                        }

                        ProgressChanged?.Invoke(this, (int)((i + 1.0) / allFiles.Count * 100));
                    }
                }

                // Enforce retention policy
                await EnforceRetentionAsync(profile);

                result.Success = true;
                result.EndTime = DateTime.Now;
                profile.LastRun = DateTime.Now;
                await SaveProfileAsync(profile);

                LogMessage?.Invoke(this, $"[DONE] {result.FilesCopied} files, {result.TotalSize / 1024 / 1024:N1} MB, {result.Errors} errors");
            }
            catch (OperationCanceledException)
            {
                result.EndTime = DateTime.Now;
                LogMessage?.Invoke(this, "[CANCELLED] Backup cancelled by user");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.EndTime = DateTime.Now;
                result.ErrorMessages.Add(ex.Message);
                LogMessage?.Invoke(this, $"[ERROR] {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Remove old backups beyond the retention limit.
        /// </summary>
        private async Task EnforceRetentionAsync(BackupProfile profile)
        {
            if (profile.MaxVersions <= 0) return;

            await Task.Run(() =>
            {
                try
                {
                    if (!Directory.Exists(profile.DestinationFolder)) return;

                    var prefix = profile.Name + "_";
                    var backups = new List<(string path, DateTime created)>();

                    // Find ZIP backups
                    foreach (var zip in Directory.GetFiles(profile.DestinationFolder, $"{prefix}*.zip"))
                        backups.Add((zip, File.GetCreationTime(zip)));

                    // Find folder backups
                    foreach (var dir in Directory.GetDirectories(profile.DestinationFolder, $"{prefix}*"))
                        backups.Add((dir, Directory.GetCreationTime(dir)));

                    var toDelete = backups.OrderByDescending(b => b.created)
                        .Skip(profile.MaxVersions)
                        .ToList();

                    foreach (var (path, _) in toDelete)
                    {
                        try
                        {
                            if (File.Exists(path)) File.Delete(path);
                            else if (Directory.Exists(path)) Directory.Delete(path, true);
                            LogMessage?.Invoke(this, $"[RETENTION] Deleted old backup: {Path.GetFileName(path)}");
                        }
                        catch { }
                    }
                }
                catch { }
            });
        }

        private static bool IsExcluded(string filePath, List<string> patterns)
        {
            var fileName = Path.GetFileName(filePath);
            foreach (var pattern in patterns)
            {
                if (pattern.Contains('*'))
                {
                    var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                    if (System.Text.RegularExpressions.Regex.IsMatch(fileName, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                        return true;
                }
                else if (fileName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public async Task SaveProfileAsync(BackupProfile profile)
        {
            Directory.CreateDirectory(ProfilesDir);
            var path = Path.Combine(ProfilesDir, $"{profile.Id}.json");
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public async Task<List<BackupProfile>> LoadAllProfilesAsync()
        {
            var profiles = new List<BackupProfile>();
            if (!Directory.Exists(ProfilesDir)) return profiles;

            foreach (var file in Directory.GetFiles(ProfilesDir, "*.json"))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var profile = JsonSerializer.Deserialize<BackupProfile>(json);
                    if (profile != null) profiles.Add(profile);
                }
                catch { }
            }

            return profiles;
        }

        public void DeleteProfile(string id)
        {
            var path = Path.Combine(ProfilesDir, $"{id}.json");
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// List existing backups for a profile.
        /// </summary>
        public List<(string Path, DateTime Created, long Size)> GetExistingBackups(BackupProfile profile)
        {
            var backups = new List<(string, DateTime, long)>();
            if (!Directory.Exists(profile.DestinationFolder)) return backups;

            var prefix = profile.Name + "_";

            foreach (var zip in Directory.GetFiles(profile.DestinationFolder, $"{prefix}*.zip"))
            {
                var fi = new FileInfo(zip);
                backups.Add((zip, fi.CreationTime, fi.Length));
            }

            foreach (var dir in Directory.GetDirectories(profile.DestinationFolder, $"{prefix}*"))
            {
                var di = new DirectoryInfo(dir);
                var size = di.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                backups.Add((dir, di.CreationTime, size));
            }

            return backups.OrderByDescending(b => b.Item2).ToList();
        }
    }
}
