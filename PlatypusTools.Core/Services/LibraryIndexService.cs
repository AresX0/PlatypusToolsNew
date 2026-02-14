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
        private bool _validateHashOnLoad = true;

        /// <summary>
        /// Gets or sets whether to validate the content hash when loading the index.
        /// When true, the index is verified against its stored SHA256 hash.
        /// </summary>
        public bool ValidateHashOnLoad
        {
            get => _validateHashOnLoad;
            set => _validateHashOnLoad = value;
        }

        /// <summary>
        /// Gets whether the last load passed hash validation (null if validation was skipped).
        /// </summary>
        public bool? LastLoadHashValid { get; private set; }

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
                        
                        // Hash validation (NEW-007)
                        if (_validateHashOnLoad && !string.IsNullOrEmpty(_currentIndex.ContentHash))
                        {
                            try
                            {
                                // Temporarily clear hash to compute what the hash should be
                                var storedHash = _currentIndex.ContentHash;
                                _currentIndex.ContentHash = null!;
                                var verifyJson = JsonSerializer.Serialize(_currentIndex, _jsonOptions);
                                using var sha = SHA256.Create();
                                var computedHash = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(verifyJson)));
                                _currentIndex.ContentHash = storedHash;
                                
                                LastLoadHashValid = string.Equals(storedHash, computedHash, StringComparison.OrdinalIgnoreCase);
                                if (LastLoadHashValid == false)
                                {
                                    System.Diagnostics.Debug.WriteLine($"WARNING: Library index hash mismatch! Stored={storedHash}, Computed={computedHash}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine("Library index hash validation passed.");
                                }
                            }
                            catch (Exception hashEx)
                            {
                                System.Diagnostics.Debug.WriteLine($"Hash validation error: {hashEx.Message}");
                                LastLoadHashValid = null;
                            }
                        }
                        else
                        {
                            LastLoadHashValid = null;
                        }
                        
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
        /// <param name="onProgressChanged">Callback for progress updates: (filesScanned, totalEstimate, currentFile).</param>
        /// <param name="onBatchProcessed">Callback when a batch of tracks is processed (for UI updates).</param>
        /// <param name="onEtaUpdated">Callback for ETA updates: (estimatedSecondsRemaining, filesPerSecond).</param>
        public async Task<LibraryIndex> ScanAndIndexDirectoryAsync(
            string directory,
            bool recursive = true,
            Action<int, int, string>? onProgressChanged = null,
            Action<List<Track>>? onBatchProcessed = null,
            Action<double, double>? onEtaUpdated = null)
        {
            if (!Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Directory not found: {directory}");

            // Ensure index is loaded
            if (_currentIndex == null)
                await LoadOrCreateIndexAsync();

            var scannedCount = 0;
            var totalFiles = 0;
            var batchCount = 0;
            var scanStartTime = DateTime.UtcNow;
            var newFilesProcessed = 0;

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
                        newFilesProcessed++;
                    }

                    if (scannedCount % 25 == 0)
                    {
                        onProgressChanged?.Invoke(scannedCount, totalFiles, $"Scanned {scannedCount}: {Path.GetFileName(filePath)}");
                        
                        // ETA calculation (NEW-008)
                        if (onEtaUpdated != null && newFilesProcessed > 5)
                        {
                            var elapsed = (DateTime.UtcNow - scanStartTime).TotalSeconds;
                            if (elapsed > 1)
                            {
                                var filesPerSecond = scannedCount / elapsed;
                                // Estimate remaining based on what we've seen so far
                                // Since we're using streaming enumeration, totalFiles grows as we discover
                                var estimatedRemaining = (totalFiles - scannedCount) / Math.Max(filesPerSecond, 0.1);
                                onEtaUpdated(estimatedRemaining, filesPerSecond);
                            }
                        }
                    }
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
        /// Supports advanced field-specific syntax: artist:X album:Y year:2020 genre:Rock title:Z
        /// If no field prefixes are found, searches across all fields.
        /// Multiple field filters are AND-combined.
        /// </summary>
        public List<Track> Search(string query, SearchType searchType = SearchType.All)
        {
            if (_currentIndex == null || string.IsNullOrWhiteSpace(query))
                return new List<Track>();

            // Check for advanced field-specific syntax
            var fieldFilters = ParseFieldQuery(query);
            if (fieldFilters != null && fieldFilters.Count > 0)
            {
                return SearchWithFields(fieldFilters);
            }

            // Simple search fallback
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
                                      track.DisplayAlbum.ToLowerInvariant().Contains(lower) ||
                                      (!string.IsNullOrEmpty(track.Genre) && track.Genre.ToLowerInvariant().Contains(lower)) ||
                                      track.FilePath.ToLowerInvariant().Contains(lower),
                    _ => false,
                };

                if (match)
                    results.Add(track);
            }

            return results;
        }

        /// <summary>
        /// Parses a query string for field-specific search syntax.
        /// Recognizes: artist:, album:, title:, genre:, year:, path:
        /// Values can be quoted for phrases: artist:"The Beatles"
        /// Returns null if no field prefixes found (use simple search instead).
        /// </summary>
        private Dictionary<string, string>? ParseFieldQuery(string query)
        {
            var fieldPrefixes = new[] { "artist:", "album:", "title:", "genre:", "year:", "path:" };
            if (!fieldPrefixes.Any(p => query.ToLowerInvariant().Contains(p)))
                return null;

            var filters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var remaining = query;

            foreach (var prefix in fieldPrefixes)
            {
                var idx = remaining.ToLowerInvariant().IndexOf(prefix);
                while (idx >= 0)
                {
                    var valueStart = idx + prefix.Length;
                    string value;
                    int valueEnd;

                    if (valueStart < remaining.Length && remaining[valueStart] == '"')
                    {
                        // Quoted value
                        var closeQuote = remaining.IndexOf('"', valueStart + 1);
                        if (closeQuote > 0)
                        {
                            value = remaining.Substring(valueStart + 1, closeQuote - valueStart - 1);
                            valueEnd = closeQuote + 1;
                        }
                        else
                        {
                            value = remaining.Substring(valueStart + 1);
                            valueEnd = remaining.Length;
                        }
                    }
                    else
                    {
                        // Unquoted: take until next space or field prefix
                        var nextSpace = remaining.IndexOf(' ', valueStart);
                        valueEnd = nextSpace > 0 ? nextSpace : remaining.Length;
                        value = remaining.Substring(valueStart, valueEnd - valueStart);
                    }

                    var fieldName = prefix.TrimEnd(':');
                    filters[fieldName] = value.Trim();

                    // Remove this filter from remaining string
                    remaining = remaining.Remove(idx, valueEnd - idx).Trim();
                    idx = remaining.ToLowerInvariant().IndexOf(prefix);
                }
            }

            // If there's remaining text after extracting fields, add as "all" filter
            if (!string.IsNullOrWhiteSpace(remaining))
            {
                filters["_freetext"] = remaining.Trim();
            }

            return filters.Count > 0 ? filters : null;
        }

        /// <summary>
        /// Search with field-specific filters (AND logic).
        /// </summary>
        private List<Track> SearchWithFields(Dictionary<string, string> filters)
        {
            if (_currentIndex == null) return new List<Track>();

            var results = new List<Track>();

            foreach (var track in _currentIndex.Tracks)
            {
                var matchAll = true;

                foreach (var filter in filters)
                {
                    var lower = filter.Value.ToLowerInvariant();
                    var match = filter.Key.ToLowerInvariant() switch
                    {
                        "artist" => track.DisplayArtist.ToLowerInvariant().Contains(lower),
                        "album" => track.DisplayAlbum.ToLowerInvariant().Contains(lower),
                        "title" => track.DisplayTitle.ToLowerInvariant().Contains(lower),
                        "genre" => !string.IsNullOrEmpty(track.Genre) && track.Genre.ToLowerInvariant().Contains(lower),
                        "year" => track.Year.HasValue && track.Year.Value.ToString() == filter.Value,
                        "path" => track.FilePath.ToLowerInvariant().Contains(lower),
                        "_freetext" => track.DisplayTitle.ToLowerInvariant().Contains(lower) ||
                                       track.DisplayArtist.ToLowerInvariant().Contains(lower) ||
                                       track.DisplayAlbum.ToLowerInvariant().Contains(lower),
                        _ => true
                    };

                    if (!match)
                    {
                        matchAll = false;
                        break;
                    }
                }

                if (matchAll)
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
        public LibraryIndex? GetCurrentIndex() => _currentIndex;

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

                stats.AverageBitrate = (int)bitratesWithValues.Average(t => t.Bitrate ?? 0);
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
