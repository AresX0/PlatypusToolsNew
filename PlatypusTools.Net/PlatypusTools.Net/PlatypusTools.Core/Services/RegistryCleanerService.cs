using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for finding and cleaning invalid registry entries
    /// </summary>
    public interface IRegistryCleanerService
    {
        Task<List<RegistryIssue>> ScanRegistry();
        Task<bool> DeleteRegistryKey(string keyPath);
        Task<string> BackupRegistry(string outputPath);
    }

    /// <summary>
    /// Represents a registry issue
    /// </summary>
    public class RegistryIssue
    {
        public string KeyPath { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public RegistrySeverity Severity { get; set; }
        public string? ValueName { get; set; }
        public string? ValueData { get; set; }
    }

    /// <summary>
    /// Severity levels for registry issues
    /// </summary>
    public enum RegistrySeverity
    {
        Low,
        Medium,
        High,
        Critical
    }

    /// <summary>
    /// Implementation of registry cleaner service
    /// </summary>
    public class RegistryCleanerService : IRegistryCleanerService
    {
        private readonly string[] _commonScanPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Classes",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs"
        };

        /// <summary>
        /// Scans the registry for orphaned entries
        /// </summary>
        /// <returns>List of registry issues found</returns>
        public async Task<List<RegistryIssue>> ScanRegistry()
        {
            return await Task.Run(() =>
            {
                var issues = new List<RegistryIssue>();

                try
                {
                    // Scan HKEY_CURRENT_USER
                    using var hkcu = Registry.CurrentUser;
                    foreach (var path in _commonScanPaths)
                    {
                        ScanRegistryPath(hkcu, path, issues);
                    }

                    // Scan HKEY_LOCAL_MACHINE (may require admin)
                    try
                    {
                        using var hklm = Registry.LocalMachine;
                        foreach (var path in _commonScanPaths)
                        {
                            ScanRegistryPath(hklm, path, issues);
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Can't access HKLM without admin rights
                        issues.Add(new RegistryIssue
                        {
                            KeyPath = "HKEY_LOCAL_MACHINE",
                            Type = "Access Denied",
                            Description = "Administrator privileges required to scan HKEY_LOCAL_MACHINE",
                            Severity = RegistrySeverity.Low
                        });
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to scan registry: {ex.Message}", ex);
                }

                return issues;
            });
        }

        /// <summary>
        /// Deletes a registry key with backup
        /// </summary>
        /// <param name="keyPath">Full registry key path</param>
        /// <returns>True if successful</returns>
        public async Task<bool> DeleteRegistryKey(string keyPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Parse the key path
                    var parts = keyPath.Split('\\');
                    if (parts.Length < 2)
                    {
                        throw new ArgumentException("Invalid registry key path");
                    }

                    var rootName = parts[0];
                    var subKeyPath = string.Join("\\", parts.Skip(1));

                    RegistryKey? rootKey = rootName switch
                    {
                        "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
                        "HKEY_LOCAL_MACHINE" or "HKLM" => Registry.LocalMachine,
                        "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
                        "HKEY_USERS" or "HKU" => Registry.Users,
                        "HKEY_CURRENT_CONFIG" or "HKCC" => Registry.CurrentConfig,
                        _ => null
                    };

                    if (rootKey == null)
                    {
                        throw new ArgumentException($"Unknown registry root: {rootName}");
                    }

                    // Backup before delete
                    BackupRegistryKey(rootKey, subKeyPath);

                    // Delete the key
                    using var key = rootKey.OpenSubKey(subKeyPath, writable: true);
                    if (key != null)
                    {
                        rootKey.DeleteSubKeyTree(subKeyPath, throwOnMissingSubKey: false);
                        return true;
                    }

                    return false;
                }
                catch (UnauthorizedAccessException)
                {
                    throw new UnauthorizedAccessException("Administrator privileges required to delete registry keys");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to delete registry key: {ex.Message}", ex);
                }
            });
        }

        /// <summary>
        /// Backs up registry to a file
        /// </summary>
        /// <param name="outputPath">Output file path</param>
        /// <returns>Path to backup file</returns>
        public async Task<string> BackupRegistry(string outputPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var backupFile = Path.Combine(outputPath, $"registry_backup_{timestamp}.reg");

                    // Use reg.exe to export registry
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "reg",
                        Arguments = $"export HKCU \"{backupFile}\" /y",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = System.Diagnostics.Process.Start(startInfo);
                    if (process == null)
                    {
                        throw new InvalidOperationException("Failed to start reg.exe");
                    }

                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        var error = process.StandardError.ReadToEnd();
                        throw new InvalidOperationException($"Registry backup failed: {error}");
                    }

                    return backupFile;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to backup registry: {ex.Message}", ex);
                }
            });
        }

        private void ScanRegistryPath(RegistryKey rootKey, string path, List<RegistryIssue> issues)
        {
            try
            {
                using var key = rootKey.OpenSubKey(path);
                if (key == null) return;

                // Check for orphaned uninstall entries
                if (path.Contains("Uninstall"))
                {
                    ScanUninstallEntries(key, issues);
                }

                // Check for missing file references
                ScanFileReferences(key, path, issues);

                // Recursively scan subkeys (limited depth)
                var subKeyNames = key.GetSubKeyNames();
                foreach (var subKeyName in subKeyNames.Take(50)) // Limit to prevent excessive scanning
                {
                    try
                    {
                        ScanRegistryPath(rootKey, $"{path}\\{subKeyName}", issues);
                    }
                    catch
                    {
                        // Skip inaccessible keys
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip keys we can't access
            }
            catch
            {
                // Skip other errors
            }
        }

        private void ScanUninstallEntries(RegistryKey key, List<RegistryIssue> issues)
        {
            var subKeyNames = key.GetSubKeyNames();
            foreach (var subKeyName in subKeyNames)
            {
                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName")?.ToString();
                    var uninstallString = subKey.GetValue("UninstallString")?.ToString();

                    if (string.IsNullOrEmpty(displayName))
                    {
                        issues.Add(new RegistryIssue
                        {
                            KeyPath = subKey.Name,
                            Type = "Orphaned Uninstall Entry",
                            Description = "Uninstall entry with no display name",
                            Severity = RegistrySeverity.Low
                        });
                    }

                    if (!string.IsNullOrEmpty(uninstallString))
                    {
                        // Extract file path from uninstall string
                        var filePath = ExtractFilePath(uninstallString);
                        if (!string.IsNullOrEmpty(filePath) && !File.Exists(filePath))
                        {
                            issues.Add(new RegistryIssue
                            {
                                KeyPath = subKey.Name,
                                Type = "Missing Uninstaller",
                                Description = $"Uninstaller not found: {filePath}",
                                Severity = RegistrySeverity.Medium,
                                ValueName = "UninstallString",
                                ValueData = uninstallString
                            });
                        }
                    }
                }
                catch
                {
                    // Skip entries we can't read
                }
            }
        }

        private void ScanFileReferences(RegistryKey key, string path, List<RegistryIssue> issues)
        {
            try
            {
                var valueNames = key.GetValueNames();
                foreach (var valueName in valueNames)
                {
                    var value = key.GetValue(valueName)?.ToString();
                    if (string.IsNullOrEmpty(value)) continue;

                    // Check if value looks like a file path
                    if (value.Contains(":\\") || value.Contains("\\\\"))
                    {
                        var filePath = ExtractFilePath(value);
                        if (!string.IsNullOrEmpty(filePath) && !File.Exists(filePath) && !Directory.Exists(filePath))
                        {
                            issues.Add(new RegistryIssue
                            {
                                KeyPath = key.Name,
                                Type = "Missing File Reference",
                                Description = $"Referenced file not found: {filePath}",
                                Severity = RegistrySeverity.Low,
                                ValueName = valueName,
                                ValueData = value
                            });
                        }
                    }
                }
            }
            catch
            {
                // Skip if we can't read values
            }
        }

        private string ExtractFilePath(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Remove quotes
            var path = input.Trim('"');

            // Extract path before arguments
            var spaceIndex = path.IndexOf(' ');
            if (spaceIndex > 0 && path.IndexOf(":\\") < spaceIndex)
            {
                path = path.Substring(0, spaceIndex);
            }

            return path.Trim('"');
        }

        private void BackupRegistryKey(RegistryKey rootKey, string subKeyPath)
        {
            try
            {
                var backupDir = Path.Combine(Path.GetTempPath(), "RegistryBackups");
                Directory.CreateDirectory(backupDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var safePath = subKeyPath.Replace("\\", "_");
                var backupFile = Path.Combine(backupDir, $"{safePath}_{timestamp}.reg");

                // Export key to .reg file
                var rootName = rootKey.Name.Split('\\')[0];
                var fullPath = $"{rootName}\\{subKeyPath}";

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "reg",
                    Arguments = $"export \"{fullPath}\" \"{backupFile}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(startInfo);
                process?.WaitForExit();
            }
            catch
            {
                // Backup failed, but don't stop deletion
            }
        }
    }
}
