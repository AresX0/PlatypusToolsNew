using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace PlatypusTools.Core.Services.Wallpaper
{
    /// <summary>
    /// Sets the Windows desktop wallpaper and (optionally) the lock-screen image.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class WallpaperSetter
    {
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE    = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        public static (int width, int height) GetPrimaryScreenSize()
        {
            try
            {
                int sw = GetSystemMetrics(0);
                int sh = GetSystemMetrics(1);
                if (sw <= 0 || sh <= 0) return (1920, 1080);
                return (sw, sh);
            }
            catch
            {
                return (1920, 1080);
            }
        }

        /// <summary>Sets the desktop wallpaper to the given absolute file path. Returns true on success.</summary>
        public static bool SetDesktopWallpaper(string absolutePath)
        {
            if (!File.Exists(absolutePath)) return false;
            int rc = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, absolutePath,
                SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
            return rc != 0;
        }

        /// <summary>
        /// Sets the Windows lock-screen image via HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP.
        /// Requires elevation. Once the registry value is established, subsequent calls just overwrite the image
        /// file (no UAC prompt needed).
        /// </summary>
        public static bool SetLockScreenImage(string sourceImagePath, out string message)
        {
            message = "";
            try
            {
                var lockDir = Path.Combine(Path.GetTempPath(), "PlatypusTools_Wallpaper");
                Directory.CreateDirectory(lockDir);
                var lockPath = Path.Combine(lockDir, "lockscreen.jpg");

                using (var img = SixLabors.ImageSharp.Image.Load(sourceImagePath))
                    img.Save(lockPath, new JpegEncoder { Quality = 95 });

                if (RegistryAlreadyPointsTo(lockPath))
                    return true;

                var scriptPath = Path.Combine(lockDir, "set_lockscreen.ps1");
                File.WriteAllText(scriptPath, $@"
$p = '{lockPath.Replace("'", "''")}'
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP'
if (!(Test-Path $k)) {{ New-Item -Path $k -Force | Out-Null }}
Set-ItemProperty -Path $k -Name LockScreenImageStatus -Value 1 -Type DWord
Set-ItemProperty -Path $k -Name LockScreenImagePath -Value $p -Type String
Set-ItemProperty -Path $k -Name LockScreenImageUrl -Value $p -Type String
");

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"Start-Process powershell -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File \"\"{scriptPath}\"\"' -Verb RunAs -Wait\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var proc = Process.Start(psi);
                if (proc == null) { message = "Failed to start elevated PowerShell."; return false; }
                proc.WaitForExit(60_000);
                return proc.ExitCode == 0;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static bool RegistryAlreadyPointsTo(string lockPath)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP", false);
                var existing = key?.GetValue("LockScreenImagePath") as string;
                return string.Equals(existing, lockPath, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns the temp staging path used by the rotator for the prepared wallpaper bitmap.
        /// </summary>
        public static string GetStagedWallpaperPath()
        {
            var dir = Path.Combine(Path.GetTempPath(), "PlatypusTools_Wallpaper");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "current.bmp");
        }
    }
}
