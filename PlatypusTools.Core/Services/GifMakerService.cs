using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for creating animated GIFs from video files using FFmpeg.
    /// </summary>
    public class GifMakerService
    {
        public class GifOptions
        {
            public string InputPath { get; set; } = "";
            public string OutputPath { get; set; } = "";
            public double StartTime { get; set; }
            public double Duration { get; set; } = 5.0;
            public int Width { get; set; } = 480;
            public int Fps { get; set; } = 15;
            public int Quality { get; set; } = 10; // lower = better, 2-256
            public bool UseHighQuality { get; set; } = true;
            public string? TextOverlay { get; set; }
        }

        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        /// <summary>
        /// Create an animated GIF from a video file.
        /// </summary>
        public async Task<bool> CreateGifAsync(GifOptions options, CancellationToken ct = default)
        {
            try
            {
                var ffmpegPath = FFmpegService.FindFfmpeg();
                if (string.IsNullOrEmpty(ffmpegPath))
                {
                    LogMessage?.Invoke(this, "[ERROR] FFmpeg not found");
                    return false;
                }

                var outputDir = Path.GetDirectoryName(options.OutputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                LogMessage?.Invoke(this, $"[START] Creating GIF from {Path.GetFileName(options.InputPath)}");
                LogMessage?.Invoke(this, $"  Size: {options.Width}px, FPS: {options.Fps}, Start: {options.StartTime}s, Duration: {options.Duration}s");

                if (options.UseHighQuality)
                {
                    // Two-pass method for high quality
                    return await CreateHighQualityGifAsync(ffmpegPath, options, ct);
                }
                else
                {
                    return await CreateSimpleGifAsync(ffmpegPath, options, ct);
                }
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"[ERROR] {ex.Message}");
                return false;
            }
        }

        private async Task<bool> CreateHighQualityGifAsync(string ffmpegPath, GifOptions options, CancellationToken ct)
        {
            // Pass 1: Generate palette
            var palettePath = Path.Combine(Path.GetTempPath(), $"pt_gif_palette_{Guid.NewGuid():N}.png");

            try
            {
                var filters = $"fps={options.Fps},scale={options.Width}:-1:flags=lanczos";
                if (!string.IsNullOrEmpty(options.TextOverlay))
                    filters += $",drawtext=text='{EscapeText(options.TextOverlay)}':fontsize=24:fontcolor=white:borderw=2:x=(w-text_w)/2:y=h-40";

                var args1 = $"-ss {options.StartTime} -t {options.Duration} -i \"{options.InputPath}\" -vf \"{filters},palettegen=stats_mode=diff\" -y \"{palettePath}\"";
                LogMessage?.Invoke(this, "[PASS 1] Generating palette...");
                ProgressChanged?.Invoke(this, 30);

                if (!await RunFFmpegAsync(ffmpegPath, args1, ct))
                {
                    LogMessage?.Invoke(this, "[ERROR] Palette generation failed");
                    return false;
                }

                // Pass 2: Generate GIF using palette
                var args2 = $"-ss {options.StartTime} -t {options.Duration} -i \"{options.InputPath}\" -i \"{palettePath}\" -lavfi \"{filters} [x]; [x][1:v] paletteuse=dither=bayer:bayer_scale=5\" -y \"{options.OutputPath}\"";
                LogMessage?.Invoke(this, "[PASS 2] Creating GIF...");
                ProgressChanged?.Invoke(this, 70);

                if (!await RunFFmpegAsync(ffmpegPath, args2, ct))
                {
                    LogMessage?.Invoke(this, "[ERROR] GIF creation failed");
                    return false;
                }

                ProgressChanged?.Invoke(this, 100);
                var size = new FileInfo(options.OutputPath).Length;
                LogMessage?.Invoke(this, $"[DONE] GIF created: {size / 1024:N0} KB");
                return true;
            }
            finally
            {
                if (File.Exists(palettePath))
                    try { File.Delete(palettePath); } catch { }
            }
        }

        private async Task<bool> CreateSimpleGifAsync(string ffmpegPath, GifOptions options, CancellationToken ct)
        {
            var filters = $"fps={options.Fps},scale={options.Width}:-1:flags=lanczos";
            if (!string.IsNullOrEmpty(options.TextOverlay))
                filters += $",drawtext=text='{EscapeText(options.TextOverlay)}':fontsize=24:fontcolor=white:borderw=2:x=(w-text_w)/2:y=h-40";

            var args = $"-ss {options.StartTime} -t {options.Duration} -i \"{options.InputPath}\" -vf \"{filters}\" -y \"{options.OutputPath}\"";
            LogMessage?.Invoke(this, "[CREATING] Simple quality GIF...");
            ProgressChanged?.Invoke(this, 50);

            var result = await RunFFmpegAsync(ffmpegPath, args, ct);
            ProgressChanged?.Invoke(this, 100);

            if (result && File.Exists(options.OutputPath))
            {
                var size = new FileInfo(options.OutputPath).Length;
                LogMessage?.Invoke(this, $"[DONE] GIF created: {size / 1024:N0} KB");
            }

            return result;
        }

        /// <summary>
        /// Get video duration in seconds.
        /// </summary>
        public async Task<double> GetVideoDurationAsync(string filePath)
        {
            try
            {
                var ffprobePath = FFprobeService.FindFfprobe();
                if (string.IsNullOrEmpty(ffprobePath)) return 0;

                var psi = new ProcessStartInfo(ffprobePath,
                    $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };

                using var p = Process.Start(psi);
                if (p == null) return 0;
                var output = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();

                if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out var duration))
                    return duration;
            }
            catch { }
            return 0;
        }

        private static async Task<bool> RunFFmpegAsync(string ffmpegPath, string args, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(ffmpegPath, args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using var p = Process.Start(psi);
            if (p == null) return false;

            try
            {
                await p.WaitForExitAsync(ct);
                return p.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(true); } catch { }
                throw;
            }
        }

        private static string EscapeText(string text) =>
            text.Replace("'", "'\\''").Replace(":", "\\:").Replace("\\", "\\\\");
    }
}
