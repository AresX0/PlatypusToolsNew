using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service to install/uninstall the PlatypusTools screensaver for Windows.
    /// Creates a .scr file in Windows\System32 that launches the application in screensaver mode.
    /// </summary>
    public static class ScreensaverInstallerService
    {
        private const string ScreensaverName = "PlatypusVisualizer.scr";
        
        /// <summary>
        /// Gets the path where the screensaver will be installed.
        /// </summary>
        public static string ScreensaverPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            ScreensaverName);
        
        /// <summary>
        /// Checks if the screensaver is currently installed.
        /// </summary>
        public static bool IsInstalled => File.Exists(ScreensaverPath);
        
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
        /// Installs the screensaver by copying the current exe to System32 as a .scr file.
        /// Requires administrator privileges.
        /// </summary>
        /// <returns>Success message or error message.</returns>
        public static async Task<(bool Success, string Message)> InstallAsync()
        {
            try
            {
                string? exePath = CurrentExePath;
                if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                {
                    return (false, "Could not find the application executable.");
                }
                
                if (!IsAdministrator)
                {
                    // Try to restart as admin
                    return await InstallWithElevationAsync();
                }
                
                // Copy exe to System32 as .scr
                await Task.Run(() => File.Copy(exePath, ScreensaverPath, overwrite: true));
                
                return (true, $"Screensaver installed successfully!\n\nYou can now select 'PlatypusVisualizer' in Windows Screen Saver Settings.");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Access denied. Please run as Administrator.");
            }
            catch (Exception ex)
            {
                return (false, $"Installation failed: {ex.Message}");
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
                
                // Use PowerShell to copy with elevation
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Copy-Item -Path '{exePath}' -Destination '{ScreensaverPath}' -Force\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                
                var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0 && File.Exists(ScreensaverPath))
                    {
                        return (true, $"Screensaver installed successfully!\n\nYou can now select 'PlatypusVisualizer' in Windows Screen Saver Settings.");
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
        /// Uninstalls the screensaver by removing the .scr file from System32.
        /// Requires administrator privileges.
        /// </summary>
        /// <returns>Success message or error message.</returns>
        public static async Task<(bool Success, string Message)> UninstallAsync()
        {
            try
            {
                if (!File.Exists(ScreensaverPath))
                {
                    return (true, "Screensaver is not installed.");
                }
                
                if (!IsAdministrator)
                {
                    return await UninstallWithElevationAsync();
                }
                
                await Task.Run(() => File.Delete(ScreensaverPath));
                
                return (true, "Screensaver uninstalled successfully.");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Access denied. Please run as Administrator.");
            }
            catch (Exception ex)
            {
                return (false, $"Uninstallation failed: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Attempts to uninstall by running a PowerShell command with elevation.
        /// </summary>
        private static async Task<(bool Success, string Message)> UninstallWithElevationAsync()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-Command \"Remove-Item -Path '{ScreensaverPath}' -Force\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = true
                };
                
                var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0 && !File.Exists(ScreensaverPath))
                    {
                        return (true, "Screensaver uninstalled successfully.");
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
