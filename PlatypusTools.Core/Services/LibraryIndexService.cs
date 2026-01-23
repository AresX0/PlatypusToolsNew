using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for managing persistent audio library index with JSON storage.
    /// Handles scanning, indexing, deduplication, and atomic writes.
    /// </summary>
    public class LibraryIndexService
    {
        private readonly string _indexFilePath;
        private readonly JsonSerializerOptions _jsonOptions;
        private LibraryIndex? _currentIndex;

        public LibraryIndexService(string? indexFilePath = null)
        {
            _indexFilePath = indexFilePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "audio_library_index.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            };
        }

        /// <summary>
        /// Load existing index or create new one.
        /// </summary>
        public async Task<LibraryIndex> LoadOrCreateIndexAsync()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"LoadOrCreateIndexAsync: Checking for index at {_indexFilePath}");
                System.Diagnostics.Debug.WriteLine($"LoadOrCreateIndexAsync: File exists = {File.Exists(_indexFilePath)}");
                
                if (File.Exists(_indexFilePath))
                {
                    var json = await File.ReadAllTextAsync(_indexFilePath);
                    System.Diagnostics.Debug.WriteLine($"LoadOrCreateIndexAsync: Read {json.Length} chars from file");
                    _currentIndex = JsonSerializer.Deserialize<LibraryIndex>(json, _jsonOptions);

                    if (_currentIndex != null)
                    {
                        System.Diagnostics.Debug.WriteLine($"LoadOrCreateIndexAsync: Deserialized index with {_currentIndex.Tracks?.Count ?? 0} tracks");
                        _currentIndex.RebuildIndices();
                        return _currentIndex;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading index: {ex.Message}");
                // Try to restore from backup
                if (AtomicFileWriter.BackupExists(_indexFilePath))
                    AtomicFileWriter.RestoreFromBackup(_indexFilePath);
            }

            // Create fresh index
            _currentIndex = new LibraryIndex
            {
                Version = "1.0.0",
                CreatedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
                SourceMetadata = new LibrarySourceMetadata(),
                Statistics = new LibraryStatistics(),
            };

            return _currentIndex;
        }

        /// <summary>
        /// Batch size for processing files - update UI and save after this many files.
        /// </summary>
        private const int BatchSize = 300;

        /// <summary>
        /// Scan a directory for audio files and update index.
        /// Optimized for large directories (50000+ files) with deep subdirectories.
        /// Processes files in batches and saves periodically to prevent timeout.
        /// </summary>
        /// <param name="directory">Directory to scan.</param>
        /// <param name="recursive">Whether to scan subdirectories.</param>
        /// <param name="onProgressChanged">Callback for progress updates.</param>
        /// <param name="onBatchProcessed">Callback when a batch of tracks is processed (for UI updates).</param>
        public async Task<LibraryIndex> ScanAndIndexDirectoryAsync(
            string directory,
            bool recursive = true,
            Action<int, int, string>? onProgressChanged = null,
            Action<List<Track>>? onBatchProcessed = null)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Directory not found: {directory}");

            // Ensure index is loaded
            if (_currentIndex == null)
                await LoadOrCreateIndexAsync();

            var scannedCount = 0;
            var totalFiles = 0;
            var batchCount = 0;

            // Build existing paths set once
            var existingPaths = new HashSet<string>(
                _currentIndex?.Tracks?.Select(t => t.FilePath) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            // Use a robust directory enumerator that handles deep structures
            var audioFiles = EnumerateAudioFilesRobust(directory, recursive);
            var batchTracks = new List<Track>();

            // Process files using streaming enumeration (memory efficient for large dirs)
            foreach (var filePath in audioFiles)
            {
                totalFiles++;
                scannedCount++;

                try
                {
                    var canonicalPath = PathCanonicalizer.Canonicalize(filePath);

                    // Skip if already in index
                    if (existingPaths.Contains(canonicalPath))
                    {
                        if (scannedCount % 100 == 0)
                            onProgressChanged?.Invoke(scannedCount, totalFiles, $"Skipped (cached): {Path.GetFileName(filePath)}");
                        continue;
                    }

                    // Extract metadata
                    var track = await MetadataExtractorService.ExtractMetadataAsync(canonicalPath);
                    if (track != null)
                    {
                        batchTracks.Add(track);
                        existingPaths.Add(canonicalPath);
                    }

                    if (scannedCount % 25 == 0)
                        onProgressChanged?.Invoke(scannedCount, totalFiles, $"Scanned {scannedCount}: {Path.GetFileName(filePath)}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error processing {filePath}: {ex.Message}");
                }

                // Process in batches to avoid memory buildup
                if (batchTracks.Count >= BatchSize)
                {
                    batchCount++;
                    
                    // Add to index
                    _currentIndex?.Tracks?.AddRange(batchTracks);
                    onBatchProcessed?.Invoke(new List<Track>(batchTracks));

                    // Save periodically (every batch)
                    await SaveIndexAsync();
                    batchTracks.Clear();

                    // Yield to UI thread
                    await Task.Delay(1);
                }

                // Yield periodically within batch
                if (scannedCount % 10 == 0)
                    await Task.Delay(1);
            }

            // Process remaining files
            if (batchTracks.Count > 0)
            {
                _currentIndex?.Tracks?.AddRange(batchTracks);
                onBatchProcessed?.Invoke(new List<Track>(batchTracks));
            }

            // Update metadata
            var canonDir = PathCanonicalizer.Canonicalize(directory);
            if (_currentIndex?.SourceMetadata != null && !_currentIndex.SourceMetadata.SourceDirs.Contains(canonDir, StringComparer.OrdinalIgnoreCase))
                _currentIndex.SourceMetadata.SourceDirs.Add(canonDir);
            if (_currentIndex?.SourceMetadata != null)
            {
                _currentIndex.SourceMetadata.LastIncrementalScanAt = DateTime.UtcNow;
                if (_currentIndex.Tracks.Count > 100)
                    _currentIndex.SourceMetadata.LastFullRescanAt = DateTime.UtcNow;
            }

            // Recalculate statistics
            RecalculateStatistics();

            // Final save to disk
            await SaveIndexAsync();

            return _currentIndex!;
        }

        /// <summary>
        /// Enumerate audio files robustly, handling access denied and deep directories.
        /// </summary>
        private IEnumerable<string> EnumerateAudioFilesRobust(string directory, bool recursive)
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".flac", ".ogg", ".m4a", ".aac", ".wma", ".wav", ".opus", ".ape"
            };

            var dirsToProcess = new Queue<string>();
            dirsToProcess.Enqueue(directory);

            while (dirsToProcess.Count > 0)
            {
                var currentDir = dirsToProcess.Dequeue();

                // Enumerate files in current directory
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(currentDir);
                }
                catch (UnauthorizedAccessException)
                {
                    System.Diagnostics.Debug.WriteLine($"Access denied: {currentDir}");
                    continue;
                }
                catch (PathTooLongException)
                {
                    System.Diagnostics.Debug.WriteLine($"Path too long: {currentDir}");
                    continue;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error enumerating {currentDir}: {ex.Message}");
                    continue;
                }

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file);
                    if (extensions.Contains(ext))
                        yield return file;
                }

                // Queue subdirectories if recursive
                if (recursive)
                {
                    IEnumerable<string> subdirs;
                    try
                    {
                        subdirs = Directory.EnumerateDirectories(currentDir);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var subdir in subdirs)
                    {
                        dirsToProcess.Enqueue(subdir);
                    }
                }
            }
        }

        /// <summary>
        /// Remove missing files from index.
        /// </summary>
        public async Task<int> RemoveMissingFilesAsync(Action<int, int, string>? onProgressChanged = null)
        {
            if (_currentIndex == null)
                return 0;

            var missingTracks = new List<Track>();
            var totalTracks = _currentIndex.Tracks.Count;
            var checkedCount = 0;

            foreach (var track in _currentIndex.Tracks)
            {
                if (!File.Exists(track.FilePath))
                    missingTracks.Add(track);

                checkedCount++;
                onProgressChanged?.Invoke(checkedCount, totalTracks, $"Checking {Path.GetFileName(track.FilePath)}...");

                if (checkedCount % 10 == 0)
                    await Task.Delay(1);
            }

            foreach (var track in missingTracks)
                _currentIndex.Tracks.Remove(track);

            if (missingTracks.Count > 0)
            {
                RecalculateStatistics();
                await SaveIndexAsync();
            }

            return missingTracks.Count;
        }

        /// <summary>
        /// Search for tracks matching criteria.
        /// </summary>
        public List<Track> Search(string query, SearchType searchType = SearchType.All)
        {
            if (_currentIndex == null || string.IsNullOrWhiteSpace(query))
                return new List<Track>();

            var lower = query.ToLowerInvariant();
            var results = new List<Track>();

            foreach (var track in _currentIndex.Tracks)
            {
                var match = searchType switch
                {
                    SearchType.Title => track.DisplayTitle.ToLowerInvariant().Contains(lower),
                    SearchType.Artist => track.DisplayArtist.ToLowerInvariant().Contains(lower),
                    SearchType.Album => track.DisplayAlbum.ToLowerInvariant().Contains(lower),
                    SearchType.All => track.DisplayTitle.ToLowerInvariant().Contains(lower) ||
                                      track.DisplayArtist.ToLowerInvariant().Contains(lower) ||
                                      track.DisplayAlbum.ToLowerInvariant().Contains(lower),
                    _ => false,
                };

                if (match)
                    results.Add(track);
            }

            return results;
        }

        /// <summary>
        /// Get all unique artists.
        /// </summary>
        public List<string> GetAllArtists()
        {
            if (_currentIndex == null)
                return new List<string>();

            return _currentIndex.Tracks
                .Select(t => t.DisplayArtist)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a)
                .ToList();
        }

        /// <summary>
        /// Get all unique albums.
        /// </summary>
        public List<string> GetAllAlbums()
        {
            if (_currentIndex == null)
                return new List<string>();

            return _currentIndex.Tracks
                .Select(t => t.DisplayAlbum)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(a => a)
                .ToList();
        }

        /// <summary>
        /// Get current index.
        /// </summary>
        public LibraryIndex GetCurrentIndex() => _currentIndex;

        /// <summary>
        /// Save index to disk atomically.
        /// </summary>
        public async Task<bool> SaveIndexAsync()
        {
            if (_currentIndex == null)
                return false;

            try
            {
                // Update metadata
                _currentIndex.LastUpdatedAt = DateTime.UtcNow;
                _currentIndex.TrackCount = _currentIndex.Tracks.Count;
                _currentIndex.TotalSize = _currentIndex.Tracks.Sum(t => t.FileSize);

                // Calculate content hash
                var json = JsonSerializer.Serialize(_currentIndex, _jsonOptions);
                using (var sha = SHA256.Create())
                {
                    var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(json));
                    _currentIndex.ContentHash = Convert.ToHexString(hash);
                }

                // Save atomically
                var success = await AtomicFileWriter.WriteTextAtomicAsync(
                    _indexFilePath,
                    JsonSerializer.Serialize(_currentIndex, _jsonOptions),
                    Encoding.UTF8,
                    keepBackup: true);

                return success;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving index: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Recalculate library statistics.
        /// </summary>
        private void RecalculateStatistics()
        {
            if (_currentIndex == null)
                return;

            var stats = new LibraryStatistics
            {
                ArtistCount = _currentIndex.Tracks.Select(t => t.DisplayArtist).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                AlbumCount = _currentIndex.Tracks.Select(t => t.DisplayAlbum).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                GenreCount = _currentIndex.Tracks.Where(t => !string.IsNullOrWhiteSpace(t.Genre))
                    .Select(t => t.Genre).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                TotalDurationMs = _currentIndex.Tracks.Sum(t => t.DurationMs),
                TracksWithArtwork = _currentIndex.Tracks.Count(t => t.HasArtwork),
                TracksWithCompleteMetadata = _currentIndex.Tracks.Count(t =>
                    !string.IsNullOrWhiteSpace(t.Title) &&
                    !string.IsNullOrWhiteSpace(t.Artist) &&
                    !string.IsNullOrWhiteSpace(t.Album) &&
                    t.DurationMs > 0),
            };

            // Calculate codec distribution
            stats.CodecDistribution = _currentIndex.Tracks
                .Where(t => !string.IsNullOrWhiteSpace(t.Codec))
                .GroupBy(t => t.Codec)
                .ToDictionary(g => g.Key, g => g.Count());

            // Calculate bitrate stats
            var bitratesWithValues = _currentIndex.Tracks.Where(t => t.Bitrate.HasValue && t.Bitrate.Value > 0).ToList();
            if (bitratesWithValues.Any())
            {
                stats.MostCommonBitrate = bitratesWithValues
                    .GroupBy(t => t.Bitrate)
                    .OrderByDescending(g => g.Count())
                    .FirstOrDefault()?.Key;

                stats.AverageBitrate = (int)bitratesWithValues.Average(t => t.Bitrate.Value);
            }

            _currentIndex.Statistics = stats;
        }

        /// <summary>
        /// Clear all data and start fresh.
        /// </summary>
        public async Task<bool> ClearAsync()
        {
            _currentIndex = new LibraryIndex
            {
                Version = "1.0.0",
                CreatedAt = DateTime.UtcNow,
                LastUpdatedAt = DateTime.UtcNow,
            };

            return await SaveIndexAsync();
        }
    }

    /// <summary>
    /// Search type for library queries.
    /// </summary>
    public enum SearchType
    {
        All,
        Title,
        Artist,
        Album,
    }
}
