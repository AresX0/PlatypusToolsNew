using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Quality preset for transcoding.
    /// </summary>
    public enum TranscodeQuality
    {
        /// <summary>Auto - match source quality up to 1080p</summary>
        Auto,
        /// <summary>480p SD</summary>
        SD480,
        /// <summary>720p HD</summary>
        HD720,
        /// <summary>1080p Full HD</summary>
        FullHD1080,
        /// <summary>Original quality, only remux container</summary>
        Original
    }

    /// <summary>
    /// Progress info for transcoding operations.
    /// </summary>
    public class TranscodeProgress
    {
        public string SourceFile { get; set; } = string.Empty;
        public string OutputFile { get; set; } = string.Empty;
        public double PercentComplete { get; set; }
        public TimeSpan Elapsed { get; set; }
        public TimeSpan? EstimatedRemaining { get; set; }
        public string Status { get; set; } = string.Empty;
        public double? CurrentFps { get; set; }
        public double? Speed { get; set; } // e.g. 2.5x means encoding 2.5x faster than realtime
    }

    /// <summary>
    /// Transcodes video files using FFmpeg for web-compatible streaming.
    /// Reuses existing FFmpeg detection from DependencyCheckerService.
    /// </summary>
    public class TranscodingService
    {
        private readonly string _cachePath;

        public TranscodingService()
        {
            _cachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "transcode_cache");
        }

        /// <summary>
        /// Get the path to a transcoded version of a video file.
        /// Returns the cached version if it exists, or null if not yet transcoded.
        /// </summary>
        public string? GetCachedTranscode(string sourceFilePath, TranscodeQuality quality = TranscodeQuality.Auto)
        {
            var outputPath = GetOutputPath(sourceFilePath, quality);
            return File.Exists(outputPath) ? outputPath : null;
        }

        /// <summary>
        /// Transcode a video file to web-compatible MP4 (H.264 + AAC).
        /// Returns the path to the transcoded file.
        /// </summary>
        public async Task<string?> TranscodeAsync(
            string sourceFilePath,
            TranscodeQuality quality = TranscodeQuality.Auto,
            IProgress<TranscodeProgress>? progress = null,
            CancellationToken ct = default)
        {
            if (!File.Exists(sourceFilePath))
                return null;

            var outputPath = GetOutputPath(sourceFilePath, quality);

            // Return cached version if available
            if (File.Exists(outputPath))
                return outputPath;

            // Ensure output directory exists
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            var ffmpegPath = FindFfmpeg();
            if (ffmpegPath == null)
                return null;

            // Get source duration for progress tracking
            var duration = await GetDurationAsync(sourceFilePath, ffmpegPath, ct);

            // Build FFmpeg arguments
            var args = BuildTranscodeArgs(sourceFilePath, outputPath, quality);

            progress?.Report(new TranscodeProgress
            {
                SourceFile = sourceFilePath,
                OutputFile = outputPath,
                Status = "Starting transcode...",
                PercentComplete = 0
            });

            var sw = Stopwatch.StartNew();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    RedirectStandardError = true, // FFmpeg outputs progress to stderr
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                // Parse FFmpeg progress output
                var progressParser = Task.Run(async () =>
                {
                    while (!process.StandardError.EndOfStream && !ct.IsCancellationRequested)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (line == null) continue;

                        var transcodeProgress = ParseFfmpegProgress(line, duration, sw.Elapsed);
                        if (transcodeProgress != null)
                        {
                            transcodeProgress.SourceFile = sourceFilePath;
                            transcodeProgress.OutputFile = outputPath;
                            progress?.Report(transcodeProgress);
                        }
                    }
                }, ct);

                await process.WaitForExitAsync(ct);
                await progressParser;

                if (ct.IsCancellationRequested)
                {
                    // Clean up partial file
                    TryDeleteFile(outputPath);
                    return null;
                }

                if (process.ExitCode != 0)
                {
                    TryDeleteFile(outputPath);
                    return null;
                }

                progress?.Report(new TranscodeProgress
                {
                    SourceFile = sourceFilePath,
                    OutputFile = outputPath,
                    Status = "Complete",
                    PercentComplete = 100,
                    Elapsed = sw.Elapsed
                });

                return outputPath;
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(outputPath);
                return null;
            }
            catch
            {
                TryDeleteFile(outputPath);
                return null;
            }
        }

        /// <summary>
        /// Transcode a segment of video (for adaptive streaming / on-the-fly).
        /// </summary>
        public async Task<string?> TranscodeSegmentAsync(
            string sourceFilePath,
            double startSeconds,
            double durationSeconds,
            TranscodeQuality quality = TranscodeQuality.Auto,
            CancellationToken ct = default)
        {
            var ffmpegPath = FindFfmpeg();
            if (ffmpegPath == null || !File.Exists(sourceFilePath))
                return null;

            var segmentPath = Path.Combine(_cachePath, "segments",
                $"{GetFileHash(sourceFilePath)}_{startSeconds:F0}_{durationSeconds:F0}_{quality}.mp4");

            if (File.Exists(segmentPath))
                return segmentPath;

            var segDir = Path.GetDirectoryName(segmentPath);
            if (!string.IsNullOrEmpty(segDir) && !Directory.Exists(segDir))
                Directory.CreateDirectory(segDir);

            var (width, height) = GetResolution(quality);
            var scaleFilter = width > 0 ? $"-vf scale={width}:{height}" : "";

            var args = $"-ss {startSeconds} -t {durationSeconds} -i \"{sourceFilePath}\" " +
                       $"-c:v libx264 -preset fast -crf 23 {scaleFilter} " +
                       $"-c:a aac -b:a 128k -movflags +faststart -y \"{segmentPath}\"";

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = args,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            await process.WaitForExitAsync(ct);

            return process.ExitCode == 0 && File.Exists(segmentPath) ? segmentPath : null;
        }

        /// <summary>
        /// Check if a video file needs transcoding for web playback.
        /// Web browsers natively support MP4 (H.264 + AAC) and WebM (VP8/VP9 + Opus/Vorbis).
        /// </summary>
        public static bool NeedsTranscoding(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            // MP4 and WebM are generally web-compatible
            return ext switch
            {
                ".mp4" => false, // Usually compatible
                ".webm" => false, // Usually compatible
                ".m4v" => false, // Usually compatible (MP4 variant)
                _ => true // MKV, AVI, MOV, WMV, FLV, etc. need transcoding
            };
        }

        /// <summary>
        /// Get the content type for a transcoded file.
        /// </summary>
        public static string GetTranscodedContentType() => "video/mp4";

        /// <summary>
        /// Clear the transcode cache.
        /// </summary>
        public long ClearCache()
        {
            long freedBytes = 0;
            try
            {
                if (Directory.Exists(_cachePath))
                {
                    var files = Directory.GetFiles(_cachePath, "*.*", SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            freedBytes += fi.Length;
                            fi.Delete();
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return freedBytes;
        }

        /// <summary>
        /// Get total size of transcode cache in bytes.
        /// </summary>
        public long GetCacheSize()
        {
            try
            {
                if (!Directory.Exists(_cachePath)) return 0;
                return new DirectoryInfo(_cachePath)
                    .EnumerateFiles("*.*", SearchOption.AllDirectories)
                    .Sum(f => f.Length);
            }
            catch { return 0; }
        }

        #region Private Helpers

        private string GetOutputPath(string sourceFilePath, TranscodeQuality quality)
        {
            var hash = GetFileHash(sourceFilePath);
            var qualitySuffix = quality switch
            {
                TranscodeQuality.SD480 => "_480p",
                TranscodeQuality.HD720 => "_720p",
                TranscodeQuality.FullHD1080 => "_1080p",
                TranscodeQuality.Original => "_original",
                _ => "_auto"
            };
            return Path.Combine(_cachePath, $"{hash}{qualitySuffix}.mp4");
        }

        private static string GetFileHash(string filePath)
        {
            // Use a fast hash of path + last write time for cache key
            var info = new FileInfo(filePath);
            var key = $"{filePath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
            return BitConverter.ToString(hash).Replace("-", "")[..16].ToLowerInvariant();
        }

        private static string BuildTranscodeArgs(string input, string output, TranscodeQuality quality)
        {
            var (width, height) = GetResolution(quality);
            var scaleFilter = width > 0 ? $"-vf \"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2\"" : "";

            // H.264 + AAC in MP4 container - universally compatible
            return $"-i \"{input}\" -c:v libx264 -preset medium -crf 22 {scaleFilter} " +
                   $"-c:a aac -b:a 192k -ac 2 -movflags +faststart " +
                   $"-c:s mov_text " + // Preserve subtitles if possible
                   $"-progress pipe:2 -y \"{output}\"";
        }

        private static (int Width, int Height) GetResolution(TranscodeQuality quality)
        {
            return quality switch
            {
                TranscodeQuality.SD480 => (854, 480),
                TranscodeQuality.HD720 => (1280, 720),
                TranscodeQuality.FullHD1080 => (1920, 1080),
                TranscodeQuality.Original => (0, 0), // No scaling
                _ => (1920, 1080) // Auto = up to 1080p
            };
        }

        private static async Task<TimeSpan> GetDurationAsync(string filePath, string ffmpegPath, CancellationToken ct)
        {
            try
            {
                // Use ffprobe if available (same directory as ffmpeg)
                var ffprobePath = Path.Combine(Path.GetDirectoryName(ffmpegPath) ?? "", "ffprobe");
                if (!File.Exists(ffprobePath) && !File.Exists(ffprobePath + ".exe"))
                    ffprobePath = "ffprobe";

                var psi = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return TimeSpan.Zero;

                var output = await process.StandardOutput.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                if (double.TryParse(output.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var seconds))
                {
                    return TimeSpan.FromSeconds(seconds);
                }
            }
            catch { }

            return TimeSpan.Zero;
        }

        private static TranscodeProgress? ParseFfmpegProgress(string line, TimeSpan totalDuration, TimeSpan elapsed)
        {
            // FFmpeg progress lines look like:
            // frame=  100 fps= 25 q=28.0 size=    1024kB time=00:00:04.00 bitrate=2097.2kbits/s speed=1.5x
            if (!line.Contains("time=")) return null;

            try
            {
                var progress = new TranscodeProgress { Elapsed = elapsed };

                var timeMatch = System.Text.RegularExpressions.Regex.Match(line, @"time=(\d+:\d+:\d+\.\d+)");
                if (timeMatch.Success && TimeSpan.TryParse(timeMatch.Groups[1].Value, out var currentTime))
                {
                    if (totalDuration > TimeSpan.Zero)
                    {
                        progress.PercentComplete = Math.Min(100, (currentTime / totalDuration) * 100);
                        var remaining = totalDuration - currentTime;
                        if (progress.PercentComplete > 0)
                        {
                            var estimatedTotal = elapsed / (progress.PercentComplete / 100);
                            progress.EstimatedRemaining = estimatedTotal - elapsed;
                        }
                    }
                }

                var fpsMatch = System.Text.RegularExpressions.Regex.Match(line, @"fps=\s*([\d.]+)");
                if (fpsMatch.Success && double.TryParse(fpsMatch.Groups[1].Value, out var fps))
                    progress.CurrentFps = fps;

                var speedMatch = System.Text.RegularExpressions.Regex.Match(line, @"speed=\s*([\d.]+)x");
                if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, out var speed))
                    progress.Speed = speed;

                progress.Status = $"Transcoding... {progress.PercentComplete:F1}%";
                return progress;
            }
            catch
            {
                return null;
            }
        }

        private static string? FindFfmpeg()
        {
            // Try common locations
            var candidates = new[]
            {
                "ffmpeg", // PATH
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ffmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ffmpeg.exe")
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;

                    // Check if available in PATH
                    if (candidate == "ffmpeg")
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "ffmpeg",
                            Arguments = "-version",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var process = Process.Start(psi);
                        if (process != null)
                        {
                            process.WaitForExit(3000);
                            if (process.ExitCode == 0) return "ffmpeg";
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        #endregion
    }
}
