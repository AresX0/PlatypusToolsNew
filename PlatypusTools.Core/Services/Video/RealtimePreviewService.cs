using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Provides real-time preview rendering for video editing.
    /// Uses FFmpeg to composite multiple tracks and apply effects on-the-fly.
    /// Similar to Shotcut's MLT-based preview but using FFmpeg.
    /// </summary>
    public class RealtimePreviewService : IDisposable
    {
        private readonly FFmpegService _ffmpeg;
        private readonly FFprobeService _ffprobe;
        private readonly string _previewCachePath;
        private readonly Dictionary<string, string> _frameCache;
        private readonly object _cacheLock = new();
        private Process? _previewProcess;
        private bool _disposed;
        
        // Preview settings
        public int PreviewWidth { get; set; } = 1280;
        public int PreviewHeight { get; set; } = 720;
        public int PreviewFps { get; set; } = 30;
        public bool UseProxyFiles { get; set; } = true;
        
        /// <summary>
        /// Event raised when a preview frame is ready.
        /// </summary>
        public event EventHandler<PreviewFrameEventArgs>? FrameReady;
        
        /// <summary>
        /// Event raised when preview rendering encounters an error.
        /// </summary>
        public event EventHandler<PreviewErrorEventArgs>? ErrorOccurred;

        public RealtimePreviewService(FFmpegService ffmpeg, FFprobeService ffprobe)
        {
            _ffmpeg = ffmpeg;
            _ffprobe = ffprobe;
            _previewCachePath = Path.Combine(Path.GetTempPath(), "PlatypusTools", "PreviewCache");
            _frameCache = new Dictionary<string, string>();
            
            Directory.CreateDirectory(_previewCachePath);
        }

        /// <summary>
        /// Generates a preview frame for the given timeline position.
        /// Composites all visible tracks and applies effects.
        /// </summary>
        /// <param name="tracks">All timeline tracks</param>
        /// <param name="position">Current playhead position</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Path to the generated preview frame</returns>
        public async Task<string?> GeneratePreviewFrameAsync(
            IEnumerable<TimelineTrack> tracks, 
            TimeSpan position,
            CancellationToken cancellationToken = default)
        {
            var trackList = tracks.ToList();
            
            // Find all clips at the current position
            var activeClips = new List<(TimelineTrack Track, TimelineClip Clip)>();
            
            foreach (var track in trackList)
            {
                var clip = track.Clips.FirstOrDefault(c => 
                    position >= c.StartPosition && 
                    position < c.StartPosition + c.Duration);
                
                if (clip != null)
                {
                    activeClips.Add((track, clip));
                }
            }
            
            if (activeClips.Count == 0)
                return null;
            
            // Check cache
            var cacheKey = GenerateCacheKey(activeClips, position);
            lock (_cacheLock)
            {
                if (_frameCache.TryGetValue(cacheKey, out var cachedPath) && File.Exists(cachedPath))
                    return cachedPath;
            }
            
            // Generate composite frame
            var outputPath = Path.Combine(_previewCachePath, $"preview_{Guid.NewGuid():N}.png");
            
            try
            {
                var filterComplex = BuildCompositeFilter(activeClips, position);
                var args = BuildFFmpegArgs(activeClips, position, filterComplex, outputPath);
                
                var result = await RunFFmpegAsync(args, cancellationToken);
                
                if (result && File.Exists(outputPath))
                {
                    lock (_cacheLock)
                    {
                        // Limit cache size
                        if (_frameCache.Count > 100)
                        {
                            CleanupOldCacheEntries();
                        }
                        _frameCache[cacheKey] = outputPath;
                    }
                    
                    FrameReady?.Invoke(this, new PreviewFrameEventArgs(outputPath, position));
                    return outputPath;
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, new PreviewErrorEventArgs(ex.Message, position));
            }
            
            return null;
        }

        /// <summary>
        /// Starts continuous preview playback.
        /// Generates frames in real-time for smooth playback.
        /// </summary>
        public async Task StartContinuousPreviewAsync(
            IEnumerable<TimelineTrack> tracks,
            TimeSpan startPosition,
            TimeSpan duration,
            CancellationToken cancellationToken = default)
        {
            var trackList = tracks.ToList();
            var frameInterval = TimeSpan.FromSeconds(1.0 / PreviewFps);
            var currentPosition = startPosition;
            var stopwatch = Stopwatch.StartNew();
            
            while (currentPosition < startPosition + duration && !cancellationToken.IsCancellationRequested)
            {
                var frameStart = stopwatch.Elapsed;
                
                await GeneratePreviewFrameAsync(trackList, currentPosition, cancellationToken);
                
                // Maintain frame timing
                var elapsed = stopwatch.Elapsed - frameStart;
                var waitTime = frameInterval - elapsed;
                
                if (waitTime > TimeSpan.Zero)
                {
                    await Task.Delay(waitTime, cancellationToken);
                }
                
                currentPosition += frameInterval;
            }
        }

        /// <summary>
        /// Generates a composite preview with all effects applied.
        /// Useful for filter preview in real-time.
        /// </summary>
        public async Task<string?> GenerateEffectPreviewAsync(
            TimelineClip clip,
            TimeSpan clipTime,
            IEnumerable<ClipEffect> effects,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(clip.SourcePath) || !File.Exists(clip.SourcePath))
                return null;
            
            var outputPath = Path.Combine(_previewCachePath, $"effect_preview_{Guid.NewGuid():N}.png");
            
            // Calculate source time (accounting for trim)
            var sourceTime = clip.SourceStart + clipTime;
            
            // Build filter chain for effects
            var filterChain = BuildEffectFilterChain(effects);
            
            var args = $"-ss {sourceTime.TotalSeconds:F3} -i \"{clip.SourcePath}\" " +
                       $"-vf \"{filterChain},scale={PreviewWidth}:{PreviewHeight}:force_original_aspect_ratio=decrease," +
                       $"pad={PreviewWidth}:{PreviewHeight}:(ow-iw)/2:(oh-ih)/2\" " +
                       $"-vframes 1 -y \"{outputPath}\"";
            
            var result = await RunFFmpegAsync(args, cancellationToken);
            
            return result && File.Exists(outputPath) ? outputPath : null;
        }

        /// <summary>
        /// Pre-generates preview frames for smooth scrubbing.
        /// </summary>
        public async Task PrecacheFramesAsync(
            IEnumerable<TimelineTrack> tracks,
            TimeSpan start,
            TimeSpan end,
            int framesToCache = 30,
            CancellationToken cancellationToken = default)
        {
            var interval = (end - start) / framesToCache;
            var tasks = new List<Task>();
            
            for (int i = 0; i < framesToCache; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                var position = start + TimeSpan.FromTicks(interval.Ticks * i);
                tasks.Add(GeneratePreviewFrameAsync(tracks, position, cancellationToken));
                
                // Don't overwhelm the system
                if (tasks.Count >= 4)
                {
                    await Task.WhenAny(tasks);
                    tasks.RemoveAll(t => t.IsCompleted);
                }
            }
            
            await Task.WhenAll(tasks);
        }

        private string GenerateCacheKey(List<(TimelineTrack Track, TimelineClip Clip)> clips, TimeSpan position)
        {
            var parts = clips.Select(c => $"{c.Clip.Id}:{c.Clip.SourcePath}:{c.Clip.Effects.Count}");
            return $"{string.Join("|", parts)}@{position.TotalMilliseconds:F0}";
        }

        private string BuildCompositeFilter(List<(TimelineTrack Track, TimelineClip Clip)> clips, TimeSpan position)
        {
            if (clips.Count == 0)
                return "null";
            
            if (clips.Count == 1)
            {
                return BuildClipFilter(clips[0].Clip, position, 0);
            }
            
            // Multi-layer compositing
            var filterParts = new List<string>();
            var overlayChain = "";
            
            // Order by track (video first, then overlays)
            var orderedClips = clips.OrderBy(c => c.Track.Type switch
            {
                TrackType.Video => 0,
                TrackType.Overlay => 1,
                _ => 2
            }).ToList();
            
            for (int i = 0; i < orderedClips.Count; i++)
            {
                var clip = orderedClips[i].Clip;
                var clipFilter = BuildClipFilter(clip, position, i);
                
                if (i == 0)
                {
                    filterParts.Add($"[{i}:v]{clipFilter}[base]");
                    overlayChain = "[base]";
                }
                else
                {
                    filterParts.Add($"[{i}:v]{clipFilter}[layer{i}]");
                    var prevChain = overlayChain;
                    overlayChain = i == orderedClips.Count - 1 ? "" : $"[comp{i}]";
                    filterParts.Add($"{prevChain}[layer{i}]overlay=format=auto{overlayChain}");
                }
            }
            
            return string.Join(";", filterParts);
        }

        private string BuildClipFilter(TimelineClip clip, TimeSpan position, int inputIndex)
        {
            var filters = new List<string>();
            
            // Scale to preview size
            filters.Add($"scale={PreviewWidth}:{PreviewHeight}:force_original_aspect_ratio=decrease");
            filters.Add($"pad={PreviewWidth}:{PreviewHeight}:(ow-iw)/2:(oh-ih)/2");
            
            // Apply clip effects
            foreach (var effect in clip.Effects)
            {
                var effectFilter = TranslateEffectToFFmpeg(effect);
                if (!string.IsNullOrEmpty(effectFilter))
                {
                    filters.Add(effectFilter);
                }
            }
            
            // Handle opacity
            if (clip.Opacity < 1.0)
            {
                filters.Add($"colorchannelmixer=aa={clip.Opacity:F2}");
            }
            
            return string.Join(",", filters);
        }

        private string BuildEffectFilterChain(IEnumerable<ClipEffect> effects)
        {
            var filters = new List<string>();
            
            foreach (var effect in effects)
            {
                var filter = TranslateEffectToFFmpeg(effect);
                if (!string.IsNullOrEmpty(filter))
                {
                    filters.Add(filter);
                }
            }
            
            return filters.Count > 0 ? string.Join(",", filters) : "null";
        }

        private string BuildFFmpegArgs(List<(TimelineTrack Track, TimelineClip Clip)> clips, TimeSpan position, string filterComplex, string outputPath)
        {
            var inputs = new List<string>();
            
            foreach (var (track, clip) in clips)
            {
                var sourceTime = clip.SourceStart + (position - clip.StartPosition);
                var sourcePath = UseProxyFiles && !string.IsNullOrEmpty(clip.ProxyPath) && File.Exists(clip.ProxyPath)
                    ? clip.ProxyPath
                    : clip.SourcePath;
                    
                inputs.Add($"-ss {sourceTime.TotalSeconds:F3} -i \"{sourcePath}\"");
            }
            
            return $"{string.Join(" ", inputs)} -filter_complex \"{filterComplex}\" -vframes 1 -y \"{outputPath}\"";
        }

        private string TranslateEffectToFFmpeg(ClipEffect effect)
        {
            // Translate common effects to FFmpeg filters
            return effect.EffectType?.ToLowerInvariant() switch
            {
                "brightness" => $"eq=brightness={GetEffectValue(effect, "value", 0):F2}",
                "contrast" => $"eq=contrast={GetEffectValue(effect, "value", 1):F2}",
                "saturation" => $"eq=saturation={GetEffectValue(effect, "value", 1):F2}",
                "blur" => $"boxblur={GetEffectValue(effect, "radius", 2):F0}",
                "sharpen" => $"unsharp=5:5:{GetEffectValue(effect, "amount", 1):F1}",
                "grayscale" => "colorchannelmixer=.3:.4:.3:0:.3:.4:.3:0:.3:.4:.3",
                "sepia" => "colorchannelmixer=.393:.769:.189:0:.349:.686:.168:0:.272:.534:.131",
                "invert" => "negate",
                "hue" => $"hue=h={GetEffectValue(effect, "shift", 0):F0}",
                "vignette" => $"vignette=PI/{GetEffectValue(effect, "strength", 4):F1}",
                "fade" => $"fade=t=in:st=0:d={GetEffectValue(effect, "duration", 1):F2}",
                "mirror" => "hflip",
                "flip" => "vflip",
                "rotate" => $"rotate={GetEffectValue(effect, "angle", 0) * Math.PI / 180:F4}",
                "scale" => $"scale=iw*{GetEffectValue(effect, "factor", 1):F2}:ih*{GetEffectValue(effect, "factor", 1):F2}",
                "chromakey" => "chromakey=0x00FF00:0.3:0.1",
                _ => ""
            };
        }

        private double GetEffectValue(ClipEffect effect, string key, double defaultValue)
        {
            if (effect.Parameters.TryGetValue(key, out var value) && value is double d)
                return d;
            if (effect.Parameters.TryGetValue(key, out value) && value is int i)
                return i;
            if (effect.Parameters.TryGetValue(key, out value) && double.TryParse(value?.ToString(), out var parsed))
                return parsed;
            return defaultValue;
        }

        private async Task<bool> RunFFmpegAsync(string args, CancellationToken cancellationToken)
        {
            var ffmpegPath = Services.FFmpegService.FindFfmpeg() ?? "ffmpeg";
            
            try
            {
                _previewProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ffmpegPath,
                        Arguments = args,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                
                _previewProcess.Start();
                
                using var registration = cancellationToken.Register(() =>
                {
                    try { _previewProcess?.Kill(); } catch { }
                });
                
                await _previewProcess.WaitForExitAsync(cancellationToken);
                
                return _previewProcess.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                _previewProcess?.Dispose();
                _previewProcess = null;
            }
        }

        private void CleanupOldCacheEntries()
        {
            var entriesToRemove = _frameCache
                .OrderBy(_ => Guid.NewGuid())
                .Take(_frameCache.Count / 2)
                .ToList();
            
            foreach (var entry in entriesToRemove)
            {
                _frameCache.Remove(entry.Key);
                try
                {
                    if (File.Exists(entry.Value))
                        File.Delete(entry.Value);
                }
                catch { }
            }
        }

        /// <summary>
        /// Clears all cached preview frames.
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                foreach (var path in _frameCache.Values)
                {
                    try
                    {
                        if (File.Exists(path))
                            File.Delete(path);
                    }
                    catch { }
                }
                _frameCache.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            try { _previewProcess?.Kill(); } catch { }
            _previewProcess?.Dispose();
            
            ClearCache();
            
            try
            {
                if (Directory.Exists(_previewCachePath))
                    Directory.Delete(_previewCachePath, true);
            }
            catch { }
        }
    }

    /// <summary>
    /// Event args for preview frame ready.
    /// </summary>
    public class PreviewFrameEventArgs : EventArgs
    {
        public string FramePath { get; }
        public TimeSpan Position { get; }
        
        public PreviewFrameEventArgs(string framePath, TimeSpan position)
        {
            FramePath = framePath;
            Position = position;
        }
    }

    /// <summary>
    /// Event args for preview errors.
    /// </summary>
    public class PreviewErrorEventArgs : EventArgs
    {
        public string Message { get; }
        public TimeSpan Position { get; }
        
        public PreviewErrorEventArgs(string message, TimeSpan position)
        {
            Message = message;
            Position = position;
        }
    }
}
