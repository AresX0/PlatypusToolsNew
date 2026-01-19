using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.AI
{
    /// <summary>
    /// Local Whisper-based speech-to-text using whisper.cpp or Faster-Whisper.
    /// Falls back to cloud provider if local is unavailable.
    /// </summary>
    public class LocalWhisperService : IAISpeechToText
    {
        private string _whisperPath;
        private string _modelPath;
        private readonly string _tempDir;
        private readonly string _installDir;
        private bool _isAvailable;

        /// <summary>
        /// Current whisper.cpp release version for download.
        /// </summary>
        private const string WHISPER_VERSION = "1.7.2";
        
        /// <summary>
        /// Download URL template for whisper.cpp releases.
        /// </summary>
        private const string WHISPER_DOWNLOAD_URL = "https://github.com/ggerganov/whisper.cpp/releases/download/v{0}/whisper-bin-x64.zip";

        private static readonly List<string> _supportedLanguages = new()
        {
            "auto", "en", "zh", "de", "es", "ru", "ko", "fr", "ja", "pt", "tr", "pl",
            "ca", "nl", "ar", "sv", "it", "id", "hi", "fi", "vi", "he", "uk", "el",
            "ms", "cs", "ro", "da", "hu", "ta", "no", "th", "ur", "hr", "bg", "lt",
            "la", "mi", "ml", "cy", "sk", "te", "fa", "lv", "bn", "sr", "az", "sl",
            "kn", "et", "mk", "br", "eu", "is", "hy", "ne", "mn", "bs", "kk", "sq",
            "sw", "gl", "mr", "pa", "si", "km", "sn", "yo", "so", "af", "oc", "ka",
            "be", "tg", "sd", "gu", "am", "yi", "lo", "uz", "fo", "ht", "ps", "tk",
            "nn", "mt", "sa", "lb", "my", "bo", "tl", "mg", "as", "tt", "haw", "ln",
            "ha", "ba", "jw", "su"
        };

        public IReadOnlyList<string> SupportedLanguages => _supportedLanguages;
        public bool IsAvailable => _isAvailable;
        
        /// <summary>
        /// Returns installation status message for display.
        /// </summary>
        public string InstallationStatus
        {
            get
            {
                if (_isAvailable) return "Whisper is ready";
                if (string.IsNullOrEmpty(_whisperPath)) return "Whisper executable not found. Click 'Install Whisper' to download.";
                if (string.IsNullOrEmpty(_modelPath)) return "Whisper model not found. Click 'Download Model' to get a model.";
                return "Whisper is not properly configured.";
            }
        }

        public LocalWhisperService(string? whisperExecutable = null, string? modelPath = null)
        {
            _installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlatypusTools", "whisper");
            _tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTools", "Whisper");
            
            Directory.CreateDirectory(_tempDir);
            Directory.CreateDirectory(_installDir);

            // Try to find whisper executable
            _whisperPath = whisperExecutable ?? FindWhisperExecutable();
            _modelPath = modelPath ?? FindWhisperModel();

            RefreshAvailability();
        }

        /// <summary>
        /// Refreshes the availability status after installation.
        /// </summary>
        public void RefreshAvailability()
        {
            _whisperPath = FindWhisperExecutable();
            _modelPath = FindWhisperModel();
            
            _isAvailable = !string.IsNullOrEmpty(_whisperPath) && 
                           File.Exists(_whisperPath) &&
                           !string.IsNullOrEmpty(_modelPath) &&
                           File.Exists(_modelPath);
        }

        /// <summary>
        /// Downloads and installs whisper.cpp executable.
        /// </summary>
        public async Task<bool> InstallWhisperAsync(
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var zipPath = Path.Combine(_tempDir, "whisper.zip");
            var extractPath = _installDir;

            try
            {
                // Download whisper.cpp release
                var url = string.Format(WHISPER_DOWNLOAD_URL, WHISPER_VERSION);
                
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(10);
                
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    // Try alternative: use Windows x64 build
                    url = $"https://github.com/ggerganov/whisper.cpp/releases/download/v{WHISPER_VERSION}/whisper-blas-bin-x64.zip";
                    response.Dispose();
                    using var altResponse = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                    
                    if (!altResponse.IsSuccessStatusCode)
                    {
                        return false;
                    }
                    
                    await DownloadFileAsync(altResponse, zipPath, progress, cancellationToken);
                }
                else
                {
                    await DownloadFileAsync(response, zipPath, progress, cancellationToken);
                }

                // Extract
                progress?.Report(0.9);
                
                if (Directory.Exists(extractPath))
                {
                    // Clean old installation
                    foreach (var file in Directory.GetFiles(extractPath, "*.exe"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
                
                ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
                
                // Cleanup
                File.Delete(zipPath);
                
                // Refresh paths
                RefreshAvailability();
                
                progress?.Report(1.0);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to install Whisper: {ex.Message}");
                return false;
            }
        }

        private static async Task DownloadFileAsync(
            HttpResponseMessage response,
            string destinationPath,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            var downloadedBytes = 0L;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    progress?.Report(0.8 * downloadedBytes / totalBytes);
                }
            }
        }

        public async Task<List<Caption>> TranscribeAsync(
            string audioPath,
            string language = "auto",
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!_isAvailable)
            {
                throw new InvalidOperationException("Whisper is not available. Please install whisper.cpp or configure the path.");
            }

            if (!File.Exists(audioPath))
            {
                throw new FileNotFoundException("Audio file not found.", audioPath);
            }

            var captions = new List<Caption>();
            var outputPath = Path.Combine(_tempDir, $"transcript_{Guid.NewGuid():N}.json");

            try
            {
                var args = BuildWhisperArgs(audioPath, outputPath, language);
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = _whisperPath,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new List<string>();

                process.OutputDataReceived += (_, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.Add(e.Data);
                        // Parse progress from whisper output
                        var progressMatch = Regex.Match(e.Data, @"\[(\d+)%\]");
                        if (progressMatch.Success && int.TryParse(progressMatch.Groups[1].Value, out var pct))
                        {
                            progress?.Report(pct / 100.0);
                        }
                    }
                };

                process.Start();
                process.BeginOutputReadLine();

                var errorTask = process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync(cancellationToken);
                var errors = await errorTask;

                if (process.ExitCode != 0 && !string.IsNullOrEmpty(errors))
                {
                    throw new InvalidOperationException($"Whisper failed: {errors}");
                }

                // Parse output
                if (File.Exists(outputPath))
                {
                    captions = await ParseWhisperOutputAsync(outputPath, cancellationToken);
                }
                else
                {
                    // Try parsing from stdout (SRT format)
                    captions = ParseSrtOutput(string.Join("\n", outputBuilder));
                }

                progress?.Report(1.0);
            }
            finally
            {
                // Cleanup
                if (File.Exists(outputPath))
                {
                    try { File.Delete(outputPath); } catch { }
                }
            }

            return captions;
        }

        private string BuildWhisperArgs(string inputPath, string outputPath, string language)
        {
            var args = new List<string>
            {
                "-f", $"\"{inputPath}\"",
                "-m", $"\"{_modelPath}\"",
                "-oj", // JSON output
                "-of", $"\"{Path.GetFileNameWithoutExtension(outputPath)}\"",
                "-pp", // Print progress
            };

            if (language != "auto")
            {
                args.Add("-l");
                args.Add(language);
            }

            // Use more threads for faster processing
            args.Add("-t");
            args.Add(Math.Max(4, Environment.ProcessorCount - 2).ToString());

            return string.Join(" ", args);
        }

        private async Task<List<Caption>> ParseWhisperOutputAsync(string jsonPath, CancellationToken ct)
        {
            var captions = new List<Caption>();
            var json = await File.ReadAllTextAsync(jsonPath, ct);
            
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("transcription", out var segments))
            {
                int id = 1;
                foreach (var segment in segments.EnumerateArray())
                {
                    var text = segment.GetProperty("text").GetString() ?? "";
                    var start = segment.GetProperty("offsets").GetProperty("from").GetInt64();
                    var end = segment.GetProperty("offsets").GetProperty("to").GetInt64();

                    var caption = new Caption
                    {
                        Id = (id++).ToString(),
                        Text = text.Trim(),
                        StartTime = TimeSpan.FromMilliseconds(start),
                        EndTime = TimeSpan.FromMilliseconds(end),
                        Confidence = 0.9 // Whisper doesn't always provide confidence
                    };

                    // Parse word-level timestamps if available
                    if (segment.TryGetProperty("tokens", out var tokens))
                    {
                        foreach (var token in tokens.EnumerateArray())
                        {
                            if (token.TryGetProperty("text", out var wordText) &&
                                token.TryGetProperty("offsets", out var offsets))
                            {
                                caption.Words.Add(new CaptionWord
                                {
                                    Text = wordText.GetString() ?? "",
                                    StartTime = TimeSpan.FromMilliseconds(offsets.GetProperty("from").GetInt64()),
                                    EndTime = TimeSpan.FromMilliseconds(offsets.GetProperty("to").GetInt64())
                                });
                            }
                        }
                    }

                    captions.Add(caption);
                }
            }

            return captions;
        }

        private List<Caption> ParseSrtOutput(string srtContent)
        {
            return SrtHelper.Parse(srtContent);
        }

        private string FindWhisperExecutable()
        {
            // Check common locations
            var paths = new[]
            {
                // PlatypusTools install directory (preferred)
                Path.Combine(_installDir, "main.exe"),
                Path.Combine(_installDir, "whisper.exe"),
                Path.Combine(_installDir, "whisper-cli.exe"),
                // User Roaming app data (user-specified location)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "whisper", "main.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "whisper", "whisper.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "whisper", "whisper-cli.exe"),
                // User local app data
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whisper", "main.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whisper", "whisper.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "whisper.cpp", "main.exe"),
                "whisper", // In PATH
                "main.exe" // Current directory
            };

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // Check PATH environment variable
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir, "whisper.exe");
                if (File.Exists(candidate)) return candidate;
                
                candidate = Path.Combine(dir, "main.exe");
                if (File.Exists(candidate)) return candidate;
                
                candidate = Path.Combine(dir, "whisper-cli.exe");
                if (File.Exists(candidate)) return candidate;
            }

            return string.Empty;
        }

        private string FindWhisperModel()
        {
            // Check PlatypusTools install directory first
            var modelDirs = new[]
            {
                Path.Combine(_installDir, "models"),
                _installDir, // Also check directly in install dir (no models subfolder)
                // User Roaming app data (user-specified location)
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "whisper", "models"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "whisper"), // Direct folder
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whisper", "models"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "whisper"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "whisper")
            };

            // Prefer medium or base model
            var modelPriority = new[] { "medium", "base", "small", "tiny", "large" };
            
            foreach (var modelDir in modelDirs)
            {
                if (!Directory.Exists(modelDir)) continue;
                
                foreach (var modelName in modelPriority)
                {
                    var files = Directory.GetFiles(modelDir, $"ggml-{modelName}*.bin");
                    if (files.Length > 0)
                    {
                        return files[0];
                    }
                }

                // Return any model found
                var anyModel = Directory.GetFiles(modelDir, "*.bin").FirstOrDefault();
                if (anyModel != null) return anyModel;
            }
            
            return string.Empty;
        }

        /// <summary>
        /// Downloads a Whisper model if not present.
        /// </summary>
        public async Task<bool> DownloadModelAsync(
            string modelName = "base",
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var modelDir = Path.Combine(_installDir, "models");
            Directory.CreateDirectory(modelDir);

            var modelPath = Path.Combine(modelDir, $"ggml-{modelName}.bin");
            if (File.Exists(modelPath))
            {
                RefreshAvailability();
                return true;
            }

            // Hugging Face URL for whisper.cpp models
            var url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{modelName}.bin";

            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(30); // Models can be large
                
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        progress?.Report((double)downloadedBytes / totalBytes);
                    }
                }

                RefreshAvailability();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to download model: {ex.Message}");
                // Cleanup partial download
                if (File.Exists(modelPath))
                {
                    try { File.Delete(modelPath); } catch { }
                }
                return false;
            }
        }

        /// <summary>
        /// Gets available model sizes with descriptions.
        /// </summary>
        public static IReadOnlyDictionary<string, string> AvailableModels => new Dictionary<string, string>
        {
            ["tiny"] = "Tiny (~75MB) - Fastest, lowest accuracy",
            ["base"] = "Base (~142MB) - Fast, good accuracy (recommended)",
            ["small"] = "Small (~466MB) - Balanced speed/accuracy",
            ["medium"] = "Medium (~1.5GB) - High accuracy, slower",
            ["large"] = "Large (~3GB) - Highest accuracy, slowest"
        };
    }
}
