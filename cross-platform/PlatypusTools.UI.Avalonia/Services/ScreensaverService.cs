using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Cross-platform screensaver / screen-lock control.
/// - Linux (X11 GNOME): gnome-screensaver-command --activate, gsettings idle-delay
/// - Linux (Wayland/GNOME): loginctl lock-session
/// - Linux (KDE): qdbus org.freedesktop.ScreenSaver Lock
/// - Linux (xscreensaver): xscreensaver-command -lock
/// - macOS: pmset displaysleepnow / "open -a ScreenSaverEngine"
/// - Windows: rundll32.exe user32.dll,LockWorkStation (fallback)
/// </summary>
public sealed class ScreensaverService
{
    public async Task<(bool ok, string detail)> LockAsync()
    {
        if (ShellHelper.IsLinux)
        {
            var attempts = new (string,string)[]
            {
                ("loginctl", "lock-session"),
                ("gnome-screensaver-command", "--lock"),
                ("xscreensaver-command", "-lock"),
                ("qdbus", "org.freedesktop.ScreenSaver /ScreenSaver Lock")
            };
            foreach (var (f, a) in attempts)
            {
                var r = await ShellHelper.RunAsync(f, a);
                if (r.ExitCode == 0) return (true, $"{f} {a}");
            }
            return (false, "No working screensaver command found.");
        }
        if (ShellHelper.IsMac)
        {
            var r = await ShellHelper.RunAsync("pmset", "displaysleepnow");
            if (r.ExitCode == 0) return (true, "pmset displaysleepnow");
            var r2 = await ShellHelper.RunAsync("open", "-a ScreenSaverEngine");
            return (r2.ExitCode == 0, $"open ScreenSaverEngine exit={r2.ExitCode}");
        }
        if (ShellHelper.IsWindows)
        {
            var r = await ShellHelper.RunAsync("rundll32.exe", "user32.dll,LockWorkStation");
            return (r.ExitCode == 0, "rundll32 LockWorkStation");
        }
        return (false, "Unsupported OS");
    }

    /// <summary>Set idle timeout in seconds before screen lock (Linux GNOME / macOS).</summary>
    public async Task<(bool ok, string detail)> SetIdleSecondsAsync(int seconds)
    {
        if (ShellHelper.IsLinux)
        {
            var r = await ShellHelper.RunAsync("gsettings",
                $"set org.gnome.desktop.session idle-delay {seconds}");
            return (r.ExitCode == 0, $"gsettings exit={r.ExitCode}");
        }
        if (ShellHelper.IsMac)
        {
            // pmset uses minutes
            var minutes = System.Math.Max(1, seconds / 60);
            var r = await ShellHelper.RunAsync("pmset", $"-a displaysleep {minutes}");
            return (r.ExitCode == 0, $"pmset displaysleep {minutes}m");
        }
        return (false, "Unsupported on this OS");
    }
}
