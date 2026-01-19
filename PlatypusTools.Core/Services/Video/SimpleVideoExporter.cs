using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Simple, robust video exporter that directly calls FFmpeg.
    /// Uses a two-pass approach similar to Shotcut:
    /// 1. Prepare individual clips with filters
    /// 2. Concatenate using concat demuxer or filter
    /// </summary>
    public class SimpleVideoExporter
    {
        private readonly string _ffmpegPath;
        private readonly string _tempDir;

        public SimpleVideoExporter(string? ffmpegPath = null)
        {
            _ffmpegPath = ffmpegPath ?? FindFFmpeg() 
                ?? throw new InvalidOperationException("FFmpeg not found. Please install FFmpeg or add it to PATH.");
            
            _tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "Export", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        private static string? FindFFmpeg()
        {
            // Check common locations - prioritize known Windows locations
            var paths = new[]
            {
                @"C:\convert\ffmpeg.exe",
                @"C:\convert\bin\ffmpeg.exe",
                @"C:\Path\ffmpeg.exe",
                @"C:\Path\bin\ffmpeg.exe",
                @"C:\ffmpeg\ffmpeg.exe",
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe"),
            };

            foreach (var p in paths)
            {
                if (File.Exists(p)) return p;
            }

            // Check PATH
            var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in envPath.Split(Path.PathSeparator))
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Exports timeline clips to a single video file.
        /// </summary>
        public async Task<ExportResult> ExportAsync(
            List<TimelineTrack> tracks,
            string outputPath,
            ExportSettings settings,
            IProgress<ExportProgress>? progress = null,
            CancellationToken ct = default)
        {
            var result = new ExportResult { OutputPath = outputPath };
            var log = new StringBuilder();

            try
            {
                log.AppendLine($"[{DateTime.Now:HH:mm:ss}] Starting export to: {outputPath}");
                log.AppendLine($"[{DateTime.Now:HH:mm:ss}] Settings: {settings.Width}x{settings.Height} @ {settings.FrameRate}fps");
                
                progress?.Report(new ExportProgress { Phase = "Preparing", Percent = 0, Message = "Analyzing clips..." });

                // Get all clips sorted by start position
                var videoClips = tracks
                    .Where(t => t.Type == TrackType.Video && t.IsVisible)
                    .SelectMany(t => t.Clips)
                    .OrderBy(c => c.StartPosition)
                    .ToList();

                var audioClips = tracks
                    .Where(t => (t.Type == TrackType.Audio || t.Type == TrackType.Video) && !t.IsMuted)
                    .SelectMany(t => t.Clips.Where(c => !c.IsMuted))
                    .OrderBy(c => c.StartPosition)
                    .ToList();

                if (videoClips.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "No video clips on timeline to export.";
                    return result;
                }

                log.AppendLine($"[{DateTime.Now:HH:mm:ss}] Found {videoClips.Count} video clips, {audioClips.Count} audio clips");

                // Simple case: Single clip export
                if (videoClips.Count == 1 && audioClips.Count <= 1)
                {
                    var clip = videoClips[0];
                    log.AppendLine($"[{DateTime.Now:HH:mm:ss}] Single clip export: {clip.SourcePath}");
                    
                    await ExportSingleClipAsync(clip, outputPath, settings, progress, log, ct);
                }
                else
                {
                    // Multiple clips: Use concat approach
                    log.AppendLine($"[{DateTime.Now:HH:mm:ss}] Multi-clip export using filter_complex");
                    
                    await ExportMultipleClipsAsync(videoClips, audioClips, outputPath, settings, progress, log, ct);
                }

                // Verify output
                if (File.Exists(outputPath))
                {
                    var fileInfo = new FileInfo(outputPath);
                    result.Success = true;
                    result.FileSize = fileInfo.Length;
                    log.AppendLine($"[{DateTime.Now:HH:mm:ss}] Export complete: {fileInfo.Length / (1024 * 1024):F1} MB");
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = "Output file was not created.";
                }
            }
            catch (Exception ex)
            {
                log.AppendLine($"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}");
                log.AppendLine(ex.StackTrace);
                result.Success = false;
                result.ErrorMessage = ex.Message;
            }
            finally
            {
                result.Log = log.ToString();
                
                // Cleanup temp files
                try
                {
                    if (Directory.Exists(_tempDir))
                    {
                        Directory.Delete(_tempDir, true);
                    }
                }
                catch { }

                progress?.Report(new ExportProgress { Phase = "Complete", Percent = 100, Message = result.Success ? "Export complete!" : result.ErrorMessage ?? "Export failed" });
            }

            return result;
        }

        private async Task ExportSingleClipAsync(
            TimelineClip clip,
            string outputPath,
            ExportSettings settings,
            IProgress<ExportProgress>? progress,
            StringBuilder log,
            CancellationToken ct)
        {
            var args = new StringBuilder();
            
            // Input with seeking
            if (clip.SourceStart > TimeSpan.Zero)
            {
                args.Append($"-ss {clip.SourceStart.TotalSeconds:F3} ");
            }
            args.Append($"-i \"{clip.SourcePath}\" ");

            // Duration
            var duration = clip.SourceEnd > TimeSpan.Zero 
                ? clip.SourceEnd - clip.SourceStart 
                : clip.Duration;
            args.Append($"-t {duration.TotalSeconds:F3} ");

            // Video filters
            var videoFilters = new List<string>
            {
                $"scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=decrease",
                $"pad={settings.Width}:{settings.Height}:(ow-iw)/2:(oh-ih)/2:black"
            };

            // Speed adjustment
            if (Math.Abs(clip.Speed - 1.0) > 0.01)
            {
                videoFilters.Add($"setpts={1.0/clip.Speed}*PTS");
            }

            args.Append($"-vf \"{string.Join(",", videoFilters)}\" ");

            // Video codec
            args.Append($"-c:v {settings.VideoCodec} ");
            args.Append($"-preset {settings.Preset} ");
            args.Append($"-crf {settings.Crf} ");
            args.Append($"-r {settings.FrameRate} ");
            args.Append($"-pix_fmt {settings.PixelFormat} ");

            // Audio - always downmix to stereo for compatibility with native AAC encoder
            if (clip.Volume > 0 && !clip.IsMuted)
            {
                args.Append($"-c:a {settings.AudioCodec} ");
                args.Append("-ac 2 "); // Stereo downmix for 5.1/7.1 compatibility
                args.Append($"-b:a {settings.AudioBitrate}k ");
                if (Math.Abs(clip.Volume - 1.0) > 0.01)
                {
                    args.Append($"-af \"volume={clip.Volume}\" ");
                }
            }
            else
            {
                args.Append("-an ");
            }

            // Output
            args.Append($"-y \"{outputPath}\"");

            log.AppendLine($"[{DateTime.Now:HH:mm:ss}] FFmpeg command: ffmpeg {args}");

            await RunFFmpegAsync(args.ToString(), duration, progress, log, ct);
        }

        private async Task ExportMultipleClipsAsync(
            List<TimelineClip> videoClips,
            List<TimelineClip> audioClips,
            string outputPath,
            ExportSettings settings,
            IProgress<ExportProgress>? progress,
            StringBuilder log,
            CancellationToken ct)
        {
            // Calculate total duration for progress
            var totalDuration = videoClips.Any() 
                ? videoClips.Max(c => c.StartPosition + c.Duration) 
                : TimeSpan.Zero;

            // Build inputs and filter complex
            var args = new StringBuilder();
            var filterComplex = new StringBuilder();
            var inputMap = new Dictionary<string, int>();
            var inputIdx = 0;

            // Add unique inputs
            foreach (var clip in videoClips.Concat(audioClips))
            {
                if (!inputMap.ContainsKey(clip.SourcePath))
                {
                    args.Append($"-i \"{clip.SourcePath}\" ");
                    inputMap[clip.SourcePath] = inputIdx++;
                }
            }

            // Build video filter chain for each clip
            var videoOutputs = new List<string>();
            for (int i = 0; i < videoClips.Count; i++)
            {
                var clip = videoClips[i];
                var srcIdx = inputMap[clip.SourcePath];
                var outLabel = $"v{i}";
                
                var filters = new List<string>();
                
                // Trim
                if (clip.SourceStart > TimeSpan.Zero || clip.SourceEnd > TimeSpan.Zero)
                {
                    var start = clip.SourceStart.TotalSeconds;
                    var end = clip.SourceEnd > TimeSpan.Zero ? clip.SourceEnd.TotalSeconds : (clip.SourceStart + clip.Duration).TotalSeconds;
                    filters.Add($"trim=start={start:F3}:end={end:F3}");
                    filters.Add("setpts=PTS-STARTPTS");
                }

                // Speed
                if (Math.Abs(clip.Speed - 1.0) > 0.01)
                {
                    filters.Add($"setpts={1.0/clip.Speed}*PTS");
                }

                // Scale and pad
                filters.Add($"scale={settings.Width}:{settings.Height}:force_original_aspect_ratio=decrease");
                filters.Add($"pad={settings.Width}:{settings.Height}:(ow-iw)/2:(oh-ih)/2:black");
                filters.Add($"fps={settings.FrameRate}");
                filters.Add("format=yuv420p");

                var filterChain = string.Join(",", filters);
                filterComplex.Append($"[{srcIdx}:v]{filterChain}[{outLabel}]; ");
                videoOutputs.Add($"[{outLabel}]");
            }

            // Concatenate video
            if (videoOutputs.Count > 1)
            {
                filterComplex.Append($"{string.Join("", videoOutputs)}concat=n={videoOutputs.Count}:v=1:a=0[vout]; ");
            }
            else
            {
                // Rename single output
                var lastFilter = filterComplex.ToString();
                filterComplex.Clear();
                filterComplex.Append(lastFilter.Replace($"[v0]", "[vout]"));
            }

            // Build audio filter chain
            var audioOutputs = new List<string>();
            for (int i = 0; i < audioClips.Count; i++)
            {
                var clip = audioClips[i];
                if (clip.IsMuted || clip.Volume <= 0) continue;
                
                var srcIdx = inputMap[clip.SourcePath];
                var outLabel = $"a{i}";
                
                var filters = new List<string>();
                
                // Trim
                if (clip.SourceStart > TimeSpan.Zero || clip.SourceEnd > TimeSpan.Zero)
                {
                    var start = clip.SourceStart.TotalSeconds;
                    var end = clip.SourceEnd > TimeSpan.Zero ? clip.SourceEnd.TotalSeconds : (clip.SourceStart + clip.Duration).TotalSeconds;
                    filters.Add($"atrim=start={start:F3}:end={end:F3}");
                    filters.Add("asetpts=PTS-STARTPTS");
                }

                // Speed
                if (Math.Abs(clip.Speed - 1.0) > 0.01)
                {
                    filters.Add($"atempo={clip.Speed}");
                }

                // Volume
                if (Math.Abs(clip.Volume - 1.0) > 0.01)
                {
                    filters.Add($"volume={clip.Volume}");
                }

                if (filters.Count == 0)
                {
                    filters.Add("anull");
                }

                var filterChain = string.Join(",", filters);
                filterComplex.Append($"[{srcIdx}:a]{filterChain}[{outLabel}]; ");
                audioOutputs.Add($"[{outLabel}]");
            }

            // Mix or concat audio
            if (audioOutputs.Count > 1)
            {
                filterComplex.Append($"{string.Join("", audioOutputs)}concat=n={audioOutputs.Count}:v=0:a=1[aout]");
            }
            else if (audioOutputs.Count == 1)
            {
                var lastFilter = filterComplex.ToString();
                filterComplex.Clear();
                filterComplex.Append(lastFilter.Replace($"[a0]", "[aout]"));
            }

            // Build full command
            var filterStr = filterComplex.ToString().TrimEnd(' ', ';');
            args.Append($"-filter_complex \"{filterStr}\" ");
            
            // Map outputs
            args.Append("-map \"[vout]\" ");
            if (audioOutputs.Count > 0)
            {
                args.Append("-map \"[aout]\" ");
            }

            // Video codec
            args.Append($"-c:v {settings.VideoCodec} ");
            args.Append($"-preset {settings.Preset} ");
            args.Append($"-crf {settings.Crf} ");
            args.Append($"-pix_fmt {settings.PixelFormat} ");

            // Audio codec - always downmix to stereo for compatibility
            if (audioOutputs.Count > 0)
            {
                args.Append($"-c:a {settings.AudioCodec} ");
                args.Append("-ac 2 "); // Stereo downmix for 5.1/7.1 compatibility
                args.Append($"-b:a {settings.AudioBitrate}k ");
            }

            // Output
            args.Append($"-y \"{outputPath}\"");

            log.AppendLine($"[{DateTime.Now:HH:mm:ss}] FFmpeg filter_complex command:");
            log.AppendLine($"ffmpeg {args}");

            await RunFFmpegAsync(args.ToString(), totalDuration, progress, log, ct);
        }

        private async Task RunFFmpegAsync(
            string args,
            TimeSpan totalDuration,
            IProgress<ExportProgress>? progress,
            StringBuilder log,
            CancellationToken ct)
        {
            // Use Arguments property instead of constructor to avoid escaping issues
            var psi = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            var errorOutput = new StringBuilder();

            process.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;
                errorOutput.AppendLine(e.Data);

                // Parse progress from FFmpeg output
                var match = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                if (match.Success && totalDuration.TotalSeconds > 0)
                {
                    var hours = int.Parse(match.Groups[1].Value);
                    var minutes = int.Parse(match.Groups[2].Value);
                    var seconds = int.Parse(match.Groups[3].Value);
                    var centiseconds = int.Parse(match.Groups[4].Value);

                    var currentSeconds = hours * 3600 + minutes * 60 + seconds + centiseconds / 100.0;
                    var percent = Math.Min(currentSeconds / totalDuration.TotalSeconds * 100, 99);
                    
                    progress?.Report(new ExportProgress 
                    { 
                        Phase = "Encoding", 
                        Percent = percent,
                        Message = $"Encoding: {FormatTime(TimeSpan.FromSeconds(currentSeconds))} / {FormatTime(totalDuration)}"
                    });
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
                throw;
            }

            log.AppendLine($"[{DateTime.Now:HH:mm:ss}] FFmpeg exit code: {process.ExitCode}");
            
            if (process.ExitCode != 0)
            {
                log.AppendLine("FFmpeg error output:");
                log.AppendLine(errorOutput.ToString());
                throw new Exception($"FFmpeg failed with exit code {process.ExitCode}. Check log for details.");
            }
        }

        private static string FormatTime(TimeSpan ts)
        {
            return ts.TotalHours >= 1 
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}" 
                : $"{ts.Minutes}:{ts.Seconds:D2}";
        }
    }

    /// <summary>
    /// Export settings.
    /// </summary>
    public class ExportSettings
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public double FrameRate { get; set; } = 30;
        
        public string VideoCodec { get; set; } = "libx264";
        public string Preset { get; set; } = "medium";
        public int Crf { get; set; } = 23;
        public string PixelFormat { get; set; } = "yuv420p";
        
        public string AudioCodec { get; set; } = "aac";
        public int AudioBitrate { get; set; } = 192;
        
        public string Container { get; set; } = "mp4";

        // Presets
        public static ExportSettings HD720p => new() { Width = 1280, Height = 720 };
        public static ExportSettings HD1080p => new() { Width = 1920, Height = 1080 };
        public static ExportSettings UHD4K => new() { Width = 3840, Height = 2160 };
        
        public static ExportSettings YouTube1080p => new() 
        { 
            Width = 1920, Height = 1080, 
            VideoCodec = "libx264", Crf = 18, Preset = "slow",
            AudioCodec = "aac", AudioBitrate = 320
        };
        
        public static ExportSettings Twitter => new() 
        { 
            Width = 1280, Height = 720, FrameRate = 30,
            VideoCodec = "libx264", Crf = 23, Preset = "medium"
        };
    }

    /// <summary>
    /// Export progress information.
    /// </summary>
    public class ExportProgress
    {
        public string Phase { get; set; } = "";
        public double Percent { get; set; }
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// Export result.
    /// </summary>
    public class ExportResult
    {
        public bool Success { get; set; }
        public string OutputPath { get; set; } = "";
        public string? ErrorMessage { get; set; }
        public long FileSize { get; set; }
        public string Log { get; set; } = "";
    }
}
