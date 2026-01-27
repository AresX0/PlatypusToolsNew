using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for screen recording functionality using FFmpeg.
    /// Supports video capture from desktop with optional audio recording.
    /// </summary>
    public class ScreenRecorderService : IDisposable
    {
        private Process? _ffmpegProcess;
        private bool _isRecording;
        private string? _outputPath;
        private DateTime _recordingStartTime;
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;

        /// <summary>
        /// Gets whether recording is currently in progress.
        /// </summary>
        public bool IsRecording
        {
            get { lock (_lock) { return _isRecording; } }
            private set { lock (_lock) { _isRecording = value; } }
        }

        /// <summary>
        /// Gets the current recording duration.
        /// </summary>
        public TimeSpan RecordingDuration => IsRecording ? DateTime.Now - _recordingStartTime : TimeSpan.Zero;

        /// <summary>
        /// Gets the output file path of the current/last recording.
        /// </summary>
        public string? OutputPath => _outputPath;

        /// <summary>
        /// Event raised when recording status changes.
        /// </summary>
        public event EventHandler<bool>? RecordingStatusChanged;

        /// <summary>
        /// Event raised when an error occurs during recording.
        /// </summary>
        public event EventHandler<string>? RecordingError;

        /// <summary>
        /// Event raised with FFmpeg output for progress/diagnostics.
        /// </summary>
        public event EventHandler<string>? RecordingProgress;

        /// <summary>
        /// Gets the list of available audio input devices.
        /// </summary>
        public async Task<List<AudioDevice>> GetAudioDevicesAsync()
        {
            var devices = new List<AudioDevice>();
            
            try
            {
                var ffmpegPath = GetFFmpegPath();
                if (string.IsNullOrEmpty(ffmpegPath))
                    return devices;

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = "-list_devices true -f dshow -i dummy",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                var output = new StringBuilder();
                
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };

                process.Start();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync();

                // Parse audio devices from FFmpeg output
                var lines = output.ToString().Split('\n');
                bool inAudioSection = false;
                
                foreach (var line in lines)
                {
                    if (line.Contains("DirectShow audio devices"))
                    {
                        inAudioSection = true;
                        continue;
                    }
                    if (inAudioSection && line.Contains("DirectShow video devices"))
                    {
                        break;
                    }
                    if (inAudioSection && line.Contains("\"") && !line.Contains("Alternative name"))
                    {
                        var start = line.IndexOf('"') + 1;
                        var end = line.LastIndexOf('"');
                        if (start > 0 && end > start)
                        {
                            var name = line.Substring(start, end - start);
                            devices.Add(new AudioDevice { Name = name, DeviceId = name });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting audio devices: {ex.Message}");
            }

            return devices;
        }

        /// <summary>
        /// Starts screen recording.
        /// </summary>
        /// <param name="options">Recording options.</param>
        /// <returns>True if recording started successfully.</returns>
        public async Task<bool> StartRecordingAsync(ScreenRecordingOptions options)
        {
            if (IsRecording)
            {
                RecordingError?.Invoke(this, "Recording is already in progress.");
                return false;
            }

            var ffmpegPath = GetFFmpegPath();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                RecordingError?.Invoke(this, "FFmpeg not found. Please ensure FFmpeg is installed and in your PATH.");
                return false;
            }

            try
            {
                _cts = new CancellationTokenSource();
                _outputPath = options.OutputPath;
                
                // Ensure output directory exists
                var outputDir = Path.GetDirectoryName(_outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                // Build FFmpeg arguments
                var args = BuildFFmpegArguments(options);
                
                RecordingProgress?.Invoke(this, $"Starting recording with command: ffmpeg {args}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = ffmpegPath,
                    Arguments = args,
                    RedirectStandardInput = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _ffmpegProcess = new Process { StartInfo = startInfo };
                
                _ffmpegProcess.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        RecordingProgress?.Invoke(this, e.Data);
                    }
                };

                                _ffmpegProcess.Start();
                _ffmpegProcess.BeginErrorReadLine();
                
                _recordingStartTime = DateTime.Now;
                IsRecording = true;
                RecordingStatusChanged?.Invoke(this, true);

                RecordingProgress?.Invoke(this, "Recording started.");
                return true;
            }
            catch (Exception ex)
            {
                RecordingError?.Invoke(this, $"Failed to start recording: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Stops the current recording.
        /// </summary>
        /// <returns>The path to the recorded file, or null if recording failed.</returns>
        public async Task<string?> StopRecordingAsync()
        {
            RecordingProgress?.Invoke(this, $"[SERVICE] StopRecordingAsync called. IsRecording={IsRecording}, _ffmpegProcess={(_ffmpegProcess != null ? "exists" : "null")}");
            
            if (!IsRecording || _ffmpegProcess == null)
            {
                RecordingProgress?.Invoke(this, $"[SERVICE] Early return - IsRecording={IsRecording}, _ffmpegProcess is null={_ffmpegProcess == null}");
                return null;
            }

            try
            {
                RecordingProgress?.Invoke(this, "[SERVICE] Beginning stop sequence...");

                // Try multiple methods to stop FFmpeg gracefully
                bool stopped = false;
                
                RecordingProgress?.Invoke(this, $"[SERVICE] FFmpeg HasExited={_ffmpegProcess.HasExited}");
                
                // Method 1: Send 'q' to FFmpeg stdin (graceful stop)
                try
                {
                    if (!_ffmpegProcess.HasExited)
                    {
                        RecordingProgress?.Invoke(this, "[SERVICE] Method 1: Sending 'q' to stdin...");
                        await _ffmpegProcess.StandardInput.WriteLineAsync("q");
                        await _ffmpegProcess.StandardInput.FlushAsync();
                        _ffmpegProcess.StandardInput.Close();
                        RecordingProgress?.Invoke(this, "[SERVICE] Stdin closed, waiting for exit...");
                        
                        // Wait briefly for graceful exit
                        stopped = _ffmpegProcess.WaitForExit(3000);
                        RecordingProgress?.Invoke(this, $"[SERVICE] Method 1 result: stopped={stopped}");
                    }
                }
                catch (Exception ex)
                {
                    RecordingProgress?.Invoke(this, $"[SERVICE] Method 1 exception: {ex.Message}");
                }

                // Method 2: If still running, try closing main window
                if (!stopped && !_ffmpegProcess.HasExited)
                {
                    try
                    {
                        RecordingProgress?.Invoke(this, "[SERVICE] Method 2: CloseMainWindow...");
                        _ffmpegProcess.CloseMainWindow();
                        stopped = _ffmpegProcess.WaitForExit(2000);
                        RecordingProgress?.Invoke(this, $"[SERVICE] Method 2 result: stopped={stopped}");
                    }
                    catch (Exception ex)
                    {
                        RecordingProgress?.Invoke(this, $"[SERVICE] Method 2 exception: {ex.Message}");
                    }
                }

                // Method 3: Force kill if still running
                if (!stopped && !_ffmpegProcess.HasExited)
                {
                    RecordingProgress?.Invoke(this, "[SERVICE] Method 3: Force killing...");
                    try
                    {
                        _ffmpegProcess.Kill(entireProcessTree: true);
                        _ffmpegProcess.WaitForExit(2000);
                        RecordingProgress?.Invoke(this, "[SERVICE] Method 3: Kill completed");
                    }
                    catch (Exception ex)
                    {
                        RecordingProgress?.Invoke(this, $"[SERVICE] Method 3 exception: {ex.Message}");
                    }
                }

                IsRecording = false;
                RecordingProgress?.Invoke(this, "[SERVICE] IsRecording set to false");
                RecordingStatusChanged?.Invoke(this, false);

                // Give filesystem a moment to finalize the file
                await Task.Delay(500);

                RecordingProgress?.Invoke(this, $"[SERVICE] Checking for output file: {_outputPath}");
                
                // Verify output file exists
                if (File.Exists(_outputPath))
                {
                    var fileInfo = new FileInfo(_outputPath);
                    RecordingProgress?.Invoke(this, $"[SERVICE] Recording saved: {_outputPath} ({FormatFileSize(fileInfo.Length)})");
                    return _outputPath;
                }
                else
                {
                    RecordingProgress?.Invoke(this, "[SERVICE] ERROR: Output file was not created");
                    RecordingError?.Invoke(this, "Recording file was not created.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                RecordingProgress?.Invoke(this, $"[SERVICE] Exception in StopRecordingAsync: {ex.GetType().Name}: {ex.Message}");
                RecordingError?.Invoke(this, $"Error stopping recording: {ex.Message}");
                return null;
            }
            finally
            {
                RecordingProgress?.Invoke(this, "[SERVICE] Finally block - disposing process");
                _ffmpegProcess?.Dispose();
                _ffmpegProcess = null;
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Cancels the current recording without saving.
        /// </summary>
        public void CancelRecording()
        {
            if (!IsRecording || _ffmpegProcess == null)
                return;

            try
            {
                _ffmpegProcess.Kill();
            }
            catch { }

            IsRecording = false;
            RecordingStatusChanged?.Invoke(this, false);

            // Delete partial file
            if (!string.IsNullOrEmpty(_outputPath) && File.Exists(_outputPath))
            {
                try { File.Delete(_outputPath); } catch { }
            }

            RecordingProgress?.Invoke(this, "Recording cancelled.");
        }

        private string BuildFFmpegArguments(ScreenRecordingOptions options)
        {
            var args = new StringBuilder();

            // Input: desktop capture using GDI grabber (gdigrab)
            args.Append("-f gdigrab ");
            
            // Frame rate
            args.Append($"-framerate {options.FrameRate} ");

            // Capture area
            if (options.CaptureRegion.HasValue)
            {
                var r = options.CaptureRegion.Value;
                args.Append($"-offset_x {r.X} -offset_y {r.Y} ");
                args.Append($"-video_size {r.Width}x{r.Height} ");
            }

            // Desktop input
            args.Append("-i desktop ");

            // Audio input if enabled
            if (options.RecordAudio)
            {
                if (options.RecordSystemAudio)
                {
                    // Use WASAPI loopback to capture system audio output (what you hear)
                    // This captures audio from speakers/headphones
                    args.Append("-f dshow -i audio=\"virtual-audio-capturer\" ");
                }
                else if (!string.IsNullOrEmpty(options.AudioDevice))
                {
                    // Use specified microphone/input device
                    args.Append($"-f dshow -i audio=\"{options.AudioDevice}\" ");
                }
            }

            // Video codec
            switch (options.VideoCodec)
            {
                case VideoCodec.H264:
                    args.Append("-c:v libx264 -preset ultrafast -crf 18 ");
                    break;
                case VideoCodec.H265:
                    args.Append("-c:v libx265 -preset ultrafast -crf 20 ");
                    break;
                case VideoCodec.VP9:
                    args.Append("-c:v libvpx-vp9 -crf 30 -b:v 0 ");
                    break;
                default:
                    args.Append("-c:v libx264 -preset ultrafast -crf 18 ");
                    break;
            }

            // Audio codec if recording audio
            if (options.RecordAudio && (options.RecordSystemAudio || !string.IsNullOrEmpty(options.AudioDevice)))
            {
                args.Append("-c:a aac -b:a 192k ");
            }

            // Pixel format for compatibility
            args.Append("-pix_fmt yuv420p ");

            // Duration limit if specified
            if (options.MaxDuration.HasValue)
            {
                args.Append($"-t {options.MaxDuration.Value.TotalSeconds:F0} ");
            }

            // Output file (overwrite if exists)
            args.Append($"-y \"{options.OutputPath}\"");

            return args.ToString();
        }

        private string? GetFFmpegPath()
        {
            // Check if ffmpeg is in PATH
            var envPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            var paths = envPath.Split(Path.PathSeparator);
            
            foreach (var path in paths)
            {
                var ffmpegPath = Path.Combine(path, "ffmpeg.exe");
                if (File.Exists(ffmpegPath))
                    return ffmpegPath;
            }

            // Check common installation locations
            var commonPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "FFmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "FFmpeg", "bin", "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppContext.BaseDirectory, "tools", "ffmpeg.exe"),
            };

            foreach (var path in commonPaths)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double len = bytes;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        public void Dispose()
        {
            if (IsRecording)
            {
                CancelRecording();
            }
            _ffmpegProcess?.Dispose();
            _cts?.Dispose();
        }
    }

    /// <summary>
    /// Represents an audio input device.
    /// </summary>
    public class AudioDevice
    {
        public string Name { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        
        public override string ToString() => Name;
    }

    /// <summary>
    /// Options for screen recording.
    /// </summary>
    public class ScreenRecordingOptions
    {
        /// <summary>
        /// Output file path for the recording.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>
        /// Whether to record audio.
        /// </summary>
        public bool RecordAudio { get; set; }

        /// <summary>
        /// Whether to record system audio output (loopback) instead of microphone.
        /// </summary>
        public bool RecordSystemAudio { get; set; } = true;

        /// <summary>
        /// Audio device name for recording (DirectShow device name).
        /// Only used if RecordSystemAudio is false.
        /// </summary>
        public string? AudioDevice { get; set; }

        /// <summary>
        /// Frame rate for recording.
        /// </summary>
        public int FrameRate { get; set; } = 30;

        /// <summary>
        /// Video codec to use.
        /// </summary>
        public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;

        /// <summary>
        /// Optional capture region. If null, captures full screen.
        /// </summary>
        public CaptureRegion? CaptureRegion { get; set; }

        /// <summary>
        /// Maximum recording duration. If null, records until stopped.
        /// </summary>
        public TimeSpan? MaxDuration { get; set; }

        /// <summary>
        /// Delay before starting recording (countdown).
        /// </summary>
        public TimeSpan? StartDelay { get; set; }
    }

    /// <summary>
    /// Video codec options.
    /// </summary>
    public enum VideoCodec
    {
        H264,
        H265,
        VP9
    }

    /// <summary>
    /// Represents a capture region on screen.
    /// </summary>
    public struct CaptureRegion
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public CaptureRegion(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }
}
