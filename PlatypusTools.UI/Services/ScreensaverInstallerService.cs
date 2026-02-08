using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service to install/uninstall the PlatypusTools screensaver for Windows.
    /// Instead of copying to System32 (which fails for self-contained .NET apps),
    /// this creates a registry entry pointing directly to the installed application
    /// and sets the current exe as the Windows screensaver.
    /// </summary>
    public static class ScreensaverInstallerService
    {
        private const string ScreensaverName = "PlatypusVisualizer.scr";
        
        /// <summary>
        /// Gets the install directory for the screensaver (in ProgramData).
        /// </summary>
        private static string InstallDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PlatypusTools", "Screensaver");
        
        /// <summary>
        /// Gets the path where the screensaver exe will be installed.
        /// </summary>
        public static string ScreensaverExePath => Path.Combine(InstallDirectory, "PlatypusTools.UI.exe");
        
        /// <summary>
        /// Gets the path where the .scr file will be placed in System32.
        /// </summary>
        public static string ScreensaverScrPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            ScreensaverName);
        
        /// <summary>
        /// Checks if the screensaver is currently installed.
        /// </summary>
        public static bool IsInstalled => File.Exists(ScreensaverExePath) || IsRegistryInstalled;
        
        /// <summary>
        /// Checks if the screensaver is set via registry.
        /// </summary>
        private static bool IsRegistryInstalled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    var scrnsave = key?.GetValue("SCRNSAVE.EXE")?.ToString();
                    return scrnsave != null && scrnsave.Contains("PlatypusTools", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }
        
        /// <summary>
        /// Checks if the application is running with administrator privileges.
        /// </summary>
        public static bool IsAdministrator
        {
            get
            {
                try
                {
                    var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
                catch
                {
                    return false;
                }
            }
        }
        
        /// <summary>
        /// Gets the path to the current executable.
        /// </summary>
        private static string? CurrentExePath => Process.GetCurrentProcess().MainModule?.FileName 
            ?? Environment.ProcessPath;
        
        /// <summary>
        /// Installs the screensaver by:
        /// 1. Copying the app directory to a shared install location
        /// 2. Setting the Windows screensaver registry to point to it
        /// </summary>
        public static async Task<(bool Success, string Message)> InstallAsync()
        {
            try
            {
                string? exePath = CurrentExePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return (false, "Could not find the application executable.");
                }
                
                string? sourceDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(sourceDir))
                {
                    return (false, "Could not determine the application directory.");
                }
                
                // Step 1: Copy the application to the install directory
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(InstallDirectory);
                    
                    // Copy all files from the source directory
                    foreach (var file in Directory.GetFiles(sourceDir))
                    {
                        string destFile = Path.Combine(InstallDirectory, Path.GetFileName(file));
                        File.Copy(file, destFile, overwrite: true);
                    }
                    
                    // Copy subdirectories (for runtime deps, etc.)
                    foreach (var dir in Directory.GetDirectories(sourceDir))
                    {
                        string destDir = Path.Combine(InstallDirectory, Path.GetFileName(dir));
                        CopyDirectory(dir, destDir);
                    }
                });
                
                // Step 2: Set registry to use our exe as the screensaver
                string installedExe = ScreensaverExePath;
                if (!File.Exists(installedExe))
                {
                    // Find the actual exe name in the install dir
                    var exeFiles = Directory.GetFiles(InstallDirectory, "PlatypusTools*.exe");
                    installedExe = exeFiles.Length > 0 ? exeFiles[0] : ScreensaverExePath;
                }
                
                SetScreensaverRegistry($"\"{installedExe}\" /s");
                
                return (true, 
                    $"Screensaver installed successfully!\n\n" +
                    $"Installed to: {InstallDirectory}\n\n" +
                    $"You can now select 'PlatypusVisualizer' in Windows Screen Saver Settings,\n" +
                    $"or it has been set as your active screensaver.");
            }
            catch (UnauthorizedAccessException)
            {
                // Try with elevation
                return await InstallWithElevationAsync();
            }
            catch (Exception ex)
            {
                return (false, $"Installation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Sets the Windows screensaver via registry.
        /// </summary>
        private static void SetScreensaverRegistry(string screensaverPath)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                if (key != null)
                {
                    key.SetValue("SCRNSAVE.EXE", screensaverPath);
                    key.SetValue("ScreenSaveActive", "1");
                }
            }
            catch
            {
                // Registry write may fail — not critical
            }
        }
        
        /// <summary>
        /// Recursively copies a directory.
        /// </summary>
        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);
            
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), overwrite: true);
            }
            
            foreach (var dir in Directory.GetDirectories(sourceDir))
            {
                CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)));
            }
        }
        
        /// <summary>
        /// Attempts to install by running a PowerShell command with elevation.
        /// </summary>
        private static async Task<(bool Success, string Message)> InstallWithElevationAsync()
        {
            try
            {
                string? exePath = CurrentExePath;
                if (string.IsNullOrEmpty(exePath))
                {
                    return (false, "Could not find the application executable.");
                }
                
                string? sourceDir = Path.GetDirectoryName(exePath);
                if (string.IsNullOrEmpty(sourceDir))
                {
                    return (false, "Could not determine the application directory.");
                }
                
                // Use PowerShell to copy with elevation and set registry
                string script = 
                    $"$dest = '{InstallDirectory}'; " +
                    $"New-Item -Path $dest -ItemType Directory -Force | Out-Null; " +
                    $"Copy-Item -Path '{sourceDir}\\*' -Destination $dest -Recurse -Force; " +
                    $"Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name 'SCRNSAVE.EXE' -Value '\"\"'{InstallDirectory}\\PlatypusTools.UI.exe\"\" /s'; " +
                    $"Set-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name 'ScreenSaveActive' -Value '1'";
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                
                var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        return (true, 
                            $"Screensaver installed successfully!\n\n" +
                            $"Installed to: {InstallDirectory}\n\n" +
                            $"It has been set as your active screensaver.");
                    }
                }
                
                return (false, "Installation was cancelled or failed.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (false, "Installation was cancelled by user.");
            }
            catch (Exception ex)
            {
                return (false, $"Installation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Uninstalls the screensaver by removing the install directory and clearing registry.
        /// </summary>
        public static async Task<(bool Success, string Message)> UninstallAsync()
        {
            try
            {
                bool anyRemoved = false;
                
                // Remove install directory
                if (Directory.Exists(InstallDirectory))
                {
                    await Task.Run(() =>
                    {
                        try { Directory.Delete(InstallDirectory, recursive: true); }
                        catch { /* may need elevation */ }
                    });
                    anyRemoved = true;
                }
                
                // Remove .scr from System32 if it exists
                if (File.Exists(ScreensaverScrPath))
                {
                    try
                    {
                        await Task.Run(() => File.Delete(ScreensaverScrPath));
                        anyRemoved = true;
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Try with elevation for System32 cleanup
                        return await UninstallWithElevationAsync();
                    }
                }
                
                // Clear registry
                ClearScreensaverRegistry();
                
                if (!anyRemoved && !IsRegistryInstalled)
                {
                    return (true, "Screensaver is not installed.");
                }
                
                return (true, "Screensaver uninstalled successfully.\n\nRegistry entry and install files have been removed.");
            }
            catch (UnauthorizedAccessException)
            {
                return await UninstallWithElevationAsync();
            }
            catch (Exception ex)
            {
                return (false, $"Uninstallation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Clears the screensaver registry entries.
        /// </summary>
        private static void ClearScreensaverRegistry()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                if (key != null)
                {
                    var current = key.GetValue("SCRNSAVE.EXE")?.ToString();
                    if (current != null && current.Contains("PlatypusTools", StringComparison.OrdinalIgnoreCase))
                    {
                        key.DeleteValue("SCRNSAVE.EXE", throwOnMissingValue: false);
                    }
                }
            }
            catch { }
        }
        
        /// <summary>
        /// Attempts to uninstall by running a PowerShell command with elevation.
        /// </summary>
        private static async Task<(bool Success, string Message)> UninstallWithElevationAsync()
        {
            try
            {
                string script = 
                    $"if (Test-Path '{InstallDirectory}') {{ Remove-Item -Path '{InstallDirectory}' -Recurse -Force }}; " +
                    $"if (Test-Path '{ScreensaverScrPath}') {{ Remove-Item -Path '{ScreensaverScrPath}' -Force }}; " +
                    $"$key = Get-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue; " +
                    $"if ($key -and $key.'SCRNSAVE.EXE' -like '*PlatypusTools*') {{ Remove-ItemProperty -Path 'HKCU:\\Control Panel\\Desktop' -Name 'SCRNSAVE.EXE' -Force }}";
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -Command \"{script}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                
                var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        return (true, "Screensaver uninstalled successfully.\n\nAll files and registry entries have been removed.");
                    }
                }
                
                return (false, "Uninstallation was cancelled or failed.");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return (false, "Uninstallation was cancelled by user.");
            }
            catch (Exception ex)
            {
                return (false, $"Uninstallation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Opens the Windows Screen Saver Settings dialog.
        /// </summary>
        public static void OpenWindowsScreensaverSettings()
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "rundll32.exe",
                    Arguments = "desk.cpl,InstallScreenSaver",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback - open control panel
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "control.exe",
                        Arguments = "desk.cpl,,@screensaver",
                        UseShellExecute = true
                    });
                }
                catch { }
            }
        }
    }
}
