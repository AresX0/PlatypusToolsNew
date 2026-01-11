using System;
using System.Diagnostics;
using System.Security.Principal;

namespace PlatypusTools.Core.Utilities
{
    /// <summary>
    /// Helper class for handling elevated permissions
    /// </summary>
    public static class ElevationHelper
    {
        /// <summary>
        /// Checks if the current process is running with administrator privileges
        /// </summary>
        /// <returns>True if running as administrator, false otherwise</returns>
        public static bool IsElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Restarts the current application with administrator privileges
        /// </summary>
        /// <returns>True if successfully restarted, false otherwise</returns>
        public static bool RestartAsAdmin()
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    WorkingDirectory = Environment.CurrentDirectory,
                    FileName = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty,
                    Verb = "runas" // This is the key to requesting elevation
                };

                Process.Start(processInfo);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Runs a command with elevated privileges
        /// </summary>
        /// <param name="fileName">The executable to run</param>
        /// <param name="arguments">Arguments to pass</param>
        /// <param name="waitForExit">Whether to wait for the process to exit</param>
        /// <returns>Process exit code if waitForExit is true, otherwise 0</returns>
        public static int RunElevated(string fileName, string arguments, bool waitForExit = true)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = fileName,
                    Arguments = arguments,
                    Verb = "runas",
                    CreateNoWindow = false
                };

                using var process = Process.Start(processInfo);
                if (process == null) return -1;

                if (waitForExit)
                {
                    process.WaitForExit();
                    return process.ExitCode;
                }

                return 0;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Executes a PowerShell command with elevated privileges
        /// </summary>
        /// <param name="command">The PowerShell command to execute</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool RunPowerShellElevated(string command)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                    Verb = "runas",
                    CreateNoWindow = false
                };

                using var process = Process.Start(processInfo);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
