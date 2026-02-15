using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for backing up and restoring all application settings.
    /// Creates a compressed archive containing all user configurations.
    /// </summary>
    public class SettingsBackupService
    {
        private static SettingsBackupService? _instance;
        public static SettingsBackupService Instance => _instance ??= new SettingsBackupService();

        private readonly string _appDataPath;
        private readonly string _backupExtension = ".ptbackup";

        public SettingsBackupService()
        {
            _appDataPath = SettingsManager.DataDirectory;
        }

        /// <summary>
        /// Creates a backup of all settings.
        /// </summary>
        /// <param name="backupPath">Path for the backup file.</param>
        /// <returns>True if backup was successful.</returns>
        public async Task<bool> CreateBackupAsync(string backupPath)
        {
            try
            {
                // Ensure backup has correct extension
                if (!backupPath.EndsWith(_backupExtension, StringComparison.OrdinalIgnoreCase))
                {
                    backupPath += _backupExtension;
                }

                // Create temp directory for staging
                var tempDir = Path.Combine(Path.GetTempPath(), $"PlatypusBackup_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Collect settings files
                    await CollectSettingsAsync(tempDir);

                    // Create backup manifest
                    var manifest = new BackupManifest
                    {
                        Version = GetAppVersion(),
                        CreatedAt = DateTime.UtcNow,
                        MachineName = Environment.MachineName,
                        UserName = Environment.UserName,
                        Files = new List<string>()
                    };

                    // List all files in temp dir
                    foreach (var file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        manifest.Files.Add(Path.GetRelativePath(tempDir, file));
                    }

                    // Write manifest
                    var manifestPath = Path.Combine(tempDir, "manifest.json");
                    var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
                    await File.WriteAllTextAsync(manifestPath, manifestJson);

                    // Create ZIP archive
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }

                    ZipFile.CreateFromDirectory(tempDir, backupPath, CompressionLevel.Optimal, false);

                    SimpleLogger.Info($"Settings backup created: {backupPath}");
                    return true;
                }
                finally
                {
                    // Cleanup temp directory
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error creating settings backup: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Restores settings from a backup.
        /// </summary>
        /// <param name="backupPath">Path to the backup file.</param>
        /// <param name="options">Restore options.</param>
        /// <returns>True if restore was successful.</returns>
        public async Task<bool> RestoreBackupAsync(string backupPath, RestoreOptions? options = null)
        {
            options ??= new RestoreOptions();

            try
            {
                if (!File.Exists(backupPath))
                {
                    throw new FileNotFoundException("Backup file not found", backupPath);
                }

                // Create temp directory for extraction
                var tempDir = Path.Combine(Path.GetTempPath(), $"PlatypusRestore_{Guid.NewGuid():N}");
                Directory.CreateDirectory(tempDir);

                try
                {
                    // Extract backup
                    ZipFile.ExtractToDirectory(backupPath, tempDir);

                    // Read manifest
                    var manifestPath = Path.Combine(tempDir, "manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        throw new InvalidDataException("Invalid backup file: manifest not found");
                    }

                    var manifestJson = await File.ReadAllTextAsync(manifestPath);
                    var manifest = JsonSerializer.Deserialize<BackupManifest>(manifestJson);

                    if (manifest == null)
                    {
                        throw new InvalidDataException("Invalid backup file: could not parse manifest");
                    }

                    // Create backup of current settings if requested
                    if (options.BackupCurrentFirst)
                    {
                        var currentBackupPath = Path.Combine(
                            Path.GetDirectoryName(backupPath) ?? _appDataPath,
                            $"pre-restore-backup-{DateTime.Now:yyyyMMdd-HHmmss}{_backupExtension}");
                        await CreateBackupAsync(currentBackupPath);
                    }

                    // Restore files
                    foreach (var relativePath in manifest.Files)
                    {
                        if (relativePath == "manifest.json") continue;

                        var sourcePath = Path.Combine(tempDir, relativePath);
                        var destPath = Path.Combine(_appDataPath, relativePath);

                        // Check restore options
                        if (!ShouldRestoreFile(relativePath, options)) continue;

                        // Ensure destination directory exists
                        var destDir = Path.GetDirectoryName(destPath);
                        if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                        {
                            Directory.CreateDirectory(destDir);
                        }

                        // Copy file
                        File.Copy(sourcePath, destPath, true);
                    }

                    SimpleLogger.Info($"Settings restored from: {backupPath}");
                    return true;
                }
                finally
                {
                    // Cleanup temp directory
                    try
                    {
                        Directory.Delete(tempDir, true);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error restoring settings: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets information about a backup file.
        /// </summary>
        public async Task<BackupManifest?> GetBackupInfoAsync(string backupPath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(backupPath);
                var manifestEntry = archive.GetEntry("manifest.json");
                
                if (manifestEntry == null) return null;

                using var stream = manifestEntry.Open();
                using var reader = new StreamReader(stream);
                var json = await reader.ReadToEndAsync();
                
                return JsonSerializer.Deserialize<BackupManifest>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the default backup file name.
        /// </summary>
        public string GetDefaultBackupFileName()
        {
            return $"PlatypusTools-Backup-{DateTime.Now:yyyyMMdd-HHmmss}{_backupExtension}";
        }

        private async Task CollectSettingsAsync(string destDir)
        {
            if (!Directory.Exists(_appDataPath)) return;

            // Copy settings.json
            var settingsFile = Path.Combine(_appDataPath, "settings.json");
            if (File.Exists(settingsFile))
            {
                File.Copy(settingsFile, Path.Combine(destDir, "settings.json"));
            }

            // Copy shortcuts.json
            var shortcutsFile = Path.Combine(_appDataPath, "shortcuts.json");
            if (File.Exists(shortcutsFile))
            {
                File.Copy(shortcutsFile, Path.Combine(destDir, "shortcuts.json"));
            }

            // Copy workspaces.json
            var workspacesFile = Path.Combine(_appDataPath, "workspaces.json");
            if (File.Exists(workspacesFile))
            {
                File.Copy(workspacesFile, Path.Combine(destDir, "workspaces.json"));
            }

            // Copy Hider config
            var hiderDir = Path.Combine(_appDataPath, "Hider");
            if (Directory.Exists(hiderDir))
            {
                var destHiderDir = Path.Combine(destDir, "Hider");
                Directory.CreateDirectory(destHiderDir);
                await CopyDirectoryAsync(hiderDir, destHiderDir);
            }

            // Copy metadata templates
            var templatesDir = Path.Combine(_appDataPath, "Templates");
            if (Directory.Exists(templatesDir))
            {
                var destTemplatesDir = Path.Combine(destDir, "Templates");
                Directory.CreateDirectory(destTemplatesDir);
                await CopyDirectoryAsync(templatesDir, destTemplatesDir);
            }

            // Copy any custom presets
            var presetsDir = Path.Combine(_appDataPath, "Presets");
            if (Directory.Exists(presetsDir))
            {
                var destPresetsDir = Path.Combine(destDir, "Presets");
                Directory.CreateDirectory(destPresetsDir);
                await CopyDirectoryAsync(presetsDir, destPresetsDir);
            }
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destDir)
        {
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                Directory.CreateDirectory(destSubDir);
                await CopyDirectoryAsync(dir, destSubDir);
            }
        }

        private bool ShouldRestoreFile(string relativePath, RestoreOptions options)
        {
            var fileName = Path.GetFileName(relativePath).ToLowerInvariant();

            // Check individual options
            if (!options.RestoreSettings && fileName == "settings.json") return false;
            if (!options.RestoreShortcuts && fileName == "shortcuts.json") return false;
            if (!options.RestoreWorkspaces && fileName == "workspaces.json") return false;
            if (!options.RestoreHiderConfig && relativePath.StartsWith("Hider", StringComparison.OrdinalIgnoreCase)) return false;
            if (!options.RestoreTemplates && relativePath.StartsWith("Templates", StringComparison.OrdinalIgnoreCase)) return false;
            if (!options.RestorePresets && relativePath.StartsWith("Presets", StringComparison.OrdinalIgnoreCase)) return false;

            return true;
        }

        private string GetAppVersion()
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            return assembly.GetName().Version?.ToString() ?? "0.0.0.0";
        }
    }

    /// <summary>
    /// Backup manifest containing metadata about the backup.
    /// </summary>
    public class BackupManifest
    {
        public string Version { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new();
    }

    /// <summary>
    /// Options for restoring a backup.
    /// </summary>
    public class RestoreOptions
    {
        public bool BackupCurrentFirst { get; set; } = true;
        public bool RestoreSettings { get; set; } = true;
        public bool RestoreShortcuts { get; set; } = true;
        public bool RestoreWorkspaces { get; set; } = true;
        public bool RestoreHiderConfig { get; set; } = true;
        public bool RestoreTemplates { get; set; } = true;
        public bool RestorePresets { get; set; } = true;
    }
}
