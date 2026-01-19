using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Manages video codecs and external dependencies.
    /// Provides automatic download and configuration of codecs for video playback/editing.
    /// 
    /// This project uses the following open-source components:
    /// - FFmpeg (https://ffmpeg.org) - LGPL/GPL licensed multimedia framework
    /// - whisper.cpp (https://github.com/ggml-org/whisper.cpp) - MIT licensed speech recognition
    /// 
    /// See THIRD_PARTY_LICENSES.md for full license information.
    /// </summary>
    public class CodecManager
    {
        private readonly string _codecsDir;
        private readonly string _ffmpegDir;
        private readonly string _whisperDir;
        private readonly HttpClient _httpClient;

        /// <summary>
        /// FFmpeg download URL (GPL build with all codecs).
        /// </summary>
        private const string FFMPEG_DOWNLOAD_URL = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
        
        /// <summary>
        /// Whisper.cpp releases URL.
        /// </summary>
        private const string WHISPER_RELEASES_URL = "https://github.com/ggml-org/whisper.cpp/releases";
        
        /// <summary>
        /// Whisper model download URL template.
        /// </summary>
        private const string WHISPER_MODEL_URL = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-{0}.bin";

        public CodecManager()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var baseDir = Path.Combine(appData, "PlatypusTools");
            
            _codecsDir = Path.Combine(baseDir, "codecs");
            _ffmpegDir = Path.Combine(baseDir, "ffmpeg");
            _whisperDir = Path.Combine(baseDir, "whisper");
            
            Directory.CreateDirectory(_codecsDir);
            Directory.CreateDirectory(_ffmpegDir);
            Directory.CreateDirectory(_whisperDir);
            
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(30);
        }

        /// <summary>
        /// Gets the path to the codecs directory.
        /// </summary>
        public string CodecsDirectory => _codecsDir;

        /// <summary>
        /// Gets the path to FFmpeg executables.
        /// </summary>
        public string FFmpegDirectory => _ffmpegDir;

        /// <summary>
        /// Gets the path to Whisper files.
        /// </summary>
        public string WhisperDirectory => _whisperDir;

        /// <summary>
        /// Checks if FFmpeg is available.
        /// </summary>
        public bool IsFFmpegAvailable()
        {
            var ffmpegPath = FindFFmpeg();
            return !string.IsNullOrEmpty(ffmpegPath) && File.Exists(ffmpegPath);
        }

        /// <summary>
        /// Finds FFmpeg executable.
        /// </summary>
        public string? FindFFmpeg()
        {
            // Check PlatypusTools directory first
            var paths = new[]
            {
                Path.Combine(_ffmpegDir, "bin", "ffmpeg.exe"),
                Path.Combine(_ffmpegDir, "ffmpeg.exe"),
                "ffmpeg.exe", // In PATH
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }

            // Check PATH
            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Finds FFplay executable for video playback.
        /// </summary>
        public string? FindFFplay()
        {
            var paths = new[]
            {
                Path.Combine(_ffmpegDir, "bin", "ffplay.exe"),
                Path.Combine(_ffmpegDir, "ffplay.exe"),
                "ffplay.exe",
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir, "ffplay.exe");
                if (File.Exists(candidate)) return candidate;
            }

            return null;
        }

        /// <summary>
        /// Checks if Whisper is available with a model.
        /// </summary>
        public bool IsWhisperAvailable()
        {
            var whisperPath = FindWhisper();
            var modelPath = FindWhisperModel();
            return !string.IsNullOrEmpty(whisperPath) && !string.IsNullOrEmpty(modelPath);
        }

        /// <summary>
        /// Finds Whisper executable.
        /// </summary>
        public string? FindWhisper()
        {
            var roamingDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "whisper");
                
            var paths = new[]
            {
                Path.Combine(_whisperDir, "main.exe"),
                Path.Combine(_whisperDir, "whisper-cli.exe"),
                Path.Combine(_whisperDir, "whisper.exe"),
                Path.Combine(roamingDir, "main.exe"),
                Path.Combine(roamingDir, "whisper-cli.exe"),
                Path.Combine(roamingDir, "whisper.exe"),
            };

            foreach (var path in paths)
            {
                if (File.Exists(path)) return path;
            }

            return null;
        }

        /// <summary>
        /// Finds a Whisper model file.
        /// </summary>
        public string? FindWhisperModel()
        {
            var roamingDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "whisper");
                
            var modelDirs = new[]
            {
                Path.Combine(_whisperDir, "models"),
                _whisperDir,
                Path.Combine(roamingDir, "models"),
                roamingDir,
            };

            var modelPriority = new[] { "base", "small", "medium", "tiny", "large" };

            foreach (var modelDir in modelDirs)
            {
                if (!Directory.Exists(modelDir)) continue;

                foreach (var modelName in modelPriority)
                {
                    var files = Directory.GetFiles(modelDir, $"ggml-{modelName}*.bin");
                    if (files.Length > 0) return files[0];
                }

                // Return any .bin file
                var anyModel = Directory.GetFiles(modelDir, "*.bin");
                if (anyModel.Length > 0) return anyModel[0];
            }

            return null;
        }

        /// <summary>
        /// Downloads a Whisper model.
        /// </summary>
        public async Task<bool> DownloadWhisperModelAsync(
            string modelName = "base",
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            var modelDir = Path.Combine(_whisperDir, "models");
            Directory.CreateDirectory(modelDir);

            var modelPath = Path.Combine(modelDir, $"ggml-{modelName}.bin");
            if (File.Exists(modelPath)) return true;

            var url = string.Format(WHISPER_MODEL_URL, modelName);

            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var downloadedBytes = 0L;

                using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var fileStream = new FileStream(modelPath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await stream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    downloadedBytes += bytesRead;

                    if (totalBytes > 0)
                    {
                        progress?.Report((double)downloadedBytes / totalBytes);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[CodecManager] Failed to download whisper model: {ex.Message}");
                if (File.Exists(modelPath)) File.Delete(modelPath);
                return false;
            }
        }

        /// <summary>
        /// Gets supported video formats based on available codecs.
        /// </summary>
        public IReadOnlyList<string> GetSupportedVideoFormats()
        {
            // FFmpeg supports virtually all formats
            return new[]
            {
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".flv",
                ".m4v", ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".vob",
                ".3gp", ".3g2", ".ogv", ".divx", ".xvid", ".asf", ".rm",
                ".rmvb", ".f4v", ".swf"
            };
        }

        /// <summary>
        /// Gets supported audio formats.
        /// </summary>
        public IReadOnlyList<string> GetSupportedAudioFormats()
        {
            return new[]
            {
                ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".wma",
                ".opus", ".aiff", ".ape", ".ac3", ".dts", ".mka"
            };
        }

        /// <summary>
        /// Gets supported image formats.
        /// </summary>
        public IReadOnlyList<string> GetSupportedImageFormats()
        {
            return new[]
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff",
                ".tif", ".ico", ".svg", ".psd", ".raw", ".cr2", ".nef"
            };
        }

        /// <summary>
        /// Gets codec information for display.
        /// </summary>
        public async Task<string> GetCodecInfoAsync()
        {
            var ffmpegPath = FindFFmpeg();
            if (string.IsNullOrEmpty(ffmpegPath))
            {
                return "FFmpeg not found. Install FFmpeg to enable all video formats.";
            }

            try
            {
                var psi = new ProcessStartInfo(ffmpegPath, "-version")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return "Unable to query FFmpeg version.";

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Extract first line (version info)
                var lines = output.Split('\n');
                return lines.Length > 0 ? lines[0].Trim() : "FFmpeg version unknown";
            }
            catch
            {
                return "Error querying FFmpeg version.";
            }
        }
    }
}
