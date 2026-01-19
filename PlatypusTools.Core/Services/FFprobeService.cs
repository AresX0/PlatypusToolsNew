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

        /// <summary>
        /// Gets basic media information from a file.
        /// </summary>
        /// <param name="path">Path to media file.</param>
        /// <param name="ffprobePath">Optional path to ffprobe.</param>
        /// <returns>Media info or null if failed.</returns>
        public static MediaProbeInfo? GetMediaInfo(string path, string? ffprobePath = null)
        {
            if (!File.Exists(path)) return null;
            var exe = ffprobePath ?? FindFfprobe();
            if (exe == null) return null;

            var args = $"-v error -show_entries format=duration:stream=width,height,codec_type,sample_rate,channels -of json \"{path}\"";
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
                if (p == null) return null;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(5000);

                var info = new MediaProbeInfo();
                
                // Parse duration
                var durationMatch = System.Text.RegularExpressions.Regex.Match(output, @"""duration""\s*:\s*""?([0-9.]+)""?");
                if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var duration))
                {
                    info.Duration = TimeSpan.FromSeconds(duration);
                }

                // Parse video dimensions
                var widthMatch = System.Text.RegularExpressions.Regex.Match(output, @"""width""\s*:\s*(\d+)");
                var heightMatch = System.Text.RegularExpressions.Regex.Match(output, @"""height""\s*:\s*(\d+)");
                if (widthMatch.Success) info.Width = int.Parse(widthMatch.Groups[1].Value);
                if (heightMatch.Success) info.Height = int.Parse(heightMatch.Groups[1].Value);

                // Parse audio info
                var sampleRateMatch = System.Text.RegularExpressions.Regex.Match(output, @"""sample_rate""\s*:\s*""?(\d+)""?");
                var channelsMatch = System.Text.RegularExpressions.Regex.Match(output, @"""channels""\s*:\s*(\d+)");
                if (sampleRateMatch.Success) info.AudioSampleRate = int.Parse(sampleRateMatch.Groups[1].Value);
                if (channelsMatch.Success) info.AudioChannels = int.Parse(channelsMatch.Groups[1].Value);

                return info;
            }
            catch { return null; }
        }
    }

    /// <summary>
    /// Basic media probe information.
    /// </summary>
    public class MediaProbeInfo
    {
        public TimeSpan Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int AudioSampleRate { get; set; } = 44100;
        public int AudioChannels { get; set; } = 2;
    }
}