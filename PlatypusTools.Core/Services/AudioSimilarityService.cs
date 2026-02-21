using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Scan mode for audio similarity detection.
    /// </summary>
    public enum AudioScanMode
    {
        /// <summary>
        /// Fast mode: analyze only a 15-second sample from the middle of each audio file.
        /// </summary>
        Fast,
        
        /// <summary>
        /// Thorough mode: analyze the entire audio file with multiple sample points.
        /// </summary>
        Thorough
    }

    /// <summary>
    /// Represents a group of acoustically similar audio files.
    /// </summary>
    public class SimilarAudioGroup
    {
        public string ReferenceHash { get; set; } = string.Empty;
        public List<SimilarAudioInfo> AudioFiles { get; set; } = new();
    }

    /// <summary>
    /// Information about a similar audio file.
    /// </summary>
    public class SimilarAudioInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string Hash { get; set; } = string.Empty;
        public double SimilarityPercent { get; set; }
        public long FileSize { get; set; }
        public double DurationSeconds { get; set; }
        public string Duration => FormatDuration(DurationSeconds);
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public string ChannelInfo => Channels == 1 ? "Mono" : Channels == 2 ? "Stereo" : $"{Channels} ch";
        public int BitRate { get; set; }
        public string BitrateText => BitRate > 0 ? $"{BitRate / 1000} kbps" : "N/A";
        public string Codec { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;

        private static string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "0:00";
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.TotalHours >= 1 
                ? ts.ToString(@"h\:mm\:ss") 
                : ts.ToString(@"m\:ss");
        }
    }

    /// <summary>
    /// Progress information for audio similarity scanning.
    /// </summary>
    public class AudioSimilarityScanProgress
    {
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int SimilarGroupsFound { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = string.Empty;
        public double ProgressPercent => TotalFiles > 0 ? (ProcessedFiles * 100.0 / TotalFiles) : 0;
    }

    /// <summary>
    /// Internal data structure for audio analysis.
    /// </summary>
    internal class AudioAnalysisData
    {
        public double DurationSeconds { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
        public int BitRate { get; set; }
        public string Codec { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string CombinedHash { get; set; } = string.Empty;
        
        // Audio fingerprint data - list of spectral hash values
        public List<ulong> SpectralHashes { get; set; } = new();
        
        // RMS levels at sample points for waveform comparison
        public List<double> RmsLevels { get; set; } = new();
    }

    /// <summary>
    /// Service for finding acoustically similar audio files using audio fingerprinting.
    /// </summary>
    public class AudioSimilarityService
    {
        private static readonly string[] SupportedExtensions = 
        { 
            ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".flac", ".wav", ".wma", 
            ".aiff", ".aif", ".ape", ".wv", ".mka", ".ac3", ".dts"
        };

        private readonly MediaFingerprintCacheService? _cacheService;

        public event EventHandler<AudioSimilarityScanProgress>? ProgressChanged;

        /// <summary>
        /// Raised when a log-worthy event occurs during scanning (errors, timeouts, cache hits, etc.).
        /// </summary>
        public event EventHandler<string>? LogMessage;

        /// <summary>
        /// Creates a new AudioSimilarityService with optional cache support.
        /// </summary>
        /// <param name="cacheService">Optional cache service for storing fingerprints.</param>
        public AudioSimilarityService(MediaFingerprintCacheService? cacheService = null)
        {
            _cacheService = cacheService;
        }

        /// <summary>
        /// Number of sample points to extract for comparison.
        /// </summary>
        public int SamplePointCount { get; set; } = 32;

        /// <summary>
        /// Duration tolerance in percent for considering audio files as potentially similar.
        /// </summary>
        public double DurationTolerancePercent { get; set; } = 5;

        /// <summary>
        /// Scan mode for speed vs. accuracy tradeoff.
        /// </summary>
        public AudioScanMode ScanMode { get; set; } = AudioScanMode.Fast;

        /// <summary>
        /// Duration in seconds to sample in Fast mode (default 15 seconds).
        /// </summary>
        public double FastScanDuration { get; set; } = 15;

        /// <summary>
        /// Timeout in seconds for analyzing a single file. Files that take longer will be skipped.
        /// </summary>
        public int FileAnalysisTimeoutSeconds { get; set; } = 20;

        /// <summary>
        /// Finds groups of acoustically similar audio files in the specified paths.
        /// </summary>
        /// <param name="paths">Folders or files to scan</param>
        /// <param name="threshold">Similarity threshold (0-100, default 85 for 85% similar)</param>
        /// <param name="recurse">Whether to search subdirectories</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Groups of similar audio files</returns>
        public async Task<List<SimilarAudioGroup>> FindSimilarAudioAsync(
            IEnumerable<string> paths,
            int threshold = 85,
            bool recurse = true,
            CancellationToken cancellationToken = default)
        {
            var progress = new AudioSimilarityScanProgress();

            // Collect all audio files
            var audioFiles = CollectAudioFiles(paths, recurse).ToList();
            progress.TotalFiles = audioFiles.Count;

            if (audioFiles.Count < 2)
                return new List<SimilarAudioGroup>();

            // Sort files by filename similarity - group similar names together for priority scanning
            audioFiles = PrioritizeByFilenameSimilarity(audioFiles);

            // Phase 1: Extract metadata and audio fingerprints
            var modeText = ScanMode == AudioScanMode.Fast ? "(Fast mode)" : "(Thorough mode)";
            progress.CurrentPhase = $"Analyzing audio files {modeText}...";
            var audioData = new Dictionary<string, AudioAnalysisData>();

            foreach (var file in audioFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.CurrentFile = Path.GetFileName(file);
                progress.ProcessedFiles++;
                ProgressChanged?.Invoke(this, progress);

                try
                {
                    AudioAnalysisData? data = null;

                    // Check cache first
                    if (_cacheService != null)
                    {
                        var cached = await _cacheService.GetAudioFingerprintAsync(file, ScanMode);
                        if (cached != null)
                        {
                            LogMessage?.Invoke(this, $"[CACHE HIT] {Path.GetFileName(file)}");
                            data = new AudioAnalysisData
                            {
                                DurationSeconds = cached.DurationSeconds,
                                SampleRate = cached.SampleRate,
                                Channels = cached.Channels,
                                BitRate = cached.BitRate,
                                Codec = cached.Codec,
                                Title = cached.Title,
                                Artist = cached.Artist,
                                CombinedHash = cached.CombinedHash,
                                SpectralHashes = cached.SpectralHashes,
                                RmsLevels = cached.RmsLevels
                            };
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
                            data = await AnalyzeAudioAsync(file, linkedCts.Token);
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

                        // Store in cache
                        if (data != null && _cacheService != null)
                        {
                            var cacheEntry = new CachedAudioFingerprint
                            {
                                DurationSeconds = data.DurationSeconds,
                                SampleRate = data.SampleRate,
                                Channels = data.Channels,
                                BitRate = data.BitRate,
                                Codec = data.Codec,
                                Title = data.Title,
                                Artist = data.Artist,
                                CombinedHash = data.CombinedHash,
                                SpectralHashes = data.SpectralHashes,
                                RmsLevels = data.RmsLevels
                            };
                            await _cacheService.StoreAudioFingerprintAsync(file, ScanMode, cacheEntry);
                        }
                    }

                    if (data != null)
                    {
                        audioData[file] = data;
                    }
                }
                catch
                {
                    // Skip files that can't be analyzed
                    var sizeMB = new FileInfo(file).Length / (1024.0 * 1024.0);
                    LogMessage?.Invoke(this, $"[ERROR] {Path.GetFileName(file)} ({sizeMB:F1} MB) - analysis failed, skipped");
                }
            }

            // Phase 2: Group similar audio files
            progress.CurrentPhase = "Comparing audio files...";
            progress.ProcessedFiles = 0;
            progress.TotalFiles = audioData.Count;

            var groups = new List<SimilarAudioGroup>();
            var processed = new HashSet<string>();

            foreach (var (filePath, data) in audioData)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.CurrentFile = Path.GetFileName(filePath);
                progress.ProcessedFiles++;
                ProgressChanged?.Invoke(this, progress);

                if (processed.Contains(filePath))
                    continue;

                var group = new SimilarAudioGroup { ReferenceHash = data.CombinedHash };
                group.AudioFiles.Add(CreateAudioInfo(filePath, data, 100));
                processed.Add(filePath);

                // Compare with all other unprocessed audio files
                foreach (var (otherPath, otherData) in audioData)
                {
                    if (processed.Contains(otherPath))
                        continue;

                    // Quick duration check first
                    if (!AreDurationsSimilar(data.DurationSeconds, otherData.DurationSeconds))
                        continue;

                    // Compare audio fingerprints
                    var similarity = CalculateAudioSimilarity(data, otherData);
                    if (similarity >= threshold)
                    {
                        group.AudioFiles.Add(CreateAudioInfo(otherPath, otherData, similarity));
                        processed.Add(otherPath);
                    }
                }

                if (group.AudioFiles.Count > 1)
                {
                    groups.Add(group);
                    progress.SimilarGroupsFound = groups.Count;
                }
            }

            return groups;
        }

        private IEnumerable<string> CollectAudioFiles(IEnumerable<string> paths, bool recurse)
        {
            var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (SupportedExtensions.Contains(ext))
                        yield return path;
                }
                else if (Directory.Exists(path))
                {
                    IEnumerable<string> files;
                    try
                    {
                        files = Directory.EnumerateFiles(path, "*.*", searchOption);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var file in files)
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (SupportedExtensions.Contains(ext))
                            yield return file;
                    }
                }
            }
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
            
            // Remove common audio quality tags
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]*(320|256|192|128|64)(kbps|kbs)?[\s_-]*", "");
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]*(flac|mp3|aac|wav|ogg)[\s_-]*", "");
            
            // Normalize spaces/underscores/hyphens to single underscore
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\s_-]+", "_");
            
            // Remove leading/trailing underscores
            normalized = normalized.Trim('_');

            return normalized;
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

        private double CalculateAudioSimilarity(AudioAnalysisData audio1, AudioAnalysisData audio2)
        {
            // Compare using multiple methods and combine results
            double similarity = 0;
            int methodsUsed = 0;

            // Method 1: RMS level comparison (waveform envelope)
            if (audio1.RmsLevels.Count > 0 && audio2.RmsLevels.Count > 0)
            {
                var rmsSimilarity = CalculateRmsSimilarity(audio1.RmsLevels, audio2.RmsLevels);
                similarity += rmsSimilarity;
                methodsUsed++;
            }

            // Method 2: Spectral hash comparison
            if (audio1.SpectralHashes.Count > 0 && audio2.SpectralHashes.Count > 0)
            {
                var spectralSimilarity = CalculateSpectralSimilarity(audio1.SpectralHashes, audio2.SpectralHashes);
                similarity += spectralSimilarity;
                methodsUsed++;
            }

            // Method 3: Duration similarity (already filtered, but adds weight)
            var durationSim = 100 - (Math.Abs(audio1.DurationSeconds - audio2.DurationSeconds) / 
                                     Math.Max(audio1.DurationSeconds, audio2.DurationSeconds) * 100);
            similarity += durationSim;
            methodsUsed++;

            return methodsUsed > 0 ? similarity / methodsUsed : 0;
        }

        private double CalculateRmsSimilarity(List<double> rms1, List<double> rms2)
        {
            // Normalize to same length
            var minLen = Math.Min(rms1.Count, rms2.Count);
            if (minLen == 0) return 0;

            double totalDiff = 0;
            for (int i = 0; i < minLen; i++)
            {
                var diff = Math.Abs(rms1[i] - rms2[i]);
                totalDiff += diff;
            }

            var avgDiff = totalDiff / minLen;
            // Convert to similarity percentage (assuming RMS values are normalized 0-1)
            return Math.Max(0, 100 - (avgDiff * 100));
        }

        private double CalculateSpectralSimilarity(List<ulong> hashes1, List<ulong> hashes2)
        {
            var minLen = Math.Min(hashes1.Count, hashes2.Count);
            if (minLen == 0) return 0;

            double totalSimilarity = 0;
            for (int i = 0; i < minLen; i++)
            {
                totalSimilarity += CalculateHashSimilarity(hashes1[i], hashes2[i]);
            }

            return totalSimilarity / minLen;
        }

        private double CalculateHashSimilarity(ulong hash1, ulong hash2)
        {
            // Hamming distance
            var xor = hash1 ^ hash2;
            int distance = 0;
            while (xor != 0)
            {
                distance++;
                xor &= xor - 1;
            }
            // 64-bit hash, so max distance is 64
            return (1 - distance / 64.0) * 100;
        }

        private SimilarAudioInfo CreateAudioInfo(string filePath, AudioAnalysisData data, double similarity)
        {
            var fileInfo = new FileInfo(filePath);
            return new SimilarAudioInfo
            {
                FilePath = filePath,
                Hash = data.CombinedHash,
                SimilarityPercent = similarity,
                FileSize = fileInfo.Exists ? fileInfo.Length : 0,
                DurationSeconds = data.DurationSeconds,
                SampleRate = data.SampleRate,
                Channels = data.Channels,
                BitRate = data.BitRate,
                Codec = data.Codec,
                Title = data.Title,
                Artist = data.Artist
            };
        }

        private async Task<AudioAnalysisData?> AnalyzeAudioAsync(string filePath, CancellationToken cancellationToken)
        {
            var data = new AudioAnalysisData();

            // Find ffprobe/ffmpeg
            var ffprobe = FindFFprobe();
            var ffmpeg = FindFFmpeg();

            if (string.IsNullOrEmpty(ffprobe) || string.IsNullOrEmpty(ffmpeg))
                return null;

            var fileName = Path.GetFileName(filePath);
            var fileSizeMB = new FileInfo(filePath).Length / (1024.0 * 1024.0);

            // Phase 1: Get audio metadata using ffprobe
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
                    // Read both pipes concurrently to prevent deadlock
                    var stdoutTask = p.StandardOutput.ReadToEndAsync(cancellationToken);
                    var stderrTask = p.StandardError.ReadToEndAsync(cancellationToken);
                    await p.WaitForExitAsync(cancellationToken);
                    var jsonOutput = await stdoutTask;
                    await stderrTask;

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

            // Phase 2: Extract RMS levels
            LogMessage?.Invoke(this, $"[RMS] {fileName} - extracting RMS levels (duration: {TimeSpan.FromSeconds(data.DurationSeconds):hh\\:mm\\:ss})...");
            var rmsLevels = await ExtractRmsLevelsAsync(filePath, ffmpeg, data.DurationSeconds, cancellationToken);
            data.RmsLevels = rmsLevels;

            // Phase 3: Calculate spectral hashes
            LogMessage?.Invoke(this, $"[SPECTRAL] {fileName} - computing spectral hashes...");
            var spectralHashes = await ExtractSpectralHashesAsync(filePath, ffmpeg, data.DurationSeconds, cancellationToken);
            data.SpectralHashes = spectralHashes;

            // Generate combined hash
            if (rmsLevels.Count > 0 || spectralHashes.Count > 0)
            {
                var hashData = string.Join(",", rmsLevels.Select(r => r.ToString("F3")));
                if (spectralHashes.Count > 0)
                {
                    hashData += "|" + string.Join(",", spectralHashes.Select(h => h.ToString("X16")));
                }
                data.CombinedHash = ComputeSimpleHash(hashData);
            }

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

        private void ParseProbeOutput(string json, AudioAnalysisData data)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Get duration and metadata from format
                if (root.TryGetProperty("format", out var format))
                {
                    if (format.TryGetProperty("duration", out var dur))
                    {
                        if (double.TryParse(dur.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                            data.DurationSeconds = d;
                    }
                    if (format.TryGetProperty("bit_rate", out var br))
                    {
                        if (int.TryParse(br.GetString(), out var b))
                            data.BitRate = b;
                    }
                    
                    // Get tags
                    if (format.TryGetProperty("tags", out var tags))
                    {
                        if (tags.TryGetProperty("title", out var title))
                            data.Title = title.GetString() ?? "";
                        else if (tags.TryGetProperty("TITLE", out var titleUpper))
                            data.Title = titleUpper.GetString() ?? "";
                            
                        if (tags.TryGetProperty("artist", out var artist))
                            data.Artist = artist.GetString() ?? "";
                        else if (tags.TryGetProperty("ARTIST", out var artistUpper))
                            data.Artist = artistUpper.GetString() ?? "";
                    }
                }

                // Get audio stream info
                if (root.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (stream.TryGetProperty("codec_type", out var ct) && ct.GetString() == "audio")
                        {
                            if (stream.TryGetProperty("sample_rate", out var sr))
                            {
                                if (int.TryParse(sr.GetString(), out var sampleRate))
                                    data.SampleRate = sampleRate;
                            }
                            if (stream.TryGetProperty("channels", out var ch))
                                data.Channels = ch.GetInt32();
                            if (stream.TryGetProperty("codec_name", out var codec))
                                data.Codec = codec.GetString() ?? "";
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

        private async Task<List<double>> ExtractRmsLevelsAsync(string filePath, string ffmpeg, double duration, CancellationToken cancellationToken)
        {
            var rmsLevels = new List<double>();
            
            double startTime;
            double sampleDuration;
            int samplePoints;

            if (ScanMode == AudioScanMode.Fast)
            {
                // Fast mode: only sample from a 15-second segment in the middle
                sampleDuration = Math.Min(FastScanDuration, duration);
                startTime = Math.Max(0, (duration - sampleDuration) / 2);
                samplePoints = Math.Min(8, SamplePointCount / 4); // Fewer sample points in fast mode
            }
            else
            {
                // Thorough mode: sample across entire duration
                startTime = 0;
                sampleDuration = duration;
                samplePoints = SamplePointCount;
            }

            var interval = sampleDuration / (samplePoints + 1);
            
            for (int i = 1; i <= samplePoints; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var timestamp = startTime + (interval * i);
                var rms = await GetRmsAtTimestampAsync(filePath, ffmpeg, timestamp, cancellationToken);
                rmsLevels.Add(rms);
            }

            return rmsLevels;
        }

        private async Task<double> GetRmsAtTimestampAsync(string filePath, string ffmpeg, double timestamp, CancellationToken cancellationToken)
        {
            // Extract a small segment and calculate RMS
            // Use volumedetect filter to get mean_volume
            var args = $"-ss {timestamp:F2} -t 0.5 -i \"{filePath}\" -af volumedetect -f null -";
            
            var psi = new ProcessStartInfo(ffmpeg, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null) return 0;

                try
                {
                    // Read both pipes concurrently to prevent deadlock
                    var stdoutDrain = p.StandardOutput.ReadToEndAsync(cancellationToken);
                    var stderrTask = p.StandardError.ReadToEndAsync(cancellationToken);
                    await p.WaitForExitAsync(cancellationToken);
                    await stdoutDrain;
                    var output = await stderrTask;

                    // Parse mean_volume from output
                    // Example: mean_volume: -20.5 dB
                    var match = System.Text.RegularExpressions.Regex.Match(output, @"mean_volume:\s*(-?\d+\.?\d*)\s*dB");
                    if (match.Success && double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var db))
                    {
                        // Normalize dB to 0-1 range (assuming -60dB to 0dB range)
                        return Math.Max(0, Math.Min(1, (db + 60) / 60));
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
            catch
            {
                // Extraction failed
            }

            return 0;
        }

        private async Task<List<ulong>> ExtractSpectralHashesAsync(string filePath, string ffmpeg, double duration, CancellationToken cancellationToken)
        {
            var hashes = new List<ulong>();
            
            double startTime;
            double sampleDuration;
            int samplePoints;

            if (ScanMode == AudioScanMode.Fast)
            {
                // Fast mode: only sample from a 15-second segment in the middle
                sampleDuration = Math.Min(FastScanDuration, duration);
                startTime = Math.Max(0, (duration - sampleDuration) / 2);
                samplePoints = Math.Min(4, SamplePointCount / 8); // Even fewer spectral samples in fast mode
            }
            else
            {
                // Thorough mode: sample across entire duration
                startTime = 0;
                sampleDuration = duration;
                samplePoints = Math.Min(8, SamplePointCount / 4);
            }

            var interval = sampleDuration / (samplePoints + 1);
            
            for (int i = 1; i <= samplePoints; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var timestamp = startTime + (interval * i);
                var hash = await GetSpectralHashAtTimestampAsync(filePath, ffmpeg, timestamp, cancellationToken);
                hashes.Add(hash);
            }

            return hashes;
        }

        private async Task<ulong> GetSpectralHashAtTimestampAsync(string filePath, string ffmpeg, double timestamp, CancellationToken cancellationToken)
        {
            // Extract raw audio samples and compute a simple spectral hash
            // Use FFmpeg to get raw PCM data and compute hash from frequency magnitudes
            var args = $"-ss {timestamp:F2} -t 0.25 -i \"{filePath}\" -ac 1 -ar 8000 -f s16le -";
            
            var psi = new ProcessStartInfo(ffmpeg, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                using var p = Process.Start(psi);
                if (p == null) return 0;

                try
                {
                    using var ms = new MemoryStream();
                    // Read stdout (raw audio) and drain stderr concurrently to prevent deadlock
                    var stderrDrain = p.StandardError.ReadToEndAsync(cancellationToken);
                    await p.StandardOutput.BaseStream.CopyToAsync(ms, cancellationToken);
                    await p.WaitForExitAsync(cancellationToken);
                    await stderrDrain;

                    var samples = ms.ToArray();
                    if (samples.Length < 64) return 0;

                    // Simple hash from audio samples
                    return ComputeAudioHash(samples);
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
            catch
            {
                return 0;
            }
        }

        private ulong ComputeAudioHash(byte[] samples)
        {
            // Convert bytes to 16-bit samples
            var sampleCount = samples.Length / 2;
            var values = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                values[i] = BitConverter.ToInt16(samples, i * 2);
            }

            // Compute hash based on average energy in 64 segments
            ulong hash = 0;
            var segmentSize = Math.Max(1, values.Length / 64);
            var avgEnergy = values.Select(v => (double)v * v).Average();

            for (int i = 0; i < 64; i++)
            {
                var start = i * segmentSize;
                var end = Math.Min(start + segmentSize, values.Length);
                if (start >= values.Length) break;

                var segmentEnergy = 0.0;
                var count = 0;
                for (int j = start; j < end; j++)
                {
                    segmentEnergy += (double)values[j] * values[j];
                    count++;
                }
                segmentEnergy /= count > 0 ? count : 1;

                // Set bit if segment energy is above average
                if (segmentEnergy > avgEnergy)
                {
                    hash |= 1UL << i;
                }
            }

            return hash;
        }

        private string ComputeSimpleHash(string data)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            var hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash)[..16];
        }

        private string? FindFFmpeg()
        {
            // Check common locations
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffmpeg.exe"),
                @"C:\ffmpeg\bin\ffmpeg.exe",
                @"C:\Program Files\ffmpeg\bin\ffmpeg.exe",
                "ffmpeg" // System PATH
            };

            foreach (var path in paths)
            {
                if (path == "ffmpeg" || File.Exists(path))
                {
                    // Verify it works
                    try
                    {
                        var psi = new ProcessStartInfo(path, "-version")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            p.WaitForExit(2000);
                            if (p.ExitCode == 0)
                                return path;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            return null;
        }

        private string? FindFFprobe()
        {
            var paths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffprobe.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "ffprobe.exe"),
                @"C:\ffmpeg\bin\ffprobe.exe",
                @"C:\Program Files\ffmpeg\bin\ffprobe.exe",
                "ffprobe" // System PATH
            };

            foreach (var path in paths)
            {
                if (path == "ffprobe" || File.Exists(path))
                {
                    try
                    {
                        var psi = new ProcessStartInfo(path, "-version")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        using var p = Process.Start(psi);
                        if (p != null)
                        {
                            p.WaitForExit(2000);
                            if (p.ExitCode == 0)
                                return path;
                        }
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            return null;
        }
    }
}
