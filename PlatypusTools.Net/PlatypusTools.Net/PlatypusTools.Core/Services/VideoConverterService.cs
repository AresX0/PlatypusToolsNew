using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Services
{
    public interface IVideoConverterService
    {
        Task<List<VideoConversionTask>> ScanFolder(string folderPath, bool includeSubfolders, string[] extensions);
        Task<bool> ConvertVideo(VideoConversionTask task, IProgress<double>? progress = null, CancellationToken cancellationToken = default);
        Task<bool> ConvertBatch(List<VideoConversionTask> tasks, IProgress<(int completed, int total)>? progress = null, CancellationToken cancellationToken = default);
        string GetFFmpegPath();
        bool IsFFmpegAvailable();
        Task<VideoInfo?> GetVideoInfo(string filePath);
    }

    public class VideoInfo
    {
        public TimeSpan Duration { get; set; }
        public string Resolution { get; set; } = string.Empty;
        public string Codec { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public double FrameRate { get; set; }
    }

    public class VideoConverterService : IVideoConverterService
    {
        private readonly string[] _defaultExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".m4v" };

        public async Task<List<VideoConversionTask>> ScanFolder(string folderPath, bool includeSubfolders, string[] extensions)
        {
            var tasks = new List<VideoConversionTask>();
            
            if (!Directory.Exists(folderPath))
                return tasks;

            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var extensionsToUse = extensions.Length > 0 ? extensions : _defaultExtensions;

            foreach (var ext in extensionsToUse)
            {
                var files = Directory.GetFiles(folderPath, $"*{ext}", searchOption);
                foreach (var file in files)
                {
                    var info = await GetVideoInfo(file);
                    tasks.Add(new VideoConversionTask
                    {
                        SourcePath = file,
                        OutputPath = Path.ChangeExtension(file, ".mp4"),
                        TargetFormat = VideoFormat.MP4,
                        Quality = VideoQuality.Medium,
                        Status = ConversionStatus.Pending,
                        Duration = info?.Duration,
                        FileSizeBytes = info?.FileSizeBytes
                    });
                }
            }

            return tasks;
        }

        public async Task<bool> ConvertVideo(VideoConversionTask task, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
        {
            try
            {
                task.Status = ConversionStatus.Converting;
                task.StatusMessage = "Converting...";

                var ffmpegPath = GetFFmpegPath();
                if (!File.Exists(ffmpegPath))
                {
                    task.Status = ConversionStatus.Failed;
                    task.StatusMessage = "FFmpeg not found";
                    return false;
                }

                var arguments = BuildFFmpegArguments(task);
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                var errorOutput = string.Empty;
                var totalDuration = task.Duration ?? TimeSpan.Zero;

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (string.IsNullOrEmpty(e.Data)) return;
                    errorOutput += e.Data + "\n";

                    // Parse FFmpeg progress from stderr
                    var match = Regex.Match(e.Data, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                    if (match.Success && totalDuration.TotalSeconds > 0)
                    {
                        var hours = int.Parse(match.Groups[1].Value);
                        var minutes = int.Parse(match.Groups[2].Value);
                        var seconds = int.Parse(match.Groups[3].Value);
                        var currentTime = new TimeSpan(hours, minutes, seconds);
                        var progressPercent = (currentTime.TotalSeconds / totalDuration.TotalSeconds) * 100;
                        task.Progress = Math.Min(progressPercent, 100);
                        progress?.Report(task.Progress);
                    }
                };

                process.Start();
                process.BeginErrorReadLine();
                
                await process.WaitForExitAsync(cancellationToken);

                if (cancellationToken.IsCancellationRequested)
                {
                    try { process.Kill(); } catch { }
                    task.Status = ConversionStatus.Cancelled;
                    task.StatusMessage = "Cancelled";
                    return false;
                }

                if (process.ExitCode == 0 && File.Exists(task.OutputPath))
                {
                    task.Status = ConversionStatus.Completed;
                    task.StatusMessage = "Completed";
                    task.Progress = 100;
                    progress?.Report(100);
                    return true;
                }
                else
                {
                    task.Status = ConversionStatus.Failed;
                    task.StatusMessage = $"FFmpeg error (code {process.ExitCode})";
                    return false;
                }
            }
            catch (Exception ex)
            {
                task.Status = ConversionStatus.Failed;
                task.StatusMessage = $"Error: {ex.Message}";
                return false;
            }
        }

        public async Task<bool> ConvertBatch(List<VideoConversionTask> tasks, IProgress<(int completed, int total)>? progress = null, CancellationToken cancellationToken = default)
        {
            var completed = 0;
            var total = tasks.Count;

            foreach (var task in tasks)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;

                await ConvertVideo(task, null, cancellationToken);
                completed++;
                progress?.Report((completed, total));
            }

            return true;
        }

        public string GetFFmpegPath()
        {
            // Check common locations
            var possiblePaths = new[]
            {
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ffmpeg", "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe"),
                "ffmpeg" // Check PATH
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            // Try to find in PATH
            try
            {
                var pathVariable = Environment.GetEnvironmentVariable("PATH");
                if (pathVariable != null)
                {
                    var paths = pathVariable.Split(Path.PathSeparator);
                    foreach (var path in paths)
                    {
                        var ffmpegPath = Path.Combine(path, "ffmpeg.exe");
                        if (File.Exists(ffmpegPath))
                            return ffmpegPath;
                    }
                }
            }
            catch { }

            return "ffmpeg"; // Fallback to PATH
        }

        public bool IsFFmpegAvailable()
        {
            try
            {
                var ffmpegPath = GetFFmpegPath();
                if (ffmpegPath == "ffmpeg")
                {
                    // Try to run it to check if it's in PATH
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "ffmpeg",
                        Arguments = "-version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    return process != null;
                }
                return File.Exists(ffmpegPath);
            }
            catch
            {
                return false;
            }
        }

        public async Task<VideoInfo?> GetVideoInfo(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                var ffprobePath = GetFFmpegPath().Replace("ffmpeg.exe", "ffprobe.exe");
                if (!File.Exists(ffprobePath))
                    return null;

                var arguments = $"-v error -show_entries format=duration:stream=codec_name,width,height,r_frame_rate -of default=noprint_wrappers=1 \"{filePath}\"";
                var startInfo = new ProcessStartInfo
                {
                    FileName = ffprobePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var info = new VideoInfo
                {
                    FileSizeBytes = new FileInfo(filePath).Length
                };

                // Parse duration
                var durationMatch = Regex.Match(output, @"duration=(\d+\.?\d*)");
                if (durationMatch.Success && double.TryParse(durationMatch.Groups[1].Value, out var seconds))
                {
                    info.Duration = TimeSpan.FromSeconds(seconds);
                }

                // Parse resolution
                var widthMatch = Regex.Match(output, @"width=(\d+)");
                var heightMatch = Regex.Match(output, @"height=(\d+)");
                if (widthMatch.Success && heightMatch.Success)
                {
                    info.Resolution = $"{widthMatch.Groups[1].Value}x{heightMatch.Groups[1].Value}";
                }

                // Parse codec
                var codecMatch = Regex.Match(output, @"codec_name=(\w+)");
                if (codecMatch.Success)
                {
                    info.Codec = codecMatch.Groups[1].Value;
                }

                // Parse frame rate
                var fpsMatch = Regex.Match(output, @"r_frame_rate=(\d+)/(\d+)");
                if (fpsMatch.Success)
                {
                    var num = double.Parse(fpsMatch.Groups[1].Value);
                    var den = double.Parse(fpsMatch.Groups[2].Value);
                    info.FrameRate = num / den;
                }

                return info;
            }
            catch
            {
                return null;
            }
        }

        private string BuildFFmpegArguments(VideoConversionTask task)
        {
            var args = new List<string>();

            // Input file
            args.Add($"-i \"{task.SourcePath}\"");

            // Quality settings
            switch (task.Quality)
            {
                case VideoQuality.Source:
                    args.Add("-c copy");
                    break;
                case VideoQuality.High:
                    args.Add("-c:v libx264 -crf 18 -c:a aac -b:a 192k");
                    break;
                case VideoQuality.Medium:
                    args.Add("-c:v libx264 -crf 23 -c:a aac -b:a 128k");
                    break;
                case VideoQuality.Low:
                    args.Add("-c:v libx264 -crf 28 -c:a aac -b:a 96k");
                    break;
                case VideoQuality.VeryLow:
                    args.Add("-c:v libx264 -crf 32 -c:a aac -b:a 64k");
                    break;
            }

            // Preset for faster encoding
            if (task.Quality != VideoQuality.Source)
            {
                args.Add("-preset medium");
            }

            // Output file
            args.Add($"-y \"{task.OutputPath}\"");

            return string.Join(" ", args);
        }
    }
}
