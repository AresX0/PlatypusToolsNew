using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Service for managing proxy files for smooth editing of high-resolution footage.
    /// </summary>
    public class ProxyService : IDisposable
    {
        private readonly FFmpegService _ffmpeg;
        private readonly FFprobeService _ffprobe;
        private readonly string _proxyDirectory;
        private readonly ConcurrentDictionary<string, ProxyFile> _proxyCache = new();
        private readonly SemaphoreSlim _generationSemaphore = new(2); // Max 2 concurrent generations
        private bool _disposed;

        public ProxyService(FFmpegService ffmpeg, FFprobeService ffprobe, string? proxyDirectory = null)
        {
            _ffmpeg = ffmpeg ?? throw new ArgumentNullException(nameof(ffmpeg));
            _ffprobe = ffprobe ?? throw new ArgumentNullException(nameof(ffprobe));
            _proxyDirectory = proxyDirectory ?? Path.Combine(Path.GetTempPath(), "PlatypusTools", "Proxies");
            Directory.CreateDirectory(_proxyDirectory);
            
            // Load existing proxy cache
            LoadProxyCache();
        }

        /// <summary>
        /// Gets the proxy file path for a media file, or the original if no proxy exists.
        /// </summary>
        public string GetProxyPath(string originalPath, ProxySettings settings)
        {
            if (!settings.IsEnabled)
                return originalPath;

            var hash = ComputeFileHash(originalPath);
            if (_proxyCache.TryGetValue(hash, out var proxy) && proxy.IsValid && File.Exists(proxy.ProxyPath))
            {
                return proxy.ProxyPath;
            }

            return originalPath;
        }

        /// <summary>
        /// Checks if a proxy exists for the given file.
        /// </summary>
        public bool HasProxy(string originalPath)
        {
            var hash = ComputeFileHash(originalPath);
            return _proxyCache.TryGetValue(hash, out var proxy) && 
                   proxy.IsValid && 
                   File.Exists(proxy.ProxyPath);
        }

        /// <summary>
        /// Checks if a file should have a proxy generated.
        /// </summary>
        public async Task<bool> ShouldGenerateProxyAsync(string path, ProxySettings settings, CancellationToken ct = default)
        {
            if (!settings.IsEnabled || !settings.AutoGenerateProxies)
                return false;

            try
            {
                var info = await _ffprobe.ProbeAsync(path, ct);
                return info.Width >= settings.MinResolutionForProxy || 
                       info.Height >= settings.MinResolutionForProxy;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Generates a proxy file for the given media.
        /// </summary>
        public async Task<ProxyFile> GenerateProxyAsync(
            string originalPath,
            ProxySettings settings,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            await _generationSemaphore.WaitAsync(ct);
            try
            {
                var hash = ComputeFileHash(originalPath);
                var proxyFileName = $"proxy_{hash}_{settings.Resolution}.mp4";
                var proxyPath = Path.Combine(_proxyDirectory, proxyFileName);

                // Check if already exists
                if (_proxyCache.TryGetValue(hash, out var existing) && 
                    existing.Resolution == settings.Resolution &&
                    File.Exists(existing.ProxyPath))
                {
                    progress?.Report(1.0);
                    return existing;
                }

                // Get original file info
                var info = await _ffprobe.ProbeAsync(originalPath, ct);
                var (width, height) = GetProxyDimensions(info.Width, info.Height, settings.Resolution);
                var crf = GetCrfForQuality(settings.Quality);

                progress?.Report(0.1);

                // Build FFmpeg command
                // Use fast encoding preset for proxies
                var args = $"-i \"{originalPath}\" " +
                          $"-vf \"scale={width}:{height}\" " +
                          $"-c:v libx264 -preset ultrafast -crf {crf} " +
                          $"-c:a aac -b:a 128k " +
                          $"-y \"{proxyPath}\"";

                // Get duration for progress tracking
                var duration = info.Duration;
                
                await _ffmpeg.ExecuteWithProgressAsync(
                    args,
                    duration,
                    new Progress<double>(p => progress?.Report(0.1 + p * 0.85)),
                    ct);

                progress?.Report(0.95);

                // Create proxy file entry
                var proxy = new ProxyFile
                {
                    OriginalPath = originalPath,
                    ProxyPath = proxyPath,
                    Resolution = settings.Resolution,
                    CreatedAt = DateTime.Now,
                    OriginalFileSize = new FileInfo(originalPath).Length,
                    ProxyFileSize = File.Exists(proxyPath) ? new FileInfo(proxyPath).Length : 0,
                    IsValid = File.Exists(proxyPath)
                };

                _proxyCache[hash] = proxy;
                SaveProxyCache();

                progress?.Report(1.0);
                return proxy;
            }
            finally
            {
                _generationSemaphore.Release();
            }
        }

        /// <summary>
        /// Generates proxies for all media that needs them.
        /// </summary>
        public async Task GenerateAllProxiesAsync(
            IEnumerable<MediaAsset> assets,
            ProxySettings settings,
            IProgress<(string FileName, double OverallProgress)>? progress = null,
            CancellationToken ct = default)
        {
            var needsProxy = new List<MediaAsset>();
            
            foreach (var asset in assets)
            {
                if (asset.Type == MediaType.Video && 
                    !HasProxy(asset.FilePath) &&
                    await ShouldGenerateProxyAsync(asset.FilePath, settings, ct))
                {
                    needsProxy.Add(asset);
                }
            }

            if (needsProxy.Count == 0)
            {
                progress?.Report(("Complete", 1.0));
                return;
            }

            var completed = 0;
            var tasks = new List<Task>();

            foreach (var asset in needsProxy)
            {
                var localAsset = asset;
                var fileProgress = new Progress<double>(p =>
                {
                    var overall = (completed + p) / needsProxy.Count;
                    progress?.Report((localAsset.FileName, overall));
                });

                await GenerateProxyAsync(localAsset.FilePath, settings, fileProgress, ct);
                completed++;
            }

            progress?.Report(("Complete", 1.0));
        }

        /// <summary>
        /// Deletes all proxy files.
        /// </summary>
        public void ClearAllProxies()
        {
            foreach (var proxy in _proxyCache.Values)
            {
                try
                {
                    if (File.Exists(proxy.ProxyPath))
                        File.Delete(proxy.ProxyPath);
                }
                catch { }
            }
            
            _proxyCache.Clear();
            SaveProxyCache();
        }

        /// <summary>
        /// Deletes the proxy for a specific file.
        /// </summary>
        public void DeleteProxy(string originalPath)
        {
            var hash = ComputeFileHash(originalPath);
            if (_proxyCache.TryRemove(hash, out var proxy))
            {
                try
                {
                    if (File.Exists(proxy.ProxyPath))
                        File.Delete(proxy.ProxyPath);
                }
                catch { }
                SaveProxyCache();
            }
        }

        /// <summary>
        /// Gets the total size of all proxy files.
        /// </summary>
        public long GetTotalProxySize()
        {
            return _proxyCache.Values
                .Where(p => File.Exists(p.ProxyPath))
                .Sum(p => p.ProxyFileSize);
        }

        /// <summary>
        /// Gets all proxy files.
        /// </summary>
        public IReadOnlyList<ProxyFile> GetAllProxies()
        {
            return _proxyCache.Values.ToList();
        }

        private (int Width, int Height) GetProxyDimensions(int originalWidth, int originalHeight, ProxyResolution resolution)
        {
            var (targetWidth, targetHeight) = resolution switch
            {
                ProxyResolution.SD480 => (854, 480),
                ProxyResolution.HD720 => (1280, 720),
                ProxyResolution.HD1080 => (1920, 1080),
                ProxyResolution.Quarter => (originalWidth / 4, originalHeight / 4),
                _ => (1280, 720)
            };

            // Maintain aspect ratio
            var aspectRatio = (double)originalWidth / originalHeight;
            
            if ((double)targetWidth / targetHeight > aspectRatio)
            {
                // Original is taller, fit to height
                targetWidth = (int)(targetHeight * aspectRatio);
            }
            else
            {
                // Original is wider, fit to width
                targetHeight = (int)(targetWidth / aspectRatio);
            }

            // Ensure dimensions are even (required for most codecs)
            targetWidth = (targetWidth / 2) * 2;
            targetHeight = (targetHeight / 2) * 2;

            return (targetWidth, targetHeight);
        }

        private int GetCrfForQuality(ProxyQuality quality)
        {
            return quality switch
            {
                ProxyQuality.Low => 32,
                ProxyQuality.Medium => 26,
                ProxyQuality.High => 20,
                _ => 26
            };
        }

        private string ComputeFileHash(string path)
        {
            // Use file path and last modified time for quick hash
            var info = new FileInfo(path);
            var input = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
            
            using var md5 = MD5.Create();
            var hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(hashBytes);
        }

        private void LoadProxyCache()
        {
            var cachePath = Path.Combine(_proxyDirectory, "proxy_cache.json");
            if (File.Exists(cachePath))
            {
                try
                {
                    var json = File.ReadAllText(cachePath);
                    var proxies = System.Text.Json.JsonSerializer.Deserialize<List<ProxyFile>>(json);
                    if (proxies != null)
                    {
                        foreach (var proxy in proxies)
                        {
                            var hash = ComputeFileHash(proxy.OriginalPath);
                            proxy.IsValid = File.Exists(proxy.ProxyPath);
                            _proxyCache[hash] = proxy;
                        }
                    }
                }
                catch { }
            }
        }

        private void SaveProxyCache()
        {
            try
            {
                var cachePath = Path.Combine(_proxyDirectory, "proxy_cache.json");
                var json = System.Text.Json.JsonSerializer.Serialize(
                    _proxyCache.Values.ToList(),
                    new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(cachePath, json);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _generationSemaphore.Dispose();
        }
    }
}
