using System.IO;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Cross-platform desktop wallpaper setter.
/// - Linux (GNOME/Cinnamon): gsettings set org.gnome.desktop.background picture-uri "file://..."
/// - Linux (KDE Plasma): qdbus org.kde.plasmashell ... (best effort)
/// - Linux (XFCE): xfconf-query -c xfce4-desktop -p .../last-image -s "..."
/// - macOS: osascript -e 'tell app "System Events" to set picture of every desktop to POSIX file ...'
/// - Windows: SystemParametersInfo (fallback for testing on Win)
/// </summary>
public sealed class WallpaperService
{
    public async Task<(bool ok, string detail)> SetAsync(string imagePath)
    {
        if (!File.Exists(imagePath)) return (false, $"Not found: {imagePath}");

        if (ShellHelper.IsLinux)
        {
            var session = System.Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP")?.ToLowerInvariant() ?? "";
            // GNOME / Cinnamon / Unity / Pantheon
            if (session.Contains("gnome") || session.Contains("unity") || session.Contains("cinnamon") || session.Contains("pantheon"))
            {
                var schema = session.Contains("cinnamon") ? "org.cinnamon.desktop.background" : "org.gnome.desktop.background";
                var r = await ShellHelper.RunAsync("gsettings", $"set {schema} picture-uri \"file://{imagePath}\"");
                var r2 = await ShellHelper.RunAsync("gsettings", $"set {schema} picture-uri-dark \"file://{imagePath}\"");
                return (r.ExitCode == 0 || r2.ExitCode == 0, $"gsettings exit={r.ExitCode}/{r2.ExitCode} ({schema})");
            }
            // XFCE
            if (session.Contains("xfce"))
            {
                var r = await ShellHelper.RunAsync("xfconf-query",
                    $"-c xfce4-desktop -p /backdrop/screen0/monitor0/workspace0/last-image -s \"{imagePath}\"");
                return (r.ExitCode == 0, $"xfconf-query exit={r.ExitCode}");
            }
            // KDE Plasma
            if (session.Contains("kde") || session.Contains("plasma"))
            {
                var script = "var allDesktops = desktops();for(i=0;i<allDesktops.length;i++){d=allDesktops[i];d.wallpaperPlugin='org.kde.image';d.currentConfigGroup=Array('Wallpaper','org.kde.image','General');d.writeConfig('Image','file://"
                             + imagePath + "');}";
                var r = await ShellHelper.RunAsync("qdbus",
                    $"org.kde.plasmashell /PlasmaShell org.kde.PlasmaShell.evaluateScript \"{script}\"");
                return (r.ExitCode == 0, $"qdbus exit={r.ExitCode}");
            }
            // Last-resort: feh
            var rfeh = await ShellHelper.RunAsync("feh", $"--bg-fill \"{imagePath}\"");
            return (rfeh.ExitCode == 0, $"feh exit={rfeh.ExitCode} (no DE detected)");
        }

        if (ShellHelper.IsMac)
        {
            var script = $"tell application \"System Events\" to tell every desktop to set picture to \"{imagePath}\"";
            var r = await ShellHelper.RunAsync("osascript", $"-e '{script}'");
            return (r.ExitCode == 0, $"osascript exit={r.ExitCode}");
        }

        if (ShellHelper.IsWindows)
        {
            try
            {
                global::PlatypusTools.UI.Avalonia.Services.WallpaperWin32.SetDesktopWallpaper(imagePath);
                return (true, "SystemParametersInfo");
            }
            catch (System.Exception ex) { return (false, ex.Message); }
        }

        return (false, "Unsupported OS");
    }
}

internal static class WallpaperWin32
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, string pvParam, uint fWinIni);
    public static void SetDesktopWallpaper(string path)
    {
        const uint SPI_SETDESKWALLPAPER = 0x0014;
        const uint SPIF_UPDATEINIFILE = 0x01;
        const uint SPIF_SENDCHANGE = 0x02;
        SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
    }
}
