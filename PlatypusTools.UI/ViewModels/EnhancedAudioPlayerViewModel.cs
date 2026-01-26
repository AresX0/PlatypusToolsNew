using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;
using PlatypusTools.UI.Utilities;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// Enhanced Audio Player ViewModel with all advanced features:
/// - AP-001: Track Rating UI (star ratings)
/// - AP-002: Play Speed Control (0.5x-2x)
/// - AP-003: Media Key Support (Play/Pause/Next/Prev)
/// - AP-004: Queue Drag-Drop Reorder
/// - AP-005: Track Info Tooltip
/// - AP-006: 10-Band EQ
/// - AP-007: LRC Lyrics Sync
/// - AP-008: Smart Playlists
/// - AP-009: Mini Player Mode
/// - AP-010: Audio File Info Panel
/// - AP-011: Gapless Playback
/// - AP-012: Auto DJ Mode
/// - AP-013: Last.fm Scrobbling
/// - AP-014: ReplayGain Support
/// - AP-015: Audio Converter Integration
/// </summary>
public class EnhancedAudioPlayerViewModel : BindableBase, IDisposable
{
    private readonly EnhancedAudioPlayerService _playerService;
    private readonly LibraryIndexService _libraryIndexService;
    private readonly UserLibraryService _userLibraryService;
    private CancellationTokenSource? _scanCts;
    
    // Performance: Debounce timer for search filtering
    private CancellationTokenSource? _searchDebounce;
    
    // Lyrics cache: TrackId -> Lyrics (null means already tried, no lyrics found)
    private readonly Dictionary<string, Lyrics?> _lyricsCache = new();
    
    #region Playback State
    
    private AudioTrack? _currentTrack;
    public AudioTrack? CurrentTrack
    {
        get => _currentTrack;
        set
        {
            if (SetProperty(ref _currentTrack, value))
            {
                RaisePropertyChanged(nameof(CurrentTrackTitle));
                RaisePropertyChanged(nameof(CurrentTrackArtist));
                RaisePropertyChanged(nameof(CurrentTrackAlbum));
                RaisePropertyChanged(nameof(CurrentTrackRating));
                RaisePropertyChanged(nameof(HasCurrentTrack));
                RaisePropertyChanged(nameof(CurrentTrackTooltip));
                LoadAlbumArt(value);
            }
        }
    }
    
    private BitmapImage? _albumArtImage;
    public BitmapImage? AlbumArtImage
    {
        get => _albumArtImage;
        set => SetProperty(ref _albumArtImage, value);
    }
    
    // Common album art filenames to check
    private static readonly string[] AlbumArtFilenames = 
    {
        "folder.jpg", "folder.png", "cover.jpg", "cover.png", 
        "album.jpg", "album.png", "front.jpg", "front.png",
        "albumart.jpg", "albumart.png", "artwork.jpg", "artwork.png"
    };
    
    private void LoadAlbumArt(AudioTrack? track)
    {
        if (track == null)
        {
            AlbumArtImage = null;
            return;
        }
        
        // STEP 1: Check for local album art file (synchronous, instant)
        var localArt = LoadLocalAlbumArtSync(track);
        if (localArt != null)
        {
            AlbumArtImage = localArt;
            return;
        }
        
        // STEP 2: Check embedded art in track
        if (track.AlbumArt != null && track.AlbumArt.Length > 0)
        {
            try
            {
                using var stream = new MemoryStream(track.AlbumArt);
                var image = ImageHelper.LoadFromStream(stream);
                if (image != null)
                {
                    AlbumArtImage = image;
                    return;
                }
            }
            catch { }
        }
        
        // STEP 3: No local file or embedded art - download asynchronously (will save locally)
        AlbumArtImage = null;
        if (!string.IsNullOrEmpty(track.Artist) && !string.IsNullOrEmpty(track.Album))
        {
            _ = DownloadAlbumArtAsync(track);
        }
    }
    
    /// <summary>
    /// Synchronously checks for local album art files in the track's folder.
    /// </summary>
    private BitmapImage? LoadLocalAlbumArtSync(AudioTrack track)
    {
        try
        {
            var directory = Path.GetDirectoryName(track.FilePath);
            if (string.IsNullOrEmpty(directory)) return null;
            
            // Check for common album art filenames
            foreach (var filename in AlbumArtFilenames)
            {
                var artPath = Path.Combine(directory, filename);
                if (File.Exists(artPath))
                {
                    return LoadImageFromFile(artPath);
                }
            }
            
            // Also check for artist-album specific file
            var specificName = $"{track.Artist} - {track.Album}".Replace(":", "").Replace("/", "").Replace("\\", "");
            var specificPath = Path.Combine(directory, specificName + ".jpg");
            if (File.Exists(specificPath))
            {
                return LoadImageFromFile(specificPath);
            }
        }
        catch { }
        
        return null;
    }
    
    private BitmapImage? LoadImageFromFile(string path)
    {
        try
        {
            return ImageHelper.LoadFromFile(path);
        }
        catch
        {
            return null;
        }
    }
    
    private async Task DownloadAlbumArtAsync(AudioTrack track)
    {
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5); // Short timeout to avoid UI delay
            httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatypusTools/1.0");
            
            // Try MusicBrainz Cover Art Archive
            var searchArtist = Uri.EscapeDataString(track.Artist ?? "");
            var searchAlbum = Uri.EscapeDataString(track.Album ?? "");
            
            // First, search for the release on MusicBrainz
            var mbUrl = $"https://musicbrainz.org/ws/2/release/?query=artist:{searchArtist}%20AND%20release:{searchAlbum}&fmt=json&limit=1";
            var mbResponse = await httpClient.GetStringAsync(mbUrl);
            
            // Parse release ID from response (basic JSON parsing)
            var releaseIdMatch = System.Text.RegularExpressions.Regex.Match(mbResponse, "\"id\":\"([a-f0-9-]+)\"");
            if (releaseIdMatch.Success)
            {
                var releaseId = releaseIdMatch.Groups[1].Value;
                
                // Get cover art from Cover Art Archive
                var coverUrl = $"https://coverartarchive.org/release/{releaseId}/front-250";
                
                var imageBytes = await httpClient.GetByteArrayAsync(coverUrl);
                if (imageBytes != null && imageBytes.Length > 0)
                {
                    // SAVE to local folder.jpg file for instant future loading
                    try
                    {
                        var directory = Path.GetDirectoryName(track.FilePath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            var savePath = Path.Combine(directory, "folder.jpg");
                            if (!File.Exists(savePath))
                            {
                                await File.WriteAllBytesAsync(savePath, imageBytes);
                                System.Diagnostics.Debug.WriteLine($"Saved album art: {savePath}");
                            }
                        }
                    }
                    catch { /* Ignore save errors */ }
                    
                    // Save to track for future use
                    track.AlbumArt = imageBytes;
                    
                    // Apply album art to ALL tracks in the same album (by artist + album)
                    var albumKey = $"{track.Artist?.ToLowerInvariant()}|{track.Album?.ToLowerInvariant()}";
                    foreach (var albumTrack in _allLibraryTracks.Where(t => 
                        $"{t.Artist?.ToLowerInvariant()}|{t.Album?.ToLowerInvariant()}" == albumKey))
                    {
                        if (albumTrack.AlbumArt == null)
                        {
                            albumTrack.AlbumArt = imageBytes;
                        }
                    }
                    // Also apply to tracks in queue
                    foreach (var queueTrack in Queue.Where(t => 
                        $"{t.Artist?.ToLowerInvariant()}|{t.Album?.ToLowerInvariant()}" == albumKey))
                    {
                        if (queueTrack.AlbumArt == null)
                        {
                            queueTrack.AlbumArt = imageBytes;
                        }
                    }
                    
                    // Display it
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            using var stream = new MemoryStream(imageBytes);
                            var image = ImageHelper.LoadFromStream(stream);
                            if (image != null)
                            {
                                AlbumArtImage = image;
                            }
                        }
                        catch { }
                    });
                }
            }
        }
        catch
        {
            // Silently fail - album art is optional
        }
    }
    
    public string CurrentTrackTitle => CurrentTrack?.DisplayTitle ?? "No track";
    public string CurrentTrackArtist => CurrentTrack?.DisplayArtist ?? "Unknown Artist";
    public string CurrentTrackAlbum => CurrentTrack?.DisplayAlbum ?? "Unknown Album";
    public bool HasCurrentTrack => CurrentTrack != null;
    
    // AP-005: Track Info Tooltip
    public string CurrentTrackTooltip => CurrentTrack != null
        ? $"""
            {CurrentTrack.DisplayTitle}
            Artist: {CurrentTrack.DisplayArtist}
            Album: {CurrentTrack.DisplayAlbum}
            Duration: {CurrentTrack.DurationFormatted}
            Bitrate: {CurrentTrack.Bitrate} kbps
            Sample Rate: {CurrentTrack.SampleRate} Hz
            Codec: {CurrentTrack.Codec}
            Size: {FormatFileSize(CurrentTrack.FileSize)}
            Play Count: {CurrentTrack.PlayCount}
            """
        : "No track loaded";
    
    private TimeSpan _position;
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                RaisePropertyChanged(nameof(PositionSeconds));
                RaisePropertyChanged(nameof(PositionDisplay));
                UpdateCurrentLyric();
            }
        }
    }
    
    private TimeSpan _duration;
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (SetProperty(ref _duration, value))
            {
                RaisePropertyChanged(nameof(DurationSeconds));
                RaisePropertyChanged(nameof(DurationDisplay));
            }
        }
    }
    
    public double PositionSeconds
    {
        get => Position.TotalSeconds;
        set => _playerService.Seek(TimeSpan.FromSeconds(value));
    }
    
    public double DurationSeconds => Duration.TotalSeconds;
    public string PositionDisplay => Position.ToString(@"m\:ss");
    public string DurationDisplay => Duration.ToString(@"m\:ss");
    
    private double _volume = 0.7;
    public double Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                _playerService.Volume = value;
                RaisePropertyChanged(nameof(VolumePercent));
            }
        }
    }
    
    public int VolumePercent => (int)(Volume * 100);
    
    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
                _playerService.IsMuted = value;
        }
    }
    
    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
                RaisePropertyChanged(nameof(PlayPauseIcon));
        }
    }
    
    public string PlayPauseIcon => IsPlaying ? "⏸" : "▶";
    
    private bool _isShuffle;
    public bool IsShuffle
    {
        get => _isShuffle;
        set
        {
            if (SetProperty(ref _isShuffle, value))
                _playerService.IsShuffle = value;
        }
    }
    
    private int _repeatMode; // 0=None, 1=All, 2=One
    public int RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (SetProperty(ref _repeatMode, value))
            {
                _playerService.Repeat = (EnhancedAudioPlayerService.RepeatMode)value;
                RaisePropertyChanged(nameof(RepeatIcon));
            }
        }
    }
    
    public string RepeatIcon => RepeatMode switch
    {
        1 => "🔁",
        2 => "🔂",
        _ => "➡️"
    };
    
    #endregion
    
    #region AP-002: Playback Speed Control
    
    private double _playbackSpeed = 1.0;
    public double PlaybackSpeed
    {
        get => _playbackSpeed;
        set
        {
            if (SetProperty(ref _playbackSpeed, Math.Round(value, 2)))
            {
                _playerService.PlaybackSpeed = value;
                RaisePropertyChanged(nameof(PlaybackSpeedDisplay));
            }
        }
    }
    
    public string PlaybackSpeedDisplay => $"{PlaybackSpeed:0.##}x";
    
    public ObservableCollection<double> SpeedPresets { get; } = new()
    {
        0.5, 0.75, 1.0, 1.25, 1.5, 2.0
    };
    
    #endregion
    
    #region AP-001: Track Rating UI
    
    public int CurrentTrackRating
    {
        get => CurrentTrack?.Rating ?? 0;
        set
        {
            if (CurrentTrack != null)
            {
                CurrentTrack.Rating = Math.Clamp(value, 0, 5);
                _ = _userLibraryService?.SetRatingAsync(CurrentTrack.FilePath, value);
                RaisePropertyChanged();
            }
        }
    }
    
    public ICommand SetRatingCommand { get; }
    
    #endregion
    
    #region AP-006: 10-Band Equalizer
    
    private double _eq32Hz;
    public double Eq32Hz { get => _eq32Hz; set { if (SetProperty(ref _eq32Hz, value)) _playerService.SetEqBand(0, (float)value); } }
    
    private double _eq64Hz;
    public double Eq64Hz { get => _eq64Hz; set { if (SetProperty(ref _eq64Hz, value)) _playerService.SetEqBand(1, (float)value); } }
    
    private double _eq125Hz;
    public double Eq125Hz { get => _eq125Hz; set { if (SetProperty(ref _eq125Hz, value)) _playerService.SetEqBand(2, (float)value); } }
    
    private double _eq250Hz;
    public double Eq250Hz { get => _eq250Hz; set { if (SetProperty(ref _eq250Hz, value)) _playerService.SetEqBand(3, (float)value); } }
    
    private double _eq500Hz;
    public double Eq500Hz { get => _eq500Hz; set { if (SetProperty(ref _eq500Hz, value)) _playerService.SetEqBand(4, (float)value); } }
    
    private double _eq1kHz;
    public double Eq1kHz { get => _eq1kHz; set { if (SetProperty(ref _eq1kHz, value)) _playerService.SetEqBand(5, (float)value); } }
    
    private double _eq2kHz;
    public double Eq2kHz { get => _eq2kHz; set { if (SetProperty(ref _eq2kHz, value)) _playerService.SetEqBand(6, (float)value); } }
    
    private double _eq4kHz;
    public double Eq4kHz { get => _eq4kHz; set { if (SetProperty(ref _eq4kHz, value)) _playerService.SetEqBand(7, (float)value); } }
    
    private double _eq8kHz;
    public double Eq8kHz { get => _eq8kHz; set { if (SetProperty(ref _eq8kHz, value)) _playerService.SetEqBand(8, (float)value); } }
    
    private double _eq16kHz;
    public double Eq16kHz { get => _eq16kHz; set { if (SetProperty(ref _eq16kHz, value)) _playerService.SetEqBand(9, (float)value); } }
    
    public ObservableCollection<EQPreset> EQPresets { get; } = new();
    
    private EQPreset? _selectedEqPreset;
    public EQPreset? SelectedEqPreset
    {
        get => _selectedEqPreset;
        set
        {
            if (SetProperty(ref _selectedEqPreset, value) && value != null)
            {
                ApplyEqPreset(value);
            }
        }
    }
    
    private void ApplyEqPreset(EQPreset preset)
    {
        _playerService.SetEqPreset(preset);
        var bands = preset.GetBands();
        Eq32Hz = bands[0];
        Eq64Hz = bands[1];
        Eq125Hz = bands[2];
        Eq250Hz = bands[3];
        Eq500Hz = bands[4];
        Eq1kHz = bands[5];
        Eq2kHz = bands[6];
        Eq4kHz = bands[7];
        Eq8kHz = bands[8];
        Eq16kHz = bands[9];
    }
    
    public ICommand ResetEqCommand { get; }
    
    #endregion
    
    #region AP-007: LRC Lyrics
    
    private Lyrics? _currentLyrics;
    public Lyrics? CurrentLyrics
    {
        get => _currentLyrics;
        set
        {
            if (SetProperty(ref _currentLyrics, value))
            {
                RaisePropertyChanged(nameof(HasLyrics));
            }
        }
    }
    
    private LyricLine? _currentLyricLine;
    public LyricLine? CurrentLyricLine
    {
        get => _currentLyricLine;
        set => SetProperty(ref _currentLyricLine, value);
    }
    
    private int _currentLyricIndex = -1;
    public int CurrentLyricIndex
    {
        get => _currentLyricIndex;
        set => SetProperty(ref _currentLyricIndex, value);
    }
    
    public bool HasLyrics => CurrentLyrics != null && CurrentLyrics.Lines.Count > 0;
    
    private bool _showLyricsOverlay;
    public bool ShowLyricsOverlay
    {
        get => _showLyricsOverlay;
        set => SetProperty(ref _showLyricsOverlay, value);
    }
    
    /// <summary>
    /// Current lyric line text for overlay display
    /// </summary>
    public string CurrentLyricLineText => CurrentLyricLine?.Text ?? "";
    
    private void UpdateCurrentLyric()
    {
        if (CurrentLyrics == null) return;
        
        var line = _playerService.GetCurrentLyricLine(CurrentLyrics, Position);
        if (line != CurrentLyricLine)
        {
            CurrentLyricLine = line;
            OnPropertyChanged(nameof(CurrentLyricLineText));
            if (line != null)
            {
                CurrentLyricIndex = CurrentLyrics.Lines.IndexOf(line);
            }
        }
    }
    
    #endregion
    
    #region AP-008: Smart Playlists
    
    public ObservableCollection<Playlist> SmartPlaylists { get; } = new();
    
    private Playlist? _selectedPlaylist;
    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (SetProperty(ref _selectedPlaylist, value))
            {
                LoadPlaylistTracks(value);
            }
        }
    }
    
    private void InitializeSmartPlaylists()
    {
        SmartPlaylists.Add(new Playlist { Name = "Recently Played", Type = PlaylistType.RecentlyPlayed, IsSmartPlaylist = true });
        SmartPlaylists.Add(new Playlist { Name = "Most Played", Type = PlaylistType.MostPlayed, IsSmartPlaylist = true });
        SmartPlaylists.Add(new Playlist { Name = "Recently Added", Type = PlaylistType.RecentlyAdded, IsSmartPlaylist = true });
        SmartPlaylists.Add(new Playlist { Name = "Top Rated", Type = PlaylistType.TopRated, IsSmartPlaylist = true });
    }
    
    private void LoadPlaylistTracks(Playlist? playlist)
    {
        if (playlist == null) return;
        
        IEnumerable<AudioTrack> tracks;
        
        if (playlist.Type == PlaylistType.Custom || playlist.Type == PlaylistType.User)
        {
            // Manual playlist - load by track IDs
            var trackIds = playlist.TrackIds ?? new List<string>();
            tracks = _allLibraryTracks.Where(t => trackIds.Contains(t.Id));
        }
        else
        {
            tracks = playlist.Type switch
            {
                PlaylistType.RecentlyPlayed => _allLibraryTracks
                    .Where(t => t.LastPlayed.HasValue)
                    .OrderByDescending(t => t.LastPlayed)
                    .Take(50),
                PlaylistType.MostPlayed => _allLibraryTracks
                    .Where(t => t.PlayCount > 0)
                    .OrderByDescending(t => t.PlayCount)
                    .Take(50),
                PlaylistType.RecentlyAdded => _allLibraryTracks
                    .OrderByDescending(t => t.DateAdded)
                    .Take(50),
                PlaylistType.TopRated => _allLibraryTracks
                    .Where(t => t.Rating >= 4)
                    .OrderByDescending(t => t.Rating)
                    .ThenByDescending(t => t.PlayCount)
                    .Take(50),
                _ => Array.Empty<AudioTrack>()
            };
        }
        
        LibraryTracks.Clear();
        foreach (var track in tracks)
            LibraryTracks.Add(track);
    }
    
    // User Playlists (manual)
    public ObservableCollection<Playlist> UserPlaylists { get; } = new();
    
    private void CreatePlaylist()
    {
        string name = Microsoft.VisualBasic.Interaction.InputBox("Enter playlist name:", "Create Playlist", "New Playlist");
        if (!string.IsNullOrWhiteSpace(name))
        {
            var playlist = new Playlist 
            { 
                Name = name, 
                Type = PlaylistType.Custom, 
                IsSmartPlaylist = false,
                TrackIds = new List<string>()
            };
            UserPlaylists.Add(playlist);
            SmartPlaylists.Add(playlist); // Also add to main list for display
            _ = SaveUserPlaylistsAsync();
            StatusMessage = $"Created playlist: {name}";
        }
    }
    
    private void DeletePlaylist()
    {
        if (SelectedPlaylist == null || SelectedPlaylist.IsSmartPlaylist) return;
        
        UserPlaylists.Remove(SelectedPlaylist);
        SmartPlaylists.Remove(SelectedPlaylist);
        _ = SaveUserPlaylistsAsync();
        StatusMessage = "Playlist deleted";
    }
    
    private void RenamePlaylist()
    {
        if (SelectedPlaylist == null || SelectedPlaylist.IsSmartPlaylist) return;
        
        string newName = Microsoft.VisualBasic.Interaction.InputBox("Enter new name:", "Rename Playlist", SelectedPlaylist.Name);
        if (!string.IsNullOrWhiteSpace(newName))
        {
            SelectedPlaylist.Name = newName;
            _ = SaveUserPlaylistsAsync();
            StatusMessage = $"Renamed to: {newName}";
        }
    }
    
    private void AddTrackToPlaylist(AudioTrack track, Playlist playlist)
    {
        playlist.TrackIds ??= new List<string>();
        if (!playlist.TrackIds.Contains(track.Id))
        {
            playlist.TrackIds.Add(track.Id);
            _ = SaveUserPlaylistsAsync();
            StatusMessage = $"Added {track.DisplayTitle} to {playlist.Name}";
        }
    }
    
    private async Task SaveUserPlaylistsAsync()
    {
        try
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatypusTools");
            Directory.CreateDirectory(appDataPath);
            var playlistsPath = Path.Combine(appDataPath, "enhanced_user_playlists.json");
            var json = JsonSerializer.Serialize(UserPlaylists.ToList(), new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(playlistsPath, json);
        }
        catch { }
    }
    
    private async Task LoadUserPlaylistsAsync()
    {
        try
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatypusTools");
            var playlistsPath = Path.Combine(appDataPath, "enhanced_user_playlists.json");
            if (File.Exists(playlistsPath))
            {
                var json = await File.ReadAllTextAsync(playlistsPath);
                var playlists = JsonSerializer.Deserialize<List<Playlist>>(json);
                if (playlists != null)
                {
                    foreach (var playlist in playlists)
                    {
                        UserPlaylists.Add(playlist);
                        SmartPlaylists.Add(playlist);
                    }
                }
            }
        }
        catch { }
    }
    
    #endregion
    
    #region AP-009: Mini Player Mode
    
    private bool _isMiniPlayerMode;
    public bool IsMiniPlayerMode
    {
        get => _isMiniPlayerMode;
        set
        {
            if (SetProperty(ref _isMiniPlayerMode, value))
            {
                RaisePropertyChanged(nameof(IsFullPlayerMode));
            }
        }
    }
    
    public bool IsFullPlayerMode => !IsMiniPlayerMode;
    
    public ICommand ToggleMiniPlayerCommand { get; }
    
    #endregion
    
    #region AP-010: Audio File Info Panel
    
    private bool _showFileInfoPanel;
    public bool ShowFileInfoPanel
    {
        get => _showFileInfoPanel;
        set => SetProperty(ref _showFileInfoPanel, value);
    }
    
    public string FileInfoText => CurrentTrack != null
        ? $"""
            ═══════════════════════════════════════
            File: {CurrentTrack.FileName}
            Path: {CurrentTrack.FilePath}
            ═══════════════════════════════════════
            Duration: {CurrentTrack.DurationFormatted}
            Codec: {CurrentTrack.Codec}
            Bitrate: {CurrentTrack.Bitrate} kbps
            Sample Rate: {CurrentTrack.SampleRate} Hz
            Channels: {CurrentTrack.Channels}
            File Size: {FormatFileSize(CurrentTrack.FileSize)}
            ═══════════════════════════════════════
            Title: {CurrentTrack.Title}
            Artist: {CurrentTrack.Artist}
            Album: {CurrentTrack.Album}
            Album Artist: {CurrentTrack.AlbumArtist}
            Genre: {CurrentTrack.Genre}
            Year: {CurrentTrack.Year}
            Track: {CurrentTrack.TrackNumber} / Disc: {CurrentTrack.DiscNumber}
            ═══════════════════════════════════════
            Play Count: {CurrentTrack.PlayCount}
            Last Played: {CurrentTrack.LastPlayed?.ToString("g") ?? "Never"}
            Date Added: {CurrentTrack.DateAdded:g}
            Rating: {"★".PadRight(CurrentTrack.Rating, '★').PadRight(5, '☆')}
            ═══════════════════════════════════════
            ReplayGain Track: {CurrentTrack.TrackGain:+0.00;-0.00;0.00} dB
            ReplayGain Album: {CurrentTrack.AlbumGain:+0.00;-0.00;0.00} dB
            """
        : "No track selected";
    
    public ICommand ToggleFileInfoCommand { get; }
    
    #endregion
    
    #region AP-012: Auto DJ Mode
    
    private bool _autoDjEnabled;
    public bool AutoDjEnabled
    {
        get => _autoDjEnabled;
        set
        {
            if (SetProperty(ref _autoDjEnabled, value))
            {
                RaisePropertyChanged(nameof(AutoDjStatusText));
            }
        }
    }
    
    public string AutoDjStatusText => AutoDjEnabled ? "Auto DJ: ON" : "Auto DJ: OFF";
    
    public ICommand ToggleAutoDjCommand { get; }
    
    private async Task QueueSimilarTrackAsync()
    {
        if (!AutoDjEnabled || CurrentTrack == null || _allLibraryTracks.Count == 0) return;
        
        // Find tracks by same artist or genre
        var similarTracks = _allLibraryTracks
            .Where(t => t.Id != CurrentTrack.Id && 
                       !Queue.Any(q => q.Id == t.Id) &&
                       (t.Artist.Equals(CurrentTrack.Artist, StringComparison.OrdinalIgnoreCase) ||
                        t.Genre.Equals(CurrentTrack.Genre, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(_ => Random.Shared.Next())
            .Take(5)
            .ToList();
        
        if (similarTracks.Count == 0)
        {
            // Fall back to random tracks
            similarTracks = _allLibraryTracks
                .Where(t => t.Id != CurrentTrack.Id && !Queue.Any(q => q.Id == t.Id))
                .OrderBy(_ => Random.Shared.Next())
                .Take(5)
                .ToList();
        }
        
        foreach (var track in similarTracks)
        {
            _playerService.AddToQueue(track);
            Queue.Add(track);
        }
        
        StatusMessage = $"Auto DJ: Added {similarTracks.Count} tracks";
    }
    
    #endregion
    
    #region AP-013: Last.fm Scrobbling
    
    private bool _scrobblingEnabled;
    public bool ScrobblingEnabled
    {
        get => _scrobblingEnabled;
        set => SetProperty(ref _scrobblingEnabled, value);
    }
    
    private string _lastFmUsername = "";
    public string LastFmUsername
    {
        get => _lastFmUsername;
        set => SetProperty(ref _lastFmUsername, value);
    }
    
    private string _lastFmStatus = "Not Connected";
    public string LastFmStatus
    {
        get => _lastFmStatus;
        set => SetProperty(ref _lastFmStatus, value);
    }
    
    public ICommand ConnectLastFmCommand { get; }
    
    #endregion
    
    #region AP-014: ReplayGain Support
    
    private bool _replayGainEnabled = true;
    public bool ReplayGainEnabled
    {
        get => _replayGainEnabled;
        set
        {
            if (SetProperty(ref _replayGainEnabled, value))
            {
                _playerService.ReplayGainEnabled = value;
            }
        }
    }
    
    private int _replayGainModeIndex; // 0=Track, 1=Album
    public int ReplayGainModeIndex
    {
        get => _replayGainModeIndex;
        set
        {
            if (SetProperty(ref _replayGainModeIndex, value))
            {
                _playerService.ReplayGainSetting = value == 0 
                    ? EnhancedAudioPlayerService.ReplayGainMode.Track 
                    : EnhancedAudioPlayerService.ReplayGainMode.Album;
            }
        }
    }
    
    #endregion
    
    #region AP-015: Audio Converter Integration
    
    public ICommand ConvertTrackCommand { get; }
    
    public ObservableCollection<string> ConversionFormats { get; } = new()
    {
        "MP3 (320 kbps)", "MP3 (256 kbps)", "MP3 (192 kbps)", "MP3 (128 kbps)",
        "FLAC (Lossless)", "AAC (256 kbps)", "OGG (192 kbps)", "WAV (PCM)"
    };
    
    private string _selectedConversionFormat = "MP3 (320 kbps)";
    public string SelectedConversionFormat
    {
        get => _selectedConversionFormat;
        set => SetProperty(ref _selectedConversionFormat, value);
    }
    
    private async Task ConvertCurrentTrackAsync()
    {
        if (CurrentTrack == null || !File.Exists(CurrentTrack.FilePath)) return;
        
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select output folder for converted file"
        };
        
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        
        var outputDir = dialog.SelectedPath;
        var baseName = Path.GetFileNameWithoutExtension(CurrentTrack.FilePath);
        var (ext, ffmpegArgs) = GetConversionArgs(SelectedConversionFormat);
        var outputPath = Path.Combine(outputDir, baseName + ext);
        
        StatusMessage = $"Converting to {SelectedConversionFormat}...";
        IsConverting = true;
        
        try
        {
            // Build full FFmpeg arguments
            var fullArgs = $"-y -i \"{CurrentTrack.FilePath}\" {ffmpegArgs} \"{outputPath}\"";
            var progress = new Progress<string>(msg =>
            {
                // Parse progress from FFmpeg output (time=xx:xx:xx.xx)
                var match = System.Text.RegularExpressions.Regex.Match(msg, @"time=(\d{2}):(\d{2}):(\d{2})\.(\d{2})");
                if (match.Success && Duration.TotalSeconds > 0)
                {
                    var hours = int.Parse(match.Groups[1].Value);
                    var mins = int.Parse(match.Groups[2].Value);
                    var secs = int.Parse(match.Groups[3].Value);
                    var ms = int.Parse(match.Groups[4].Value) * 10;
                    var elapsed = new TimeSpan(0, hours, mins, secs, ms);
                    ConversionProgress = (int)Math.Min(100, elapsed.TotalSeconds / Duration.TotalSeconds * 100);
                }
            });
            
            var result = await FFmpegService.RunAsync(fullArgs, progress: progress);
            
            StatusMessage = result.Success 
                ? $"Converted: {Path.GetFileName(outputPath)}" 
                : $"Conversion failed: {result.StdErr}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsConverting = false;
            ConversionProgress = 0;
        }
    }
    
    private (string ext, string args) GetConversionArgs(string format)
    {
        return format switch
        {
            "MP3 (320 kbps)" => (".mp3", "-c:a libmp3lame -b:a 320k"),
            "MP3 (256 kbps)" => (".mp3", "-c:a libmp3lame -b:a 256k"),
            "MP3 (192 kbps)" => (".mp3", "-c:a libmp3lame -b:a 192k"),
            "MP3 (128 kbps)" => (".mp3", "-c:a libmp3lame -b:a 128k"),
            "FLAC (Lossless)" => (".flac", "-c:a flac"),
            "AAC (256 kbps)" => (".m4a", "-c:a aac -b:a 256k"),
            "OGG (192 kbps)" => (".ogg", "-c:a libvorbis -b:a 192k"),
            "WAV (PCM)" => (".wav", "-c:a pcm_s16le"),
            _ => (".mp3", "-c:a libmp3lame -b:a 320k")
        };
    }
    
    private bool _isConverting;
    public bool IsConverting
    {
        get => _isConverting;
        set => SetProperty(ref _isConverting, value);
    }
    
    private int _conversionProgress;
    public int ConversionProgress
    {
        get => _conversionProgress;
        set => SetProperty(ref _conversionProgress, value);
    }
    
    #endregion
    
    #region Queue & Library
    
    public ObservableCollection<AudioTrack> Queue { get; } = new();
    
    private ObservableCollection<AudioTrack> _libraryTracks = new();
    public ObservableCollection<AudioTrack> LibraryTracks
    {
        get => _libraryTracks;
        private set => SetProperty(ref _libraryTracks, value);
    }
    
    public ObservableCollection<string> LibraryFolders { get; } = new();
    private List<AudioTrack> _allLibraryTracks = new();
    
    private string? _selectedLibraryFolder;
    public string? SelectedLibraryFolder
    {
        get => _selectedLibraryFolder;
        set
        {
            if (SetProperty(ref _selectedLibraryFolder, value))
            {
                // Notify command to re-evaluate CanExecute
                (RemoveLibraryFolderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }
    }
    
    private bool _includeSubfolders = true;
    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }
    
    private bool _isScanning;
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }
    
    private int _scanProgress;
    public int ScanProgress
    {
        get => _scanProgress;
        set => SetProperty(ref _scanProgress, value);
    }
    
    private string _scanStatus = "";
    public string ScanStatus
    {
        get => _scanStatus;
        set => SetProperty(ref _scanStatus, value);
    }
    
    public int LibraryTrackCount => _allLibraryTracks.Count;
    public int LibraryArtistCount => _allLibraryTracks.Select(t => t.DisplayArtist).Distinct().Count();
    public int LibraryAlbumCount => _allLibraryTracks.Select(t => t.DisplayAlbum).Distinct().Count();
    
    private AudioTrack? _selectedQueueTrack;
    public AudioTrack? SelectedQueueTrack
    {
        get => _selectedQueueTrack;
        set => SetProperty(ref _selectedQueueTrack, value);
    }
    
    private AudioTrack? _selectedLibraryTrack;
    public AudioTrack? SelectedLibraryTrack
    {
        get => _selectedLibraryTrack;
        set => SetProperty(ref _selectedLibraryTrack, value);
    }
    
    private string _searchQuery = "";
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                FilterLibraryTracksDebounced();
        }
    }
    
    /// <summary>
    /// Debounced filter - waits 150ms after last keystroke before filtering.
    /// Prevents UI freeze when typing quickly in large libraries.
    /// </summary>
    private async void FilterLibraryTracksDebounced()
    {
        // Cancel previous debounce
        _searchDebounce?.Cancel();
        _searchDebounce = new CancellationTokenSource();
        var token = _searchDebounce.Token;
        
        try
        {
            // Wait 150ms for user to stop typing
            await Task.Delay(150, token);
            if (token.IsCancellationRequested) return;
            
            FilterLibraryTracks();
        }
        catch (OperationCanceledException)
        {
            // Expected when user types another character
        }
    }
    
    private void FilterLibraryTracks()
    {
        var query = SearchQuery?.ToLowerInvariant() ?? "";
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allLibraryTracks
            : _allLibraryTracks.Where(t =>
                t.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.DisplayArtist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.DisplayAlbum.Contains(query, StringComparison.OrdinalIgnoreCase));
        
        // Display up to 2000 tracks in UI for performance (still plenty for browsing)
        // Create new collection in one shot to avoid UI freeze from individual add notifications
        var tracksToShow = filtered.Take(2000).ToList();
        LibraryTracks = new ObservableCollection<AudioTrack>(tracksToShow);
        
        // Update status if there are more tracks than displayed
        if (_allLibraryTracks.Count > 2000 && tracksToShow.Count == 2000)
            StatusMessage = $"Showing 2,000 of {_allLibraryTracks.Count:N0} tracks. Use search to narrow results.";
    }
    
    // AP-004: Queue Reorder
    public void MoveQueueItem(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= Queue.Count || toIndex < 0 || toIndex >= Queue.Count)
            return;
        
        var item = Queue[fromIndex];
        Queue.RemoveAt(fromIndex);
        Queue.Insert(toIndex, item);
        _playerService.MoveInQueue(fromIndex, toIndex);
    }
    
    #endregion
    
    #region Status & Spectrum
    
    private string _statusMessage = "Ready";
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    
    private float[] _spectrumData = new float[32];
    public float[] SpectrumData
    {
        get => _spectrumData;
        set => SetProperty(ref _spectrumData, value);
    }
    
    // Visualizer properties
    private int _visualizerModeIndex;
    public int VisualizerModeIndex
    {
        get => _visualizerModeIndex;
        set
        {
            if (SetProperty(ref _visualizerModeIndex, value))
                RaisePropertyChanged(nameof(VisualizerModeName));
        }
    }
    
    public string VisualizerModeName => _visualizerModeIndex switch
    {
        0 => "Bars",
        1 => "Mirror",
        2 => "Waveform",
        3 => "Circular",
        4 => "Radial",
        5 => "Particles",
        6 => "Aurora",
        7 => "Wave Grid",
        _ => "Bars"
    };
    
    private int _colorSchemeIndex;
    public int ColorSchemeIndex
    {
        get => _colorSchemeIndex;
        set => SetProperty(ref _colorSchemeIndex, value);
    }
    
    private int _barCount = 32;
    public int BarCount
    {
        get => _barCount;
        set => SetProperty(ref _barCount, value);
    }
    
    // Dropdown collections for visualizer controls
    public List<string> VisualizerModes { get; } = new()
    {
        "Bars", "Mirror", "Waveform", "Circular", "Radial", "Particles", "Aurora", "Wave Grid", "Starfield", "Toasters"
    };
    
    public List<string> ColorSchemes { get; } = new()
    {
        "Blue-Green", "Rainbow", "Fire", "Purple", "Neon", "Ocean", "Sunset", "Monochrome", "Pip-Boy", "LCARS"
    };
    
    public List<int> BarCountOptions { get; } = new() { 16, 24, 32, 48, 64, 72, 96, 128 };
    
    private double _visualizerIntensity = 1.0;
    public double VisualizerIntensity
    {
        get => _visualizerIntensity;
        set => SetProperty(ref _visualizerIntensity, value);
    }
    
    // Sensitivity control (0.1 = very smooth/low sensitivity, 2.0 = very responsive/high sensitivity)
    private double _visualizerSensitivity = 0.7;
    public double VisualizerSensitivity
    {
        get => _visualizerSensitivity;
        set => SetProperty(ref _visualizerSensitivity, Math.Clamp(value, 0.1, 2.0));
    }
    
    // FPS control for visualizer performance
    private int _visualizerFps = 22;
    public int VisualizerFps
    {
        get => _visualizerFps;
        set => SetProperty(ref _visualizerFps, Math.Clamp(value, 10, 60));
    }
    
    public List<int> FpsOptions { get; } = new() { 15, 22, 30, 45, 60 };
    
    // Queue Sort properties
    private string _queueSortBy = "None";
    public string QueueSortBy
    {
        get => _queueSortBy;
        set
        {
            if (SetProperty(ref _queueSortBy, value))
                SortQueue();
        }
    }
    
    public List<string> QueueSortOptions { get; } = new() { "None", "Title", "Artist", "Album", "Duration", "Rating" };
    
    private void SortQueue()
    {
        if (Queue.Count == 0 || QueueSortBy == "None") return;
        
        var sorted = QueueSortBy switch
        {
            "Title" => Queue.OrderBy(t => t.DisplayTitle).ToList(),
            "Artist" => Queue.OrderBy(t => t.DisplayArtist).ToList(),
            "Album" => Queue.OrderBy(t => t.DisplayAlbum).ToList(),
            "Duration" => Queue.OrderBy(t => t.Duration).ToList(),
            "Rating" => Queue.OrderByDescending(t => t.Rating).ToList(),
            _ => null
        };
        
        if (sorted != null)
        {
            Queue.Clear();
            foreach (var track in sorted)
                Queue.Add(track);
        }
    }
    
    // Sleep Timer
    private System.Timers.Timer? _sleepTimer;
    private DateTime _sleepTimerEndTime;
    
    private bool _sleepTimerEnabled;
    public bool SleepTimerEnabled
    {
        get => _sleepTimerEnabled;
        set => SetProperty(ref _sleepTimerEnabled, value);
    }
    
    private string _sleepTimerStatusText = "";
    public string SleepTimerStatusText
    {
        get => _sleepTimerStatusText;
        set => SetProperty(ref _sleepTimerStatusText, value);
    }
    
    private void SetSleepTimer(int minutes)
    {
        _sleepTimer?.Stop();
        _sleepTimer?.Dispose();
        
        if (minutes <= 0)
        {
            // End of current track
            SleepTimerEnabled = true;
            SleepTimerStatusText = "End of track";
            _playerService.TrackEnded += OnSleepTimerTrackEnded;
            return;
        }
        
        _sleepTimerEndTime = DateTime.Now.AddMinutes(minutes);
        SleepTimerEnabled = true;
        
        _sleepTimer = new System.Timers.Timer(1000);
        _sleepTimer.Elapsed += (s, e) =>
        {
            var remaining = _sleepTimerEndTime - DateTime.Now;
            if (remaining.TotalSeconds <= 0)
            {
                // Use InvokeAsync instead of Invoke for better performance (non-blocking)
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _playerService.Stop();
                    CancelSleepTimer();
                    StatusMessage = "Sleep timer: Playback stopped";
                });
            }
            else
            {
                // Use InvokeAsync for UI updates
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SleepTimerStatusText = $"{(int)remaining.TotalMinutes}:{remaining.Seconds:D2}";
                });
            }
        };
        _sleepTimer.Start();
        SleepTimerStatusText = $"{minutes}:00";
    }
    
    private void OnSleepTimerTrackEnded(object? sender, EventArgs e)
    {
        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _playerService.Stop();
            CancelSleepTimer();
            StatusMessage = "Sleep timer: Playback stopped at end of track";
        });
    }
    
    private void CancelSleepTimer()
    {
        _sleepTimer?.Stop();
        _sleepTimer?.Dispose();
        _sleepTimer = null;
        SleepTimerEnabled = false;
        SleepTimerStatusText = "";
        _playerService.TrackEnded -= OnSleepTimerTrackEnded;
    }
    
    #endregion
    
    #region Commands
    
    public ICommand PlayPauseCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand NextCommand { get; }
    public ICommand PreviousCommand { get; }
    public ICommand OpenFileCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand ToggleShuffleCommand { get; }
    public ICommand CycleRepeatCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand PlayTrackCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand RemoveFromQueueCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand PlaySelectedCommand { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand VolumeDownCommand { get; }
    public ICommand ScanLibraryCommand { get; }
    public ICommand SetSleepTimerCommand { get; }
    public ICommand CancelSleepTimerCommand { get; }
    public ICommand AddLibraryFolderCommand { get; }
    public ICommand RemoveLibraryFolderCommand { get; }
    public ICommand ScanAllLibraryFoldersCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand AddAllToQueueCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand DeletePlaylistCommand { get; }
    public ICommand RenamePlaylistCommand { get; }
    public ICommand AddToPlaylistCommand { get; }
    public ICommand LoadPlaylistCommand { get; }
    public ICommand PlayPlaylistCommand { get; }
    public ICommand AddSelectedToPlaylistCommand { get; }
    
    // Library management commands
    public ICommand RemoveTrackFromLibraryCommand { get; }
    public ICommand RemoveSelectedTracksFromLibraryCommand { get; }
    public ICommand ClearLibraryCommand { get; }
    public ICommand RemoveMissingTracksCommand { get; }
    
    // Navigation commands
    public ICommand ScrollToCurrentTrackCommand { get; }
    public ICommand ShowLibraryFromPlaylistCommand { get; }
    
    // Event for UI to scroll queue
    public event EventHandler? RequestScrollToCurrentTrack;
    
    #endregion
    
    public EnhancedAudioPlayerViewModel()
    {
        _playerService = EnhancedAudioPlayerService.Instance;
        _libraryIndexService = new LibraryIndexService();
        _userLibraryService = new UserLibraryService();
        
        // Set visualizer color scheme based on current theme
        UpdateVisualizerColorSchemeForTheme();
        
        // Subscribe to theme changes to update visualizer colors
        ThemeManager.Instance.ThemeChanged += OnThemeChanged;
        
        // Load saved library folders
        _ = LoadLibraryFoldersAsync();
        
        // Subscribe to service events
        _playerService.TrackChanged += OnTrackChanged;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playerService.SpectrumDataUpdated += OnSpectrumDataUpdated;
        _playerService.DurationChanged += OnDurationChanged;
        _playerService.PlaybackError += OnPlaybackError;
        _playerService.TrackEnded += OnTrackEnded;
        
        // Initialize EQ presets
        foreach (var preset in EQPreset.AllPresets)
            EQPresets.Add(preset);
        SelectedEqPreset = EQPresets.FirstOrDefault(p => p.Name == "Flat");
        
        // Initialize smart playlists
        InitializeSmartPlaylists();
        
        // Load user playlists
        _ = LoadUserPlaylistsAsync();
        
        // Initialize commands
        PlayPauseCommand = new RelayCommand(_ => PlayPauseOrFirstInQueue());
        StopCommand = new RelayCommand(_ => _playerService.Stop());
        NextCommand = new AsyncRelayCommand(async () => await _playerService.NextAsync());
        PreviousCommand = new AsyncRelayCommand(async () => await _playerService.PreviousAsync());
        OpenFileCommand = new AsyncRelayCommand(OpenFileAsync);
        OpenFolderCommand = new AsyncRelayCommand(OpenFolderAsync);
        ToggleShuffleCommand = new RelayCommand(_ => IsShuffle = !IsShuffle);
        CycleRepeatCommand = new RelayCommand(_ => RepeatMode = (RepeatMode + 1) % 3);
        ToggleMuteCommand = new RelayCommand(_ => IsMuted = !IsMuted);
        
        PlayTrackCommand = new AsyncRelayCommand<AudioTrack>(async track =>
        {
            if (track != null) await _playerService.PlayTrackAsync(track);
        });
        
        AddToQueueCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track)
            {
                _playerService.AddToQueue(track);
                Queue.Add(track);
                StatusMessage = $"Added: {track.DisplayTitle}";
            }
        });
        
        RemoveFromQueueCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track)
            {
                _playerService.RemoveFromQueue(track);
                Queue.Remove(track);
            }
        });
        
        ClearQueueCommand = new RelayCommand(_ =>
        {
            _playerService.ClearQueue();
            Queue.Clear();
        });
        
        PlaySelectedCommand = new AsyncRelayCommand(async () =>
        {
            if (SelectedLibraryTrack != null)
                await _playerService.PlayTrackAsync(SelectedLibraryTrack);
        });
        
        VolumeUpCommand = new RelayCommand(_ => Volume = Math.Min(1, Volume + 0.05));
        VolumeDownCommand = new RelayCommand(_ => Volume = Math.Max(0, Volume - 0.05));
        
        ScanLibraryCommand = new AsyncRelayCommand(ScanLibraryAsync);
        
        // Library folder management commands
        AddLibraryFolderCommand = new RelayCommand(_ => AddLibraryFolder());
        RemoveLibraryFolderCommand = new RelayCommand(_ => RemoveLibraryFolder(), _ => SelectedLibraryFolder != null);
        ScanAllLibraryFoldersCommand = new AsyncRelayCommand(ScanAllLibraryFoldersAsync);
        CancelScanCommand = new RelayCommand(_ => _scanCts?.Cancel());
        
        // Sleep timer commands
        SetSleepTimerCommand = new RelayCommand(param =>
        {
            if (param is string s && int.TryParse(s, out int minutes))
                SetSleepTimer(minutes);
            else if (param is int m)
                SetSleepTimer(m);
        });
        CancelSleepTimerCommand = new RelayCommand(_ => CancelSleepTimer());
        
        // Feature-specific commands
        SetRatingCommand = new RelayCommand(param =>
        {
            if (param is string s && int.TryParse(s, out int rating))
                CurrentTrackRating = rating;
            else if (param is int r)
                CurrentTrackRating = r;
            
            // Re-sort queue if sorted by rating
            if (QueueSortBy == "Rating")
                SortQueue();
        });
        
        // Add All to Queue command - adds all matching tracks (not limited by display)
        AddAllToQueueCommand = new RelayCommand(_ =>
        {
            var query = SearchQuery?.ToLowerInvariant() ?? "";
            var tracksToAdd = string.IsNullOrWhiteSpace(query)
                ? _allLibraryTracks
                : _allLibraryTracks.Where(t =>
                    t.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.DisplayArtist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    t.DisplayAlbum.Contains(query, StringComparison.OrdinalIgnoreCase));
            
            int addedCount = 0;
            foreach (var track in tracksToAdd)
            {
                if (!Queue.Any(t => t.Id == track.Id))
                {
                    _playerService.AddToQueue(track);
                    Queue.Add(track);
                    addedCount++;
                }
            }
            StatusMessage = $"Added {addedCount:N0} tracks to queue";
        });
        
        // Manual Playlist commands
        CreatePlaylistCommand = new RelayCommand(_ => CreatePlaylist());
        DeletePlaylistCommand = new RelayCommand(_ => DeletePlaylist(), _ => SelectedPlaylist != null && !SelectedPlaylist.IsSmartPlaylist);
        RenamePlaylistCommand = new RelayCommand(_ => RenamePlaylist(), _ => SelectedPlaylist != null && !SelectedPlaylist.IsSmartPlaylist);
        AddToPlaylistCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track && SelectedPlaylist != null && !SelectedPlaylist.IsSmartPlaylist)
            {
                AddTrackToPlaylist(track, SelectedPlaylist);
            }
        });
        LoadPlaylistCommand = new RelayCommand(param =>
        {
            if (param is Playlist playlist)
            {
                SelectedPlaylist = playlist;
            }
        });
        
        PlayPlaylistCommand = new AsyncRelayCommand<Playlist>(async playlist =>
        {
            if (playlist == null) return;
            
            // Load playlist tracks first
            SelectedPlaylist = playlist;
            
            // Clear queue and add all playlist tracks
            Queue.Clear();
            _playerService.ClearQueue();
            
            foreach (var track in LibraryTracks)
            {
                Queue.Add(track);
                _playerService.AddToQueue(track);
            }
            
            // Play first track
            if (Queue.Count > 0)
            {
                await _playerService.PlayTrackAsync(Queue[0]);
            }
        });
        
        AddSelectedToPlaylistCommand = new RelayCommand(param =>
        {
            if (param is Playlist targetPlaylist && SelectedLibraryTrack != null && !targetPlaylist.IsSmartPlaylist)
            {
                AddTrackToPlaylist(SelectedLibraryTrack, targetPlaylist);
            }
        });
        
        // Navigation commands
        ScrollToCurrentTrackCommand = new RelayCommand(_ =>
        {
            if (CurrentTrack != null)
            {
                // Find the track in queue and select it
                var queueTrack = Queue.FirstOrDefault(t => t.Id == CurrentTrack.Id);
                if (queueTrack != null)
                {
                    SelectedQueueTrack = queueTrack;
                    RequestScrollToCurrentTrack?.Invoke(this, EventArgs.Empty);
                }
            }
        });
        
        // Library management commands
        RemoveTrackFromLibraryCommand = new AsyncRelayCommand<AudioTrack>(RemoveTrackFromLibraryAsync);
        RemoveSelectedTracksFromLibraryCommand = new AsyncRelayCommand(RemoveSelectedTrackFromLibraryAsync);
        ClearLibraryCommand = new AsyncRelayCommand(ClearLibraryAsync);
        RemoveMissingTracksCommand = new AsyncRelayCommand(RemoveMissingTracksAsync);
        
        ShowLibraryFromPlaylistCommand = new RelayCommand(_ =>
        {
            // Deselect playlist to show all library tracks
            SelectedPlaylist = null;
            StatusMessage = "Showing all library tracks";
        });
        
        ResetEqCommand = new RelayCommand(_ =>
        {
            _playerService.ResetEq();
            Eq32Hz = Eq64Hz = Eq125Hz = Eq250Hz = Eq500Hz = 0;
            Eq1kHz = Eq2kHz = Eq4kHz = Eq8kHz = Eq16kHz = 0;
        });
        
        ToggleMiniPlayerCommand = new RelayCommand(_ => IsMiniPlayerMode = !IsMiniPlayerMode);
        ToggleFileInfoCommand = new RelayCommand(_ => ShowFileInfoPanel = !ShowFileInfoPanel);
        ToggleAutoDjCommand = new RelayCommand(_ => AutoDjEnabled = !AutoDjEnabled);
        
        ConvertTrackCommand = new AsyncRelayCommand(ConvertCurrentTrackAsync);
        
        ConnectLastFmCommand = new AsyncRelayCommand(async () =>
        {
            // Last.fm OAuth requires API key configuration
            // To enable: add your Last.fm API key and secret in Settings
            LastFmStatus = "Not Connected";
            StatusMessage = "Last.fm requires API credentials. Get your API key at https://www.last.fm/api/account/create";
            
            // Open Last.fm API registration page
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://www.last.fm/api/account/create",
                    UseShellExecute = true
                });
            }
            catch { }
            
            await Task.CompletedTask;
        });
        
        // Load library
        _ = LoadLibraryAsync();
    }
    
    #region Event Handlers
    
    private async void OnTrackChanged(object? sender, AudioTrack? track)
    {
        CurrentTrack = track;
        
        if (track != null)
        {
            // Sync queue display
            if (!Queue.Any(t => t.Id == track.Id))
            {
                Queue.Add(track);
            }
            
            // STEP 1: Try SYNCHRONOUS local .lrc file load first (instant)
            var localLyrics = _playerService.LoadLocalLyricsSync(track);
            if (localLyrics != null)
            {
                CurrentLyrics = localLyrics;
                _lyricsCache[track.Id] = localLyrics;
                RaisePropertyChanged(nameof(HasLyrics));
            }
            // STEP 2: Check memory cache
            else if (_lyricsCache.TryGetValue(track.Id, out var cachedLyrics))
            {
                CurrentLyrics = cachedLyrics;
                RaisePropertyChanged(nameof(HasLyrics));
            }
            // STEP 3: No local file, no cache - try async download (will save .lrc for future)
            else
            {
                CurrentLyrics = null;
                RaisePropertyChanged(nameof(HasLyrics));
                _ = LoadAndCacheLyricsAsync(track);
            }
            
            RaisePropertyChanged(nameof(FileInfoText));
            
            // Pre-fetch lyrics for next track in queue for instant display
            _ = PrefetchNextTrackLyricsAsync();
            
            // Auto DJ: queue more tracks if running low
            if (AutoDjEnabled && Queue.Count <= 2)
            {
                await QueueSimilarTrackAsync();
            }
            
            // Scrobble
            if (ScrobblingEnabled)
            {
                await _playerService.UpdateNowPlayingAsync();
            }
        }
    }
    
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        Position = position;
        
        // Scrobble check
        if (ScrobblingEnabled && CurrentTrack != null)
        {
            _ = _playerService.ScrobbleCurrentTrackAsync();
        }
    }
    
    private async void PlayPauseOrFirstInQueue()
    {
        // If already playing or paused, toggle play/pause
        if (CurrentTrack != null)
        {
            _playerService.PlayPause();
            return;
        }
        
        // Nothing playing - try to play first item in queue
        if (Queue.Count > 0)
        {
            await _playerService.PlayTrackAsync(Queue[0]);
        }
    }
    
    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        IsPlaying = isPlaying;
    }
    
    private void OnSpectrumDataUpdated(object? sender, float[] data)
    {
        SpectrumData = data;
    }
    
    private void OnDurationChanged(object? sender, TimeSpan duration)
    {
        Duration = duration;
    }
    
    /// <summary>
    /// Loads lyrics for a track and stores in cache for instant retrieval.
    /// </summary>
    private async Task LoadAndCacheLyricsAsync(AudioTrack track)
    {
        try
        {
            var lyrics = await _playerService.LoadLyricsAsync(track);
            
            // Store in cache (even if null, to avoid re-fetching)
            _lyricsCache[track.Id] = lyrics;
            
            // Update UI if this is still the current track
            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (CurrentTrack?.Id == track.Id)
                {
                    CurrentLyrics = lyrics;
                    RaisePropertyChanged(nameof(HasLyrics));
                }
            });
        }
        catch { /* Silently ignore lyrics loading errors */ }
    }
    
    /// <summary>
    /// Pre-fetches lyrics for the next track in queue to eliminate delay.
    /// </summary>
    private async Task PrefetchNextTrackLyricsAsync()
    {
        try
        {
            if (CurrentTrack == null || Queue.Count <= 1) return;
            
            var currentIndex = Queue.IndexOf(CurrentTrack);
            if (currentIndex < 0)
            {
                // Find by Id without creating a list copy
                for (int i = 0; i < Queue.Count; i++)
                {
                    if (Queue[i].Id == CurrentTrack.Id)
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }
            
            var nextIndex = currentIndex + 1;
            if (nextIndex < Queue.Count)
            {
                var nextTrack = Queue[nextIndex];
                
                // Skip if already cached
                if (_lyricsCache.ContainsKey(nextTrack.Id)) return;
                
                // Pre-fetch and cache lyrics
                var lyrics = await _playerService.LoadLyricsAsync(nextTrack);
                _lyricsCache[nextTrack.Id] = lyrics;
            }
        }
        catch { /* Silently ignore prefetch errors */ }
    }
    
    /// <summary>
    /// Background task that scans the library and downloads missing lyrics.
    /// Downloaded lyrics are saved as .lrc files for instant future loading.
    /// Runs slowly to avoid impacting UI performance and rate limiting.
    /// </summary>
    private async Task BackgroundLyricsScanAsync()
    {
        try
        {
            // Wait before starting to let UI settle
            await Task.Delay(5000);
            
            var tracksWithoutLrc = _allLibraryTracks
                .Where(t => !string.IsNullOrEmpty(t.Artist) && !string.IsNullOrEmpty(t.Title))
                .Where(t => 
                {
                    // Check if .lrc file already exists
                    var dir = Path.GetDirectoryName(t.FilePath);
                    var baseName = Path.GetFileNameWithoutExtension(t.FilePath);
                    if (string.IsNullOrEmpty(dir)) return false;
                    
                    var lrcPath = Path.Combine(dir, baseName + ".lrc");
                    return !File.Exists(lrcPath);
                })
                .Take(100) // Process 100 tracks per session
                .ToList();
            
            if (tracksWithoutLrc.Count == 0) return;
            
            System.Diagnostics.Debug.WriteLine($"Background lyrics scan: {tracksWithoutLrc.Count} tracks to process");
            
            int downloaded = 0;
            foreach (var track in tracksWithoutLrc)
            {
                try
                {
                    // LoadLyricsAsync will download and save to .lrc file
                    var lyrics = await _playerService.LoadLyricsAsync(track);
                    if (lyrics != null)
                    {
                        downloaded++;
                        _lyricsCache[track.Id] = lyrics;
                    }
                    
                    // Rate limit: wait 2 seconds between requests to be nice to the API
                    await Task.Delay(2000);
                }
                catch { /* Ignore individual track errors */ }
            }
            
            if (downloaded > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Background lyrics scan: Downloaded {downloaded} lyrics files");
            }
        }
        catch { /* Ignore scan errors */ }
    }
    
    /// <summary>
    /// Background task that scans the library and downloads missing album art.
    /// Downloaded art is saved as folder.jpg for instant future loading.
    /// Runs slowly to avoid impacting UI performance and rate limiting.
    /// </summary>
    private async Task BackgroundAlbumArtScanAsync()
    {
        try
        {
            // Wait before starting (after lyrics scan has some time)
            await Task.Delay(10000);
            
            // Group by album folder to avoid duplicate downloads
            var foldersWithoutArt = _allLibraryTracks
                .Where(t => !string.IsNullOrEmpty(t.Artist) && !string.IsNullOrEmpty(t.Album))
                .Select(t => new { Track = t, Directory = Path.GetDirectoryName(t.FilePath) })
                .Where(x => !string.IsNullOrEmpty(x.Directory))
                .GroupBy(x => x.Directory)
                .Where(g => !AlbumArtFilenames.Any(f => File.Exists(Path.Combine(g.Key!, f))))
                .Select(g => g.First().Track)
                .Take(50) // Process 50 albums per session
                .ToList();
            
            if (foldersWithoutArt.Count == 0) return;
            
            System.Diagnostics.Debug.WriteLine($"Background album art scan: {foldersWithoutArt.Count} albums to process");
            
            int downloaded = 0;
            foreach (var track in foldersWithoutArt)
            {
                try
                {
                    await DownloadAlbumArtAsync(track);
                    
                    // Check if we actually saved a file
                    var dir = Path.GetDirectoryName(track.FilePath);
                    if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "folder.jpg")))
                    {
                        downloaded++;
                    }
                    
                    // Rate limit: wait 3 seconds between requests (MusicBrainz rate limit)
                    await Task.Delay(3000);
                }
                catch { /* Ignore individual track errors */ }
            }
            
            if (downloaded > 0)
            {
                System.Diagnostics.Debug.WriteLine($"Background album art scan: Downloaded {downloaded} cover images");
            }
        }
        catch { /* Ignore scan errors */ }
    }
    
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        // Update visualizer color scheme when theme changes
        UpdateVisualizerColorSchemeForTheme();
    }
    
    /// <summary>
    /// Sets the default visualizer color scheme based on the current application theme.
    /// PipBoy theme -> PipBoy colors (index 8)
    /// LCARS theme -> LCARS colors (index 9)
    /// Light/Dark themes -> Rainbow (index 1)
    /// </summary>
    private void UpdateVisualizerColorSchemeForTheme()
    {
        var theme = ThemeManager.Instance.CurrentTheme;
        
        int newColorScheme = theme switch
        {
            ThemeManager.PipBoy => 8,  // PipBoy green phosphor
            ThemeManager.LCARS => 9,   // LCARS orange/tan/purple
            ThemeManager.Light => 1,   // Rainbow for Light theme
            ThemeManager.Dark => 1,    // Rainbow for Dark theme
            _ => 1                     // Default to Rainbow
        };
        
        // Only update if it's different to avoid unnecessary redraws
        if (_colorSchemeIndex != newColorScheme)
        {
            ColorSchemeIndex = newColorScheme;
            System.Diagnostics.Debug.WriteLine($"Visualizer color scheme updated to {newColorScheme} for theme {theme}");
        }
    }
    
    private async void OnPlaybackError(object? sender, string error)
    {
        StatusMessage = $"Skipping (error: {error})";
        
        // Auto-skip to next track on playback error
        await Task.Delay(500); // Brief delay so user sees the message
        await _playerService.NextAsync();
    }
    
    private async void OnTrackEnded(object? sender, EventArgs e)
    {
        if (AutoDjEnabled)
        {
            await QueueSimilarTrackAsync();
        }
    }
    
    #endregion
    
    #region File Operations
    
    private async Task OpenFileAsync()
    {
        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.flac;*.wav;*.ogg;*.m4a;*.aac;*.wma|All Files|*.*",
            Multiselect = true
        };
        
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        
        foreach (var file in dialog.FileNames)
        {
            var track = await LoadTrackAsync(file);
            if (track != null)
            {
                _playerService.AddToQueue(track);
                Queue.Add(track);
            }
        }
        
        if (Queue.Count > 0 && !IsPlaying)
        {
            await _playerService.PlayTrackAsync(Queue[0]);
        }
    }
    
    private async Task OpenFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to add to queue"
        };
        
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        
        var extensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma" };
        var files = Directory.GetFiles(dialog.SelectedPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        
        int added = 0;
        foreach (var file in files)
        {
            var track = await LoadTrackAsync(file);
            if (track != null)
            {
                _playerService.AddToQueue(track);
                Queue.Add(track);
                added++;
            }
        }
        
        StatusMessage = $"Added {added} tracks from folder";
        
        if (Queue.Count > 0 && !IsPlaying)
        {
            await _playerService.PlayTrackAsync(Queue[0]);
        }
    }
    
    private async Task<AudioTrack?> LoadTrackAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        
        try
        {
            var track = new AudioTrack
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath)
            };
            
            // Read metadata using TagLib
            await Task.Run(() =>
            {
                try
                {
                    using var file = TagLib.File.Create(filePath);
                    track.Title = file.Tag.Title ?? track.Title;
                    track.Artist = file.Tag.FirstPerformer ?? "";
                    track.Album = file.Tag.Album ?? "";
                    track.AlbumArtist = file.Tag.FirstAlbumArtist ?? "";
                    track.Genre = file.Tag.FirstGenre ?? "";
                    track.Year = (int)file.Tag.Year;
                    track.TrackNumber = (int)file.Tag.Track;
                    track.DiscNumber = (int)file.Tag.Disc;
                    track.Duration = file.Properties.Duration;
                    track.Bitrate = file.Properties.AudioBitrate;
                    track.SampleRate = file.Properties.AudioSampleRate;
                    track.Channels = file.Properties.AudioChannels;
                    track.FileSize = new FileInfo(filePath).Length;
                    
                    // Check for ReplayGain tags
                    if (file.Tag is TagLib.Ogg.XiphComment xiph)
                    {
                        if (double.TryParse(xiph.GetFirstField("REPLAYGAIN_TRACK_GAIN")?.Replace(" dB", ""), out var tg))
                            track.TrackGain = tg;
                        if (double.TryParse(xiph.GetFirstField("REPLAYGAIN_ALBUM_GAIN")?.Replace(" dB", ""), out var ag))
                            track.AlbumGain = ag;
                    }
                }
                catch { }
            });
            
            return track;
        }
        catch
        {
            return null;
        }
    }
    
    private async Task ScanLibraryAsync()
    {
        const int BatchSize = 300;
        
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select music library folder"
        };
        
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        
        StatusMessage = "Scanning library...";
        _scanCts = new CancellationTokenSource();
        
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma"
        };
        
        _allLibraryTracks.Clear();
        int count = 0;
        
        // Use robust enumeration for deep directories
        foreach (var file in EnumerateAudioFilesRobust(dialog.SelectedPath, true, extensions))
        {
            if (_scanCts.Token.IsCancellationRequested) break;
            
            var track = await LoadTrackAsync(file);
            if (track != null)
            {
                _allLibraryTracks.Add(track);
                count++;
                
                // Update UI in batches
                if (count % BatchSize == 0)
                {
                    StatusMessage = $"Scanned {count} files ({_allLibraryTracks.Count} tracks)...";
                    FilterLibraryTracks();
                    await Task.Delay(1); // Yield to UI
                }
            }
        }
        
        FilterLibraryTracks();
        StatusMessage = $"Library: {_allLibraryTracks.Count} tracks";
        
        // Save cache
        await SaveLibraryCacheAsync();
    }
    
    private async Task LoadLibraryAsync()
    {
        // Load cached library if available
        var cacheFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "enhanced_audio_library.json");
        
        if (File.Exists(cacheFile))
        {
            try
            {
                StatusMessage = "Loading library cache...";
                
                // Use stream-based deserialization for better performance with large files
                await using var fileStream = new FileStream(cacheFile, FileMode.Open, FileAccess.Read, FileShare.Read, 
                    bufferSize: 65536, useAsync: true); // 64KB buffer for faster reads
                
                _allLibraryTracks = await JsonSerializer.DeserializeAsync<List<AudioTrack>>(fileStream) ?? new();
                
                // Update UI on main thread - batch operation for performance
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    FilterLibraryTracks();
                    RaisePropertyChanged(nameof(LibraryTrackCount));
                    RaisePropertyChanged(nameof(LibraryArtistCount));
                    RaisePropertyChanged(nameof(LibraryAlbumCount));
                    StatusMessage = $"Library: {_allLibraryTracks.Count:N0} tracks";
                });
                
                // Start background scanning after library loads
                _ = BackgroundLyricsScanAsync();
                _ = BackgroundAlbumArtScanAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading library cache: {ex.Message}");
                StatusMessage = "Library cache load failed";
            }
        }
    }
    
    #endregion
    
    #region Library Folder Management
    
    private void AddLibraryFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select a folder to add to your music library"
        };
        
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        
        var folder = dialog.SelectedPath;
        if (!string.IsNullOrEmpty(folder) && !LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            LibraryFolders.Add(folder);
            _ = SaveLibraryFoldersAsync();
            StatusMessage = $"Added folder: {Path.GetFileName(folder)}";
        }
        else if (LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            StatusMessage = "Folder already in library";
        }
    }
    
    private void RemoveLibraryFolder()
    {
        if (SelectedLibraryFolder == null) return;
        LibraryFolders.Remove(SelectedLibraryFolder);
        _ = SaveLibraryFoldersAsync();
        StatusMessage = "Folder removed from library";
    }
    
    private async Task ScanAllLibraryFoldersAsync()
    {
        const int BatchSize = 300;
        const int SaveInterval = 600; // Save cache every 600 files
        
        if (LibraryFolders.Count == 0)
        {
            StatusMessage = "No library folders. Add a folder first.";
            return;
        }
        
        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        
        // PERFORMANCE: Build index of existing tracks for incremental scanning
        // This allows skipping files already in the library (by path + file size)
        var existingTracks = _allLibraryTracks.ToDictionary(
            t => (t.FilePath.ToLowerInvariant(), t.FileSize),
            t => t);
        
        _allLibraryTracks.Clear();
        
        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma"
        };
        
        int scannedFiles = 0;
        int skippedFiles = 0;
        int batchCount = 0;
        var batchTracks = new List<AudioTrack>();
        
        ScanStatus = "Discovering files...";
        
        // Process folders using robust enumeration
        foreach (var folder in LibraryFolders.ToList())
        {
            if (_scanCts.Token.IsCancellationRequested) break;
            if (!Directory.Exists(folder)) continue;
            
            try
            {
                // Use robust enumeration for deep directories
                foreach (var file in EnumerateAudioFilesRobust(folder, IncludeSubfolders, extensions))
                {
                    if (_scanCts.Token.IsCancellationRequested) break;
                    
                    // INCREMENTAL: Check if file already scanned (same path + size = unchanged)
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var key = (file.ToLowerInvariant(), fileInfo.Length);
                        
                        if (existingTracks.TryGetValue(key, out var existingTrack))
                        {
                            // File unchanged, reuse existing track metadata
                            _allLibraryTracks.Add(existingTrack);
                            batchTracks.Add(existingTrack);
                            skippedFiles++;
                        }
                        else
                        {
                            // New or modified file, scan metadata
                            var track = await LoadTrackAsync(file);
                            if (track != null)
                            {
                                _allLibraryTracks.Add(track);
                                batchTracks.Add(track);
                            }
                        }
                    }
                    catch
                    {
                        // If FileInfo fails, fall back to full scan
                        var track = await LoadTrackAsync(file);
                        if (track != null)
                        {
                            _allLibraryTracks.Add(track);
                            batchTracks.Add(track);
                        }
                    }
                    
                    scannedFiles++;
                    
                    // Update UI in batches of 300
                    if (scannedFiles % BatchSize == 0)
                    {
                        batchCount++;
                        ScanStatus = $"Scanned {scannedFiles} files ({_allLibraryTracks.Count} tracks)...";
                        ScanProgress = Math.Min(99, batchCount * 5); // Progress indicator
                        
                        // Update filtered view and counts in real-time
                        FilterLibraryTracks();
                        RaisePropertyChanged(nameof(LibraryTrackCount));
                        RaisePropertyChanged(nameof(LibraryArtistCount));
                        RaisePropertyChanged(nameof(LibraryAlbumCount));
                        
                        // Clear batch and yield to UI
                        batchTracks.Clear();
                        await Task.Delay(1);
                    }
                    
                    // Save cache periodically
                    if (scannedFiles % SaveInterval == 0)
                    {
                        await SaveLibraryCacheAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning folder {folder}: {ex.Message}");
            }
        }
        
        IsScanning = false;
        ScanStatus = "";
        ScanProgress = 100;
        FilterLibraryTracks();
        RaisePropertyChanged(nameof(LibraryTrackCount));
        RaisePropertyChanged(nameof(LibraryArtistCount));
        RaisePropertyChanged(nameof(LibraryAlbumCount));
        
        // Show incremental scan results if applicable
        if (skippedFiles > 0)
            StatusMessage = $"Library updated: {_allLibraryTracks.Count} tracks ({skippedFiles} cached, {scannedFiles - skippedFiles} new)";
        else
            StatusMessage = $"Library updated: {_allLibraryTracks.Count} tracks from {LibraryFolders.Count} folders";
        
        // Final save
        await SaveLibraryCacheAsync();
    }
    
    /// <summary>
    /// Enumerate audio files robustly, handling access denied and deep directories.
    /// </summary>
    private IEnumerable<string> EnumerateAudioFilesRobust(string directory, bool recursive, HashSet<string> extensions)
    {
        var dirsToProcess = new Queue<string>();
        dirsToProcess.Enqueue(directory);
        
        while (dirsToProcess.Count > 0)
        {
            var currentDir = dirsToProcess.Dequeue();
            
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (PathTooLongException)
            {
                continue;
            }
            catch
            {
                continue;
            }
            
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (extensions.Contains(ext))
                    yield return file;
            }
            
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
    
    private async Task SaveLibraryCacheAsync()
    {
        try
        {
            var cacheFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "enhanced_audio_library.json");
            
            var dir = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            // Use stream-based serialization for better performance with large libraries
            await using var fileStream = new FileStream(cacheFile, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 65536, useAsync: true);
            await JsonSerializer.SerializeAsync(fileStream, _allLibraryTracks);
        }
        catch { }
    }
    
    private async Task SaveLibraryFoldersAsync()
    {
        try
        {
            var foldersPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "enhanced_library_folders.json");
            
            var dir = Path.GetDirectoryName(foldersPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            
            var json = JsonSerializer.Serialize(LibraryFolders.ToList());
            await File.WriteAllTextAsync(foldersPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving library folders: {ex.Message}");
        }
    }
    
    private async Task LoadLibraryFoldersAsync()
    {
        try
        {
            var foldersPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "enhanced_library_folders.json");
            
            if (File.Exists(foldersPath))
            {
                var json = await File.ReadAllTextAsync(foldersPath);
                var folders = JsonSerializer.Deserialize<List<string>>(json);
                if (folders != null)
                {
                    foreach (var folder in folders)
                    {
                        if (!LibraryFolders.Contains(folder))
                            LibraryFolders.Add(folder);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading library folders: {ex.Message}");
        }
    }
    
    #endregion
    
    #region Track Removal
    
    /// <summary>
    /// Remove a single track from the library.
    /// </summary>
    private async Task RemoveTrackFromLibraryAsync(AudioTrack? track)
    {
        if (track == null) return;
        
        _allLibraryTracks.RemoveAll(t => t.Id == track.Id);
        LibraryTracks.Remove(track);
        
        // Also remove from queue if present
        var queueTrack = Queue.FirstOrDefault(t => t.Id == track.Id);
        if (queueTrack != null)
        {
            Queue.Remove(queueTrack);
            _playerService.RemoveFromQueue(queueTrack);
        }
        
        // Update stats
        RaisePropertyChanged(nameof(LibraryTrackCount));
        RaisePropertyChanged(nameof(LibraryArtistCount));
        RaisePropertyChanged(nameof(LibraryAlbumCount));
        
        // Save the updated library cache
        await SaveLibraryCacheAsync();
        
        StatusMessage = $"Removed: {track.DisplayTitle}";
    }
    
    /// <summary>
    /// Remove the currently selected track from the library.
    /// </summary>
    private async Task RemoveSelectedTrackFromLibraryAsync()
    {
        if (SelectedLibraryTrack == null) return;
        await RemoveTrackFromLibraryAsync(SelectedLibraryTrack);
    }
    
    /// <summary>
    /// Clear all tracks from the library.
    /// </summary>
    private async Task ClearLibraryAsync()
    {
        var result = MessageBox.Show(
            $"Are you sure you want to clear all {_allLibraryTracks.Count:N0} tracks from the library?\n\nThis will not delete the actual files, only remove them from the library index.",
            "Clear Library",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        
        if (result != MessageBoxResult.Yes) return;
        
        var count = _allLibraryTracks.Count;
        _allLibraryTracks.Clear();
        LibraryTracks.Clear();
        
        // Also clear the queue
        Queue.Clear();
        _playerService.ClearQueue();
        
        // Update stats
        RaisePropertyChanged(nameof(LibraryTrackCount));
        RaisePropertyChanged(nameof(LibraryArtistCount));
        RaisePropertyChanged(nameof(LibraryAlbumCount));
        
        // Save the updated library cache
        await SaveLibraryCacheAsync();
        
        // Also clear the LibraryIndexService
        await _libraryIndexService.ClearAsync();
        
        StatusMessage = $"Cleared {count:N0} tracks from library";
    }
    
    /// <summary>
    /// Remove tracks whose files no longer exist on disk.
    /// </summary>
    private async Task RemoveMissingTracksAsync()
    {
        StatusMessage = "Checking for missing files...";
        IsScanning = true;
        
        var missingTracks = new List<AudioTrack>();
        var total = _allLibraryTracks.Count;
        var checked_count = 0;
        
        foreach (var track in _allLibraryTracks.ToList())
        {
            if (!File.Exists(track.FilePath))
            {
                missingTracks.Add(track);
            }
            
            checked_count++;
            if (checked_count % 100 == 0)
            {
                ScanProgress = (int)((double)checked_count / total * 100);
                ScanStatus = $"Checked {checked_count:N0}/{total:N0} files...";
                await Task.Delay(1); // Yield to UI
            }
        }
        
        if (missingTracks.Count > 0)
        {
            var result = MessageBox.Show(
                $"Found {missingTracks.Count:N0} tracks with missing files.\n\nRemove them from the library?",
                "Missing Tracks",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            
            if (result == MessageBoxResult.Yes)
            {
                foreach (var track in missingTracks)
                {
                    _allLibraryTracks.RemoveAll(t => t.Id == track.Id);
                    LibraryTracks.Remove(track);
                    
                    // Also remove from queue if present
                    var queueTrack = Queue.FirstOrDefault(t => t.Id == track.Id);
                    if (queueTrack != null)
                    {
                        Queue.Remove(queueTrack);
                        _playerService.RemoveFromQueue(queueTrack);
                    }
                }
                
                // Update stats
                RaisePropertyChanged(nameof(LibraryTrackCount));
                RaisePropertyChanged(nameof(LibraryArtistCount));
                RaisePropertyChanged(nameof(LibraryAlbumCount));
                
                // Save the updated library cache
                await SaveLibraryCacheAsync();
                
                StatusMessage = $"Removed {missingTracks.Count:N0} missing tracks";
            }
            else
            {
                StatusMessage = $"Found {missingTracks.Count:N0} missing tracks (not removed)";
            }
        }
        else
        {
            StatusMessage = "All tracks found on disk";
        }
        
        IsScanning = false;
        ScanProgress = 0;
        ScanStatus = "";
    }
    
    #endregion
    
    #region Helpers
    
    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:0.##} {sizes[order]}";
    }
    
    #endregion
    
    public void Dispose()
    {
        _scanCts?.Cancel();
        _playerService.TrackChanged -= OnTrackChanged;
        _playerService.PositionChanged -= OnPositionChanged;
        _playerService.PlaybackStateChanged -= OnPlaybackStateChanged;
        _playerService.SpectrumDataUpdated -= OnSpectrumDataUpdated;
        _playerService.DurationChanged -= OnDurationChanged;
        _playerService.PlaybackError -= OnPlaybackError;
        _playerService.TrackEnded -= OnTrackEnded;
    }
}
