using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

public static class ShellHelper
{
    public sealed record Result(int ExitCode, string StdOut, string StdErr);

    public static async Task<Result> RunAsync(
        string fileName, string arguments, CancellationToken token = default,
        Action<string>? onLine = null)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var so = new StringBuilder();
        var se = new StringBuilder();
        p.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            so.AppendLine(e.Data);
            onLine?.Invoke(e.Data);
        };
        p.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null) return;
            se.AppendLine(e.Data);
        };
        try
        {
            p.Start();
        }
        catch (Exception ex)
        {
            return new Result(-1, "", $"Failed to launch '{fileName}': {ex.Message}");
        }
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using (token.Register(() => { try { if (!p.HasExited) p.Kill(true); } catch { } }))
        {
            await p.WaitForExitAsync(token).ConfigureAwait(false);
        }
        return new Result(p.ExitCode, so.ToString(), se.ToString());
    }

    public static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
    public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public static string PlatformName =>
        IsLinux ? "Linux" : IsMac ? "macOS" : IsWindows ? "Windows" : "Unknown";

    public static string AppDataDir
    {
        get
        {
            var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
                root = Path.Combine(home, ".local", "share");
            }
            var dir = Path.Combine(root, "PlatypusTools");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}
