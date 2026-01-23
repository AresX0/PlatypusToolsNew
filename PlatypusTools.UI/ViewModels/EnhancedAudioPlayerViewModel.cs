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
    
    private void LoadAlbumArt(AudioTrack? track)
    {
        if (track?.AlbumArt != null && track.AlbumArt.Length > 0)
        {
            try
            {
                var image = new BitmapImage();
                using (var stream = new MemoryStream(track.AlbumArt))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                }
                AlbumArtImage = image;
                return;
            }
            catch { }
        }
        
        // No embedded art - try to download
        AlbumArtImage = null;
        if (track != null && !string.IsNullOrEmpty(track.Artist) && !string.IsNullOrEmpty(track.Album))
        {
            _ = DownloadAlbumArtAsync(track);
        }
    }
    
    private async Task DownloadAlbumArtAsync(AudioTrack track)
    {
        try
        {
            using var httpClient = new System.Net.Http.HttpClient();
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
                    // Save to track for future use
                    track.AlbumArt = imageBytes;
                    
                    // Display it
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            var image = new BitmapImage();
                            using (var stream = new MemoryStream(imageBytes))
                            {
                                image.BeginInit();
                                image.CacheOption = BitmapCacheOption.OnLoad;
                                image.StreamSource = stream;
                                image.EndInit();
                                image.Freeze();
                            }
                            AlbumArtImage = image;
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
            SaveUserPlaylists();
            StatusMessage = $"Created playlist: {name}";
        }
    }
    
    private void DeletePlaylist()
    {
        if (SelectedPlaylist == null || SelectedPlaylist.IsSmartPlaylist) return;
        
        UserPlaylists.Remove(SelectedPlaylist);
        SmartPlaylists.Remove(SelectedPlaylist);
        SaveUserPlaylists();
        StatusMessage = "Playlist deleted";
    }
    
    private void RenamePlaylist()
    {
        if (SelectedPlaylist == null || SelectedPlaylist.IsSmartPlaylist) return;
        
        string newName = Microsoft.VisualBasic.Interaction.InputBox("Enter new name:", "Rename Playlist", SelectedPlaylist.Name);
        if (!string.IsNullOrWhiteSpace(newName))
        {
            SelectedPlaylist.Name = newName;
            SaveUserPlaylists();
            StatusMessage = $"Renamed to: {newName}";
        }
    }
    
    private void AddTrackToPlaylist(AudioTrack track, Playlist playlist)
    {
        playlist.TrackIds ??= new List<string>();
        if (!playlist.TrackIds.Contains(track.Id))
        {
            playlist.TrackIds.Add(track.Id);
            SaveUserPlaylists();
            StatusMessage = $"Added {track.DisplayTitle} to {playlist.Name}";
        }
    }
    
    private void SaveUserPlaylists()
    {
        try
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatypusTools");
            Directory.CreateDirectory(appDataPath);
            var playlistsPath = Path.Combine(appDataPath, "enhanced_user_playlists.json");
            var json = JsonSerializer.Serialize(UserPlaylists.ToList(), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(playlistsPath, json);
        }
        catch { }
    }
    
    private void LoadUserPlaylists()
    {
        try
        {
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatypusTools");
            var playlistsPath = Path.Combine(appDataPath, "enhanced_user_playlists.json");
            if (File.Exists(playlistsPath))
            {
                var json = File.ReadAllText(playlistsPath);
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
    public ObservableCollection<AudioTrack> LibraryTracks { get; } = new();
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
                FilterLibraryTracks();
        }
    }
    
    private void FilterLibraryTracks()
    {
        LibraryTracks.Clear();
        var query = SearchQuery.ToLowerInvariant();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allLibraryTracks
            : _allLibraryTracks.Where(t =>
                t.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.DisplayArtist.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.DisplayAlbum.Contains(query, StringComparison.OrdinalIgnoreCase));
        
        foreach (var track in filtered.Take(500))
            LibraryTracks.Add(track);
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
        "Bars", "Mirror", "Waveform", "Circular", "Radial", "Particles", "Aurora", "Wave Grid"
    };
    
    public List<string> ColorSchemes { get; } = new()
    {
        "Blue-Green", "Rainbow", "Fire", "Purple", "Neon", "Ocean", "Sunset", "Monochrome"
    };
    
    public List<int> BarCountOptions { get; } = new() { 16, 24, 32, 48, 64, 72, 96, 128 };
    
    private double _visualizerIntensity = 1.0;
    public double VisualizerIntensity
    {
        get => _visualizerIntensity;
        set => SetProperty(ref _visualizerIntensity, value);
    }
    
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
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _playerService.Stop();
                    CancelSleepTimer();
                    StatusMessage = "Sleep timer: Playback stopped";
                });
            }
            else
            {
                Application.Current.Dispatcher.Invoke(() =>
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
        Application.Current.Dispatcher.Invoke(() =>
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
    
    #endregion
    
    public EnhancedAudioPlayerViewModel()
    {
        _playerService = EnhancedAudioPlayerService.Instance;
        _libraryIndexService = new LibraryIndexService();
        _userLibraryService = new UserLibraryService();
        
        // Load saved library folders
        LoadLibraryFolders();
        
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
        LoadUserPlaylists();
        
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
        
        // Add All to Queue command
        AddAllToQueueCommand = new RelayCommand(_ =>
        {
            foreach (var track in LibraryTracks)
            {
                if (!Queue.Any(t => t.Id == track.Id))
                {
                    _playerService.AddToQueue(track);
                    Queue.Add(track);
                }
            }
            StatusMessage = $"Added {LibraryTracks.Count} tracks to queue";
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
            
            // Load lyrics
            CurrentLyrics = await _playerService.LoadLyricsAsync(track);
            RaisePropertyChanged(nameof(HasLyrics));
            RaisePropertyChanged(nameof(FileInfoText));
            
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
    
    private void OnPlaybackError(object? sender, string error)
    {
        StatusMessage = $"Error: {error}";
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
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select music library folder"
        };
        
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        
        StatusMessage = "Scanning library...";
        _scanCts = new CancellationTokenSource();
        
        var extensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma" };
        var files = Directory.GetFiles(dialog.SelectedPath, "*.*", SearchOption.AllDirectories)
            .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        
        _allLibraryTracks.Clear();
        int count = 0;
        
        foreach (var file in files)
        {
            if (_scanCts.Token.IsCancellationRequested) break;
            
            var track = await LoadTrackAsync(file);
            if (track != null)
            {
                _allLibraryTracks.Add(track);
                count++;
                
                if (count % 50 == 0)
                    StatusMessage = $"Scanned {count} / {files.Count} files...";
            }
        }
        
        FilterLibraryTracks();
        StatusMessage = $"Library: {_allLibraryTracks.Count} tracks";
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
                var json = await File.ReadAllTextAsync(cacheFile);
                _allLibraryTracks = JsonSerializer.Deserialize<List<AudioTrack>>(json) ?? new();
                FilterLibraryTracks();
                StatusMessage = $"Library: {_allLibraryTracks.Count} tracks";
            }
            catch { }
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
            SaveLibraryFolders();
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
        SaveLibraryFolders();
        StatusMessage = "Folder removed from library";
    }
    
    private async Task ScanAllLibraryFoldersAsync()
    {
        if (LibraryFolders.Count == 0)
        {
            StatusMessage = "No library folders. Add a folder first.";
            return;
        }
        
        IsScanning = true;
        _scanCts = new CancellationTokenSource();
        _allLibraryTracks.Clear();
        
        var extensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma" };
        int totalFiles = 0;
        int scannedFiles = 0;
        
        // First pass: count files
        foreach (var folder in LibraryFolders.ToList())
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                totalFiles += Directory.GetFiles(folder, "*.*", searchOption)
                    .Count(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            }
            catch { }
        }
        
        ScanStatus = $"Scanning 0/{totalFiles} files...";
        
        // Second pass: scan files
        foreach (var folder in LibraryFolders.ToList())
        {
            if (_scanCts.Token.IsCancellationRequested) break;
            if (!Directory.Exists(folder)) continue;
            
            try
            {
                var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(folder, "*.*", searchOption)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();
                
                foreach (var file in files)
                {
                    if (_scanCts.Token.IsCancellationRequested) break;
                    
                    var track = await LoadTrackAsync(file);
                    if (track != null)
                    {
                        _allLibraryTracks.Add(track);
                    }
                    
                    scannedFiles++;
                    ScanProgress = totalFiles > 0 ? (scannedFiles * 100) / totalFiles : 0;
                    
                    if (scannedFiles % 25 == 0)
                        ScanStatus = $"Scanning {scannedFiles}/{totalFiles} files...";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning folder {folder}: {ex.Message}");
            }
        }
        
        IsScanning = false;
        ScanStatus = "";
        FilterLibraryTracks();
        RaisePropertyChanged(nameof(LibraryTrackCount));
        RaisePropertyChanged(nameof(LibraryArtistCount));
        RaisePropertyChanged(nameof(LibraryAlbumCount));
        StatusMessage = $"Library updated: {_allLibraryTracks.Count} tracks from {LibraryFolders.Count} folders";
        
        // Save library cache
        await SaveLibraryCacheAsync();
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
            
            var json = JsonSerializer.Serialize(_allLibraryTracks);
            await File.WriteAllTextAsync(cacheFile, json);
        }
        catch { }
    }
    
    private void SaveLibraryFolders()
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
            File.WriteAllText(foldersPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving library folders: {ex.Message}");
        }
    }
    
    private void LoadLibraryFolders()
    {
        try
        {
            var foldersPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "enhanced_library_folders.json");
            
            if (File.Exists(foldersPath))
            {
                var json = File.ReadAllText(foldersPath);
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
