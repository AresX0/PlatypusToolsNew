using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// A record of a media file being played/watched.
    /// </summary>
    public class PlaybackHistoryEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public MediaType MediaType { get; set; }
        public double DurationSeconds { get; set; }
        public double LastPositionSeconds { get; set; }
        public double ProgressPercent => DurationSeconds > 0 ? (LastPositionSeconds / DurationSeconds) * 100 : 0;
        public bool IsComplete => ProgressPercent >= 90; // Consider complete at 90%
        public DateTime LastPlayedAt { get; set; } = DateTime.UtcNow;
        public DateTime FirstPlayedAt { get; set; } = DateTime.UtcNow;
        public int PlayCount { get; set; } = 1;

        // Series info for TV shows
        public string? SeriesName { get; set; }
        public int? SeasonNumber { get; set; }
        public int? EpisodeNumber { get; set; }
    }

    /// <summary>
    /// Manages watch/listen history and resume positions with JSON persistence.
    /// </summary>
    public class PlaybackHistoryService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _storagePath;
        private List<PlaybackHistoryEntry> _history = new();
        private bool _loaded;

        // Keep a maximum of 5000 history entries
        private const int MaxHistoryEntries = 5000;

        public PlaybackHistoryService()
        {
            _storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "playback_history.json");
        }

        /// <summary>
        /// Load history from disk.
        /// </summary>
        public async Task LoadAsync()
        {
            if (_loaded) return;

            try
            {
                if (File.Exists(_storagePath))
                {
                    var json = await File.ReadAllTextAsync(_storagePath);
                    _history = JsonSerializer.Deserialize<List<PlaybackHistoryEntry>>(json, JsonOptions) ?? new();
                }
            }
            catch
            {
                _history = new();
            }

            _loaded = true;
        }

        /// <summary>
        /// Save history to disk.
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_history, JsonOptions);
                await File.WriteAllTextAsync(_storagePath, json);
            }
            catch { }
        }

        /// <summary>
        /// Record playback of a media file (or update existing entry).
        /// </summary>
        public async Task RecordPlaybackAsync(string filePath, string title, string artist, string album,
            MediaType mediaType, double durationSeconds, double positionSeconds,
            string? seriesName = null, int? seasonNumber = null, int? episodeNumber = null)
        {
            await LoadAsync();

            var existing = _history.FirstOrDefault(h =>
                string.Equals(h.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                existing.LastPositionSeconds = positionSeconds;
                existing.LastPlayedAt = DateTime.UtcNow;
                existing.PlayCount++;
                // Update metadata if provided
                if (!string.IsNullOrEmpty(title)) existing.Title = title;
                if (!string.IsNullOrEmpty(artist)) existing.Artist = artist;
                if (!string.IsNullOrEmpty(album)) existing.Album = album;
                if (durationSeconds > 0) existing.DurationSeconds = durationSeconds;
                if (seriesName != null) existing.SeriesName = seriesName;
                if (seasonNumber.HasValue) existing.SeasonNumber = seasonNumber;
                if (episodeNumber.HasValue) existing.EpisodeNumber = episodeNumber;
            }
            else
            {
                _history.Add(new PlaybackHistoryEntry
                {
                    FilePath = filePath,
                    Title = title,
                    Artist = artist,
                    Album = album,
                    MediaType = mediaType,
                    DurationSeconds = durationSeconds,
                    LastPositionSeconds = positionSeconds,
                    SeriesName = seriesName,
                    SeasonNumber = seasonNumber,
                    EpisodeNumber = episodeNumber
                });
            }

            // Trim old entries if over limit
            if (_history.Count > MaxHistoryEntries)
            {
                _history = _history
                    .OrderByDescending(h => h.LastPlayedAt)
                    .Take(MaxHistoryEntries)
                    .ToList();
            }

            await SaveAsync();
        }

        /// <summary>
        /// Update the resume position for a file.
        /// </summary>
        public async Task UpdatePositionAsync(string filePath, double positionSeconds)
        {
            await LoadAsync();

            var entry = _history.FirstOrDefault(h =>
                string.Equals(h.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                entry.LastPositionSeconds = positionSeconds;
                entry.LastPlayedAt = DateTime.UtcNow;
                await SaveAsync();
            }
        }

        /// <summary>
        /// Get resume position for a file (returns 0 if not found or already complete).
        /// </summary>
        public async Task<double> GetResumePositionAsync(string filePath)
        {
            await LoadAsync();

            var entry = _history.FirstOrDefault(h =>
                string.Equals(h.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (entry == null || entry.IsComplete)
                return 0;

            return entry.LastPositionSeconds;
        }

        /// <summary>
        /// Get recent playback history.
        /// </summary>
        public async Task<List<PlaybackHistoryEntry>> GetRecentAsync(int count = 50, MediaType? typeFilter = null)
        {
            await LoadAsync();

            var query = _history.AsEnumerable();
            if (typeFilter.HasValue)
                query = query.Where(h => h.MediaType == typeFilter.Value);

            return query
                .OrderByDescending(h => h.LastPlayedAt)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get items currently in progress (started but not complete).
        /// </summary>
        public async Task<List<PlaybackHistoryEntry>> GetInProgressAsync(MediaType? typeFilter = null)
        {
            await LoadAsync();

            var query = _history.AsEnumerable();
            if (typeFilter.HasValue)
                query = query.Where(h => h.MediaType == typeFilter.Value);

            return query
                .Where(h => h.LastPositionSeconds > 10 && !h.IsComplete) // Started but not done
                .OrderByDescending(h => h.LastPlayedAt)
                .ToList();
        }

        /// <summary>
        /// Get most played items.
        /// </summary>
        public async Task<List<PlaybackHistoryEntry>> GetMostPlayedAsync(int count = 50, MediaType? typeFilter = null)
        {
            await LoadAsync();

            var query = _history.AsEnumerable();
            if (typeFilter.HasValue)
                query = query.Where(h => h.MediaType == typeFilter.Value);

            return query
                .OrderByDescending(h => h.PlayCount)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Get history for a specific series (TV shows).
        /// </summary>
        public async Task<List<PlaybackHistoryEntry>> GetSeriesHistoryAsync(string seriesName)
        {
            await LoadAsync();

            return _history
                .Where(h => string.Equals(h.SeriesName, seriesName, StringComparison.OrdinalIgnoreCase))
                .OrderBy(h => h.SeasonNumber)
                .ThenBy(h => h.EpisodeNumber)
                .ToList();
        }

        /// <summary>
        /// Get the next unwatched episode for a series.
        /// </summary>
        public async Task<PlaybackHistoryEntry?> GetNextUnwatchedEpisodeAsync(string seriesName)
        {
            var seriesHistory = await GetSeriesHistoryAsync(seriesName);
            return seriesHistory.FirstOrDefault(h => !h.IsComplete);
        }

        /// <summary>
        /// Clear all history.
        /// </summary>
        public async Task ClearAsync()
        {
            _history.Clear();
            await SaveAsync();
        }

        /// <summary>
        /// Remove history for a specific file.
        /// </summary>
        public async Task RemoveAsync(string filePath)
        {
            await LoadAsync();
            _history.RemoveAll(h =>
                string.Equals(h.FilePath, filePath, StringComparison.OrdinalIgnoreCase));
            await SaveAsync();
        }
    }
}
