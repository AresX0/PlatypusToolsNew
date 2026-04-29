using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Cross-platform file launcher.
/// - Linux: xdg-open / nautilus / dolphin / thunar (revealing in file manager)
/// - macOS: open / open -R
/// - Windows fallback: explorer.exe (used only when running the Avalonia port on Windows for testing)
/// </summary>
public sealed class FileLauncher : IFileLauncher
{
    public Task OpenFileAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) && !Directory.Exists(path))
            return Task.CompletedTask;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            Run("xdg-open", Quote(path));
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            Run("open", Quote(path));
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });

        return Task.CompletedTask;
    }

    public Task RevealInFileManagerAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return Task.CompletedTask;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // No standard "reveal" — open the parent folder.
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path;
            Run("xdg-open", Quote(dir));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Run("open", "-R " + Quote(path));
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Process.Start("explorer.exe", "/select,\"" + path + "\"");
        }

        return Task.CompletedTask;
    }

    private static void Run(string file, string args)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"FileLauncher: {file} {args} failed: {ex.Message}");
        }
    }

    private static string Quote(string s) => "\"" + s.Replace("\"", "\\\"") + "\"";
}
