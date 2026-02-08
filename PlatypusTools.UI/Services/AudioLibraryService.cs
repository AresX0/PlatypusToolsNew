using PlatypusTools.Core.Models.Audio;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services;

/// <summary>
/// Audio library service for organizing and managing music collections.
/// Provides indexing, searching, and organization by artist, album, genre.
/// </summary>
public class AudioLibraryService
{
    private static AudioLibraryService? _instance;
    public static AudioLibraryService Instance => _instance ??= new AudioLibraryService();
    
    private readonly Dictionary<string, AudioTrack> _tracks = new();
    private readonly Dictionary<string, Artist> _artists = new();
    private readonly Dictionary<string, Album> _albums = new();
    private readonly Dictionary<string, Playlist> _playlists = new();
    private readonly List<string> _watchFolders = new();
    private readonly Dictionary<string, FileSystemWatcher> _fileWatchers = new();
    private System.Timers.Timer? _watcherDebounceTimer;
    private readonly List<string> _pendingWatchFiles = new();
    
    private string _libraryPath = string.Empty;
    
    // Events
    public event EventHandler<int>? ScanProgressChanged;
    public event EventHandler? LibraryUpdated;
    public event EventHandler<string>? ErrorOccurred;
    public event EventHandler<string>? FileAutoImported;
    
    public AudioLibraryService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _libraryPath = Path.Combine(appData, "PlatypusTools", "AudioLibrary.json");
    }
    
    // Properties
    public IReadOnlyCollection<AudioTrack> AllTracks => _tracks.Values;
    public IReadOnlyCollection<Artist> AllArtists => _artists.Values;
    public IReadOnlyCollection<Album> AllAlbums => _albums.Values;
    public IReadOnlyCollection<Playlist> AllPlaylists => _playlists.Values;
    public IReadOnlyList<string> WatchFolders => _watchFolders.AsReadOnly();
    
    public int TrackCount => _tracks.Count;
    public int ArtistCount => _artists.Count;
    public int AlbumCount => _albums.Count;
    
    // Track management
    public void AddTrack(AudioTrack track)
    {
        if (_tracks.ContainsKey(track.Id)) return;
        
        _tracks[track.Id] = track;
        
        // Update artist
        var artistName = track.DisplayArtist;
        if (!_artists.ContainsKey(artistName))
        {
            _artists[artistName] = new Artist { Name = artistName };
        }
        _artists[artistName].TrackCount++;
        
        // Update album
        var albumKey = $"{track.DisplayAlbum}|{track.DisplayArtist}";
        if (!_albums.ContainsKey(albumKey))
        {
            _albums[albumKey] = new Album
            {
                Name = track.DisplayAlbum,
                Artist = track.DisplayArtist,
                AlbumArtist = track.AlbumArtist,
                Year = track.Year,
                Genre = track.Genre ?? string.Empty
            };
            
            if (!_artists[artistName].AlbumIds.Contains(albumKey))
                _artists[artistName].AlbumIds.Add(albumKey);
        }
        _albums[albumKey].TrackIds.Add(track.Id);
        _albums[albumKey].TotalDuration += track.Duration;
        
        // Copy album art if not set
        if (_albums[albumKey].CoverArt == null && track.AlbumArt != null)
        {
            _albums[albumKey].CoverArt = track.AlbumArt;
        }
    }
    
    public void RemoveTrack(string trackId)
    {
        if (!_tracks.TryGetValue(trackId, out var track)) return;
        
        _tracks.Remove(trackId);
        
        // Update artist
        var artistName = track.DisplayArtist;
        if (_artists.TryGetValue(artistName, out var artist))
        {
            artist.TrackCount--;
            if (artist.TrackCount <= 0)
                _artists.Remove(artistName);
        }
        
        // Update album
        var albumKey = $"{track.DisplayAlbum}|{track.DisplayArtist}";
        if (_albums.TryGetValue(albumKey, out var album))
        {
            album.TrackIds.Remove(trackId);
            album.TotalDuration -= track.Duration;
            if (album.TrackIds.Count == 0)
            {
                _albums.Remove(albumKey);
                if (_artists.TryGetValue(artistName, out artist))
                    artist.AlbumIds.Remove(albumKey);
            }
        }
    }
    
    public AudioTrack? GetTrack(string trackId)
    {
        return _tracks.TryGetValue(trackId, out var track) ? track : null;
    }
    
    // Folder scanning
    public async Task<int> ScanFolderAsync(string folderPath, bool recursive = true, 
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        if (!Directory.Exists(folderPath))
        {
            ErrorOccurred?.Invoke(this, $"Folder not found: {folderPath}");
            return 0;
        }
        
        var playerService = EnhancedAudioPlayerService.Instance;
        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        
        var files = Directory.EnumerateFiles(folderPath, "*.*", searchOption)
            .Where(f => IsAudioFile(f))
            .ToList();
        
        var added = 0;
        var total = files.Count;
        
        for (int i = 0; i < files.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested) break;
            
            var file = files[i];
            
            // Check if already in library
            if (_tracks.Values.Any(t => t.FilePath.Equals(file, StringComparison.OrdinalIgnoreCase)))
                continue;
            
            var track = await playerService.LoadTrackAsync(file);
            if (track != null)
            {
                AddTrack(track);
                added++;
            }
            
            var progressValue = (int)((i + 1) * 100.0 / total);
            progress?.Report(progressValue);
            ScanProgressChanged?.Invoke(this, progressValue);
        }
        
        if (added > 0)
        {
            LibraryUpdated?.Invoke(this, EventArgs.Empty);
        }
        
        return added;
    }
    
    public static bool IsAudioFile(string filePath)
    {
        var extensions = new[] { ".mp3", ".wav", ".wma", ".aac", ".m4a", ".flac", ".ogg", ".opus" };
        return extensions.Contains(Path.GetExtension(filePath).ToLowerInvariant());
    }
    
    public void AddWatchFolder(string folderPath)
    {
        if (!_watchFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
        {
            _watchFolders.Add(folderPath);
            StartWatchingFolder(folderPath);
        }
    }
    
    public void RemoveWatchFolder(string folderPath)
    {
        _watchFolders.RemoveAll(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase));
        StopWatchingFolder(folderPath);
    }
    
    /// <summary>
    /// Start FileSystemWatcher for a folder to auto-import new audio files.
    /// </summary>
    public void StartWatchingFolder(string folderPath)
    {
        if (!Directory.Exists(folderPath)) return;
        if (_fileWatchers.ContainsKey(folderPath.ToLowerInvariant())) return;
        
        try
        {
            var watcher = new FileSystemWatcher(folderPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            
            // Watch for new audio files
            watcher.Created += OnWatcherFileCreated;
            watcher.Renamed += OnWatcherFileRenamed;
            
            _fileWatchers[folderPath.ToLowerInvariant()] = watcher;
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to watch folder '{folderPath}': {ex.Message}");
        }
    }
    
    /// <summary>
    /// Stop watching a folder.
    /// </summary>
    public void StopWatchingFolder(string folderPath)
    {
        var key = folderPath.ToLowerInvariant();
        if (_fileWatchers.TryGetValue(key, out var watcher))
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnWatcherFileCreated;
            watcher.Renamed -= OnWatcherFileRenamed;
            watcher.Dispose();
            _fileWatchers.Remove(key);
        }
    }
    
    /// <summary>
    /// Start watching all configured watch folders.
    /// </summary>
    public void StartAllWatchers()
    {
        foreach (var folder in _watchFolders)
        {
            StartWatchingFolder(folder);
        }
    }
    
    /// <summary>
    /// Stop all file watchers and release resources.
    /// </summary>
    public void StopAllWatchers()
    {
        foreach (var watcher in _fileWatchers.Values)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        _fileWatchers.Clear();
    }
    
    private void OnWatcherFileCreated(object sender, FileSystemEventArgs e)
    {
        if (!IsAudioFile(e.FullPath)) return;
        QueueWatcherFile(e.FullPath);
    }
    
    private void OnWatcherFileRenamed(object sender, RenamedEventArgs e)
    {
        if (!IsAudioFile(e.FullPath)) return;
        QueueWatcherFile(e.FullPath);
    }
    
    /// <summary>
    /// Debounce file watcher events - many files may arrive at once (e.g., copying a folder).
    /// Waits 2 seconds after last event before importing.
    /// </summary>
    private void QueueWatcherFile(string filePath)
    {
        lock (_pendingWatchFiles)
        {
            if (!_pendingWatchFiles.Contains(filePath, StringComparer.OrdinalIgnoreCase))
            {
                _pendingWatchFiles.Add(filePath);
            }
        }
        
        // Reset debounce timer
        _watcherDebounceTimer?.Stop();
        _watcherDebounceTimer ??= new System.Timers.Timer(2000) { AutoReset = false };
        _watcherDebounceTimer.Elapsed -= OnWatcherDebounceElapsed;
        _watcherDebounceTimer.Elapsed += OnWatcherDebounceElapsed;
        _watcherDebounceTimer.Start();
    }
    
    private async void OnWatcherDebounceElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        List<string> filesToImport;
        lock (_pendingWatchFiles)
        {
            filesToImport = new List<string>(_pendingWatchFiles);
            _pendingWatchFiles.Clear();
        }
        
        int imported = 0;
        foreach (var filePath in filesToImport)
        {
            try
            {
                if (_tracks.Values.Any(t => string.Equals(t.FilePath, filePath, StringComparison.OrdinalIgnoreCase)))
                    continue; // Already in library
                    
                var track = await EnhancedAudioPlayerService.Instance.LoadTrackAsync(filePath);
                if (track != null)
                {
                    AddTrack(track);
                    imported++;
                    FileAutoImported?.Invoke(this, filePath);
                }
            }
            catch { /* Skip files that can't be loaded */ }
        }
        
        if (imported > 0)
        {
            LibraryUpdated?.Invoke(this, EventArgs.Empty);
            await SaveLibraryAsync();
        }
    }
    
    // Search and filtering
    public IEnumerable<AudioTrack> SearchTracks(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return AllTracks;
        
        var lowerQuery = query.ToLowerInvariant();
        return _tracks.Values.Where(t =>
            t.Title.ToLowerInvariant().Contains(lowerQuery) ||
            t.Artist.ToLowerInvariant().Contains(lowerQuery) ||
            t.Album.ToLowerInvariant().Contains(lowerQuery) ||
            (t.Genre?.ToLowerInvariant().Contains(lowerQuery) ?? false));
    }
    
    public IEnumerable<AudioTrack> GetTracksByArtist(string artistName)
    {
        return _tracks.Values.Where(t => 
            t.Artist.Equals(artistName, StringComparison.OrdinalIgnoreCase) ||
            t.AlbumArtist.Equals(artistName, StringComparison.OrdinalIgnoreCase));
    }
    
    public IEnumerable<AudioTrack> GetTracksByAlbum(string albumId)
    {
        if (!_albums.TryGetValue(albumId, out var album)) return Enumerable.Empty<AudioTrack>();
        return album.TrackIds.Select(id => _tracks.GetValueOrDefault(id)).Where(t => t != null)!;
    }
    
    public IEnumerable<AudioTrack> GetTracksByGenre(string genre)
    {
        return _tracks.Values.Where(t => 
            (t.Genre?.Equals(genre, StringComparison.OrdinalIgnoreCase) ?? false));
    }
    
    public IEnumerable<Album> GetAlbumsByArtist(string artistName)
    {
        return _albums.Values.Where(a => 
            a.Artist.Equals(artistName, StringComparison.OrdinalIgnoreCase) ||
            a.AlbumArtist.Equals(artistName, StringComparison.OrdinalIgnoreCase));
    }
    
    public IEnumerable<string> GetAllGenres()
    {
        return _tracks.Values
            .Select(t => t.Genre)
            .Where(g => !string.IsNullOrEmpty(g))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g)!;
    }
    
    public IEnumerable<int> GetAllYears()
    {
        return _tracks.Values
            .Select(t => t.Year)
            .Where(y => y > 0)
            .Distinct()
            .OrderByDescending(y => y);
    }
    
    // Smart playlists
    public IEnumerable<AudioTrack> GetRecentlyAdded(int count = 50)
    {
        return _tracks.Values
            .OrderByDescending(t => t.DateAdded)
            .Take(count);
    }
    
    public IEnumerable<AudioTrack> GetRecentlyPlayed(int count = 50)
    {
        return _tracks.Values
            .Where(t => t.PlayCount > 0)
            .OrderByDescending(t => t.LastPlayed)
            .Take(count);
    }
    
    public IEnumerable<AudioTrack> GetMostPlayed(int count = 50)
    {
        return _tracks.Values
            .Where(t => t.PlayCount > 0)
            .OrderByDescending(t => t.PlayCount)
            .Take(count);
    }
    
    public IEnumerable<AudioTrack> GetTopRated(int count = 50)
    {
        return _tracks.Values
            .Where(t => t.Rating > 0)
            .OrderByDescending(t => t.Rating)
            .ThenByDescending(t => t.PlayCount)
            .Take(count);
    }
    
    public IEnumerable<AudioTrack> GetNeverPlayed()
    {
        return _tracks.Values.Where(t => t.PlayCount == 0);
    }
    
    // Playlist management
    public Playlist CreatePlaylist(string name)
    {
        var playlist = new Playlist
        {
            Name = name,
            Type = PlaylistType.User
        };
        _playlists[playlist.Id] = playlist;
        return playlist;
    }
    
    public void DeletePlaylist(string playlistId)
    {
        _playlists.Remove(playlistId);
    }
    
    public void AddToPlaylist(string playlistId, string trackId)
    {
        if (_playlists.TryGetValue(playlistId, out var playlist))
        {
            if (!playlist.TrackIds.Contains(trackId))
            {
                playlist.TrackIds.Add(trackId);
                playlist.DateModified = DateTime.Now;
            }
        }
    }
    
    public void RemoveFromPlaylist(string playlistId, string trackId)
    {
        if (_playlists.TryGetValue(playlistId, out var playlist))
        {
            playlist.TrackIds.Remove(trackId);
            playlist.DateModified = DateTime.Now;
        }
    }
    
    public IEnumerable<AudioTrack> GetPlaylistTracks(string playlistId)
    {
        if (!_playlists.TryGetValue(playlistId, out var playlist))
            return Enumerable.Empty<AudioTrack>();
            
        return playlist.TrackIds
            .Select(id => _tracks.GetValueOrDefault(id))
            .Where(t => t != null)!;
    }
    
    // Persistence
    public async Task SaveLibraryAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_libraryPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            
            var data = new LibraryData
            {
                Tracks = _tracks.Values.ToList(),
                Playlists = _playlists.Values.ToList(),
                WatchFolders = _watchFolders.ToList()
            };
            
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_libraryPath, json);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to save library: {ex.Message}");
        }
    }
    
    public async Task LoadLibraryAsync()
    {
        try
        {
            if (!File.Exists(_libraryPath)) return;
            
            var json = await File.ReadAllTextAsync(_libraryPath);
            var data = JsonSerializer.Deserialize<LibraryData>(json);
            
            if (data != null)
            {
                _tracks.Clear();
                _artists.Clear();
                _albums.Clear();
                _playlists.Clear();
                _watchFolders.Clear();
                
                foreach (var track in data.Tracks)
                {
                    AddTrack(track);
                }
                
                foreach (var playlist in data.Playlists)
                {
                    _playlists[playlist.Id] = playlist;
                }
                
                _watchFolders.AddRange(data.WatchFolders);
                
                // Start file watchers for all configured folders
                StartAllWatchers();
                
                LibraryUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke(this, $"Failed to load library: {ex.Message}");
        }
    }
    
    public void ClearLibrary()
    {
        _tracks.Clear();
        _artists.Clear();
        _albums.Clear();
        _playlists.Clear();
        LibraryUpdated?.Invoke(this, EventArgs.Empty);
    }
    
    // Statistics
    public TimeSpan GetTotalDuration()
    {
        return TimeSpan.FromTicks(_tracks.Values.Sum(t => t.Duration.Ticks));
    }
    
    public long GetTotalSize()
    {
        return _tracks.Values.Sum(t => t.FileSize);
    }
    
    private class LibraryData
    {
        public List<AudioTrack> Tracks { get; set; } = new();
        public List<Playlist> Playlists { get; set; } = new();
        public List<string> WatchFolders { get; set; } = new();
    }
}
