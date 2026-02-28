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
    /// A saved media playlist containing media file references.
    /// Named MediaPlaylist to avoid conflict with PlatypusTools.Core.Models.Audio.Playlist.
    /// </summary>
    public class MediaPlaylist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public string? CoverImagePath { get; set; }
        public bool IsCollection { get; set; } // true = collection (auto-curated), false = manual playlist
        public MediaPlaylistType Type { get; set; } = MediaPlaylistType.Manual;
        public List<PlaylistItem> Items { get; set; } = new();
        public int ItemCount => Items.Count;
        public double TotalDurationSeconds => Items.Sum(i => i.DurationSeconds);
    }

    /// <summary>
    /// An item within a playlist.
    /// </summary>
    public class PlaylistItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public int SortOrder { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public MediaType MediaType { get; set; } = MediaType.Audio;
    }

    public enum MediaPlaylistType
    {
        Manual,
        Collection,
        Smart, // auto-generated based on rules
        RecentlyAdded,
        MostPlayed
    }

    /// <summary>
    /// Manages saved playlists and collections with JSON persistence.
    /// </summary>
    public class PlaylistService
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _storagePath;
        private List<MediaPlaylist> _playlists = new();
        private bool _loaded;

        public PlaylistService()
        {
            _storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "playlists.json");
        }

        /// <summary>
        /// Load playlists from disk.
        /// </summary>
        public async Task LoadAsync()
        {
            if (_loaded) return;

            try
            {
                if (File.Exists(_storagePath))
                {
                    var json = await File.ReadAllTextAsync(_storagePath);
                    _playlists = JsonSerializer.Deserialize<List<MediaPlaylist>>(json, JsonOptions) ?? new();
                }
            }
            catch
            {
                _playlists = new();
            }

            _loaded = true;
        }

        /// <summary>
        /// Save playlists to disk.
        /// </summary>
        public async Task SaveAsync()
        {
            try
            {
                var dir = Path.GetDirectoryName(_storagePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(_playlists, JsonOptions);
                await File.WriteAllTextAsync(_storagePath, json);
            }
            catch { }
        }

        /// <summary>
        /// Get all playlists.
        /// </summary>
        public async Task<List<MediaPlaylist>> GetAllAsync()
        {
            await LoadAsync();
            return _playlists.ToList();
        }

        /// <summary>
        /// Get a playlist by ID.
        /// </summary>
        public async Task<MediaPlaylist?> GetByIdAsync(string id)
        {
            await LoadAsync();
            return _playlists.FirstOrDefault(p => p.Id == id);
        }

        /// <summary>
        /// Create a new playlist.
        /// </summary>
        public async Task<MediaPlaylist> CreateAsync(string name, string? description = null, bool isCollection = false)
        {
            await LoadAsync();

            var playlist = new MediaPlaylist
            {
                Name = name,
                Description = description,
                IsCollection = isCollection,
                Type = isCollection ? MediaPlaylistType.Collection : MediaPlaylistType.Manual
            };

            _playlists.Add(playlist);
            await SaveAsync();
            return playlist;
        }

        /// <summary>
        /// Delete a playlist.
        /// </summary>
        public async Task<bool> DeleteAsync(string id)
        {
            await LoadAsync();
            var removed = _playlists.RemoveAll(p => p.Id == id);
            if (removed > 0)
                await SaveAsync();
            return removed > 0;
        }

        /// <summary>
        /// Rename a playlist.
        /// </summary>
        public async Task<bool> RenameAsync(string id, string newName)
        {
            await LoadAsync();
            var playlist = _playlists.FirstOrDefault(p => p.Id == id);
            if (playlist == null) return false;

            playlist.Name = newName;
            playlist.UpdatedAt = DateTime.UtcNow;
            await SaveAsync();
            return true;
        }

        /// <summary>
        /// Add an item to a playlist.
        /// </summary>
        public async Task<bool> AddItemAsync(string playlistId, PlaylistItem item)
        {
            await LoadAsync();
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return false;

            item.SortOrder = playlist.Items.Count;
            playlist.Items.Add(item);
            playlist.UpdatedAt = DateTime.UtcNow;
            await SaveAsync();
            return true;
        }

        /// <summary>
        /// Add multiple items to a playlist.
        /// </summary>
        public async Task<bool> AddItemsAsync(string playlistId, IEnumerable<PlaylistItem> items)
        {
            await LoadAsync();
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return false;

            var startOrder = playlist.Items.Count;
            foreach (var item in items)
            {
                item.SortOrder = startOrder++;
                playlist.Items.Add(item);
            }
            playlist.UpdatedAt = DateTime.UtcNow;
            await SaveAsync();
            return true;
        }

        /// <summary>
        /// Remove an item from a playlist by file path.
        /// </summary>
        public async Task<bool> RemoveItemAsync(string playlistId, string filePath)
        {
            await LoadAsync();
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return false;

            var removed = playlist.Items.RemoveAll(i =>
                string.Equals(i.FilePath, filePath, StringComparison.OrdinalIgnoreCase));

            if (removed > 0)
            {
                // Reorder
                for (int i = 0; i < playlist.Items.Count; i++)
                    playlist.Items[i].SortOrder = i;

                playlist.UpdatedAt = DateTime.UtcNow;
                await SaveAsync();
            }
            return removed > 0;
        }

        /// <summary>
        /// Reorder items in a playlist.
        /// </summary>
        public async Task<bool> ReorderAsync(string playlistId, int oldIndex, int newIndex)
        {
            await LoadAsync();
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return false;
            if (oldIndex < 0 || oldIndex >= playlist.Items.Count) return false;
            if (newIndex < 0 || newIndex >= playlist.Items.Count) return false;

            var item = playlist.Items[oldIndex];
            playlist.Items.RemoveAt(oldIndex);
            playlist.Items.Insert(newIndex, item);

            for (int i = 0; i < playlist.Items.Count; i++)
                playlist.Items[i].SortOrder = i;

            playlist.UpdatedAt = DateTime.UtcNow;
            await SaveAsync();
            return true;
        }

        /// <summary>
        /// Clear all items from a playlist.
        /// </summary>
        public async Task<bool> ClearAsync(string playlistId)
        {
            await LoadAsync();
            var playlist = _playlists.FirstOrDefault(p => p.Id == playlistId);
            if (playlist == null) return false;

            playlist.Items.Clear();
            playlist.UpdatedAt = DateTime.UtcNow;
            await SaveAsync();
            return true;
        }
    }
}
