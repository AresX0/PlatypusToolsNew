using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for generating video thumbnail strips for timeline clips.
    /// Creates frame previews at regular intervals for visual navigation.
    /// </summary>
    public class TimelineThumbnailService
    {
        private static readonly Lazy<TimelineThumbnailService> _instance = new(() => new TimelineThumbnailService());
        public static TimelineThumbnailService Instance => _instance.Value;

        private readonly Dictionary<string, ThumbnailStrip> _cache = new();
        private readonly object _cacheLock = new();
        private readonly int _maxCacheSize = 50;

        public event EventHandler<ThumbnailGenerationProgress>? ProgressChanged;

        #region Models

        public class ThumbnailStrip
        {
            public string VideoPath { get; set; } = string.Empty;
            public List<ThumbnailFrame> Frames { get; set; } = new();
            public TimeSpan Duration { get; set; }
            public int ThumbnailWidth { get; set; }
            public int ThumbnailHeight { get; set; }
            public DateTime GeneratedAt { get; set; }
            public string CacheKey { get; set; } = string.Empty;
        }

        public class ThumbnailFrame
        {
            public int Index { get; set; }
            public TimeSpan Position { get; set; }
            public BitmapSource? Image { get; set; }
            public byte[]? ImageData { get; set; }
        }

        public class ThumbnailGenerationOptions
        {
            /// <summary>
            /// Number of thumbnails to generate across the video.
            /// </summary>
            public int ThumbnailCount { get; set; } = 20;

            /// <summary>
            /// Width of each thumbnail in pixels.
            /// </summary>
            public int ThumbnailWidth { get; set; } = 120;

            /// <summary>
            /// Height of each thumbnail in pixels.
            /// </summary>
            public int ThumbnailHeight { get; set; } = 68;

            /// <summary>
            /// Whether to use cached thumbnails if available.
            /// </summary>
            public bool UseCache { get; set; } = true;

            /// <summary>
            /// Cache thumbnails to disk for faster reload.
            /// </summary>
            public bool PersistCache { get; set; } = true;

            /// <summary>
            /// Quality for JPEG compression (1-100).
            /// </summary>
            public int JpegQuality { get; set; } = 75;
        }

        public class ThumbnailGenerationProgress
        {
            public string VideoPath { get; set; } = string.Empty;
            public int CurrentFrame { get; set; }
            public int TotalFrames { get; set; }
            public TimeSpan Position { get; set; }
            public double PercentComplete => TotalFrames > 0 ? (double)CurrentFrame / TotalFrames * 100 : 0;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Generates a thumbnail strip for a video file.
        /// </summary>
        public async Task<ThumbnailStrip> GenerateThumbnailStripAsync(
            string videoPath, 
            ThumbnailGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new ThumbnailGenerationOptions();

            // Check cache
            var cacheKey = GenerateCacheKey(videoPath, options);
            if (options.UseCache)
            {
                var cached = GetFromCache(cacheKey);
                if (cached != null)
                    return cached;

                // Try disk cache
                if (options.PersistCache)
                {
                    var diskCached = await LoadFromDiskCacheAsync(cacheKey, cancellationToken);
                    if (diskCached != null)
                    {
                        AddToCache(cacheKey, diskCached);
                        return diskCached;
                    }
                }
            }

            var strip = new ThumbnailStrip
            {
                VideoPath = videoPath,
                ThumbnailWidth = options.ThumbnailWidth,
                ThumbnailHeight = options.ThumbnailHeight,
                GeneratedAt = DateTime.UtcNow,
                CacheKey = cacheKey
            };

            // Get video duration using FFprobe or MediaInfo
            var duration = await GetVideoDurationAsync(videoPath, cancellationToken);
            strip.Duration = duration;

            if (duration <= TimeSpan.Zero)
                return strip;

            var interval = duration / options.ThumbnailCount;

            var progress = new ThumbnailGenerationProgress
            {
                VideoPath = videoPath,
                TotalFrames = options.ThumbnailCount
            };

            for (int i = 0; i < options.ThumbnailCount; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var position = TimeSpan.FromTicks(interval.Ticks * i);

                progress.CurrentFrame = i + 1;
                progress.Position = position;
                ProgressChanged?.Invoke(this, progress);

                var frame = await ExtractFrameAsync(videoPath, position, options, cancellationToken);
                if (frame != null)
                {
                    frame.Index = i;
                    strip.Frames.Add(frame);
                }
            }

            // Cache results
            if (options.UseCache)
            {
                AddToCache(cacheKey, strip);

                if (options.PersistCache)
                {
                    await SaveToDiskCacheAsync(strip, cancellationToken);
                }
            }

            return strip;
        }

        /// <summary>
        /// Gets a single thumbnail at a specific position.
        /// </summary>
        public async Task<BitmapSource?> GetThumbnailAtPositionAsync(
            string videoPath,
            TimeSpan position,
            int width = 120,
            int height = 68,
            CancellationToken cancellationToken = default)
        {
            var options = new ThumbnailGenerationOptions
            {
                ThumbnailWidth = width,
                ThumbnailHeight = height
            };

            var frame = await ExtractFrameAsync(videoPath, position, options, cancellationToken);
            return frame?.Image;
        }

        /// <summary>
        /// Generates thumbnails for a timeline strip UI element.
        /// </summary>
        public async Task<List<ImageSource>> GenerateTimelineStripAsync(
            string videoPath,
            double stripWidth,
            int thumbnailHeight,
            CancellationToken cancellationToken = default)
        {
            // Calculate how many thumbnails fit in the strip
            var aspectRatio = 16.0 / 9.0; // Assume 16:9
            var thumbnailWidth = (int)(thumbnailHeight * aspectRatio);
            var count = Math.Max(1, (int)(stripWidth / thumbnailWidth));

            var options = new ThumbnailGenerationOptions
            {
                ThumbnailCount = count,
                ThumbnailWidth = thumbnailWidth,
                ThumbnailHeight = thumbnailHeight
            };

            var strip = await GenerateThumbnailStripAsync(videoPath, options, cancellationToken);
            return strip.Frames.Where(f => f.Image != null).Select(f => (ImageSource)f.Image!).ToList();
        }

        /// <summary>
        /// Clears the thumbnail cache.
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
            }
        }

        /// <summary>
        /// Clears disk cache for thumbnails.
        /// </summary>
        public void ClearDiskCache()
        {
            try
            {
                var cacheDir = GetCacheDirectory();
                if (Directory.Exists(cacheDir))
                {
                    Directory.Delete(cacheDir, true);
                }
            }
            catch { }
        }

        #endregion

        #region Private Methods

        private async Task<TimeSpan> GetVideoDurationAsync(string videoPath, CancellationToken cancellationToken)
        {
            try
            {
                // Try using FFprobe
                var ffprobePath = FindExecutable("ffprobe");
                if (!string.IsNullOrEmpty(ffprobePath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffprobePath,
                        Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{videoPath}\"",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(psi);
                    if (process != null)
                    {
                        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                        await process.WaitForExitAsync(cancellationToken);

                        if (double.TryParse(output.Trim(), out var seconds))
                        {
                            return TimeSpan.FromSeconds(seconds);
                        }
                    }
                }

                // Fallback: Estimate from file size (very rough)
                var fileInfo = new FileInfo(videoPath);
                var estimatedBitrate = 5_000_000; // 5 Mbps estimate
                return TimeSpan.FromSeconds(fileInfo.Length * 8.0 / estimatedBitrate);
            }
            catch
            {
                return TimeSpan.Zero;
            }
        }

        private async Task<ThumbnailFrame?> ExtractFrameAsync(
            string videoPath,
            TimeSpan position,
            ThumbnailGenerationOptions options,
            CancellationToken cancellationToken)
        {
            try
            {
                var ffmpegPath = FindExecutable("ffmpeg");
                if (string.IsNullOrEmpty(ffmpegPath))
                    return null;

                var tempFile = Path.Combine(Path.GetTempPath(), $"thumb_{Guid.NewGuid()}.jpg");

                try
                {
                    var args = $"-ss {position.TotalSeconds:F3} -i \"{videoPath}\" -vframes 1 -s {options.ThumbnailWidth}x{options.ThumbnailHeight} -q:v 5 -y \"{tempFile}\"";

                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var process = System.Diagnostics.Process.Start(psi);
                    if (process == null)
                        return null;

                    await process.WaitForExitAsync(cancellationToken);

                    if (File.Exists(tempFile))
                    {
                        var imageData = await File.ReadAllBytesAsync(tempFile, cancellationToken);

                        // Create BitmapImage on UI thread
                        BitmapSource? image = null;
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var bitmap = new BitmapImage();
                            using var ms = new MemoryStream(imageData);
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            image = bitmap;
                        });

                        return new ThumbnailFrame
                        {
                            Position = position,
                            Image = image,
                            ImageData = imageData
                        };
                    }
                }
                finally
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Frame extraction error: {ex.Message}");
            }

            return null;
        }

        private string? FindExecutable(string name)
        {
            // Check common locations
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{name}.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", $"{name}.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", $"{name}.exe"),
                $"{name}.exe" // PATH lookup
            };

            foreach (var path in paths)
            {
                if (File.Exists(path) || path == $"{name}.exe")
                {
                    // Test if it works
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "-version",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = System.Diagnostics.Process.Start(psi);
                        if (process != null)
                        {
                            process.WaitForExit(1000);
                            if (process.ExitCode == 0)
                                return path;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private string GenerateCacheKey(string videoPath, ThumbnailGenerationOptions options)
        {
            var fileInfo = new FileInfo(videoPath);
            return $"{fileInfo.FullName}_{fileInfo.Length}_{fileInfo.LastWriteTimeUtc.Ticks}_{options.ThumbnailCount}_{options.ThumbnailWidth}x{options.ThumbnailHeight}";
        }

        private ThumbnailStrip? GetFromCache(string key)
        {
            lock (_cacheLock)
            {
                return _cache.TryGetValue(key, out var strip) ? strip : null;
            }
        }

        private void AddToCache(string key, ThumbnailStrip strip)
        {
            lock (_cacheLock)
            {
                // Evict old entries if cache is full
                while (_cache.Count >= _maxCacheSize)
                {
                    var oldest = _cache.OrderBy(kv => kv.Value.GeneratedAt).FirstOrDefault();
                    if (!string.IsNullOrEmpty(oldest.Key))
                        _cache.Remove(oldest.Key);
                }

                _cache[key] = strip;
            }
        }

        private string GetCacheDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlatypusTools", "ThumbnailCache");
        }

        private async Task SaveToDiskCacheAsync(ThumbnailStrip strip, CancellationToken cancellationToken)
        {
            try
            {
                var cacheDir = GetCacheDirectory();
                Directory.CreateDirectory(cacheDir);

                var hash = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(strip.CacheKey)))
                    .Replace("/", "_").Replace("+", "-").Substring(0, 32);

                var cachePath = Path.Combine(cacheDir, hash);
                Directory.CreateDirectory(cachePath);

                // Save metadata
                var metadata = new
                {
                    strip.VideoPath,
                    DurationTicks = strip.Duration.Ticks,
                    strip.ThumbnailWidth,
                    strip.ThumbnailHeight,
                    strip.CacheKey,
                    FrameCount = strip.Frames.Count
                };

                await File.WriteAllTextAsync(
                    Path.Combine(cachePath, "metadata.json"),
                    System.Text.Json.JsonSerializer.Serialize(metadata),
                    cancellationToken);

                // Save frames
                for (int i = 0; i < strip.Frames.Count; i++)
                {
                    var frame = strip.Frames[i];
                    if (frame.ImageData != null)
                    {
                        await File.WriteAllBytesAsync(
                            Path.Combine(cachePath, $"{i:D4}.jpg"),
                            frame.ImageData,
                            cancellationToken);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Cache save error: {ex.Message}");
            }
        }

        private async Task<ThumbnailStrip?> LoadFromDiskCacheAsync(string cacheKey, CancellationToken cancellationToken)
        {
            try
            {
                var cacheDir = GetCacheDirectory();
                var hash = Convert.ToBase64String(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(cacheKey)))
                    .Replace("/", "_").Replace("+", "-").Substring(0, 32);

                var cachePath = Path.Combine(cacheDir, hash);
                var metadataPath = Path.Combine(cachePath, "metadata.json");

                if (!File.Exists(metadataPath))
                    return null;

                var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                var strip = new ThumbnailStrip
                {
                    VideoPath = doc.RootElement.GetProperty("VideoPath").GetString() ?? "",
                    Duration = TimeSpan.FromTicks(doc.RootElement.GetProperty("DurationTicks").GetInt64()),
                    ThumbnailWidth = doc.RootElement.GetProperty("ThumbnailWidth").GetInt32(),
                    ThumbnailHeight = doc.RootElement.GetProperty("ThumbnailHeight").GetInt32(),
                    CacheKey = cacheKey
                };

                var frameCount = doc.RootElement.GetProperty("FrameCount").GetInt32();

                for (int i = 0; i < frameCount; i++)
                {
                    var framePath = Path.Combine(cachePath, $"{i:D4}.jpg");
                    if (File.Exists(framePath))
                    {
                        var imageData = await File.ReadAllBytesAsync(framePath, cancellationToken);

                        BitmapSource? image = null;
                        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            var bitmap = new BitmapImage();
                            using var ms = new MemoryStream(imageData);
                            bitmap.BeginInit();
                            bitmap.CacheOption = BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = ms;
                            bitmap.EndInit();
                            bitmap.Freeze();
                            image = bitmap;
                        });

                        strip.Frames.Add(new ThumbnailFrame
                        {
                            Index = i,
                            Image = image,
                            ImageData = imageData
                        });
                    }
                }

                return strip;
            }
            catch
            {
                return null;
            }
        }

        #endregion
    }
}
