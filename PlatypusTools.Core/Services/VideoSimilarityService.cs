using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System.Diagnostics;
using System.Globalization;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Scan mode for video similarity detection.
    /// </summary>
    public enum VideoScanMode
    {
        /// <summary>
        /// Fast mode: analyze only 3 frames from a 15-second sample in the middle of the video.
        /// </summary>
        Fast,
        
        /// <summary>
        /// Thorough mode: analyze 5+ frames spread across the entire video.
        /// </summary>
        Thorough
    }

    /// <summary>
    /// Represents a group of similar videos.
    /// </summary>
    public class SimilarVideoGroup
    {
        public string ReferenceHash { get; set; } = string.Empty;
        public List<SimilarVideoInfo> Videos { get; set; } = new();
    }

    /// <summary>
    /// Information about a similar video.
    /// </summary>
    public class SimilarVideoInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string Hash { get; set; } = string.Empty;
        public double SimilarityPercent { get; set; }
        public long FileSize { get; set; }
        public double DurationSeconds { get; set; }
        public string Duration => TimeSpan.FromSeconds(DurationSeconds).ToString(@"hh\:mm\:ss");
        public int Width { get; set; }
        public int Height { get; set; }
        public string Resolution => $"{Width}x{Height}";
        public double FrameRate { get; set; }
        public Bitmap? Thumbnail { get; set; }
    }

    /// <summary>
    /// Progress information for video similarity scanning.
    /// </summary>
    public class VideoSimilarityScanProgress
    {
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int SimilarGroupsFound { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = string.Empty;
        public double ProgressPercent => TotalFiles > 0 ? (ProcessedFiles * 100.0 / TotalFiles) : 0;
    }

    /// <summary>
    /// Service for finding similar videos using frame sampling and perceptual hashing.
    /// </summary>
    public class VideoSimilarityService
    {
        private static readonly string[] SupportedExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".3gp" };
        
        private readonly MediaFingerprintCacheService? _cacheService;

        public event EventHandler<VideoSimilarityScanProgress>? ProgressChanged;

        /// <summary>
        /// Raised when a log-worthy event occurs during scanning (errors, timeouts, cache hits, etc.).
        /// </summary>
        public event EventHandler<string>? LogMessage;

        /// <summary>
        /// Creates a new VideoSimilarityService with optional cache support.
        /// </summary>
        /// <param name="cacheService">Optional cache service for storing fingerprints.</param>
        public VideoSimilarityService(MediaFingerprintCacheService? cacheService = null)
        {
            _cacheService = cacheService;
        }

        /// <summary>
        /// Number of frames to sample from each video for comparison.
        /// </summary>
        public int FrameSampleCount { get; set; } = 5;

        /// <summary>
        /// Duration tolerance in percent for considering videos as potentially similar.
        /// </summary>
        public double DurationTolerancePercent { get; set; } = 10;

        /// <summary>
        /// Scan mode for speed vs. accuracy tradeoff.
        /// </summary>
        public VideoScanMode ScanMode { get; set; } = VideoScanMode.Fast;

        /// <summary>
        /// Duration in seconds to sample in Fast mode (default 15 seconds).
        /// </summary>
        public double FastScanDuration { get; set; } = 15;

        /// <summary>
        /// Timeout in seconds for analyzing a single file. Files that take longer will be skipped.
        /// </summary>
        public int FileAnalysisTimeoutSeconds { get; set; } = 20;

        /// <summary>
        /// Finds groups of similar videos in the specified paths.
        /// </summary>
        /// <param name="paths">Folders or files to scan</param>
        /// <param name="threshold">Similarity threshold (0-100, default 85 for 85% similar)</param>
        /// <param name="recurse">Whether to search subdirectories</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Groups of similar videos</returns>
        public async Task<List<SimilarVideoGroup>> FindSimilarVideosAsync(
            IEnumerable<string> paths,
            int threshold = 85,
            bool recurse = true,
            CancellationToken cancellationToken = default)
        {
            var progress = new VideoSimilarityScanProgress();

            // Collect all video files
            var videoFiles = CollectVideoFiles(paths, recurse).ToList();
            progress.TotalFiles = videoFiles.Count;

            if (videoFiles.Count < 2)
                return new List<SimilarVideoGroup>();

            // Sort files by filename similarity - group similar names together for priority scanning
            videoFiles = PrioritizeByFilenameSimilarity(videoFiles);

            // Phase 1: Extract metadata and frame hashes for all videos
            var modeText = ScanMode == VideoScanMode.Fast ? "(Fast mode)" : "(Thorough mode)";
            progress.CurrentPhase = $"Extracting video information {modeText}...";
            var videoData = new Dictionary<string, VideoAnalysisData>();

            foreach (var file in videoFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.CurrentFile = Path.GetFileName(file);
                progress.ProcessedFiles++;
                ProgressChanged?.Invoke(this, progress);

                try
                {
                    VideoAnalysisData? data = null;

                    // Check cache first (note: can't cache thumbnail bitmaps)
                    if (_cacheService != null)
                    {
                        var cached = await _cacheService.GetVideoFingerprintAsync(file, ScanMode);
                        if (cached != null)
                        {
                            LogMessage?.Invoke(this, $"[CACHE HIT] {Path.GetFileName(file)}");
                            data = new VideoAnalysisData
                            {
                                DurationSeconds = cached.DurationSeconds,
                                Width = cached.Width,
                                Height = cached.Height,
                                FrameRate = cached.FrameRate,
                                FileSize = cached.FileSize,
                                FrameHashes = cached.FrameHashes,
                                CombinedHash = cached.CombinedHash,
                                Thumbnail = null // Will extract fresh thumbnail
                            };

                            // Extract thumbnail separately (not cached)
                            var ffmpeg = FFmpegService.FindFfmpeg();
                            if (ffmpeg != null)
                            {
                                data.Thumbnail = await ExtractThumbnailAsync(file, ffmpeg, data.DurationSeconds / 2, cancellationToken);
                            }
                        }
                    }

                    // Analyze if not cached
                    if (data == null)
                    {
                        // Apply per-file timeout to prevent freezing on problematic files
                        CancellationTokenSource? timeoutCts = null;
                        CancellationTokenSource? linkedCts = null;
                        var timedOut = false;
                        
                        try
                        {
#pragma warning disable CA2000 // Dispose objects before losing scope - properly disposed in finally block
                            timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(FileAnalysisTimeoutSeconds));
                            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
#pragma warning restore CA2000
                            data = await AnalyzeVideoAsync(file, linkedCts.Token);
                        }
                        catch (OperationCanceledException) when (timeoutCts?.IsCancellationRequested == true && !cancellationToken.IsCancellationRequested)
                        {
                            // File analysis timed out, skip this file
                            var sizeMB = new FileInfo(file).Length / (1024.0 * 1024.0);
                            LogMessage?.Invoke(this, $"[TIMEOUT] {Path.GetFileName(file)} ({sizeMB:F1} MB) - exceeded {FileAnalysisTimeoutSeconds}s limit, skipped. FFmpeg may be hanging on this file format.");
                            timedOut = true;
                        }
                        finally
                        {
                            linkedCts?.Dispose();
                            timeoutCts?.Dispose();
                        }

                        if (timedOut) continue;

                        // Store in cache (without thumbnail)
                        if (data != null && _cacheService != null)
                        {
                            var cacheEntry = new CachedVideoFingerprint
                            {
                                DurationSeconds = data.DurationSeconds,
                                Width = data.Width,
                                Height = data.Height,
                                FrameRate = data.FrameRate,
                                FileSize = data.FileSize,
                                FrameHashes = data.FrameHashes,
                                CombinedHash = data.CombinedHash
                            };
                            await _cacheService.StoreVideoFingerprintAsync(file, ScanMode, cacheEntry);
                        }
                    }

                    if (data != null)
                    {
                        videoData[file] = data;
                    }
                }
                catch
                {
                    // Skip files that can't be analyzed
                    var sizeMB = new FileInfo(file).Length / (1024.0 * 1024.0);
                    LogMessage?.Invoke(this, $"[ERROR] {Path.GetFileName(file)} ({sizeMB:F1} MB) - analysis failed, skipped");
                }
            }

            // Phase 2: Group similar videos
            progress.CurrentPhase = "Comparing videos...";
            progress.ProcessedFiles = 0;
            progress.TotalFiles = videoData.Count;

            var groups = new List<SimilarVideoGroup>();
            var processed = new HashSet<string>();

            foreach (var (filePath, data) in videoData)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.CurrentFile = Path.GetFileName(filePath);
                progress.ProcessedFiles++;
                ProgressChanged?.Invoke(this, progress);

                if (processed.Contains(filePath))
                    continue;

                var group = new SimilarVideoGroup { ReferenceHash = data.CombinedHash };
                group.Videos.Add(CreateVideoInfo(filePath, data, 100));
                processed.Add(filePath);

                // Compare with all other unprocessed videos
                foreach (var (otherPath, otherData) in videoData)
                {
                    if (processed.Contains(otherPath))
                        continue;

                    // Quick duration check first
                    if (!AreDurationsSimilar(data.DurationSeconds, otherData.DurationSeconds))
                        continue;

                    // Compare frame hashes
                    var similarity = CalculateVideoSimilarity(data, otherData);
                    if (similarity >= threshold)
                    {
                        group.Videos.Add(CreateVideoInfo(otherPath, otherData, similarity));
                        processed.Add(otherPath);
                    }
                }

                if (group.Videos.Count > 1)
                {
                    groups.Add(group);
                    progress.SimilarGroupsFound = groups.Count;
                }
            }

            return groups;
        }

        private bool AreDurationsSimilar(double duration1, double duration2)
        {
            if (duration1 <= 0 || duration2 <= 0)
                return false;

            var avg = (duration1 + duration2) / 2;
            var diff = Math.Abs(duration1 - duration2);
            var toleranceSeconds = avg * (DurationTolerancePercent / 100);

            return diff <= toleranceSeconds;
        }

        private double CalculateVideoSimilarity(VideoAnalysisData video1, VideoAnalysisData video2)
        {
            if (video1.FrameHashes.Count == 0 || video2.FrameHashes.Count == 0)
                return 0;

            // Compare each frame hash pair
            var minFrames = Math.Min(video1.FrameHashes.Count, video2.FrameHashes.Count);
            double totalSimilarity = 0;

            for (int i = 0; i < minFrames; i++)
            {
                totalSimilarity += CalculateHashSimilarity(video1.FrameHashes[i], video2.FrameHashes[i]);
            }

            return totalSimilarity / minFrames;
        }

        private double CalculateHashSimilarity(ulong hash1, ulong hash2)
        {
            // Count different bits (Hamming distance)
            var xor = hash1 ^ hash2;
            int distance = 0;
            while (xor != 0)
            {
                distance += (int)(xor & 1);
                xor >>= 1;
            }

            // Convert to similarity percentage (64 bits total)
            return (1.0 - (distance / 64.0)) * 100;
        }

        private SimilarVideoInfo CreateVideoInfo(string filePath, VideoAnalysisData data, double similarity)
        {
            return new SimilarVideoInfo
            {
                FilePath = filePath,
                Hash = data.CombinedHash,
                SimilarityPercent = similarity,
                FileSize = data.FileSize,
                DurationSeconds = data.DurationSeconds,
                Width = data.Width,
                Height = data.Height,
                FrameRate = data.FrameRate,
                Thumbnail = data.Thumbnail
            };
        }

        private async Task<VideoAnalysisData?> AnalyzeVideoAsync(string filePath, CancellationToken cancellationToken)
        {
            var ffprobe = FFprobeService.FindFfprobe();
            var ffmpeg = FFmpegService.FindFfmpeg();

            if (ffprobe == null || ffmpeg == null)
                return null;

            var data = new VideoAnalysisData();
            data.FileSize = new FileInfo(filePath).Length;
            var fileName = Path.GetFileName(filePath);
            var fileSizeMB = data.FileSize / (1024.0 * 1024.0);

            // Phase 1: Get video metadata using ffprobe
            LogMessage?.Invoke(this, $"[PROBE] {fileName} ({fileSizeMB:F1} MB) - reading metadata...");
            var probeArgs = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"";
            var psi = new ProcessStartInfo(ffprobe, probeArgs)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null) return null;

                try
                {
                    var jsonOutput = await p.StandardOutput.ReadToEndAsync(cancellationToken);
                    await p.WaitForExitAsync(cancellationToken);

                    if (!string.IsNullOrEmpty(jsonOutput))
                    {
                        ParseProbeOutput(jsonOutput, data);
                    }
                }
                catch (OperationCanceledException)
                {
                    TryKillProcess(p);
                    throw;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogMessage?.Invoke(this, $"[ERROR] {fileName} - ffprobe failed: {ex.Message}");
                return null;
            }

            if (data.DurationSeconds <= 0)
            {
                LogMessage?.Invoke(this, $"[SKIP] {fileName} - could not determine duration");
                return null;
            }

            // Phase 2: Extract key frames and compute hashes
            LogMessage?.Invoke(this, $"[FRAMES] {fileName} - extracting frame hashes (duration: {TimeSpan.FromSeconds(data.DurationSeconds):hh\\:mm\\:ss})...");
            var frameHashes = await ExtractFrameHashesAsync(filePath, ffmpeg, data.DurationSeconds, cancellationToken);
            data.FrameHashes = frameHashes;

            // Generate combined hash from all frame hashes
            if (frameHashes.Count > 0)
            {
                ulong combined = frameHashes[0];
                for (int i = 1; i < frameHashes.Count; i++)
                {
                    combined ^= frameHashes[i];
                }
                data.CombinedHash = combined.ToString("X16");
            }

            // Phase 3: Extract thumbnail
            data.Thumbnail = await ExtractThumbnailAsync(filePath, ffmpeg, data.DurationSeconds / 2, cancellationToken);

            return data;
        }

        private static void TryKillProcess(Process? p)
        {
            try
            {
                if (p != null && !p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch { }
        }

        private void ParseProbeOutput(string json, VideoAnalysisData data)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Get duration from format
                if (root.TryGetProperty("format", out var format))
                {
                    if (format.TryGetProperty("duration", out var dur))
                    {
                        if (double.TryParse(dur.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                            data.DurationSeconds = d;
                    }
                }

                // Get video stream info
                if (root.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (stream.TryGetProperty("codec_type", out var ct) && ct.GetString() == "video")
                        {
                            if (stream.TryGetProperty("width", out var w))
                                data.Width = w.GetInt32();
                            if (stream.TryGetProperty("height", out var h))
                                data.Height = h.GetInt32();
                            if (stream.TryGetProperty("r_frame_rate", out var fr))
                            {
                                var frStr = fr.GetString();
                                if (!string.IsNullOrEmpty(frStr))
                                {
                                    var parts = frStr.Split('/');
                                    if (parts.Length == 2 &&
                                        double.TryParse(parts[0], out var num) &&
                                        double.TryParse(parts[1], out var den) &&
                                        den > 0)
                                    {
                                        data.FrameRate = num / den;
                                    }
                                }
                            }
                            break;
                        }
                    }
                }
            }
            catch
            {
                // JSON parsing failed
            }
        }

        private async Task<List<ulong>> ExtractFrameHashesAsync(string filePath, string ffmpeg, double duration, CancellationToken cancellationToken)
        {
            var hashes = new List<ulong>();
            var tempDir = Path.Combine(Path.GetTempPath(), "PlatypusVideoSimilarity", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDir);

            try
            {
                // Calculate timestamps for frame extraction based on scan mode
                var timestamps = new List<double>();
                
                if (ScanMode == VideoScanMode.Fast)
                {
                    // Fast mode: analyze only 3 frames from a 15-second sample in the middle
                    var sampleDuration = Math.Min(FastScanDuration, duration);
                    var startTime = Math.Max(0, (duration - sampleDuration) / 2);
                    var frameSamples = Math.Min(3, FrameSampleCount);
                    
                    for (int i = 0; i < frameSamples; i++)
                    {
                        // Sample frames at 25%, 50%, 75% of the sampled segment
                        var percent = 0.25 + (0.5 * i / Math.Max(1, frameSamples - 1));
                        timestamps.Add(startTime + sampleDuration * percent);
                    }
                }
                else
                {
                    // Thorough mode: sample frames across entire video
                    for (int i = 0; i < FrameSampleCount; i++)
                    {
                        // Sample frames at 10%, 30%, 50%, 70%, 90% of duration
                        var percent = 0.1 + (0.8 * i / (FrameSampleCount - 1));
                        timestamps.Add(duration * percent);
                    }
                }

                foreach (var ts in timestamps)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var outputPath = Path.Combine(tempDir, $"frame_{ts:F2}.png");
                    var args = $"-ss {ts:F2} -i \"{filePath}\" -vframes 1 -s 32x32 -f image2 \"{outputPath}\" -y";

                    var psi = new ProcessStartInfo(ffmpeg, args)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using var p = Process.Start(psi);
                    if (p != null)
                    {
                        try
                        {
                            await p.WaitForExitAsync(cancellationToken);
                        }
                        catch (OperationCanceledException)
                        {
                            TryKillProcess(p);
                            throw;
                        }

                        if (File.Exists(outputPath))
                        {
                            var hash = ComputePerceptualHash(outputPath);
                            hashes.Add(hash);
                        }
                    }
                }
            }
            finally
            {
                // Cleanup temp files
                try
                {
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, true);
                }
                catch { }
            }

            return hashes;
        }

        private ulong ComputePerceptualHash(string imagePath)
        {
            try
            {
                using var img = new Bitmap(imagePath);
                using var resized = new Bitmap(8, 8);
                using var g = Graphics.FromImage(resized);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, 0, 0, 8, 8);

                // Convert to grayscale and compute average
                double total = 0;
                var pixels = new byte[64];
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        var pixel = resized.GetPixel(x, y);
                        var gray = (byte)(0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B);
                        pixels[y * 8 + x] = gray;
                        total += gray;
                    }
                }

                var avg = total / 64;

                // Build hash
                ulong hash = 0;
                for (int i = 0; i < 64; i++)
                {
                    if (pixels[i] > avg)
                        hash |= (1UL << i);
                }

                return hash;
            }
            catch
            {
                return 0;
            }
        }

        private async Task<Bitmap?> ExtractThumbnailAsync(string filePath, string ffmpeg, double timestamp, CancellationToken cancellationToken)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"thumb_{Guid.NewGuid()}.png");

            try
            {
                var args = $"-ss {timestamp:F2} -i \"{filePath}\" -vframes 1 -s 160x120 -f image2 \"{tempPath}\" -y";

                var psi = new ProcessStartInfo(ffmpeg, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    await p.WaitForExitAsync(cancellationToken);

                    if (File.Exists(tempPath))
                    {
                        using var stream = new FileStream(tempPath, FileMode.Open, FileAccess.Read);
                        return new Bitmap(stream);
                    }
                }
            }
            catch { }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }

            return null;
        }

        private IEnumerable<string> CollectVideoFiles(IEnumerable<string> paths, bool recurse)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    if (IsVideoFile(path))
                        yield return path;
                }
                else if (Directory.Exists(path))
                {
                    var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    foreach (var file in Utilities.SafeFileEnumerator.EnumerateFiles(path, "*", recurse))
                    {
                        if (IsVideoFile(file))
                            yield return file;
                    }
                }
            }
        }

        private bool IsVideoFile(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return SupportedExtensions.Contains(ext);
        }

        /// <summary>
        /// Prioritizes files by filename similarity - files with similar names are grouped together
        /// so they are compared first, improving chances of finding matches early.
        /// </summary>
        private List<string> PrioritizeByFilenameSimilarity(List<string> files)
        {
            if (files.Count < 2)
                return files;

            // Extract normalized base names (without extension, lowercase, common words removed)
            var fileInfos = files.Select(f => new
            {
                Path = f,
                BaseName = NormalizeFilename(Path.GetFileNameWithoutExtension(f))
            }).ToList();

            // Group files by their normalized name prefix (first 10 chars or full name if shorter)
            var groups = fileInfos
                .GroupBy(f => f.BaseName.Length >= 10 ? f.BaseName.Substring(0, 10) : f.BaseName)
                .OrderByDescending(g => g.Count()) // Put groups with more potential duplicates first
                .SelectMany(g => g.Select(f => f.Path))
                .ToList();

            return groups;
        }

        /// <summary>
        /// Normalizes a filename for similarity comparison - removes common words, numbers, and extra chars.
        /// </summary>
        private string NormalizeFilename(string filename)
        {
            // Convert to lowercase
            var normalized = filename.ToLowerInvariant();

            // Remove common suffixes like (1), (2), _copy, -copy, etc.
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]*\(\d+\)$", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]+copy[\s_-]*\d*$", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]+\d+$", "");
            
            // Remove common video quality tags
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]*(1080p|720p|480p|4k|2160p|hd|sd|uhd)[\s_-]*", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]*(x264|x265|h264|h265|hevc|avc|xvid|divx)[\s_-]*", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]*(bluray|bdrip|dvdrip|webrip|hdtv)[\s_-]*", "");
            
            // Normalize spaces/underscores/hyphens to single underscore
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]+", "_");
            
            // Remove leading/trailing underscores
            normalized = normalized.Trim('_');

            return normalized;
        }

        private class VideoAnalysisData
        {
            public double DurationSeconds { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public double FrameRate { get; set; }
            public long FileSize { get; set; }
            public List<ulong> FrameHashes { get; set; } = new();
            public string CombinedHash { get; set; } = string.Empty;
            public Bitmap? Thumbnail { get; set; }
        }
    }
}
