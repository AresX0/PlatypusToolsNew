using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services.Video
{
    /// <summary>
    /// Phase 3.4 — detects scene changes in a video using ffmpeg's `select='gt(scene,N)'`
    /// filter and writes a `;FFMETADATA1` chapter file (and an optional .ffmetadata
    /// for muxing back into a container).
    /// </summary>
    public sealed class SceneDetectionService
    {
        private static readonly Lazy<SceneDetectionService> _instance = new(() => new SceneDetectionService());
        public static SceneDetectionService Instance => _instance.Value;

        private static readonly Regex SceneRegex = new(
            @"pts_time:(?<t>[0-9]+(?:\.[0-9]+)?)", RegexOptions.Compiled);

        public sealed record Scene(TimeSpan Start, TimeSpan End, string Title);

        /// <summary>
        /// Runs scene detection on the given video. Returns ordered list of scenes.
        /// </summary>
        public async Task<IReadOnlyList<Scene>> DetectScenesAsync(
            string videoPath,
            double sceneThreshold = 0.4,
            CancellationToken ct = default)
        {
            if (!File.Exists(videoPath))
                throw new FileNotFoundException("Video not found", videoPath);

            var ffmpeg = FFmpegService.FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg))
                throw new InvalidOperationException("ffmpeg.exe not found.");

            // Get duration via ffprobe-equivalent: ffmpeg writes duration to stderr too.
            var duration = await ProbeDurationAsync(videoPath, ct).ConfigureAwait(false);

            var args =
                $"-hide_banner -nostats -i \"{videoPath}\" " +
                $"-filter_complex \"select='gt(scene,{sceneThreshold.ToString(System.Globalization.CultureInfo.InvariantCulture)})',showinfo\" " +
                "-an -f null -";

            var psi = new ProcessStartInfo(ffmpeg, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var times = new List<double>();
            using var slot = await PlatypusTools.UI.Services.Performance.ResourceGovernor.Instance
                .AcquireAsync(PlatypusTools.UI.Services.Performance.ResourceCategory.Cpu, ct).ConfigureAwait(false);
            using var proc = Process.Start(psi)!;
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;
                var m = SceneRegex.Match(e.Data);
                if (m.Success && double.TryParse(m.Groups["t"].Value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var t))
                {
                    times.Add(t);
                }
            };
            proc.BeginErrorReadLine();
            proc.BeginOutputReadLine();
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);

            // Build scene list: 0..t1, t1..t2, ..., tN..duration
            var scenes = new List<Scene>();
            if (times.Count == 0)
            {
                scenes.Add(new Scene(TimeSpan.Zero, duration, "Chapter 1"));
                return scenes;
            }

            double prev = 0;
            int idx = 1;
            foreach (var t in times)
            {
                if (t - prev < 0.5) continue;
                scenes.Add(new Scene(TimeSpan.FromSeconds(prev), TimeSpan.FromSeconds(t), $"Chapter {idx++}"));
                prev = t;
            }
            scenes.Add(new Scene(TimeSpan.FromSeconds(prev), duration, $"Chapter {idx}"));
            return scenes;
        }

        /// <summary>
        /// Writes a `;FFMETADATA1` chapter file that can be muxed via:
        /// <c>ffmpeg -i in.mp4 -i chapters.txt -map_metadata 1 -codec copy out.mp4</c>.
        /// </summary>
        public async Task WriteFfmetadataAsync(IReadOnlyList<Scene> scenes, string outputPath, CancellationToken ct = default)
        {
            var sb = new StringBuilder();
            sb.AppendLine(";FFMETADATA1");
            foreach (var s in scenes)
            {
                sb.AppendLine("[CHAPTER]");
                sb.AppendLine("TIMEBASE=1/1000");
                sb.AppendLine($"START={(long)s.Start.TotalMilliseconds}");
                sb.AppendLine($"END={(long)s.End.TotalMilliseconds}");
                sb.AppendLine($"title={s.Title}");
            }
            await File.WriteAllTextAsync(outputPath, sb.ToString(), ct).ConfigureAwait(false);
        }

        private static async Task<TimeSpan> ProbeDurationAsync(string videoPath, CancellationToken ct)
        {
            var ffmpeg = FFmpegService.FindFfmpeg();
            if (ffmpeg == null) return TimeSpan.Zero;
            var psi = new ProcessStartInfo(ffmpeg, $"-hide_banner -i \"{videoPath}\"")
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)!;
            var stderr = await proc.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
            await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            var m = Regex.Match(stderr, @"Duration:\s*(\d+):(\d+):(\d+\.\d+)");
            if (m.Success)
            {
                return new TimeSpan(0,
                    int.Parse(m.Groups[1].Value),
                    int.Parse(m.Groups[2].Value),
                    0,
                    (int)(double.Parse(m.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) * 1000));
            }
            return TimeSpan.Zero;
        }
    }
}
