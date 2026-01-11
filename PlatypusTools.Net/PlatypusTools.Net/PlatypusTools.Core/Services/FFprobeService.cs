using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public static class FFprobeService
    {
        public static string? FindFfprobe(string? toolsFolder = null)
        {
            toolsFolder = toolsFolder ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(toolsFolder))
            {
                var candidate = Path.Combine(toolsFolder, "ffprobe.exe");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(toolsFolder, "ffprobe");
                if (File.Exists(candidate)) return candidate;
            }

            try
            {
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
                foreach (var p in paths)
                {
                    var a = Path.Combine(p, "ffprobe.exe");
                    if (File.Exists(a)) return a;
                    var b = Path.Combine(p, "ffprobe");
                    if (File.Exists(b)) return b;
                }
            }
            catch { }

            return null;
        }

        public static async Task<double> GetDurationSecondsAsync(string path, string? ffprobePath = null, int timeoutMs = 5000)
        {
            if (!File.Exists(path)) return -1;
            var exe = ffprobePath ?? FindFfprobe();
            if (exe == null) return -1;
            var args = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{path}\"";
            var psi = new ProcessStartInfo(exe, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using var p = Process.Start(psi);
                if (p == null) return -1;
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                var completed = await Task.WhenAll(outTask, errTask);
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return -1;
                }
                var outStr = completed[0]?.Trim();
                if (double.TryParse(outStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var secs)) return secs;
                return -1;
            }
            catch { return -1; }
        }
    }
}