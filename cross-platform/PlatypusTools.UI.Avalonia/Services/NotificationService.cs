using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Native desktop notifications.
/// - Linux: notify-send (libnotify; standard on GNOME/KDE/XFCE)
/// - macOS: osascript "display notification"
/// - Windows: noop (Avalonia toast fallback could be added later)
/// </summary>
public sealed class NotificationService : INotificationService
{
    public Task NotifyAsync(string title, string message)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Spawn("notify-send", $"\"{Escape(title)}\" \"{Escape(message)}\"");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var script = $"display notification \"{Escape(message)}\" with title \"{Escape(title)}\"";
                Spawn("osascript", $"-e '{script}'");
            }
            // Windows: handled by separate WPF code path; no-op here.
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"NotificationService: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    private static void Spawn(string file, string args)
    {
        Process.Start(new ProcessStartInfo(file, args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        });
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"");
}
