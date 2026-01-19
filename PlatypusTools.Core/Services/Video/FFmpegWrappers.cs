using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Instance-based FFmpeg service wrapper for video editing.
    /// Wraps the static FFmpegService with additional features for video editing.
    /// </summary>
    public class FFmpegService
    {
        private readonly string? _ffmpegPath;
        
        public FFmpegService(string? ffmpegPath = null)
        {
            _ffmpegPath = ffmpegPath ?? Services.FFmpegService.FindFfmpeg();
        }

        public bool IsAvailable => !string.IsNullOrEmpty(_ffmpegPath) && File.Exists(_ffmpegPath);

        /// <summary>
        /// Executes FFmpeg with the given arguments.
        /// </summary>
        public async Task<FFmpegResult> ExecuteAsync(string args, CancellationToken ct = default)
        {
            return await Services.FFmpegService.RunAsync(args, _ffmpegPath, null, ct);
        }

        /// <summary>
        /// Executes FFmpeg with progress tracking based on duration.
        /// </summary>
        public async Task ExecuteWithProgressAsync(
            string args,
            TimeSpan totalDuration,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                throw new InvalidOperationException("FFmpeg not found");
            }

            var psi = new ProcessStartInfo(_ffmpegPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var totalSeconds = totalDuration.TotalSeconds;

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                // Parse FFmpeg progress output (e.g., "time=00:01:23.45")
                var match = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                if (match.Success)
                {
                    var hours = int.Parse(match.Groups[1].Value);
                    var minutes = int.Parse(match.Groups[2].Value);
                    var seconds = int.Parse(match.Groups[3].Value);
                    var centiseconds = int.Parse(match.Groups[4].Value);

                    var currentSeconds = hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
                    
                    if (totalSeconds > 0)
                    {
                        progress?.Report(Math.Min(currentSeconds / totalSeconds, 1.0));
                    }
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}");
            }
        }

        /// <summary>
        /// Gets FFmpeg version information.
        /// </summary>
        public async Task<string> GetVersionAsync()
        {
            var result = await ExecuteAsync("-version");
            return result.StdOut;
        }
    }

    /// <summary>
    /// Instance-based FFprobe service wrapper for video editing.
    /// Uses robust stream detection similar to Shotcut/MLT approach.
    /// </summary>
    public class FFprobeService
    {
        private readonly string? _ffprobePath;

        public FFprobeService(string? ffprobePath = null)
        {
            _ffprobePath = ffprobePath ?? Services.FFprobeService.FindFfprobe();
        }

        public bool IsAvailable => !string.IsNullOrEmpty(_ffprobePath) && File.Exists(_ffprobePath);

        /// <summary>
        /// Probes a media file for information using comprehensive JSON output.
        /// This approach is more reliable for various container formats (MKV, MP4, MOV, etc.)
        /// </summary>
        public async Task<MediaInfo> ProbeAsync(string path, CancellationToken ct = default)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Media file not found", path);
            }

            if (string.IsNullOrEmpty(_ffprobePath))
            {
                throw new InvalidOperationException("FFprobe not found");
            }

            var info = new MediaInfo { FilePath = path };

            try
            {
                // Use comprehensive JSON probe for all streams - most reliable method
                var args = $"-v quiet -print_format json -show_format -show_streams \"{path}\"";
                var jsonResult = await RunProbeAsync(args, ct);
                
                System.Diagnostics.Debug.WriteLine($"[FFprobe] Probing: {path}");
                System.Diagnostics.Debug.WriteLine($"[FFprobe] JSON length: {jsonResult?.Length ?? 0}");
                
                if (!string.IsNullOrEmpty(jsonResult))
                {
                    ParseJsonProbeResult(jsonResult, info);
                }
                
                // Fallback: If JSON parsing didn't find streams, try direct stream query
                if (!info.HasVideo && !info.HasAudio)
                {
                    System.Diagnostics.Debug.WriteLine("[FFprobe] JSON parse failed, trying direct stream query");
                    await ProbeStreamsDirect(path, info, ct);
                }

                System.Diagnostics.Debug.WriteLine($"[FFprobe] Result: HasVideo={info.HasVideo}, HasAudio={info.HasAudio}, Duration={info.Duration}, {info.Width}x{info.Height}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFprobe] Error: {ex.Message}");
                // Try fallback based on file extension
                info.HasVideo = IsVideoFileExtension(path);
                info.HasAudio = IsAudioFileExtension(path) || info.HasVideo;
            }

            return info;
        }

        private void ParseJsonProbeResult(string json, MediaInfo info)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[FFprobe] Parsing JSON ({json.Length} chars)");
                
                // Parse duration from format section
                var durationMatch = System.Text.RegularExpressions.Regex.Match(
                    json, @"""duration""\s*:\s*""?([0-9.]+)""?");
                if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value, 
                    System.Globalization.NumberStyles.Float, 
                    System.Globalization.CultureInfo.InvariantCulture, out var duration))
                {
                    info.Duration = TimeSpan.FromSeconds(duration);
                }

                // More robust: Find codec_type occurrences and extract surrounding context
                // Look for "codec_type": "video" or "codec_type": "audio" patterns
                var videoTypeMatch = System.Text.RegularExpressions.Regex.Match(
                    json, @"""codec_type""\s*:\s*""video""", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                if (videoTypeMatch.Success)
                {
                    info.HasVideo = true;
                    System.Diagnostics.Debug.WriteLine("[FFprobe] Found codec_type=video in JSON");
                    
                    // Extract width - search entire JSON since we know video exists
                    var widthMatch = System.Text.RegularExpressions.Regex.Match(
                        json, @"""width""\s*:\s*(\d+)");
                    if (widthMatch.Success)
                        info.Width = int.Parse(widthMatch.Groups[1].Value);

                    // Extract height
                    var heightMatch = System.Text.RegularExpressions.Regex.Match(
                        json, @"""height""\s*:\s*(\d+)");
                    if (heightMatch.Success)
                        info.Height = int.Parse(heightMatch.Groups[1].Value);

                    // Extract frame rate
                    var fpsMatch = System.Text.RegularExpressions.Regex.Match(
                        json, @"""r_frame_rate""\s*:\s*""(\d+)/(\d+)""");
                    if (fpsMatch.Success)
                    {
                        var num = double.Parse(fpsMatch.Groups[1].Value);
                        var den = double.Parse(fpsMatch.Groups[2].Value);
                        if (den > 0) info.FrameRate = num / den;
                    }

                    // Extract video codec name - look near video codec_type
                    var codecMatch = System.Text.RegularExpressions.Regex.Match(
                        json, @"""codec_name""\s*:\s*""(\w+)""");
                    if (codecMatch.Success)
                        info.VideoCodec = codecMatch.Groups[1].Value;
                        
                    System.Diagnostics.Debug.WriteLine($"[FFprobe] Video: {info.Width}x{info.Height} @ {info.FrameRate:F2}fps, codec={info.VideoCodec}");
                }
                
                var audioTypeMatch = System.Text.RegularExpressions.Regex.Match(
                    json, @"""codec_type""\s*:\s*""audio""", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    
                if (audioTypeMatch.Success)
                {
                    info.HasAudio = true;
                    System.Diagnostics.Debug.WriteLine("[FFprobe] Found codec_type=audio in JSON");
                    
                    // Extract sample rate
                    var srMatch = System.Text.RegularExpressions.Regex.Match(
                        json, @"""sample_rate""\s*:\s*""?(\d+)""?");
                    if (srMatch.Success)
                        info.AudioSampleRate = int.Parse(srMatch.Groups[1].Value);

                    // Extract channels
                    var chMatch = System.Text.RegularExpressions.Regex.Match(
                        json, @"""channels""\s*:\s*(\d+)");
                    if (chMatch.Success)
                        info.AudioChannels = int.Parse(chMatch.Groups[1].Value);
                        
                    System.Diagnostics.Debug.WriteLine($"[FFprobe] Audio: {info.AudioSampleRate}Hz, {info.AudioChannels}ch");
                }
                
                // If still no video/audio detected, try looking for common codec names
                if (!info.HasVideo)
                {
                    // Check for common video codecs
                    if (System.Text.RegularExpressions.Regex.IsMatch(json, 
                        @"""codec_name""\s*:\s*""(h264|hevc|h265|vp8|vp9|av1|mpeg4|mpeg2|mjpeg|prores|dnxhd)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        info.HasVideo = true;
                        System.Diagnostics.Debug.WriteLine("[FFprobe] Detected video by codec name");
                    }
                }
                
                if (!info.HasAudio)
                {
                    // Check for common audio codecs
                    if (System.Text.RegularExpressions.Regex.IsMatch(json,
                        @"""codec_name""\s*:\s*""(aac|mp3|opus|vorbis|flac|pcm_|ac3|eac3|dts|alac)""",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        info.HasAudio = true;
                        System.Diagnostics.Debug.WriteLine("[FFprobe] Detected audio by codec name");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FFprobe] JSON parse error: {ex.Message}");
            }
        }

        private async Task ProbeStreamsDirect(string path, MediaInfo info, CancellationToken ct)
        {
            // Try to get video stream info directly
            var videoArgs = $"-v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate -of csv=p=0 \"{path}\"";
            var videoResult = await RunProbeAsync(videoArgs, ct);
            
            if (!string.IsNullOrWhiteSpace(videoResult))
            {
                var parts = videoResult.Trim().Split(',');
                if (parts.Length >= 2)
                {
                    if (int.TryParse(parts[0], out var width) && width > 0) info.Width = width;
                    if (int.TryParse(parts[1], out var height) && height > 0) info.Height = height;
                    info.HasVideo = info.Width > 0 && info.Height > 0;
                }
            }

            // Try to get audio stream info
            var audioArgs = $"-v error -select_streams a:0 -show_entries stream=codec_name -of csv=p=0 \"{path}\"";
            var audioResult = await RunProbeAsync(audioArgs, ct);
            info.HasAudio = !string.IsNullOrWhiteSpace(audioResult);

            // Get duration if not already set
            if (info.Duration == TimeSpan.Zero)
            {
                var duration = await Services.FFprobeService.GetDurationSecondsAsync(path, _ffprobePath);
                if (duration > 0)
                    info.Duration = TimeSpan.FromSeconds(duration);
            }
        }

        private static bool IsVideoFileExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".wmv" or ".flv" or ".m4v" or ".mpg" or ".mpeg" or ".ts" or ".mts" or ".m2ts" or ".3gp";
        }

        private static bool IsAudioFileExtension(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext is ".mp3" or ".wav" or ".aac" or ".flac" or ".ogg" or ".m4a" or ".wma" or ".opus" or ".aiff";
        }

        private async Task<string> RunProbeAsync(string args, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(_ffprobePath)) return string.Empty;

            var psi = new ProcessStartInfo(_ffprobePath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return string.Empty;

            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync(ct);

            return output.Trim();
        }
    }

    /// <summary>
    /// Media file information from FFprobe.
    /// </summary>
    public class MediaInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double FrameRate { get; set; }
        public bool HasVideo { get; set; }
        public bool HasAudio { get; set; }
        public string VideoCodec { get; set; } = string.Empty;
        public string AudioCodec { get; set; } = string.Empty;
        public int AudioChannels { get; set; }
        public int AudioSampleRate { get; set; }
    }
}
