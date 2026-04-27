using System;
using System.Diagnostics;
using Microsoft.Win32;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Helper to register/unregister PlatypusTools as a background daemon at user login
    /// (runs `PlatypusTools.UI.exe --wallpaper-daemon` minimized to tray).
    /// </summary>
    internal static class AutostartHelper
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "PlatypusToolsWallpaperDaemon";

        public static bool SetWallpaperDaemonAutostart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
                if (key == null) return false;

                if (!enable)
                {
                    if (key.GetValue(ValueName) != null) key.DeleteValue(ValueName, throwOnMissingValue: false);
                    return true;
                }

                var exe = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return false;

                key.SetValue(ValueName, $"\"{exe}\" --wallpaper-daemon", RegistryValueKind.String);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsWallpaperDaemonAutostartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
                return key?.GetValue(ValueName) != null;
            }
            catch { return false; }
        }
    }
}
