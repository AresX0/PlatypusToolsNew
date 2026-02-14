using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Audio
{
    /// <summary>
    /// Versioned library index container for safe updates.
    /// </summary>
    public class LibraryIndex
    {
        /// <summary>
        /// Version of the index format (semantic versioning).
        /// Increment when schema changes incompatibly.
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0.0";

        /// <summary>
        /// Timestamp when index was created (UTC).
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Timestamp when index was last updated (UTC).
        /// </summary>
        [JsonPropertyName("lastUpdatedAt")]
        public DateTime LastUpdatedAt { get; set; }

        /// <summary>
        /// SHA256 hash of the index content (for integrity check).
        /// </summary>
        [JsonPropertyName("contentHash")]
        public string ContentHash { get; set; } = string.Empty;

        /// <summary>
        /// Number of tracks in this index.
        /// </summary>
        [JsonPropertyName("trackCount")]
        public int TrackCount { get; set; }

        /// <summary>
        /// Total size of all tracks in bytes.
        /// </summary>
        [JsonPropertyName("totalSize")]
        public long TotalSize { get; set; }

        /// <summary>
        /// All indexed audio tracks.
        /// </summary>
        [JsonPropertyName("tracks")]
        public List<Track> Tracks { get; set; } = new();

        /// <summary>
        /// Metadata about library source(s).
        /// </summary>
        [JsonPropertyName("sourceMetadata")]
        public LibrarySourceMetadata SourceMetadata { get; set; } = new();

        /// <summary>
        /// Statistics about the library.
        /// </summary>
        [JsonPropertyName("statistics")]
        public LibraryStatistics Statistics { get; set; } = new();

        /// <summary>
        /// Index of tracks by artist for quick lookup.
        /// </summary>
        [JsonIgnore]
        private Dictionary<string, List<Track>> _artistIndex = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Index of tracks by album for quick lookup.
        /// </summary>
        [JsonIgnore]
        private Dictionary<string, List<Track>> _albumIndex = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Rebuild internal indices for quick lookups.
        /// Call after loading or modifying tracks.
        /// </summary>
        public void RebuildIndices()
        {
            _artistIndex = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);
            _albumIndex = new Dictionary<string, List<Track>>(StringComparer.OrdinalIgnoreCase);

            foreach (var track in Tracks)
            {
                var artist = track.DisplayArtist;
                var album = track.DisplayAlbum;

                if (!_artistIndex.ContainsKey(artist))
                    _artistIndex[artist] = new List<Track>();
                _artistIndex[artist].Add(track);

                if (!_albumIndex.ContainsKey(album))
                    _albumIndex[album] = new List<Track>();
                _albumIndex[album].Add(track);
            }
        }

        /// <summary>
        /// Get all tracks by artist.
        /// </summary>
        public List<Track> GetTracksByArtist(string artist)
        {
            if (_artistIndex == null)
                RebuildIndices();

            return _artistIndex?.ContainsKey(artist) == true
                ? _artistIndex[artist]
                : new List<Track>();
        }

        /// <summary>
        /// Get all tracks by album.
        /// </summary>
        public List<Track> GetTracksByAlbum(string album)
        {
            if (_albumIndex == null)
                RebuildIndices();

            return _albumIndex?.ContainsKey(album) == true
                ? _albumIndex[album]
                : new List<Track>();
        }

        /// <summary>
        /// Find track by file path.
        /// </summary>
        public Track? FindTrackByPath(string filePath)
        {
            var normalizedPath = filePath.ToLowerInvariant();
            return Tracks.Find(t => t.FilePath.ToLowerInvariant() == normalizedPath);
        }

        /// <summary>
        /// Search tracks by title (case-insensitive partial match).
        /// </summary>
        public List<Track> SearchByTitle(string query)
        {
            var lower = query.ToLowerInvariant();
            return Tracks.FindAll(t => t.DisplayTitle.ToLowerInvariant().Contains(lower));
        }
    }

    /// <summary>
    /// Metadata about library source directories.
    /// </summary>
    public class LibrarySourceMetadata
    {
        /// <summary>
        /// Scanned source directories.
        /// </summary>
        [JsonPropertyName("sourceDirs")]
        public List<string> SourceDirs { get; set; } = new();

        /// <summary>
        /// File extensions scanned (e.g., ".mp3", ".flac").
        /// </summary>
        [JsonPropertyName("fileExtensions")]
        public List<string> FileExtensions { get; set; } = new();

        /// <summary>
        /// Timestamp of last full rescan (UTC).
        /// </summary>
        [JsonPropertyName("lastFullRescanAt")]
        public DateTime? LastFullRescanAt { get; set; }

        /// <summary>
        /// Timestamp of last incremental scan (UTC).
        /// </summary>
        [JsonPropertyName("lastIncrementalScanAt")]
        public DateTime? LastIncrementalScanAt { get; set; }

        /// <summary>
        /// Whether incremental scanning is enabled.
        /// </summary>
        [JsonPropertyName("incrementalScanEnabled")]
        public bool IncrementalScanEnabled { get; set; } = true;
    }

    /// <summary>
    /// Statistics about the library.
    /// </summary>
    public class LibraryStatistics
    {
        /// <summary>
        /// Total number of artists.
        /// </summary>
        [JsonPropertyName("artistCount")]
        public int ArtistCount { get; set; }

        /// <summary>
        /// Total number of albums.
        /// </summary>
        [JsonPropertyName("albumCount")]
        public int AlbumCount { get; set; }

        /// <summary>
        /// Total number of genres.
        /// </summary>
        [JsonPropertyName("genreCount")]
        public int GenreCount { get; set; }

        /// <summary>
        /// Total duration in milliseconds.
        /// </summary>
        [JsonPropertyName("totalDurationMs")]
        public long TotalDurationMs { get; set; }

        /// <summary>
        /// Number of tracks with artwork.
        /// </summary>
        [JsonPropertyName("tracksWithArtwork")]
        public int TracksWithArtwork { get; set; }

        /// <summary>
        /// Number of tracks with complete metadata.
        /// </summary>
        [JsonPropertyName("tracksWithCompleteMetadata")]
        public int TracksWithCompleteMetadata { get; set; }

        /// <summary>
        /// Distribution by codec.
        /// </summary>
        [JsonPropertyName("codecDistribution")]
        public Dictionary<string, int> CodecDistribution { get; set; } = new();

        /// <summary>
        /// Most common bit rate.
        /// </summary>
        [JsonPropertyName("mostCommonBitrate")]
        public int? MostCommonBitrate { get; set; }

        /// <summary>
        /// Average bit rate across library.
        /// </summary>
        [JsonPropertyName("averageBitrate")]
        public int? AverageBitrate { get; set; }
    }
}
