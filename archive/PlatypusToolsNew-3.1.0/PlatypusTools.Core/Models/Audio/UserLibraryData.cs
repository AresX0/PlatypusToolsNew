using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Audio;

/// <summary>
/// User-specific library data including playlists, favorites, and custom metadata.
/// Persisted separately from the main library index for faster updates.
/// </summary>
public class UserLibraryData
{
    /// <summary>
    /// Version of the data format.
    /// </summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>
    /// When the data was created.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the data was last updated.
    /// </summary>
    [JsonPropertyName("lastUpdatedAt")]
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// File paths of favorited tracks.
    /// </summary>
    [JsonPropertyName("favorites")]
    public HashSet<string> Favorites { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// User-created playlists.
    /// </summary>
    [JsonPropertyName("playlists")]
    public List<Playlist> Playlists { get; set; } = new();

    /// <summary>
    /// Per-track user ratings (file path -> 0-5 stars).
    /// </summary>
    [JsonPropertyName("ratings")]
    public Dictionary<string, int> Ratings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-track play counts (file path -> count).
    /// </summary>
    [JsonPropertyName("playCounts")]
    public Dictionary<string, int> PlayCounts { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-track last played dates (file path -> datetime).
    /// </summary>
    [JsonPropertyName("lastPlayed")]
    public Dictionary<string, DateTime> LastPlayed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Library folders that should be monitored.
    /// </summary>
    [JsonPropertyName("libraryFolders")]
    public List<string> LibraryFolders { get; set; } = new();

    /// <summary>
    /// Check if a track is favorited.
    /// </summary>
    public bool IsFavorite(string filePath) => Favorites.Contains(filePath);

    /// <summary>
    /// Toggle favorite status for a track.
    /// </summary>
    public bool ToggleFavorite(string filePath)
    {
        if (Favorites.Contains(filePath))
        {
            Favorites.Remove(filePath);
            return false;
        }
        else
        {
            Favorites.Add(filePath);
            return true;
        }
    }

    /// <summary>
    /// Set favorite status for a track.
    /// </summary>
    public void SetFavorite(string filePath, bool isFavorite)
    {
        if (isFavorite)
            Favorites.Add(filePath);
        else
            Favorites.Remove(filePath);
    }

    /// <summary>
    /// Get or create a playlist by name.
    /// </summary>
    public Playlist GetOrCreatePlaylist(string name)
    {
        var playlist = Playlists.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (playlist == null)
        {
            playlist = new Playlist { Name = name };
            Playlists.Add(playlist);
        }
        return playlist;
    }

    /// <summary>
    /// Delete a playlist by name.
    /// </summary>
    public bool DeletePlaylist(string name)
    {
        var playlist = Playlists.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (playlist != null)
        {
            Playlists.Remove(playlist);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Record a track play.
    /// </summary>
    public void RecordPlay(string filePath)
    {
        if (!PlayCounts.ContainsKey(filePath))
            PlayCounts[filePath] = 0;
        PlayCounts[filePath]++;
        LastPlayed[filePath] = DateTime.UtcNow;
    }

    /// <summary>
    /// Get play count for a track.
    /// </summary>
    public int GetPlayCount(string filePath) => 
        PlayCounts.TryGetValue(filePath, out var count) ? count : 0;

    /// <summary>
    /// Get last played date for a track.
    /// </summary>
    public DateTime? GetLastPlayed(string filePath) => 
        LastPlayed.TryGetValue(filePath, out var date) ? date : null;

    /// <summary>
    /// Set rating for a track (0-5).
    /// </summary>
    public void SetRating(string filePath, int rating)
    {
        rating = Math.Clamp(rating, 0, 5);
        if (rating == 0)
            Ratings.Remove(filePath);
        else
            Ratings[filePath] = rating;
    }

    /// <summary>
    /// Get rating for a track.
    /// </summary>
    public int GetRating(string filePath) => 
        Ratings.TryGetValue(filePath, out var rating) ? rating : 0;
}
