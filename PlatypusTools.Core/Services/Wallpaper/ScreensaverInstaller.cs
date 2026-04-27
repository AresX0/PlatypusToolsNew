using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace PlatypusTools.Core.Services.Wallpaper
{
    /// <summary>
    /// Installs / uninstalls PlatypusTools as a Windows screensaver (.scr) and manages the idle timeout.
    /// Strategy: copy the running PlatypusTools.UI.exe to %LOCALAPPDATA%\PlatypusTools\PlatypusScreensaver.scr
    /// (no UAC needed) and point HKCU\Control Panel\Desktop\SCRNSAVE.EXE at it. Windows will then invoke
    /// the .scr with /s, /p, or /c — already handled by App.OnStartup.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static class ScreensaverInstaller
    {
        private const string DesktopKey = @"Control Panel\Desktop";

        public static string GetScrPath()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlatypusTools");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "PlatypusScreensaver.scr");
        }

        public static bool IsInstalled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(DesktopKey, false);
                var current = key?.GetValue("SCRNSAVE.EXE") as string;
                return !string.IsNullOrEmpty(current) &&
                       string.Equals(current, GetScrPath(), StringComparison.OrdinalIgnoreCase) &&
                       File.Exists(current);
            }
            catch { return false; }
        }

        /// <summary>Installs by copying the host exe to a .scr file and registering it. Returns true on success.</summary>
        public static bool Install(out string message)
        {
            message = "";
            try
            {
                var src = GetHostExePath();
                if (string.IsNullOrEmpty(src) || !File.Exists(src))
                {
                    message = "Could not locate PlatypusTools.UI.exe.";
                    return false;
                }

                var dest = GetScrPath();

                // Best-effort overwrite
                try { if (File.Exists(dest)) File.Delete(dest); } catch { /* in-use */ }

                File.Copy(src, dest, overwrite: true);

                using (var key = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: true))
                {
                    key.SetValue("SCRNSAVE.EXE", dest, RegistryValueKind.String);
                    key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
                }

                BroadcastSettingChange();
                message = $"Installed: {dest}";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static bool Uninstall(out string message)
        {
            message = "";
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(DesktopKey, writable: true))
                {
                    key?.SetValue("SCRNSAVE.EXE", "", RegistryValueKind.String);
                    key?.SetValue("ScreenSaveActive", "0", RegistryValueKind.String);
                }

                var dest = GetScrPath();
                if (File.Exists(dest))
                {
                    try { File.Delete(dest); }
                    catch { /* in use; will be cleaned next run */ }
                }

                BroadcastSettingChange();
                message = "Uninstalled.";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        public static int? GetIdleTimeoutSeconds()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(DesktopKey, false);
                var v = key?.GetValue("ScreenSaveTimeOut") as string;
                if (int.TryParse(v, out var s) && s > 0) return s;
            }
            catch { }
            return null;
        }

        public static bool SetIdleTimeoutSeconds(int seconds)
        {
            if (seconds < 30) seconds = 30;
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(DesktopKey, writable: true);
                key.SetValue("ScreenSaveTimeOut", seconds.ToString(), RegistryValueKind.String);
                key.SetValue("ScreenSaveActive", "1", RegistryValueKind.String);
                BroadcastSettingChange();
                return true;
            }
            catch { return false; }
        }

        private static string? GetHostExePath()
        {
            try
            {
                var path = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) return path;
            }
            catch { }
            return Environment.ProcessPath;
        }

        // ── Notify Windows that desktop settings changed ──────────────────────

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string? lpvParam, int fuWinIni);

        private static void BroadcastSettingChange()
        {
            try { SystemParametersInfo(0x0011 /* SPI_SETSCREENSAVEACTIVE */, 1, null, 0x02); } catch { }
        }
    }
}
