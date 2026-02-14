using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Audio
{
    /// <summary>
    /// Represents an audio track with complete metadata.
    /// </summary>
    public class Track
    {
        /// <summary>
        /// Unique identifier for this track (UUID).
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Full file path (after canonicalization).
        /// </summary>
        [JsonPropertyName("filePath")]
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// File size in bytes.
        /// </summary>
        [JsonPropertyName("fileSize")]
        public long FileSize { get; set; }

        /// <summary>
        /// File last modified time (UTC).
        /// </summary>
        [JsonPropertyName("lastModified")]
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Track title.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Artist name.
        /// </summary>
        [JsonPropertyName("artist")]
        public string Artist { get; set; } = string.Empty;

        /// <summary>
        /// Album name.
        /// </summary>
        [JsonPropertyName("album")]
        public string Album { get; set; } = string.Empty;

        /// <summary>
        /// Album artist (for compilation albums).
        /// </summary>
        [JsonPropertyName("albumArtist")]
        public string AlbumArtist { get; set; } = string.Empty;

        /// <summary>
        /// Genre classification.
        /// </summary>
        [JsonPropertyName("genre")]
        public string Genre { get; set; } = string.Empty;

        /// <summary>
        /// Year of release.
        /// </summary>
        [JsonPropertyName("year")]
        public int? Year { get; set; }

        /// <summary>
        /// Track number on the album.
        /// </summary>
        [JsonPropertyName("trackNumber")]
        public int? TrackNumber { get; set; }

        /// <summary>
        /// Total tracks on the album.
        /// </summary>
        [JsonPropertyName("totalTracks")]
        public int? TotalTracks { get; set; }

        /// <summary>
        /// Disc number (for multi-disc albums).
        /// </summary>
        [JsonPropertyName("discNumber")]
        public int? DiscNumber { get; set; }

        /// <summary>
        /// Total duration in milliseconds.
        /// </summary>
        [JsonPropertyName("durationMs")]
        public long DurationMs { get; set; }

        /// <summary>
        /// Audio bitrate in kbps.
        /// </summary>
        [JsonPropertyName("bitrate")]
        public int? Bitrate { get; set; }

        /// <summary>
        /// Sample rate in Hz.
        /// </summary>
        [JsonPropertyName("sampleRate")]
        public int? SampleRate { get; set; }

        /// <summary>
        /// Number of audio channels.
        /// </summary>
        [JsonPropertyName("channels")]
        public int? Channels { get; set; }

        /// <summary>
        /// Audio codec (e.g., MP3, AAC, FLAC).
        /// </summary>
        [JsonPropertyName("codec")]
        public string Codec { get; set; } = string.Empty;

        /// <summary>
        /// Comments or description.
        /// </summary>
        [JsonPropertyName("comments")]
        public string Comments { get; set; } = string.Empty;

        /// <summary>
        /// List of genres (if multiple).
        /// </summary>
        [JsonPropertyName("genres")]
        public List<string> Genres { get; set; } = new();

        /// <summary>
        /// Composer/songwriter.
        /// </summary>
        [JsonPropertyName("composer")]
        public string Composer { get; set; } = string.Empty;

        /// <summary>
        /// Conductor (for classical music).
        /// </summary>
        [JsonPropertyName("conductor")]
        public string Conductor { get; set; } = string.Empty;

        /// <summary>
        /// Lyrics content.
        /// </summary>
        [JsonPropertyName("lyrics")]
        public string Lyrics { get; set; } = string.Empty;

        /// <summary>
        /// Whether artwork/cover image is embedded.
        /// </summary>
        [JsonPropertyName("hasArtwork")]
        public bool HasArtwork { get; set; }

        /// <summary>
        /// Base64-encoded artwork thumbnail (small size for index).
        /// </summary>
        [JsonPropertyName("artworkThumbnail")]
        public string ArtworkThumbnail { get; set; } = string.Empty;

        /// <summary>
        /// ReplayGain album gain in dB.
        /// </summary>
        [JsonPropertyName("replayGainAlbum")]
        public float? ReplayGainAlbum { get; set; }

        /// <summary>
        /// ReplayGain track gain in dB.
        /// </summary>
        [JsonPropertyName("replayGainTrack")]
        public float? ReplayGainTrack { get; set; }

        /// <summary>
        /// Timestamp when metadata was extracted (UTC).
        /// </summary>
        [JsonPropertyName("metadataExtractedAt")]
        public DateTime MetadataExtractedAt { get; set; }

        /// <summary>
        /// Whether this track is marked for deletion.
        /// </summary>
        [JsonPropertyName("isMarkedForDeletion")]
        public bool IsMarkedForDeletion { get; set; }

        /// <summary>
        /// Custom rating (0-5 stars).
        /// </summary>
        [JsonPropertyName("userRating")]
        public int? UserRating { get; set; }

        /// <summary>
        /// Play count.
        /// </summary>
        [JsonPropertyName("playCount")]
        public int PlayCount { get; set; }

        /// <summary>
        /// Last played time (UTC).
        /// </summary>
        [JsonPropertyName("lastPlayed")]
        public DateTime? LastPlayed { get; set; }

        /// <summary>
        /// Display title (fallback to filename if metadata unavailable).
        /// </summary>
        [JsonIgnore]
        public string DisplayTitle => !string.IsNullOrWhiteSpace(Title)
            ? Title
            : System.IO.Path.GetFileNameWithoutExtension(FilePath);

        /// <summary>
        /// Display artist (fallback to "Unknown Artist").
        /// </summary>
        [JsonIgnore]
        public string DisplayArtist => !string.IsNullOrWhiteSpace(Artist) ? Artist : "Unknown Artist";

        /// <summary>
        /// Display album (fallback to "Unknown Album").
        /// </summary>
        [JsonIgnore]
        public string DisplayAlbum => !string.IsNullOrWhiteSpace(Album) ? Album : "Unknown Album";

        /// <summary>
        /// Format duration as MM:SS.
        /// </summary>
        [JsonIgnore]
        public string DurationFormatted
        {
            get
            {
                var totalSeconds = DurationMs / 1000;
                var minutes = totalSeconds / 60;
                var seconds = totalSeconds % 60;
                return $"{minutes}:{seconds:D2}";
            }
        }

        /// <summary>
        /// Get track key for deduplication (path-based).
        /// </summary>
        public string GetDeduplicationKey() => FilePath.ToLowerInvariant();
    }
}
