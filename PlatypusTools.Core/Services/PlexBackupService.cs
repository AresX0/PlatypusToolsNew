using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Native C# implementation of Plex Media Server backup and restore functionality.
    /// Based on PlexBackup.ps1 by Alek Davis (https://github.com/alekdavis/PlexBackup)
    /// </summary>
    public class PlexBackupService
    {
        #region Constants

        // Default Plex paths
        private static readonly string DefaultPlexAppDataDir = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plex Media Server");
        
        private static readonly string DefaultPlexServerPath = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Plex", "Plex Media Server", "Plex Media Server.exe");

        private static readonly string Default7ZipPath = 
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");

        // Registry keys for Plex
        private static readonly string[] PlexRegistryKeys = new[]
        {
            @"HKEY_CURRENT_USER\Software\Plex, Inc.\Plex Media Server",
            @"HKEY_USERS\.DEFAULT\Software\Plex, Inc.\Plex Media Server"
        };

        // Folders to exclude from backup (non-essential)
        private static readonly string[] DefaultExcludeDirs = new[]
        {
            "Diagnostics",
            "Crash Reports", 
            "Updates",
            "Logs"
        };

        // File types to exclude
        private static readonly string[] DefaultExcludeFiles = new[]
        {
            "*.bif",      // Thumbnail previews
            "Transcode"   // Cache folder for transcoding
        };

        // Special folders with potentially long paths
        private static readonly string[] SpecialDirs = new[]
        {
            @"Plug-in Support\Data\com.plexapp.system\DataItems\Deactivated"
        };

        // Backup subfolder structure
        private const string SubdirFiles = "1";
        private const string SubdirFolders = "2";
        private const string SubdirRegistry = "3";
        private const string SubdirSpecial = "4";

        private const string BackupFilename = "Plex";
        private const string VersionFileName = "Version.txt";
        private const string BackupDirNameFormat = "yyyyMMddHHmmss";
        private static readonly Regex BackupDirNameRegex = new(@"^\d{14}$", RegexOptions.Compiled);

        #endregion

        #region Events

        public event EventHandler<PlexBackupProgressEventArgs>? ProgressChanged;
        public event EventHandler<PlexBackupLogEventArgs>? LogMessage;

        #endregion

        #region Properties

        public PlexBackupOptions Options { get; set; } = new();

        #endregion

        #region Public Methods

        /// <summary>
        /// Checks if Plex Media Server is installed and gets its status.
        /// </summary>
        public PlexStatus GetPlexStatus()
        {
            var status = new PlexStatus();

            // Check if Plex app data folder exists
            var appDataDir = string.IsNullOrEmpty(Options.PlexAppDataDir) 
                ? DefaultPlexAppDataDir 
                : Options.PlexAppDataDir;
            status.AppDataFolderExists = Directory.Exists(appDataDir);
            status.AppDataPath = appDataDir;

            // Check if Plex is running
            var plexProcesses = Process.GetProcessesByName("Plex Media Server");
            status.IsRunning = plexProcesses.Length > 0;

            if (status.IsRunning && plexProcesses.Length > 0)
            {
                try
                {
                    status.ExecutablePath = plexProcesses[0].MainModule?.FileName;
                }
                catch { /* Access denied */ }
            }

            // Try to get Plex version
            if (!string.IsNullOrEmpty(status.ExecutablePath) && File.Exists(status.ExecutablePath))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(status.ExecutablePath);
                    status.Version = versionInfo.FileVersion;
                }
                catch { }
            }
            else if (File.Exists(DefaultPlexServerPath))
            {
                try
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(DefaultPlexServerPath);
                    status.Version = versionInfo.FileVersion;
                    status.ExecutablePath = DefaultPlexServerPath;
                }
                catch { }
            }

            // Check for running Plex services
            try
            {
                var services = System.ServiceProcess.ServiceController.GetServices();
                status.RunningServices = services
                    .Where(s => s.DisplayName.StartsWith("Plex", StringComparison.OrdinalIgnoreCase) && 
                               s.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                    .Select(s => s.DisplayName)
                    .ToList();
            }
            catch { }

            // Check registry
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Plex, Inc.\Plex Media Server");
                status.RegistryKeyExists = key != null;
            }
            catch { }

            return status;
        }

        /// <summary>
        /// Gets a list of available backups in the backup root directory.
        /// </summary>
        public List<PlexBackupInfo> GetAvailableBackups()
        {
            var backups = new List<PlexBackupInfo>();
            var backupRoot = string.IsNullOrEmpty(Options.BackupRootDir) 
                ? Environment.CurrentDirectory 
                : Options.BackupRootDir;

            if (!Directory.Exists(backupRoot))
                return backups;

            var directories = Directory.GetDirectories(backupRoot)
                .Select(d => new DirectoryInfo(d))
                .Where(d => BackupDirNameRegex.IsMatch(d.Name))
                .OrderByDescending(d => d.Name);

            foreach (var dir in directories)
            {
                var backup = new PlexBackupInfo
                {
                    Path = dir.FullName,
                    Name = dir.Name,
                    Created = dir.CreationTime
                };

                // Try to parse timestamp from name
                if (DateTime.TryParseExact(dir.Name, BackupDirNameFormat, 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, out var parsed))
                {
                    backup.BackupTime = parsed;
                }

                // Check for version file
                var versionFile = Path.Combine(dir.FullName, VersionFileName);
                if (File.Exists(versionFile))
                {
                    try
                    {
                        backup.PlexVersion = File.ReadAllText(versionFile).Trim();
                    }
                    catch { }
                }

                // Calculate size
                try
                {
                    backup.SizeBytes = GetDirectorySize(dir.FullName);
                    backup.FileCount = Directory.GetFiles(dir.FullName, "*", SearchOption.AllDirectories).Length;
                }
                catch { }

                // Determine backup type
                var filesSubdir = Path.Combine(dir.FullName, SubdirFiles);
                if (Directory.Exists(filesSubdir))
                {
                    var hasZip = Directory.GetFiles(filesSubdir, "*.zip").Any();
                    var has7z = Directory.GetFiles(filesSubdir, "*.7z").Any();
                    var hasRaw = Directory.GetDirectories(filesSubdir).Any();

                    if (has7z) backup.BackupType = PlexBackupType.SevenZip;
                    else if (hasRaw) backup.BackupType = PlexBackupType.Robocopy;
                    else backup.BackupType = PlexBackupType.Default;
                }

                backups.Add(backup);
            }

            return backups;
        }

        /// <summary>
        /// Performs a backup operation.
        /// </summary>
        public async Task<PlexBackupResult> BackupAsync(CancellationToken cancellationToken = default)
        {
            var result = new PlexBackupResult { StartTime = DateTime.Now };
            var stoppedServices = new List<System.ServiceProcess.ServiceController>();
            bool plexWasRunning = false;
            string? plexExePath = null;

            try
            {
                Log(PlexLogLevel.Info, "Starting backup operation...");
                ReportProgress(0, "Initializing backup...");

                // Validate options
                ValidateOptions(PlexBackupMode.Backup);

                // Get Plex status
                var plexStatus = GetPlexStatus();
                plexExePath = plexStatus.ExecutablePath;
                plexWasRunning = plexStatus.IsRunning;

                if (!plexStatus.AppDataFolderExists)
                {
                    throw new InvalidOperationException($"Plex app data folder not found: {Options.PlexAppDataDir ?? DefaultPlexAppDataDir}");
                }

                // Create backup directory
                var backupDirName = DateTime.Now.ToString(BackupDirNameFormat);
                var backupDir = Path.Combine(Options.BackupRootDir ?? Environment.CurrentDirectory, backupDirName);
                result.BackupPath = backupDir;

                Log(PlexLogLevel.Info, $"Backup will be saved to: {backupDir}");
                ReportProgress(5, "Creating backup directory...");

                if (!Options.TestMode)
                {
                    Directory.CreateDirectory(backupDir);
                    Directory.CreateDirectory(Path.Combine(backupDir, SubdirFiles));
                    Directory.CreateDirectory(Path.Combine(backupDir, SubdirFolders));
                    Directory.CreateDirectory(Path.Combine(backupDir, SubdirRegistry));
                    Directory.CreateDirectory(Path.Combine(backupDir, SubdirSpecial));
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Purge old backups
                if (Options.KeepBackups > 0)
                {
                    ReportProgress(8, "Purging old backups...");
                    await PurgeOldBackupsAsync(backupDirName, cancellationToken);
                }

                // Save Plex version
                if (!string.IsNullOrEmpty(plexStatus.Version) && !Options.TestMode)
                {
                    var versionFile = Path.Combine(backupDir, VersionFileName);
                    await File.WriteAllTextAsync(versionFile, plexStatus.Version, cancellationToken);
                    Log(PlexLogLevel.Info, $"Saved Plex version: {plexStatus.Version}");
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Stop Plex services and process
                ReportProgress(10, "Stopping Plex services...");
                stoppedServices = await StopPlexServicesAsync(cancellationToken);

                ReportProgress(15, "Stopping Plex Media Server...");
                await StopPlexMediaServerAsync(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                // Perform backup based on type
                var plexAppDataDir = Options.PlexAppDataDir ?? DefaultPlexAppDataDir;

                if (Options.BackupType == PlexBackupType.Robocopy)
                {
                    ReportProgress(20, "Backing up using Robocopy...");
                    await BackupWithRobocopyAsync(plexAppDataDir, backupDir, cancellationToken);
                }
                else
                {
                    // Handle special folders first
                    ReportProgress(20, "Processing special folders...");
                    await BackupSpecialFoldersAsync(plexAppDataDir, backupDir, cancellationToken);

                    // Compress files
                    ReportProgress(25, "Compressing Plex data...");
                    await CompressPlexDataAsync(plexAppDataDir, backupDir, cancellationToken);

                    // Restore special folders to original location
                    await RestoreSpecialFoldersAsync(plexAppDataDir, backupDir, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Export registry keys
                ReportProgress(85, "Exporting registry keys...");
                await ExportRegistryKeysAsync(backupDir, cancellationToken);

                // Calculate statistics
                ReportProgress(95, "Calculating backup statistics...");
                result.FileCount = Directory.GetFiles(backupDir, "*", SearchOption.AllDirectories).Length;
                result.TotalSizeBytes = GetDirectorySize(backupDir);

                result.Success = true;
                result.EndTime = DateTime.Now;
                Log(PlexLogLevel.Info, $"Backup completed successfully! {result.FileCount} files, {FormatSize(result.TotalSizeBytes)}");
                ReportProgress(100, "Backup completed successfully!");
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Operation was cancelled.";
                Log(PlexLogLevel.Warning, "Backup operation cancelled.");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Log(PlexLogLevel.Error, $"Backup failed: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;

                // Restart Plex if needed
                if (!Options.NoRestart && (result.Success || Options.Mode != PlexBackupMode.Restore))
                {
                    try
                    {
                        ReportProgress(98, "Restarting Plex services...");
                        await StartPlexServicesAsync(stoppedServices, cancellationToken);

                        if (plexWasRunning && !string.IsNullOrEmpty(plexExePath))
                        {
                            await StartPlexMediaServerAsync(plexExePath, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(PlexLogLevel.Warning, $"Failed to restart Plex: {ex.Message}");
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Performs a restore operation.
        /// </summary>
        public async Task<PlexBackupResult> RestoreAsync(string? backupPath = null, CancellationToken cancellationToken = default)
        {
            var result = new PlexBackupResult { StartTime = DateTime.Now };
            var stoppedServices = new List<System.ServiceProcess.ServiceController>();
            bool plexWasRunning = false;
            string? plexExePath = null;

            try
            {
                Log(PlexLogLevel.Info, "Starting restore operation...");
                ReportProgress(0, "Initializing restore...");

                // Validate options
                ValidateOptions(PlexBackupMode.Restore);

                // Determine backup path
                var restoreFromPath = backupPath ?? Options.BackupDir;
                if (string.IsNullOrEmpty(restoreFromPath))
                {
                    var backups = GetAvailableBackups();
                    if (backups.Count == 0)
                    {
                        throw new InvalidOperationException("No backups found in the backup root directory.");
                    }
                    restoreFromPath = backups[0].Path;
                }

                if (!Directory.Exists(restoreFromPath))
                {
                    throw new DirectoryNotFoundException($"Backup directory not found: {restoreFromPath}");
                }

                result.BackupPath = restoreFromPath;
                Log(PlexLogLevel.Info, $"Restoring from: {restoreFromPath}");

                // Version check
                if (!Options.NoVersionCheck)
                {
                    var versionFile = Path.Combine(restoreFromPath, VersionFileName);
                    if (File.Exists(versionFile))
                    {
                        var backupVersion = await File.ReadAllTextAsync(versionFile, cancellationToken);
                        var plexStatus = GetPlexStatus();
                        
                        if (!string.IsNullOrEmpty(plexStatus.Version) && 
                            !string.IsNullOrEmpty(backupVersion) &&
                            plexStatus.Version.Trim() != backupVersion.Trim())
                        {
                            throw new InvalidOperationException(
                                $"Version mismatch! Backup version: {backupVersion.Trim()}, Current Plex version: {plexStatus.Version}. " +
                                "Use NoVersionCheck option to override.");
                        }
                    }
                }

                // Get Plex status
                var status = GetPlexStatus();
                plexExePath = status.ExecutablePath;
                plexWasRunning = status.IsRunning;

                cancellationToken.ThrowIfCancellationRequested();

                // Stop Plex services and process
                ReportProgress(10, "Stopping Plex services...");
                stoppedServices = await StopPlexServicesAsync(cancellationToken);

                ReportProgress(15, "Stopping Plex Media Server...");
                await StopPlexMediaServerAsync(cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();

                var plexAppDataDir = Options.PlexAppDataDir ?? DefaultPlexAppDataDir;

                // Determine backup type and restore
                var filesSubdir = Path.Combine(restoreFromPath, SubdirFiles);
                var foldersSubdir = Path.Combine(restoreFromPath, SubdirFolders);

                if (Directory.Exists(filesSubdir) && Directory.GetDirectories(filesSubdir).Any())
                {
                    // Robocopy backup - mirror restore
                    ReportProgress(20, "Restoring using Robocopy...");
                    await RestoreWithRobocopyAsync(restoreFromPath, plexAppDataDir, cancellationToken);
                }
                else
                {
                    // Compressed backup - extract
                    ReportProgress(20, "Extracting backup files...");
                    await DecompressPlexDataAsync(restoreFromPath, plexAppDataDir, cancellationToken);

                    // Restore special folders
                    ReportProgress(80, "Restoring special folders...");
                    await RestoreSpecialFoldersAsync(plexAppDataDir, restoreFromPath, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Import registry keys
                ReportProgress(85, "Importing registry keys...");
                await ImportRegistryKeysAsync(restoreFromPath, cancellationToken);

                result.Success = true;
                result.EndTime = DateTime.Now;
                Log(PlexLogLevel.Info, "Restore completed successfully!");
                ReportProgress(100, "Restore completed successfully!");
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Operation was cancelled.";
                Log(PlexLogLevel.Warning, "Restore operation cancelled.");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                Log(PlexLogLevel.Error, $"Restore failed: {ex.Message}");
            }
            finally
            {
                result.EndTime = DateTime.Now;

                // Restart Plex if needed
                if (!Options.NoRestart && result.Success)
                {
                    try
                    {
                        ReportProgress(98, "Restarting Plex services...");
                        await StartPlexServicesAsync(stoppedServices, cancellationToken);

                        if (!string.IsNullOrEmpty(plexExePath))
                        {
                            await StartPlexMediaServerAsync(plexExePath, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log(PlexLogLevel.Warning, $"Failed to restart Plex: {ex.Message}");
                    }
                }
            }

            return result;
        }

        #endregion

        #region Private Methods - Backup Operations

        private async Task BackupWithRobocopyAsync(string sourceDir, string backupDir, CancellationToken cancellationToken)
        {
            var targetDir = Path.Combine(backupDir, SubdirFiles);

            var excludeDirs = Options.ExcludeDirs ?? DefaultExcludeDirs;
            var excludeFiles = Options.ExcludeFiles ?? DefaultExcludeFiles;

            var args = new StringBuilder();
            args.Append($"\"{sourceDir}\" \"{targetDir}\" /MIR /R:{Options.Retries} /W:{Options.RetryWaitSec} /MT");

            if (excludeDirs.Length > 0)
            {
                args.Append(" /XD");
                foreach (var dir in excludeDirs)
                {
                    args.Append($" \"{Path.Combine(sourceDir, dir)}\"");
                }
            }

            if (excludeFiles.Length > 0)
            {
                args.Append(" /XF");
                foreach (var file in excludeFiles)
                {
                    args.Append($" \"{file}\"");
                }
            }

            Log(PlexLogLevel.Info, "Starting Robocopy backup...");
            Log(PlexLogLevel.Debug, $"robocopy {args}");

            if (!Options.TestMode)
            {
                await RunProcessAsync("robocopy", args.ToString(), cancellationToken, allowedExitCodes: new[] { 0, 1, 2, 3 });
            }

            Log(PlexLogLevel.Info, "Robocopy backup completed.");
        }

        private async Task RestoreWithRobocopyAsync(string backupDir, string targetDir, CancellationToken cancellationToken)
        {
            var sourceDir = Path.Combine(backupDir, SubdirFiles);

            var args = $"\"{sourceDir}\" \"{targetDir}\" /MIR /R:{Options.Retries} /W:{Options.RetryWaitSec} /MT";

            Log(PlexLogLevel.Info, "Starting Robocopy restore...");

            if (!Options.TestMode)
            {
                await RunProcessAsync("robocopy", args, cancellationToken, allowedExitCodes: new[] { 0, 1, 2, 3 });
            }

            Log(PlexLogLevel.Info, "Robocopy restore completed.");
        }

        private async Task CompressPlexDataAsync(string plexAppDataDir, string backupDir, CancellationToken cancellationToken)
        {
            var excludeDirs = Options.ExcludeDirs ?? DefaultExcludeDirs;
            var excludeFiles = Options.ExcludeFiles ?? DefaultExcludeFiles;

            // Backup root files
            var rootFiles = Directory.GetFiles(plexAppDataDir);
            if (rootFiles.Length > 0)
            {
                var rootZipPath = Path.Combine(backupDir, SubdirFiles, $"{BackupFilename}.zip");
                Log(PlexLogLevel.Info, $"Compressing root files to: {rootZipPath}");
                ReportProgress(30, "Compressing root files...");

                if (!Options.TestMode)
                {
                    if (Options.BackupType == PlexBackupType.SevenZip)
                    {
                        await CompressWithSevenZipAsync(rootFiles, rootZipPath, cancellationToken);
                    }
                    else
                    {
                        using var zipStream = new FileStream(rootZipPath, FileMode.Create);
                        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create);
                        
                        foreach (var file in rootFiles)
                        {
                            var fileName = Path.GetFileName(file);
                            if (!excludeFiles.Any(ef => MatchesWildcard(fileName, ef)))
                            {
                                archive.CreateEntryFromFile(file, fileName, CompressionLevel.Optimal);
                            }
                        }
                    }
                }
            }

            // Backup subdirectories
            var subdirs = Directory.GetDirectories(plexAppDataDir)
                .Select(d => new DirectoryInfo(d))
                .Where(d => !excludeDirs.Contains(d.Name, StringComparer.OrdinalIgnoreCase))
                .Where(d => !SpecialDirs.Any(sd => d.FullName.Contains(sd, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            var foldersDir = Path.Combine(backupDir, SubdirFolders);
            var totalDirs = subdirs.Count;
            var processedDirs = 0;

            foreach (var subdir in subdirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                processedDirs++;
                var progress = 30 + (int)(50.0 * processedDirs / totalDirs);
                ReportProgress(progress, $"Compressing: {subdir.Name}");

                var zipPath = Path.Combine(foldersDir, $"{subdir.Name}.zip");
                Log(PlexLogLevel.Info, $"Compressing folder: {subdir.Name}");

                if (!Options.TestMode)
                {
                    if (Options.BackupType == PlexBackupType.SevenZip)
                    {
                        await CompressFolderWithSevenZipAsync(subdir.FullName, zipPath, excludeFiles, cancellationToken);
                    }
                    else
                    {
                        try
                        {
                            if (File.Exists(zipPath)) File.Delete(zipPath);
                            ZipFile.CreateFromDirectory(subdir.FullName, zipPath, CompressionLevel.Optimal, false);
                        }
                        catch (PathTooLongException)
                        {
                            Log(PlexLogLevel.Warning, $"Path too long in folder '{subdir.Name}', using robocopy instead...");
                            var altPath = Path.Combine(foldersDir, subdir.Name);
                            await RunProcessAsync("robocopy", $"\"{subdir.FullName}\" \"{altPath}\" /MIR /R:3 /W:5", 
                                cancellationToken, allowedExitCodes: new[] { 0, 1, 2, 3 });
                        }
                    }
                }
            }
        }

        private async Task DecompressPlexDataAsync(string backupDir, string plexAppDataDir, CancellationToken cancellationToken)
        {
            var filesDir = Path.Combine(backupDir, SubdirFiles);
            var foldersDir = Path.Combine(backupDir, SubdirFolders);

            // Ensure target directory exists
            if (!Options.TestMode)
            {
                Directory.CreateDirectory(plexAppDataDir);
            }

            // Extract root files
            var rootZip = Path.Combine(filesDir, $"{BackupFilename}.zip");
            var root7z = Path.Combine(filesDir, $"{BackupFilename}.7z");

            if (File.Exists(rootZip) || File.Exists(root7z))
            {
                var zipPath = File.Exists(root7z) ? root7z : rootZip;
                Log(PlexLogLevel.Info, $"Extracting root files from: {zipPath}");
                ReportProgress(25, "Extracting root files...");

                if (!Options.TestMode)
                {
                    if (zipPath.EndsWith(".7z"))
                    {
                        await ExtractWithSevenZipAsync(zipPath, plexAppDataDir, cancellationToken);
                    }
                    else
                    {
                        ZipFile.ExtractToDirectory(zipPath, plexAppDataDir, true);
                    }
                }
            }

            // Extract folder archives
            if (Directory.Exists(foldersDir))
            {
                var archives = Directory.GetFiles(foldersDir, "*.zip")
                    .Concat(Directory.GetFiles(foldersDir, "*.7z"))
                    .ToList();

                var totalArchives = archives.Count;
                var processed = 0;

                foreach (var archive in archives)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    processed++;
                    var progress = 30 + (int)(45.0 * processed / totalArchives);
                    var folderName = Path.GetFileNameWithoutExtension(archive);
                    ReportProgress(progress, $"Extracting: {folderName}");

                    var targetPath = Path.Combine(plexAppDataDir, folderName);
                    Log(PlexLogLevel.Info, $"Extracting: {folderName}");

                    if (!Options.TestMode)
                    {
                        Directory.CreateDirectory(targetPath);

                        if (archive.EndsWith(".7z"))
                        {
                            await ExtractWithSevenZipAsync(archive, targetPath, cancellationToken);
                        }
                        else
                        {
                            ZipFile.ExtractToDirectory(archive, targetPath, true);
                        }
                    }
                }

                // Handle uncompressed folders (from robocopy fallback)
                var folders = Directory.GetDirectories(foldersDir);
                foreach (var folder in folders)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var folderName = Path.GetFileName(folder);
                    var targetPath = Path.Combine(plexAppDataDir, folderName);
                    Log(PlexLogLevel.Info, $"Copying folder: {folderName}");

                    if (!Options.TestMode)
                    {
                        await RunProcessAsync("robocopy", $"\"{folder}\" \"{targetPath}\" /MIR /R:3 /W:5",
                            cancellationToken, allowedExitCodes: new[] { 0, 1, 2, 3 });
                    }
                }
            }
        }

        private async Task BackupSpecialFoldersAsync(string plexAppDataDir, string backupDir, CancellationToken cancellationToken)
        {
            var specialBackupDir = Path.Combine(backupDir, SubdirSpecial);

            foreach (var specialDir in SpecialDirs)
            {
                var sourcePath = Path.Combine(plexAppDataDir, specialDir);
                if (!Directory.Exists(sourcePath)) continue;

                var targetPath = Path.Combine(specialBackupDir, specialDir);
                Log(PlexLogLevel.Info, $"Moving special folder: {specialDir}");

                if (!Options.TestMode)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await RunProcessAsync("robocopy", $"\"{sourcePath}\" \"{targetPath}\" /MIR /MOVE /R:3 /W:5",
                        cancellationToken, allowedExitCodes: new[] { 0, 1, 2, 3 });
                }
            }
        }

        private async Task RestoreSpecialFoldersAsync(string plexAppDataDir, string backupDir, CancellationToken cancellationToken)
        {
            var specialBackupDir = Path.Combine(backupDir, SubdirSpecial);
            if (!Directory.Exists(specialBackupDir)) return;

            foreach (var specialDir in SpecialDirs)
            {
                var sourcePath = Path.Combine(specialBackupDir, specialDir);
                if (!Directory.Exists(sourcePath)) continue;

                var targetPath = Path.Combine(plexAppDataDir, specialDir);
                Log(PlexLogLevel.Info, $"Restoring special folder: {specialDir}");

                if (!Options.TestMode)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                    await RunProcessAsync("robocopy", $"\"{sourcePath}\" \"{targetPath}\" /MIR /R:3 /W:5",
                        cancellationToken, allowedExitCodes: new[] { 0, 1, 2, 3 });
                }
            }
        }

        private async Task CompressWithSevenZipAsync(string[] files, string outputPath, CancellationToken cancellationToken)
        {
            var sevenZipPath = Options.SevenZipPath ?? Default7ZipPath;
            if (!File.Exists(sevenZipPath))
            {
                throw new FileNotFoundException($"7-Zip not found at: {sevenZipPath}");
            }

            var fileList = string.Join("\" \"", files);
            var args = $"a \"{outputPath}\" \"{fileList}\" -y";

            await RunProcessAsync(sevenZipPath, args, cancellationToken);
        }

        private async Task CompressFolderWithSevenZipAsync(string folderPath, string outputPath, string[] excludeFiles, CancellationToken cancellationToken)
        {
            var sevenZipPath = Options.SevenZipPath ?? Default7ZipPath;
            if (!File.Exists(sevenZipPath))
            {
                throw new FileNotFoundException($"7-Zip not found at: {sevenZipPath}");
            }

            // Change extension to .7z
            outputPath = Path.ChangeExtension(outputPath, ".7z");

            var args = new StringBuilder();
            args.Append($"a \"{outputPath}\" \"{Path.Combine(folderPath, "*")}\" -r -y");

            foreach (var exclude in excludeFiles)
            {
                args.Append($" -x!{exclude}");
            }

            await RunProcessAsync(sevenZipPath, args.ToString(), cancellationToken);
        }

        private async Task ExtractWithSevenZipAsync(string archivePath, string outputPath, CancellationToken cancellationToken)
        {
            var sevenZipPath = Options.SevenZipPath ?? Default7ZipPath;
            if (!File.Exists(sevenZipPath))
            {
                throw new FileNotFoundException($"7-Zip not found at: {sevenZipPath}");
            }

            var args = $"x \"{archivePath}\" -o\"{outputPath}\" -aoa -y";
            await RunProcessAsync(sevenZipPath, args, cancellationToken);
        }

        #endregion

        #region Private Methods - Registry Operations

        private async Task ExportRegistryKeysAsync(string backupDir, CancellationToken cancellationToken)
        {
            var registryDir = Path.Combine(backupDir, SubdirRegistry);

            foreach (var regKeyPath in PlexRegistryKeys)
            {
                try
                {
                    // Convert HKEY path for reg.exe
                    var exportPath = regKeyPath.Replace("HKEY_CURRENT_USER", "HKCU").Replace("HKEY_USERS", "HKU");
                    
                    // Check if key exists
                    var hive = regKeyPath.StartsWith("HKEY_CURRENT_USER") ? Registry.CurrentUser : Registry.Users;
                    var subKeyPath = regKeyPath.Substring(regKeyPath.IndexOf('\\') + 1);
                    
                    using var key = hive.OpenSubKey(subKeyPath);
                    if (key == null) continue;

                    var fileName = ComputeMd5Hash(regKeyPath) + ".reg";
                    var filePath = Path.Combine(registryDir, fileName);

                    Log(PlexLogLevel.Info, $"Exporting registry key: {regKeyPath}");

                    if (!Options.TestMode)
                    {
                        await RunProcessAsync("reg", $"export \"{exportPath}\" \"{filePath}\" /y", cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    Log(PlexLogLevel.Warning, $"Failed to export registry key '{regKeyPath}': {ex.Message}");
                }
            }
        }

        private async Task ImportRegistryKeysAsync(string backupDir, CancellationToken cancellationToken)
        {
            var registryDir = Path.Combine(backupDir, SubdirRegistry);
            if (!Directory.Exists(registryDir)) return;

            var regFiles = Directory.GetFiles(registryDir, "*.reg");

            foreach (var regFile in regFiles)
            {
                try
                {
                    Log(PlexLogLevel.Info, $"Importing registry file: {Path.GetFileName(regFile)}");

                    if (!Options.TestMode)
                    {
                        await RunProcessAsync("reg", $"import \"{regFile}\"", cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    Log(PlexLogLevel.Warning, $"Failed to import registry file '{regFile}': {ex.Message}");
                }
            }
        }

        #endregion

        #region Private Methods - Plex Process Management

        private async Task<List<System.ServiceProcess.ServiceController>> StopPlexServicesAsync(CancellationToken cancellationToken)
        {
            var stoppedServices = new List<System.ServiceProcess.ServiceController>();

            try
            {
                var services = System.ServiceProcess.ServiceController.GetServices()
                    .Where(s => s.DisplayName.StartsWith("Plex", StringComparison.OrdinalIgnoreCase) &&
                               s.Status == System.ServiceProcess.ServiceControllerStatus.Running)
                    .ToList();

                foreach (var service in services)
                {
                    try
                    {
                        Log(PlexLogLevel.Info, $"Stopping service: {service.DisplayName}");

                        if (!Options.TestMode)
                        {
                            service.Stop();
                            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                        }

                        stoppedServices.Add(service);
                    }
                    catch (Exception ex)
                    {
                        Log(PlexLogLevel.Warning, $"Failed to stop service '{service.DisplayName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log(PlexLogLevel.Warning, $"Error enumerating Plex services: {ex.Message}");
            }

            return stoppedServices;
        }

        private async Task StartPlexServicesAsync(List<System.ServiceProcess.ServiceController> services, CancellationToken cancellationToken)
        {
            foreach (var service in services)
            {
                try
                {
                    // Refresh service status
                    service.Refresh();
                    
                    if (service.Status != System.ServiceProcess.ServiceControllerStatus.Running)
                    {
                        Log(PlexLogLevel.Info, $"Starting service: {service.DisplayName}");

                        if (!Options.TestMode)
                        {
                            service.Start();
                            service.WaitForStatus(System.ServiceProcess.ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log(PlexLogLevel.Warning, $"Failed to start service '{service.DisplayName}': {ex.Message}");
                }
            }
        }

        private async Task StopPlexMediaServerAsync(CancellationToken cancellationToken)
        {
            var processes = Process.GetProcessesByName("Plex Media Server");
            if (processes.Length == 0) return;

            Log(PlexLogLevel.Info, "Stopping Plex Media Server process...");

            if (Options.TestMode) return;

            // Try graceful shutdown first
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = "/im \"Plex Media Server.exe\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                await process!.WaitForExitAsync(cancellationToken);

                // Wait for graceful shutdown
                var timeout = DateTime.Now.AddSeconds(60);
                while (DateTime.Now < timeout)
                {
                    await Task.Delay(1000, cancellationToken);
                    processes = Process.GetProcessesByName("Plex Media Server");
                    if (processes.Length == 0) break;
                }
            }
            catch { }

            // Force kill if still running
            processes = Process.GetProcessesByName("Plex Media Server");
            if (processes.Length > 0)
            {
                Log(PlexLogLevel.Warning, "Force killing Plex Media Server...");

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "taskkill",
                        Arguments = "/f /im \"Plex Media Server.exe\" /t",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(psi);
                    await process!.WaitForExitAsync(cancellationToken);
                }
                catch { }
            }

            Log(PlexLogLevel.Info, "Plex Media Server stopped.");
        }

        private async Task StartPlexMediaServerAsync(string exePath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return;

            // Check if already running
            var processes = Process.GetProcessesByName("Plex Media Server");
            if (processes.Length > 0)
            {
                Log(PlexLogLevel.Info, "Plex Media Server is already running.");
                return;
            }

            Log(PlexLogLevel.Info, "Starting Plex Media Server...");

            if (Options.TestMode) return;

            try
            {
                // Start with reduced privileges using runas /trustlevel
                var psi = new ProcessStartInfo
                {
                    FileName = "runas",
                    Arguments = $"/trustlevel:0x20000 \"{exePath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                // Don't wait for the Plex process to exit
            }
            catch (Exception ex)
            {
                Log(PlexLogLevel.Warning, $"Failed to start Plex Media Server with runas, trying direct start: {ex.Message}");

                try
                {
                    Process.Start(exePath);
                }
                catch (Exception ex2)
                {
                    Log(PlexLogLevel.Error, $"Failed to start Plex Media Server: {ex2.Message}");
                }
            }
        }

        #endregion

        #region Private Methods - Utility

        private async Task PurgeOldBackupsAsync(string currentBackupName, CancellationToken cancellationToken)
        {
            var backupRoot = Options.BackupRootDir ?? Environment.CurrentDirectory;
            if (!Directory.Exists(backupRoot)) return;

            var oldBackups = Directory.GetDirectories(backupRoot)
                .Select(d => new DirectoryInfo(d))
                .Where(d => BackupDirNameRegex.IsMatch(d.Name) && d.Name != currentBackupName)
                .OrderByDescending(d => d.Name)
                .Skip(Options.KeepBackups - 1)
                .ToList();

            foreach (var backup in oldBackups)
            {
                try
                {
                    Log(PlexLogLevel.Info, $"Deleting old backup: {backup.Name}");

                    if (!Options.TestMode)
                    {
                        backup.Delete(true);
                    }
                }
                catch (Exception ex)
                {
                    Log(PlexLogLevel.Warning, $"Failed to delete old backup '{backup.Name}': {ex.Message}");
                }
            }
        }

        private void ValidateOptions(PlexBackupMode mode)
        {
            if (mode == PlexBackupMode.Backup || mode == PlexBackupMode.Continue)
            {
                var appDataDir = Options.PlexAppDataDir ?? DefaultPlexAppDataDir;
                if (!Directory.Exists(appDataDir))
                {
                    throw new DirectoryNotFoundException($"Plex app data folder not found: {appDataDir}");
                }
            }

            if (Options.BackupType == PlexBackupType.SevenZip)
            {
                var sevenZipPath = Options.SevenZipPath ?? Default7ZipPath;
                if (!File.Exists(sevenZipPath))
                {
                    throw new FileNotFoundException($"7-Zip executable not found: {sevenZipPath}");
                }
            }
        }

        private async Task RunProcessAsync(string fileName, string arguments, CancellationToken cancellationToken, int[]? allowedExitCodes = null)
        {
            Log(PlexLogLevel.Debug, $"Running: {fileName} {arguments}");

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(output))
            {
                Log(PlexLogLevel.Debug, output);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                Log(PlexLogLevel.Debug, error);
            }

            var allowed = allowedExitCodes ?? new[] { 0 };
            if (!allowed.Contains(process.ExitCode))
            {
                throw new Exception($"{fileName} failed with exit code {process.ExitCode}: {error}");
            }
        }

        private static long GetDirectorySize(string path)
        {
            try
            {
                return new DirectoryInfo(path)
                    .EnumerateFiles("*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatSize(long bytes)
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

        private static bool MatchesWildcard(string input, string pattern)
        {
            var regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(input, regexPattern, RegexOptions.IgnoreCase);
        }

        private static string ComputeMd5Hash(string input)
        {
            using var md5 = System.Security.Cryptography.MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = md5.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        private void Log(PlexLogLevel level, string message)
        {
            LogMessage?.Invoke(this, new PlexBackupLogEventArgs(level, message));
        }

        private void ReportProgress(int percentage, string status)
        {
            ProgressChanged?.Invoke(this, new PlexBackupProgressEventArgs(percentage, status));
        }

        #endregion
    }

    #region Supporting Types

    public enum PlexBackupMode
    {
        Backup,
        Continue,
        Restore
    }

    public enum PlexBackupType
    {
        Default,    // Built-in ZIP compression
        SevenZip,   // 7-Zip compression
        Robocopy    // File mirroring with Robocopy
    }

    public enum PlexLogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public class PlexBackupOptions
    {
        public PlexBackupMode Mode { get; set; } = PlexBackupMode.Backup;
        public PlexBackupType BackupType { get; set; } = PlexBackupType.Default;
        
        public string? PlexAppDataDir { get; set; }
        public string? BackupRootDir { get; set; }
        public string? BackupDir { get; set; }
        public string? TempDir { get; set; }
        public string? SevenZipPath { get; set; }
        
        public int KeepBackups { get; set; } = 3;
        public int Retries { get; set; } = 5;
        public int RetryWaitSec { get; set; } = 10;
        
        public bool TestMode { get; set; }
        public bool NoRestart { get; set; }
        public bool NoVersionCheck { get; set; }
        public bool Inactive { get; set; }
        
        public string[]? ExcludeDirs { get; set; }
        public string[]? ExcludeFiles { get; set; }
    }

    public class PlexStatus
    {
        public bool IsRunning { get; set; }
        public bool AppDataFolderExists { get; set; }
        public bool RegistryKeyExists { get; set; }
        public string? Version { get; set; }
        public string? ExecutablePath { get; set; }
        public string? AppDataPath { get; set; }
        public List<string> RunningServices { get; set; } = new();
    }

    public class PlexBackupInfo
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime Created { get; set; }
        public DateTime? BackupTime { get; set; }
        public string? PlexVersion { get; set; }
        public long SizeBytes { get; set; }
        public int FileCount { get; set; }
        public PlexBackupType BackupType { get; set; }

        public string FormattedSize => FormatSize(SizeBytes);

        private static string FormatSize(long bytes)
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

    public class PlexBackupResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? BackupPath { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public int FileCount { get; set; }
        public long TotalSizeBytes { get; set; }
    }

    public class PlexBackupProgressEventArgs : EventArgs
    {
        public int Percentage { get; }
        public string Status { get; }

        public PlexBackupProgressEventArgs(int percentage, string status)
        {
            Percentage = percentage;
            Status = status;
        }
    }

    public class PlexBackupLogEventArgs : EventArgs
    {
        public PlexLogLevel Level { get; }
        public string Message { get; }
        public DateTime Timestamp { get; }

        public PlexBackupLogEventArgs(PlexLogLevel level, string message)
        {
            Level = level;
            Message = message;
            Timestamp = DateTime.Now;
        }
    }

    #endregion
}
