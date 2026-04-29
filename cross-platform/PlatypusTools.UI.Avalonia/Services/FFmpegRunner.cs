using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Cross-platform ffmpeg/ffprobe runner. Discovery order:
///   1. PT_FFMPEG / PT_FFPROBE env vars
///   2. PATH lookup (where/which)
///   3. Common bundle locations
/// </summary>
public static class FFmpegRunner
{
    public static string? FFmpegPath => Resolve("ffmpeg", "PT_FFMPEG");
    public static string? FFprobePath => Resolve("ffprobe", "PT_FFPROBE");
    public static bool IsAvailable => !string.IsNullOrEmpty(FFmpegPath);

    private static string? _cachedFf;
    private static string? _cachedFfp;

    private static string? Resolve(string exe, string envVar)
    {
        var cache = exe == "ffmpeg" ? _cachedFf : _cachedFfp;
        if (cache != null) return cache;

        var fromEnv = Environment.GetEnvironmentVariable(envVar);
        if (!string.IsNullOrEmpty(fromEnv) && File.Exists(fromEnv))
        {
            return Cache(exe, fromEnv);
        }

        var name = exe + (ShellHelper.IsWindows ? ".exe" : "");
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var sep = ShellHelper.IsWindows ? ';' : ':';
        foreach (var dir in path.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, name);
                if (File.Exists(candidate)) return Cache(exe, candidate);
            }
            catch { }
        }

        // Common locations
        string[] guesses = ShellHelper.IsMac
            ? new[] { "/opt/homebrew/bin/" + name, "/usr/local/bin/" + name }
            : ShellHelper.IsLinux
                ? new[] { "/usr/bin/" + name, "/usr/local/bin/" + name, "/snap/bin/" + name }
                : new[] { @"C:\ffmpeg\bin\" + name };
        foreach (var g in guesses)
            if (File.Exists(g)) return Cache(exe, g);

        return null;
    }

    private static string Cache(string exe, string path)
    {
        if (exe == "ffmpeg") _cachedFf = path;
        else _cachedFfp = path;
        return path;
    }

    public static async Task<ShellHelper.Result> RunAsync(
        string args, CancellationToken token = default, Action<string>? onLine = null)
    {
        var f = FFmpegPath;
        if (string.IsNullOrEmpty(f))
            return new ShellHelper.Result(-1, "", "ffmpeg not found in PATH. Install via apt/brew/winget.");
        return await ShellHelper.RunAsync(f, args, token, onLine);
    }

    /// <summary>Convenience: quote a path for ffmpeg arg use.</summary>
    public static string Q(string p) => "\"" + p.Replace("\"", "\\\"") + "\"";
}
