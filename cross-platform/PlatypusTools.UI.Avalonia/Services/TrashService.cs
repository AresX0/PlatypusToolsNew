using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Send-to-trash for Linux (gio trash) and macOS (osascript Finder).
/// On Windows, falls back to a regular File.Delete (the WPF app has its own native recycle-bin shim).
/// </summary>
public sealed class TrashService : ITrashService
{
    public async Task<bool> MoveToTrashAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
            return false;

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return await RunAsync("gio", $"trash \"{path}\"").ConfigureAwait(false);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                var script =
                    $"tell application \"Finder\" to delete POSIX file \"{path.Replace("\"", "\\\"")}\"";
                return await RunAsync("osascript", $"-e '{script}'").ConfigureAwait(false);
            }

            // Windows fallback (testing only).
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TrashService failed: {ex.Message}");
            return false;
        }
    }

    private static Task<bool> RunAsync(string file, string args)
    {
        var tcs = new TaskCompletionSource<bool>();
#pragma warning disable CA2000 // Process is disposed in Exited handler / catch.
        Process? p = null;
        try
        {
            p = new Process
            {
                StartInfo = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };
            p.Exited += (sender, _) =>
            {
                if (sender is Process proc)
                {
                    tcs.TrySetResult(proc.ExitCode == 0);
                    proc.Dispose();
                }
            };
            p.Start();
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"TrashService run {file} failed: {ex.Message}");
            p?.Dispose();
            tcs.TrySetResult(false);
        }
#pragma warning restore CA2000
        return tcs.Task;
    }
}
