using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

// Alias to the root services to avoid ambiguity with Video namespace classes
using RootFFmpegService = PlatypusTools.Core.Services.FFmpegService;
using RootFFprobeService = PlatypusTools.Core.Services.FFprobeService;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Service for importing and extracting audio from media files.
    /// </summary>
    public class AudioImportService
    {
        private readonly string? _ffmpegPath;

        public AudioImportService(string? ffmpegPath = null)
        {
            _ffmpegPath = ffmpegPath ?? RootFFmpegService.FindFfmpeg();
        }

        /// <summary>
        /// Extracts audio from a video/audio file.
        /// </summary>
        /// <param name="inputPath">Path to source media file.</param>
        /// <param name="outputPath">Optional output path (auto-generated if null).</param>
        /// <param name="format">Output format (wav, mp3, aac, flac).</param>
        /// <param name="normalize">Whether to normalize audio levels.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Path to extracted audio file.</returns>
        public async Task<AudioImportResult> ExtractAudioAsync(
            string inputPath,
            string? outputPath = null,
            AudioFormat format = AudioFormat.Wav,
            bool normalize = false,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new AudioImportResult { SourcePath = inputPath };
            
            if (!File.Exists(inputPath))
            {
                result.Success = false;
                result.Error = "Source file not found";
                return result;
            }

            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                result.Success = false;
                result.Error = "FFmpeg not found";
                return result;
            }

            try
            {
                // Generate output path if not provided
                if (string.IsNullOrEmpty(outputPath))
                {
                    var dir = Path.GetDirectoryName(inputPath) ?? Path.GetTempPath();
                    var name = Path.GetFileNameWithoutExtension(inputPath);
                    var ext = GetExtension(format);
                    outputPath = Path.Combine(dir, $"{name}_audio{ext}");
                }

                result.OutputPath = outputPath;

                // Build FFmpeg arguments
                var args = BuildExtractArgs(inputPath, outputPath, format, normalize);
                
                progress?.Report(0.1);

                // Get duration for progress tracking
                var mediaInfo = RootFFprobeService.GetMediaInfo(inputPath);
                var totalDuration = mediaInfo?.Duration ?? TimeSpan.FromMinutes(5);

                // Run FFmpeg
                var ffmpegProgress = new Progress<string>(line =>
                {
                    var parsed = FFmpegProgressParser.ParseProgressLine(line, totalDuration);
                    if (parsed.HasValue)
                    {
                        progress?.Report(0.1 + parsed.Value * 0.9);
                    }
                });

                var ffResult = await RootFFmpegService.RunAsync(args, _ffmpegPath, ffmpegProgress, cancellationToken);

                if (ffResult.Success && File.Exists(outputPath))
                {
                    result.Success = true;
                    
                    // Get extracted audio info
                    var audioInfo = RootFFprobeService.GetMediaInfo(outputPath);
                    result.Duration = audioInfo?.Duration ?? TimeSpan.Zero;
                    result.SampleRate = audioInfo?.AudioSampleRate ?? 44100;
                    result.Channels = audioInfo?.AudioChannels ?? 2;
                    
                    progress?.Report(1.0);
                }
                else
                {
                    result.Success = false;
                    result.Error = ffResult.StdErr;
                }
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.Error = "Operation cancelled";
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                SimpleLogger.Error($"Audio extraction failed: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Imports an audio file into the project format.
        /// </summary>
        public async Task<AudioImportResult> ImportAudioAsync(
            string inputPath,
            string projectFolder,
            bool normalize = false,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new AudioImportResult { SourcePath = inputPath };
            
            if (!File.Exists(inputPath))
            {
                result.Success = false;
                result.Error = "Source file not found";
                return result;
            }

            try
            {
                // Ensure project folder exists
                Directory.CreateDirectory(projectFolder);
                
                var name = Path.GetFileNameWithoutExtension(inputPath);
                var outputPath = Path.Combine(projectFolder, $"{name}_imported.wav");
                result.OutputPath = outputPath;

                // Check if already WAV and no normalization needed
                var ext = Path.GetExtension(inputPath).ToLowerInvariant();
                if (ext == ".wav" && !normalize)
                {
                    // Just copy
                    File.Copy(inputPath, outputPath, overwrite: true);
                    result.Success = true;
                    
                    var info = RootFFprobeService.GetMediaInfo(outputPath);
                    result.Duration = info?.Duration ?? TimeSpan.Zero;
                    result.SampleRate = info?.AudioSampleRate ?? 44100;
                    result.Channels = info?.AudioChannels ?? 2;
                    
                    progress?.Report(1.0);
                    return result;
                }

                // Convert/normalize via FFmpeg
                return await ExtractAudioAsync(
                    inputPath, 
                    outputPath, 
                    AudioFormat.Wav, 
                    normalize, 
                    progress, 
                    cancellationToken);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Applies audio processing (normalize, compress, etc.).
        /// </summary>
        public async Task<AudioImportResult> ProcessAudioAsync(
            string inputPath,
            string outputPath,
            AudioProcessingOptions options,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new AudioImportResult { SourcePath = inputPath, OutputPath = outputPath };

            if (string.IsNullOrEmpty(_ffmpegPath))
            {
                result.Success = false;
                result.Error = "FFmpeg not found";
                return result;
            }

            try
            {
                var filters = BuildAudioFilters(options);
                var args = $"-i \"{inputPath}\" -af \"{filters}\" -y \"{outputPath}\"";

                var mediaInfo = RootFFprobeService.GetMediaInfo(inputPath);
                var totalDuration = mediaInfo?.Duration ?? TimeSpan.FromMinutes(5);

                var ffmpegProgress = new Progress<string>(line =>
                {
                    var parsed = FFmpegProgressParser.ParseProgressLine(line, totalDuration);
                    if (parsed.HasValue)
                    {
                        progress?.Report(parsed.Value);
                    }
                });

                var ffResult = await RootFFmpegService.RunAsync(args, _ffmpegPath, ffmpegProgress, cancellationToken);

                result.Success = ffResult.Success;
                if (!ffResult.Success)
                {
                    result.Error = ffResult.StdErr;
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        #region Private Methods

        private string BuildExtractArgs(string input, string output, AudioFormat format, bool normalize)
        {
            var codecArgs = format switch
            {
                AudioFormat.Wav => "-c:a pcm_s16le",
                AudioFormat.Mp3 => "-c:a libmp3lame -q:a 2",
                AudioFormat.Aac => "-c:a aac -b:a 192k",
                AudioFormat.Flac => "-c:a flac",
                AudioFormat.Opus => "-c:a libopus -b:a 128k",
                _ => "-c:a pcm_s16le"
            };

            var filterArgs = "";
            if (normalize)
            {
                filterArgs = "-af loudnorm=I=-16:LRA=11:TP=-1.5";
            }

            return $"-i \"{input}\" -vn {codecArgs} {filterArgs} -y \"{output}\"";
        }

        private string BuildAudioFilters(AudioProcessingOptions options)
        {
            var filters = new System.Collections.Generic.List<string>();

            if (options.Normalize)
            {
                filters.Add($"loudnorm=I={options.TargetLufs}:LRA=11:TP=-1.5");
            }

            if (Math.Abs(options.Volume - 1.0) > 0.01)
            {
                filters.Add($"volume={options.Volume}");
            }

            if (options.FadeInMs > 0)
            {
                filters.Add($"afade=t=in:st=0:d={options.FadeInMs / 1000.0}");
            }

            if (options.FadeOutMs > 0 && options.Duration.TotalMilliseconds > options.FadeOutMs)
            {
                var fadeStart = (options.Duration.TotalMilliseconds - options.FadeOutMs) / 1000.0;
                filters.Add($"afade=t=out:st={fadeStart}:d={options.FadeOutMs / 1000.0}");
            }

            if (options.Compress)
            {
                filters.Add("acompressor=threshold=0.5:ratio=4:attack=5:release=50");
            }

            if (options.RemoveSilence)
            {
                filters.Add("silenceremove=start_periods=1:start_duration=0:start_threshold=-50dB");
            }

            return string.Join(",", filters);
        }

        private string GetExtension(AudioFormat format) => format switch
        {
            AudioFormat.Wav => ".wav",
            AudioFormat.Mp3 => ".mp3",
            AudioFormat.Aac => ".m4a",
            AudioFormat.Flac => ".flac",
            AudioFormat.Opus => ".opus",
            _ => ".wav"
        };

        #endregion
    }

    /// <summary>
    /// Audio format options.
    /// </summary>
    public enum AudioFormat
    {
        Wav,
        Mp3,
        Aac,
        Flac,
        Opus
    }

    /// <summary>
    /// Result of audio import/extraction.
    /// </summary>
    public class AudioImportResult
    {
        public bool Success { get; set; }
        public string SourcePath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
    }

    /// <summary>
    /// Options for audio processing.
    /// </summary>
    public class AudioProcessingOptions
    {
        /// <summary>
        /// Whether to normalize audio.
        /// </summary>
        public bool Normalize { get; set; }
        
        /// <summary>
        /// Target LUFS for normalization.
        /// </summary>
        public double TargetLufs { get; set; } = -16;
        
        /// <summary>
        /// Volume multiplier.
        /// </summary>
        public double Volume { get; set; } = 1.0;
        
        /// <summary>
        /// Fade in duration in milliseconds.
        /// </summary>
        public int FadeInMs { get; set; }
        
        /// <summary>
        /// Fade out duration in milliseconds.
        /// </summary>
        public int FadeOutMs { get; set; }
        
        /// <summary>
        /// Total duration (for fade out timing).
        /// </summary>
        public TimeSpan Duration { get; set; }
        
        /// <summary>
        /// Whether to apply compression.
        /// </summary>
        public bool Compress { get; set; }
        
        /// <summary>
        /// Whether to remove silence.
        /// </summary>
        public bool RemoveSilence { get; set; }
    }
}
