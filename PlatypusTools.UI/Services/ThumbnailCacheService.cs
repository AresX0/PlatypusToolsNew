using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlatypusTools.UI.Utilities;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Caches and manages thumbnails for files with memory-efficient loading.
    /// </summary>
    public class ThumbnailCacheService
    {
        private static ThumbnailCacheService? _instance;
        public static ThumbnailCacheService Instance => _instance ??= new ThumbnailCacheService();

        private readonly ConcurrentDictionary<string, WeakReference<BitmapSource>> _memoryCache = new();
        private readonly string _diskCachePath;
        private static readonly int MaxCpuThreads = Math.Max(1, (int)(Environment.ProcessorCount * 0.75));
        private readonly SemaphoreSlim _loadSemaphore = new(MaxCpuThreads);
        
        public int DefaultThumbnailSize { get; set; } = 150;
        public int MaxMemoryCacheSize { get; set; } = 1000;

        public ThumbnailCacheService()
        {
            _diskCachePath = Path.Combine(
                SettingsManager.DataDirectory, "Cache", "Thumbnails");
            Directory.CreateDirectory(_diskCachePath);
        }

        /// <summary>
        /// Gets a thumbnail for an image file.
        /// </summary>
        public async Task<BitmapSource?> GetThumbnailAsync(string filePath, int size = 0, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(filePath)) return null;
            if (size <= 0) size = DefaultThumbnailSize;

            var cacheKey = GetCacheKey(filePath, size);

            // Check memory cache
            if (_memoryCache.TryGetValue(cacheKey, out var weakRef) && weakRef.TryGetTarget(out var cached))
            {
                return cached;
            }

            // Check disk cache
            var diskCachePath = GetDiskCachePath(cacheKey);
            if (File.Exists(diskCachePath))
            {
                var diskCached = await LoadFromDiskCacheAsync(diskCachePath, cancellationToken);
                if (diskCached != null)
                {
                    AddToMemoryCache(cacheKey, diskCached);
                    return diskCached;
                }
            }

            // Generate thumbnail
            await _loadSemaphore.WaitAsync(cancellationToken);
            try
            {
                var thumbnail = await GenerateThumbnailAsync(filePath, size, cancellationToken);
                if (thumbnail != null)
                {
                    AddToMemoryCache(cacheKey, thumbnail);
                    await SaveToDiskCacheAsync(diskCachePath, thumbnail, cancellationToken);
                }
                return thumbnail;
            }
            finally
            {
                _loadSemaphore.Release();
            }
        }

        /// <summary>
        /// Preloads thumbnails for a list of files.
        /// </summary>
        public async Task PreloadThumbnailsAsync(IEnumerable<string> filePaths, int size = 0, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
        {
            var paths = filePaths.ToList();
            var completed = 0;

            await Parallel.ForEachAsync(paths, new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxCpuThreads,
                CancellationToken = cancellationToken
            }, async (path, ct) =>
            {
                await GetThumbnailAsync(path, size, ct);
                Interlocked.Increment(ref completed);
                progress?.Report(completed * 100 / paths.Count);
            });
        }

        /// <summary>
        /// Clears the memory cache.
        /// </summary>
        public void ClearMemoryCache()
        {
            _memoryCache.Clear();
        }

        /// <summary>
        /// Clears the disk cache.
        /// </summary>
        public void ClearDiskCache()
        {
            try
            {
                if (Directory.Exists(_diskCachePath))
                {
                    Directory.Delete(_diskCachePath, true);
                    Directory.CreateDirectory(_diskCachePath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error clearing disk cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the total size of the disk cache in bytes.
        /// </summary>
        public long GetDiskCacheSize()
        {
            try
            {
                var dir = new DirectoryInfo(_diskCachePath);
                return dir.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Invalidates the cache for a specific file.
        /// </summary>
        public void InvalidateCache(string filePath)
        {
            var keysToRemove = _memoryCache.Keys.Where(k => k.StartsWith(filePath)).ToList();
            foreach (var key in keysToRemove)
            {
                _memoryCache.TryRemove(key, out _);
            }
        }

        private async Task<BitmapSource?> GenerateThumbnailAsync(string filePath, int size, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    
                    // Handle images
                    if (IsImageFile(ext))
                    {
                        return ImageHelper.LoadFromFile(filePath, decodePixelWidth: size);
                    }

                    // Phase 5.5 — HDR-aware video thumbnails via ffmpeg.
                    if (IsVideoFile(ext))
                    {
                        return GenerateVideoThumbnail(filePath, size, cancellationToken);
                    }
                    
                    // For other file types, return null (use default icon)
                    return null;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error generating thumbnail: {ex.Message}");
                    return null;
                }
            }, cancellationToken);
        }

        private static BitmapSource? GenerateVideoThumbnail(string filePath, int size, CancellationToken ct)
        {
            var ffmpeg = PlatypusTools.Core.Services.FFmpegService.FindFfmpeg();
            if (string.IsNullOrEmpty(ffmpeg)) return null;

            // Detect HDR by inspecting the first video stream's color_transfer.
            bool isHdr = false;
            try
            {
                var probeArgs = $"-hide_banner -i \"{filePath}\"";
                var probePsi = new System.Diagnostics.ProcessStartInfo(ffmpeg, probeArgs)
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var probe = System.Diagnostics.Process.Start(probePsi)!;
                var stderr = probe.StandardError.ReadToEnd();
                probe.WaitForExit(5000);
                // smpte2084 (PQ) and arib-std-b67 (HLG) are the two HDR transfer functions.
                if (stderr.IndexOf("smpte2084", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    stderr.IndexOf("arib-std-b67", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    stderr.IndexOf("bt2020", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    isHdr = true;
                }
            }
            catch { /* probe failures fall back to non-HDR */ }

            ct.ThrowIfCancellationRequested();

            var tempJpg = Path.Combine(Path.GetTempPath(),
                $"platypus_thumb_{Guid.NewGuid():N}.jpg");
            try
            {
                // Build filter graph. For HDR sources we tone-map to BT.709 SDR.
                var vf = isHdr
                    ? $"zscale=t=linear:npl=100,format=gbrpf32le,zscale=p=bt709,tonemap=tonemap=hable:desat=0,zscale=t=bt709:m=bt709:r=tv,format=yuv420p,scale={size}:-2"
                    : $"scale={size}:-2";

                var args =
                    $"-hide_banner -loglevel error -ss 00:00:01 -i \"{filePath}\" -frames:v 1 " +
                    $"-vf \"{vf}\" -q:v 4 -y \"{tempJpg}\"";

                var psi = new System.Diagnostics.ProcessStartInfo(ffmpeg, args)
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                proc.WaitForExit(15000);

                if (!File.Exists(tempJpg)) return null;
                return ImageHelper.LoadFromFile(tempJpg, decodePixelWidth: size);
            }
            finally
            {
                try { if (File.Exists(tempJpg)) File.Delete(tempJpg); } catch { }
            }
        }

        private async Task<BitmapSource?> LoadFromDiskCacheAsync(string cachePath, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    return ImageHelper.LoadFromFile(cachePath);
                }
                catch
                {
                    return null;
                }
            }, cancellationToken);
        }

        private async Task SaveToDiskCacheAsync(string cachePath, BitmapSource thumbnail, CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                    encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                    
                    using var stream = File.Create(cachePath);
                    encoder.Save(stream);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error saving thumbnail to disk cache: {ex.Message}");
                }
            }, cancellationToken);
        }

        private void AddToMemoryCache(string key, BitmapSource thumbnail)
        {
            // Clean up if cache is too large
            if (_memoryCache.Count >= MaxMemoryCacheSize)
            {
                var keysToRemove = _memoryCache.Keys.Take(_memoryCache.Count / 4).ToList();
                foreach (var k in keysToRemove)
                {
                    _memoryCache.TryRemove(k, out _);
                }
            }

            _memoryCache[key] = new WeakReference<BitmapSource>(thumbnail);
        }

        private string GetCacheKey(string filePath, int size)
        {
            var lastWrite = File.GetLastWriteTimeUtc(filePath);
            return $"{filePath}|{size}|{lastWrite.Ticks}";
        }

        private string GetDiskCachePath(string cacheKey)
        {
            var hash = cacheKey.GetHashCode().ToString("X");
            return Path.Combine(_diskCachePath, $"{hash}.jpg");
        }

        private static bool IsImageFile(string extension)
        {
            return extension switch
            {
                ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" or ".webp" or ".ico" => true,
                _ => false
            };
        }

        private static bool IsVideoFile(string extension)
        {
            return extension switch
            {
                ".mp4" or ".mkv" or ".mov" or ".avi" or ".webm" or ".m4v" or ".wmv" or ".flv" or ".ts" or ".mts" or ".m2ts" => true,
                _ => false
            };
        }
    }
}
