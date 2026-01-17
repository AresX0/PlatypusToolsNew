using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.Core.Services;

/// <summary>
/// Service for managing user library data (favorites, playlists, ratings).
/// Provides atomic persistence and quick access to user preferences.
/// </summary>
public class UserLibraryService
{
    private readonly string _dataFilePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private UserLibraryData? _data;
    private bool _isDirty = false;

    /// <summary>
    /// Event raised when data changes.
    /// </summary>
    public event EventHandler? DataChanged;

    public UserLibraryService(string? dataFilePath = null)
    {
        _dataFilePath = dataFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools",
            "user_library_data.json");

        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    /// <summary>
    /// Gets the current user library data.
    /// </summary>
    public UserLibraryData Data => _data ?? new UserLibraryData();

    /// <summary>
    /// Load user data from disk or create new.
    /// </summary>
    public async Task<UserLibraryData> LoadAsync()
    {
        try
        {
            if (File.Exists(_dataFilePath))
            {
                var json = await File.ReadAllTextAsync(_dataFilePath);
                _data = JsonSerializer.Deserialize<UserLibraryData>(json, _jsonOptions);
                if (_data != null)
                {
                    System.Diagnostics.Debug.WriteLine($"UserLibraryService: Loaded {_data.Favorites.Count} favorites, {_data.Playlists.Count} playlists");
                    return _data;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading user data: {ex.Message}");
        }

        // Create new data
        _data = new UserLibraryData();
        return _data;
    }

    /// <summary>
    /// Save user data to disk atomically.
    /// </summary>
    public async Task<bool> SaveAsync()
    {
        if (_data == null)
            return false;

        try
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(_dataFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            _data.LastUpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(_data, _jsonOptions);
            
            // Use atomic write if available, otherwise direct write
            if (typeof(AtomicFileWriter).GetMethod("WriteTextAtomicAsync") != null)
            {
                await AtomicFileWriter.WriteTextAtomicAsync(_dataFilePath, json, Encoding.UTF8, true);
            }
            else
            {
                await File.WriteAllTextAsync(_dataFilePath, json, Encoding.UTF8);
            }

            _isDirty = false;
            System.Diagnostics.Debug.WriteLine($"UserLibraryService: Saved {_data.Favorites.Count} favorites, {_data.Playlists.Count} playlists");
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving user data: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Save if data has been modified.
    /// </summary>
    public async Task SaveIfDirtyAsync()
    {
        if (_isDirty)
            await SaveAsync();
    }

    /// <summary>
    /// Check if a track is favorited.
    /// </summary>
    public bool IsFavorite(string filePath)
    {
        return _data?.IsFavorite(filePath) ?? false;
    }

    /// <summary>
    /// Toggle favorite status for a track.
    /// </summary>
    public async Task<bool> ToggleFavoriteAsync(string filePath)
    {
        if (_data == null)
            await LoadAsync();

        var isFav = _data!.ToggleFavorite(filePath);
        _isDirty = true;
        await SaveAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
        return isFav;
    }

    /// <summary>
    /// Set favorite status for a track.
    /// </summary>
    public async Task SetFavoriteAsync(string filePath, bool isFavorite)
    {
        if (_data == null)
            await LoadAsync();

        _data!.SetFavorite(filePath, isFavorite);
        _isDirty = true;
        await SaveAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Get all favorite file paths.
    /// </summary>
    public IReadOnlyCollection<string> GetFavorites()
    {
        return _data?.Favorites ?? new HashSet<string>();
    }

    /// <summary>
    /// Get favorite count.
    /// </summary>
    public int FavoriteCount => _data?.Favorites.Count ?? 0;

    // ===== Playlist Management =====

    /// <summary>
    /// Get all playlists.
    /// </summary>
    public IReadOnlyList<Playlist> GetPlaylists()
    {
        return _data?.Playlists ?? new System.Collections.Generic.List<Playlist>();
    }

    /// <summary>
    /// Create a new playlist.
    /// </summary>
    public async Task<Playlist> CreatePlaylistAsync(string name, string? description = null)
    {
        if (_data == null)
            await LoadAsync();

        var playlist = new Playlist
        {
            Name = name,
            Description = description
        };
        _data!.Playlists.Add(playlist);
        _isDirty = true;
        await SaveAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
        return playlist;
    }

    /// <summary>
    /// Get a playlist by ID.
    /// </summary>
    public Playlist? GetPlaylist(string id)
    {
        return _data?.Playlists.Find(p => p.Id == id);
    }

    /// <summary>
    /// Get a playlist by name.
    /// </summary>
    public Playlist? GetPlaylistByName(string name)
    {
        return _data?.Playlists.Find(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Delete a playlist.
    /// </summary>
    public async Task<bool> DeletePlaylistAsync(string id)
    {
        if (_data == null)
            return false;

        var playlist = _data.Playlists.Find(p => p.Id == id);
        if (playlist != null)
        {
            _data.Playlists.Remove(playlist);
            _isDirty = true;
            await SaveAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Add a track to a playlist.
    /// </summary>
    public async Task<bool> AddToPlaylistAsync(string playlistId, string filePath)
    {
        var playlist = GetPlaylist(playlistId);
        if (playlist == null)
            return false;

        if (!playlist.TrackIds.Contains(filePath))
        {
            playlist.TrackIds.Add(filePath);
            playlist.DateModified = DateTime.Now;
        }
        _isDirty = true;
        await SaveAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Remove a track from a playlist.
    /// </summary>
    public async Task<bool> RemoveFromPlaylistAsync(string playlistId, string filePath)
    {
        var playlist = GetPlaylist(playlistId);
        if (playlist == null)
            return false;

        var removed = playlist.TrackIds.Remove(filePath);
        if (removed)
        {
            playlist.DateModified = DateTime.Now;
            _isDirty = true;
            await SaveAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }

    // ===== Ratings =====

    /// <summary>
    /// Set rating for a track.
    /// </summary>
    public async Task SetRatingAsync(string filePath, int rating)
    {
        if (_data == null)
            await LoadAsync();

        _data!.SetRating(filePath, rating);
        _isDirty = true;
        await SaveAsync();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Get rating for a track.
    /// </summary>
    public int GetRating(string filePath)
    {
        return _data?.GetRating(filePath) ?? 0;
    }

    // ===== Play Stats =====

    /// <summary>
    /// Record a track play.
    /// </summary>
    public async Task RecordPlayAsync(string filePath)
    {
        if (_data == null)
            await LoadAsync();

        _data!.RecordPlay(filePath);
        _isDirty = true;
        // Don't save immediately for play counts - save periodically
    }

    /// <summary>
    /// Get play count for a track.
    /// </summary>
    public int GetPlayCount(string filePath)
    {
        return _data?.GetPlayCount(filePath) ?? 0;
    }

    /// <summary>
    /// Get last played date for a track.
    /// </summary>
    public DateTime? GetLastPlayed(string filePath)
    {
        return _data?.GetLastPlayed(filePath);
    }

    // ===== Library Folders =====

    /// <summary>
    /// Get library folders.
    /// </summary>
    public IReadOnlyList<string> GetLibraryFolders()
    {
        return _data?.LibraryFolders ?? new System.Collections.Generic.List<string>();
    }

    /// <summary>
    /// Add a library folder.
    /// </summary>
    public async Task<bool> AddLibraryFolderAsync(string folderPath)
    {
        if (_data == null)
            await LoadAsync();

        if (!_data!.LibraryFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
        {
            _data.LibraryFolders.Add(folderPath);
            _isDirty = true;
            await SaveAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Remove a library folder.
    /// </summary>
    public async Task<bool> RemoveLibraryFolderAsync(string folderPath)
    {
        if (_data == null)
            return false;

        var removed = _data.LibraryFolders.RemoveAll(f => f.Equals(folderPath, StringComparison.OrdinalIgnoreCase)) > 0;
        if (removed)
        {
            _isDirty = true;
            await SaveAsync();
            DataChanged?.Invoke(this, EventArgs.Empty);
        }
        return removed;
    }
}
