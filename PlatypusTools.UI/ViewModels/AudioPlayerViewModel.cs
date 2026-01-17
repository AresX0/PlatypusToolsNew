using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;
using System.Windows.Forms;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// ViewModel for the integrated Audio Player with visualizer, library organization, and queue management.
/// </summary>
public class AudioPlayerViewModel : BindableBase
{
    // Services
    private readonly AudioPlayerService _playerService;
    private readonly LibraryIndexService _libraryIndexService;
    private readonly UserLibraryService _userLibraryService;
    
    private AudioTrack? _currentTrack;
    private TimeSpan _position;
    private TimeSpan _duration;
    private double _volume = 0.7;
    private bool _isMuted;
    private bool _isPlaying;
    private bool _isShuffle;
    private int _repeatMode; // 0=None, 1=All, 2=One
    private string _statusMessage = "Ready";
    private double[] _spectrumData = new double[32];
    private EQPreset _selectedPreset;
    
    // New properties for enhanced features
    private bool _includeSubfolders = true;
    private int _viewModeIndex = 0; // 0=Player, 1=Library
    private int _visualizerModeIndex = 0; // 0=Bars, 1=Mirror, 2=Waveform, 3=Circular, 4=None
    private int _organizeModeIndex = 0; // 0=All, 1=Artist, 2=Album, 3=Genre, 4=Folder
    private string _searchQuery = string.Empty;
    private bool _isScanning;
    private string _scanStatus = string.Empty;
    private int _scanProgress;
    private AudioTrack? _selectedLibraryTrack;
    private LibraryGroup? _selectedGroup;
    
    // Library data
    private List<AudioTrack> _allLibraryTracks = new();
    private List<LibraryGroup> _libraryGroups = new();
    
    // Library folder management
    public ObservableCollection<string> LibraryFolders { get; } = new();
    private string? _selectedLibraryFolder;
    
    public string? SelectedLibraryFolder
    {
        get => _selectedLibraryFolder;
        set => SetProperty(ref _selectedLibraryFolder, value);
    }
    
    // Properties
    public AudioTrack? CurrentTrack
    {
        get => _currentTrack;
        set 
        {
            if (SetProperty(ref _currentTrack, value))
            {
                // Explicitly notify dependent properties
                RaisePropertyChanged(nameof(CurrentTrackTitle));
                RaisePropertyChanged(nameof(CurrentTrackArtist));
                RaisePropertyChanged(nameof(CurrentTrackAlbum));
            }
        }
    }
    
    // Helper properties for Now Playing panel (ensures proper binding updates)
    public string CurrentTrackTitle => CurrentTrack?.DisplayTitle ?? "No track";
    public string CurrentTrackArtist => CurrentTrack?.DisplayArtist ?? "Unknown";
    public string CurrentTrackAlbum => CurrentTrack?.DisplayAlbum ?? "Unknown";
    
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (SetProperty(ref _position, value))
            {
                RaisePropertyChanged(nameof(PositionSeconds));
                RaisePropertyChanged(nameof(PositionDisplay));
            }
        }
    }
    
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
    
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
                _playerService.IsMuted = value;
        }
    }
    
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
    
    public bool IsShuffle
    {
        get => _isShuffle;
        set
        {
            if (SetProperty(ref _isShuffle, value))
                _playerService.IsShuffle = value;
        }
    }
    
    public int RepeatMode
    {
        get => _repeatMode;
        set
        {
            if (SetProperty(ref _repeatMode, value))
            {
                _playerService.Repeat = (AudioPlayerService.RepeatMode)value;
                RaisePropertyChanged(nameof(RepeatIcon));
            }
        }
    }
    
    public string RepeatIcon => RepeatMode switch
    {
        1 => "🔁",  // Repeat All
        2 => "🔂",  // Repeat One
        _ => "➡️"   // No repeat
    };
    
    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }
    
    public double[] SpectrumData
    {
        get => _spectrumData;
        set => SetProperty(ref _spectrumData, value);
    }
    
    public ObservableCollection<AudioTrack> Queue { get; } = new();
    public ObservableCollection<EQPreset> EQPresets { get; } = new();
    
    public EQPreset SelectedPreset
    {
        get => _selectedPreset;
        set => SetProperty(ref _selectedPreset, value);
    }
    
    // Enhanced properties
    public bool IncludeSubfolders
    {
        get => _includeSubfolders;
        set => SetProperty(ref _includeSubfolders, value);
    }
    
    public int ViewModeIndex
    {
        get => _viewModeIndex;
        set
        {
            if (SetProperty(ref _viewModeIndex, value))
            {
                RaisePropertyChanged(nameof(IsPlayerView));
                RaisePropertyChanged(nameof(IsLibraryView));
            }
        }
    }
    
    public bool IsPlayerView => ViewModeIndex == 0;
    public bool IsLibraryView => ViewModeIndex == 1;
    
    public int VisualizerModeIndex
    {
        get => _visualizerModeIndex;
        set
        {
            if (SetProperty(ref _visualizerModeIndex, value))
            {
                RaisePropertyChanged(nameof(VisualizerModeName));
            }
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
        7 => "WaveGrid",
        _ => "Bars"
    };

    private int _barCount = 72;
    public int BarCount
    {
        get => _barCount;
        set
        {
            if (SetProperty(ref _barCount, value))
            {
                RaisePropertyChanged(nameof(BarCount));
            }
        }
    }
    
    private int _colorSchemeIndex = 0;
    public int ColorSchemeIndex
    {
        get => _colorSchemeIndex;
        set
        {
            if (SetProperty(ref _colorSchemeIndex, value))
            {
                RaisePropertyChanged(nameof(ColorSchemeIndex));
            }
        }
    }
    
    // EQ properties (visual for now, actual audio processing requires NAudio)
    private double _eqBass = 0;
    public double EqBass
    {
        get => _eqBass;
        set
        {
            if (SetProperty(ref _eqBass, Math.Round(value)))
            {
                RaisePropertyChanged(nameof(EqBass));
                Services.AudioPlayerService.Instance.SetEqBass((int)_eqBass);
            }
        }
    }
    
    private double _eqMid = 0;
    public double EqMid
    {
        get => _eqMid;
        set
        {
            if (SetProperty(ref _eqMid, Math.Round(value)))
            {
                RaisePropertyChanged(nameof(EqMid));
                Services.AudioPlayerService.Instance.SetEqMid((int)_eqMid);
            }
        }
    }
    
    private double _eqTreble = 0;
    public double EqTreble
    {
        get => _eqTreble;
        set
        {
            if (SetProperty(ref _eqTreble, Math.Round(value)))
            {
                RaisePropertyChanged(nameof(EqTreble));
                Services.AudioPlayerService.Instance.SetEqTreble((int)_eqTreble);
            }
        }
    }
    
    public int OrganizeModeIndex
    {
        get => _organizeModeIndex;
        set
        {
            if (SetProperty(ref _organizeModeIndex, value))
            {
                UpdateLibraryGroups();
                RaisePropertyChanged(nameof(ShowGroups));
            }
        }
    }
    
    public bool ShowGroups => OrganizeModeIndex > 0 && OrganizeModeIndex < 4;
    
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
                FilterLibraryTracks();
        }
    }
    
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }
    
    public string ScanStatus
    {
        get => _scanStatus;
        set => SetProperty(ref _scanStatus, value);
    }
    
    public int ScanProgress
    {
        get => _scanProgress;
        set => SetProperty(ref _scanProgress, value);
    }
    
    public int LibraryTrackCount => _allLibraryTracks.Count;
    
    public int LibraryArtistCount 
        => _allLibraryTracks.Select(t => t.DisplayArtist).Distinct().Count();
    
    public int LibraryAlbumCount 
        => _allLibraryTracks.Select(t => t.DisplayAlbum).Distinct().Count();
    
    public int FavoriteCount => _userLibraryService?.FavoriteCount ?? 0;
    
    public AudioTrack? SelectedLibraryTrack
    {
        get => _selectedLibraryTrack;
        set => SetProperty(ref _selectedLibraryTrack, value);
    }
    
    public LibraryGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
                FilterLibraryTracks();
        }
    }
    
    public ObservableCollection<AudioTrack> LibraryTracks { get; } = new();
    public ObservableCollection<LibraryGroup> LibraryGroups { get; } = new();
    
    // Commands
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
    public ICommand RemoveFromQueueCommand { get; }
    public ICommand RemoveSelectedFromQueueCommand { get; }
    public ICommand ClearQueueCommand { get; }
    public ICommand ScanFolderAddToQueueCommand { get; }
    
    // New commands
    public ICommand ToggleLibraryCommand { get; }
    public ICommand PlaySelectedCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand AddAllToQueueCommand { get; }
    public ICommand ScanLibraryCommand { get; }
    public ICommand CancelScanCommand { get; }
    public ICommand AddFolderToLibraryCommand { get; }
    public ICommand RemoveFolderFromLibraryCommand { get; }
    public ICommand ScanAllLibraryFoldersCommand { get; }
    
    // Keyboard shortcut commands
    public ICommand VolumeUpCommand { get; }
    public ICommand VolumeDownCommand { get; }
    
    // Context menu commands for queue
    public ICommand PlayNextCommand { get; }
    public ICommand RevealInExplorerCommand { get; }
    public ICommand MoveUpInQueueCommand { get; }
    public ICommand MoveDownInQueueCommand { get; }
    
    // Favorites commands
    public ICommand ToggleFavoriteCommand { get; }
    
    // Crossfade properties
    private bool _crossfadeEnabled = true;
    public bool CrossfadeEnabled
    {
        get => _crossfadeEnabled;
        set
        {
            if (SetProperty(ref _crossfadeEnabled, value))
                _playerService.CrossfadeEnabled = value;
        }
    }
    
    private int _crossfadeDurationMs = 2000;
    public int CrossfadeDurationMs
    {
        get => _crossfadeDurationMs;
        set
        {
            if (SetProperty(ref _crossfadeDurationMs, value))
                _playerService.CrossfadeDurationMs = value;
        }
    }
    
    public double CrossfadeDurationSeconds
    {
        get => _crossfadeDurationMs / 1000.0;
        set
        {
            CrossfadeDurationMs = (int)(value * 1000);
            RaisePropertyChanged();
        }
    }
    
    // Queue selection
    private AudioTrack? _selectedQueueTrack;
    public AudioTrack? SelectedQueueTrack
    {
        get => _selectedQueueTrack;
        set => SetProperty(ref _selectedQueueTrack, value);
    }
    
    private ObservableCollection<AudioTrack> _selectedQueueTracks = new();
    public ObservableCollection<AudioTrack> SelectedQueueTracks
    {
        get => _selectedQueueTracks;
        set => SetProperty(ref _selectedQueueTracks, value);
    }
    
    public AudioPlayerViewModel()
    {
        try { SimpleLogger.Info("AudioPlayerViewModel constructed"); } catch {}
        Log("AudioPlayerViewModel: Constructor starting...");
        _playerService = AudioPlayerService.Instance;
        _libraryIndexService = new LibraryIndexService();
        _userLibraryService = new UserLibraryService();
        
        // Load user library data and library index asynchronously
        _ = InitializeUserLibraryAsync();
        _ = InitializeLibraryAsync();  // Load library index eagerly, not when view loads
        
        // Initialize EQ presets
        foreach (var preset in EQPreset.AllPresets)
            EQPresets.Add(preset);
        
        // Default to Rock preset
        _selectedPreset = EQPresets.FirstOrDefault(p => p.Name == "Rock") ?? EQPreset.Flat;
        
        // Subscribe to service events
        _playerService.TrackChanged += OnTrackChanged;
        _playerService.PositionChanged += OnPositionChanged;
        _playerService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playerService.SpectrumDataUpdated += OnSpectrumDataUpdated;
        
        // Initialize commands
        PlayPauseCommand = new RelayCommand(_ => PlayPause());
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
            if (track != null)
                await _playerService.PlayTrackAsync(track);
        });
        RemoveFromQueueCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track)
                _playerService.RemoveFromQueue(track);
            UpdateQueue();
        });
        RemoveSelectedFromQueueCommand = new RelayCommand(_ =>
        {
            if (SelectedQueueTrack != null)
            {
                _playerService.RemoveFromQueue(SelectedQueueTrack);
                UpdateQueue();
            }
        });
        ClearQueueCommand = new RelayCommand(_ =>
        {
            _playerService.ClearQueue();
            UpdateQueue();
        });
        ScanFolderAddToQueueCommand = new AsyncRelayCommand(ScanFolderAddToQueueAsync);
        
        // New commands
        ToggleLibraryCommand = new RelayCommand(_ => ViewModeIndex = ViewModeIndex == 0 ? 1 : 0);
        PlaySelectedCommand = new AsyncRelayCommand(async () =>
        {
            if (SelectedLibraryTrack != null)
            {
                _playerService.AddToQueue(SelectedLibraryTrack);
                UpdateQueue();
                await _playerService.PlayTrackAsync(SelectedLibraryTrack);
            }
        });
        AddToQueueCommand = new RelayCommand(_ =>
        {
            if (SelectedLibraryTrack != null)
            {
                _playerService.AddToQueue(SelectedLibraryTrack);
                UpdateQueue();
            }
        });
        AddAllToQueueCommand = new RelayCommand(_ =>
        {
            foreach (var track in LibraryTracks)
                _playerService.AddToQueue(track);
            UpdateQueue();
            StatusMessage = $"Added {LibraryTracks.Count} tracks to queue";
        });
        
        // Library Management Commands
        ScanLibraryCommand = new AsyncRelayCommand(async () =>
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Music Folder",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection"
            };
            
            if (DialogHelper.ShowOpenFileDialog(dialog) == true)
            {
                var folder = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (System.IO.Directory.Exists(folder))
                    await ScanLibraryDirectoryAsync(folder);
            }
        });
        
        CancelScanCommand = new RelayCommand(_ =>
        {
            IsScanning = false;
            ScanStatus = "Scan cancelled";
        });
        
        // Library Folder Management Commands
        AddFolderToLibraryCommand = new RelayCommand(_ => AddFolderToLibrary());
        RemoveFolderFromLibraryCommand = new RelayCommand(_ => RemoveFolderFromLibrary());
        ScanAllLibraryFoldersCommand = new AsyncRelayCommand(ScanAllLibraryFoldersAsync);
        
        // Keyboard shortcut commands
        VolumeUpCommand = new RelayCommand(_ =>
        {
            Volume = Math.Min(1.0, Volume + 0.05); // +5%
        });
        VolumeDownCommand = new RelayCommand(_ =>
        {
            Volume = Math.Max(0.0, Volume - 0.05); // -5%
        });
        
        // Context menu commands for queue
        PlayNextCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track)
            {
                _playerService.PlayNext(track);
                UpdateQueue();
                StatusMessage = $"'{track.DisplayTitle}' will play next";
            }
        });
        
        RevealInExplorerCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track)
            {
                _playerService.RevealInExplorer(track);
            }
        });
        
        MoveUpInQueueCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track)
            {
                var index = Queue.IndexOf(track);
                if (index > 0)
                {
                    _playerService.MoveInQueue(index, index - 1);
                    UpdateQueue();
                }
            }
        });
        
        MoveDownInQueueCommand = new RelayCommand(param =>
        {
            if (param is AudioTrack track)
            {
                var index = Queue.IndexOf(track);
                if (index >= 0 && index < Queue.Count - 1)
                {
                    _playerService.MoveInQueue(index, index + 1);
                    UpdateQueue();
                }
            }
        });
        
        // Favorites command
        ToggleFavoriteCommand = new AsyncRelayCommand<AudioTrack>(async track =>
        {
            if (track != null)
                await ToggleFavoriteAsync(track);
        });
        
        // Sync crossfade settings with service
        _playerService.CrossfadeEnabled = _crossfadeEnabled;
        _playerService.CrossfadeDurationMs = _crossfadeDurationMs;
        
        // Set initial volume
        _playerService.Volume = _volume;
        
        // Load saved library folders
        LoadLibraryFolders();
    }
    
    private void PlayPause()
    {
        if (IsPlaying)
        {
            _playerService.Pause();
            return;
        }

        // If there's a current track loaded, just resume
        if (CurrentTrack != null)
        {
            _playerService.Play();
            return;
        }
        
        // If a library track is selected, add it to queue and play
        if (SelectedLibraryTrack != null)
        {
            _playerService.AddToQueue(SelectedLibraryTrack);
            UpdateQueue();
            _ = _playerService.PlayTrackAsync(SelectedLibraryTrack);
            return;
        }
        
        // If queue has tracks, play the first one
        if (_playerService.Queue.Count > 0)
        {
            _ = _playerService.PlayTrackAsync(_playerService.Queue[0]);
            return;
        }
        
        // Nothing to play
        StatusMessage = "No track selected. Select a track from the library or add files to the queue.";
    }
    
    private async Task OpenFileAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Audio File",
            Filter = "Audio Files|*.mp3;*.wav;*.wma;*.aac;*.m4a;*.flac;*.ogg;*.opus|All Files|*.*",
            Multiselect = true
        };
        
        if (dialog.ShowDialog() == true)
        {
            StatusMessage = "Loading files...";
            
            foreach (var file in dialog.FileNames)
            {
                var track = await _playerService.LoadTrackAsync(file);
                if (track != null)
                    _playerService.AddToQueue(track);
            }
            
            UpdateQueue();
            
            if (_playerService.Queue.Count > 0 && CurrentTrack == null)
            {
                await _playerService.PlayTrackAsync(_playerService.Queue[0]);
            }
            
            StatusMessage = $"Loaded {dialog.FileNames.Length} file(s)";
        }
    }
    
    private async Task OpenFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder with audio files"
        };
        
        // Show dialog with safe owner handling
        var result = DialogHelper.ShowFolderDialog(dialog);
        
        if (result == System.Windows.Forms.DialogResult.OK)
        {
            IsScanning = true;
            ScanStatus = "Scanning folder...";
            
            try
            {
                var tracks = await _playerService.ScanFolderAsync(dialog.SelectedPath, IncludeSubfolders);
                
                if (tracks.Count > 0)
                {
                    // Add to library in one go
                    UpdateLibraryGroups();
                    FilterLibraryTracks();
                    RaisePropertyChanged(nameof(LibraryTrackCount));
                    
                    // Set as queue and play
                    _playerService.SetQueue(tracks);
                    UpdateQueue();
                    
                    // Ensure CurrentTrack is updated before playing
                    var firstTrack = tracks[0];
                    CurrentTrack = firstTrack;
                    
                    await _playerService.PlayTrackAsync(firstTrack);
                    StatusMessage = $"Loaded {tracks.Count} tracks";
                    ScanStatus = $"Found {tracks.Count} audio files";
                }
                else
                {
                    StatusMessage = "No audio files found";
                    ScanStatus = "No audio files found in folder";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning folder: {ex.Message}";
                ScanStatus = "Scan failed";
            }
            finally
            {
                IsScanning = false;
            }
        }
    }
    
    /// <summary>
    /// Scans a folder and adds tracks to the existing queue dynamically without replacing it.
    /// </summary>
    private async Task ScanFolderAddToQueueAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to scan and add to queue"
        };
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            IsScanning = true;
            ScanStatus = "Scanning folder...";
            
            try
            {
                // Scan asynchronously - all tracks at once
                var tracks = await _playerService.ScanFolderAsync(dialog.SelectedPath, IncludeSubfolders);
                
                if (tracks.Count == 0)
                {
                    StatusMessage = "No audio files found";
                    ScanStatus = "No audio files found in folder";
                    return;
                }
                
                // Add all tracks at once to avoid multiple UI updates
                int addedCount = 0;
                foreach (var track in tracks)
                {
                    _playerService.AddToQueue(track);
                    addedCount++;
                    
                    // Update status less frequently to avoid freezing (every 10 tracks)
                    if (addedCount % 10 == 0)
                    {
                        ScanStatus = $"Added {addedCount} tracks...";
                        // Allow UI to refresh
                        await Task.Delay(1);
                    }
                }
                
                // Single bulk update instead of multiple incremental updates
                UpdateQueue();
                
                // Add to library in one go
                _allLibraryTracks.AddRange(tracks);
                UpdateLibraryGroups();
                FilterLibraryTracks();
                RaisePropertyChanged(nameof(LibraryTrackCount));
                
                StatusMessage = $"Added {addedCount} tracks to queue";
                ScanStatus = $"Added {addedCount} audio files to queue";
                
                // Start playing if nothing is playing
                if (CurrentTrack == null && Queue.Count > 0)
                {
                    await _playerService.PlayTrackAsync(Queue[0]);
                }
            }
            finally
            {
                IsScanning = false;
            }
        }
    }
    
    /// <summary>
    /// Set the library tracks from an external source (used by code-behind after scanning).
    /// This updates the internal list and refreshes all UI elements.
    /// </summary>
    public void SetLibraryTracks(IEnumerable<AudioTrack> tracks)
    {
        _allLibraryTracks = tracks.ToList();
        SyncFavoritesWithTracks();
        RebuildLibraryGroups();
        FilterLibraryTracks();
        
        // Raise property changes for stats
        RaisePropertyChanged(nameof(LibraryTrackCount));
        RaisePropertyChanged(nameof(LibraryArtistCount));
        RaisePropertyChanged(nameof(LibraryAlbumCount));
        RaisePropertyChanged(nameof(FavoriteCount));
        
        System.Diagnostics.Debug.WriteLine($"SetLibraryTracks: {_allLibraryTracks.Count} tracks, {LibraryArtistCount} artists, {LibraryAlbumCount} albums");
    }
    
    private void UpdateLibraryGroups()
    {
        RebuildLibraryGroups();
        FilterLibraryTracks();
    }
    
    private void FilterLibraryTracks()
    {
        LibraryTracks.Clear();
        
        IEnumerable<AudioTrack> source = _allLibraryTracks;
        
        // Filter by favorites when mode 5 is selected
        if (_organizeModeIndex == 5)
        {
            source = source.Where(t => t.IsFavorite);
        }
        // Filter by selected group for other modes
        else if (SelectedGroup != null && ShowGroups)
        {
            source = SelectedGroup.Tracks;
        }
        
        // Filter by search query
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var query = SearchQuery.ToLowerInvariant();
            source = source.Where(t =>
                t.DisplayTitle.ToLowerInvariant().Contains(query) ||
                t.DisplayArtist.ToLowerInvariant().Contains(query) ||
                t.DisplayAlbum.ToLowerInvariant().Contains(query) ||
                (t.Genre?.ToLowerInvariant().Contains(query) ?? false));
        }
        
        foreach (var track in source.OrderBy(t => t.DisplayArtist).ThenBy(t => t.DisplayAlbum).ThenBy(t => t.TrackNumber))
            LibraryTracks.Add(track);
    }
    
    private void UpdateQueue()
    {
        Queue.Clear();
        foreach (var track in _playerService.Queue)
            Queue.Add(track);
        
        // Save queue for persistence across sessions
        _ = _playerService.SaveQueueAsync();
    }
    
    /// <summary>
    /// Initialize and load library index on startup.
    /// </summary>
    private static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[AudioPlayerVM] {message}");
    }
    
    public async Task InitializeLibraryAsync()
    {
        try
        {
            Log("InitializeLibraryAsync: Starting...");
            IsScanning = true;
            ScanStatus = "Loading saved queue...";
            
            // Load saved queue from previous session
            var queueLoaded = await _playerService.LoadQueueAsync();
            if (queueLoaded)
            {
                UpdateQueue();
                StatusMessage = $"Restored {Queue.Count} tracks from previous session";
            }
            
            ScanStatus = "Loading library index...";
            Log("InitializeLibraryAsync: About to call LoadOrCreateIndexAsync...");
            
            var index = await _libraryIndexService.LoadOrCreateIndexAsync();
            var loadedCount = index?.Tracks?.Count ?? 0;
            Log($"InitializeLibraryAsync: Loaded index with {loadedCount} tracks");
            
            if (loadedCount > 0)
            {
                Log("InitializeLibraryAsync: About to populate _allLibraryTracks...");
                
                // Must update collections on UI thread
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null)
                {
                    Log("InitializeLibraryAsync: WARNING - Dispatcher is null, updating directly");
                    PopulateLibraryFromIndex(index!);
                }
                else
                {
                    await dispatcher.InvokeAsync(() => PopulateLibraryFromIndex(index!));
                }
                
                Log($"InitializeLibraryAsync: Done. _allLibraryTracks has {_allLibraryTracks.Count} tracks, LibraryTracks has {LibraryTracks.Count} tracks");
            }
            else
            {
                ScanStatus = "No library tracks found";
                Log("InitializeLibraryAsync: No tracks in index");
            }
        }
        catch (Exception ex)
        {
            Log($"InitializeLibraryAsync: ERROR - {ex}");
            StatusMessage = $"Error loading library: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
    
    private void PopulateLibraryFromIndex(LibraryIndex index)
    {
        Log($"PopulateLibraryFromIndex: Processing {index.Tracks?.Count ?? 0} tracks...");
        
        _allLibraryTracks = new List<AudioTrack>();
        foreach (var indexTrack in index.Tracks!)
        {
            // Convert Track (from index) to AudioTrack (legacy format used by player)
            var audioTrack = new AudioTrack
            {
                Title = indexTrack.DisplayTitle,
                Artist = indexTrack.DisplayArtist,
                Album = indexTrack.DisplayAlbum,
                FilePath = indexTrack.FilePath,
                Duration = TimeSpan.FromMilliseconds(indexTrack.DurationMs),
                Genre = indexTrack.Genre ?? string.Empty,
            };
            _allLibraryTracks.Add(audioTrack);
        }
        
        Log($"PopulateLibraryFromIndex: Built _allLibraryTracks with {_allLibraryTracks.Count} items");
        
        RebuildLibraryGroups();
        FilterLibraryTracks();
        SyncFavoritesWithTracks();
        
        Log($"PopulateLibraryFromIndex: After FilterLibraryTracks, LibraryTracks has {LibraryTracks.Count} items");
        
        ScanStatus = $"Library: {_allLibraryTracks.Count} tracks";
        RaisePropertyChanged(nameof(LibraryTrackCount));
        RaisePropertyChanged(nameof(LibraryArtistCount));
        RaisePropertyChanged(nameof(LibraryAlbumCount));
    }
    
    /// <summary>
    /// Scan a directory and add tracks to library index.
    /// </summary>
    public async Task ScanLibraryDirectoryAsync(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
            return;
        
        try
        {
            IsScanning = true;
            ScanStatus = "Scanning...";
            
            // Scan and index
            await _libraryIndexService.ScanAndIndexDirectoryAsync(
                directory,
                recursive: _includeSubfolders,
                onProgressChanged: (current, total, status) =>
                {
                    ScanStatus = $"{status} ({current}/{total})";
                });
            
            // Reload library
            var index = _libraryIndexService.GetCurrentIndex();
            if (index?.Tracks != null)
            {
                _allLibraryTracks.Clear();
                foreach (var indexTrack in index.Tracks)
                {
                    var audioTrack = new AudioTrack
                    {
                        Title = indexTrack.DisplayTitle,
                        Artist = indexTrack.DisplayArtist,
                        Album = indexTrack.DisplayAlbum,
                        FilePath = indexTrack.FilePath,
                        Duration = TimeSpan.FromMilliseconds(indexTrack.DurationMs),
                        Genre = indexTrack.Genre ?? string.Empty,
                    };
                    _allLibraryTracks.Add(audioTrack);
                }
                
                RebuildLibraryGroups();
                FilterLibraryTracks();  // Populate the LibraryTracks ObservableCollection
                SyncFavoritesWithTracks();
                ScanStatus = $"Scanned complete: {_allLibraryTracks.Count} tracks";
                RaisePropertyChanged(nameof(LibraryTrackCount));
                RaisePropertyChanged(nameof(LibraryArtistCount));
                RaisePropertyChanged(nameof(LibraryAlbumCount));
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning library: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }
    
    /// <summary>
    /// Add a folder to the library folders list.
    /// </summary>
    private void AddFolderToLibrary()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to add to library",
            ShowNewFolderButton = false
        };
        
        var result = dialog.ShowDialog();
        System.Diagnostics.Debug.WriteLine($"Dialog result: {result}, Path: {dialog.SelectedPath}");
        
        if (result == System.Windows.Forms.DialogResult.OK)
        {
            var folder = dialog.SelectedPath;
            System.Diagnostics.Debug.WriteLine($"Adding folder: {folder}");
            
            if (!string.IsNullOrEmpty(folder) && !LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                // Add on UI thread to ensure ObservableCollection notifies properly
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    LibraryFolders.Add(folder);
                    RaisePropertyChanged(nameof(LibraryFolders));
                });
                SaveLibraryFolders();
                StatusMessage = $"Added folder: {folder}";
                System.Diagnostics.Debug.WriteLine($"LibraryFolders count: {LibraryFolders.Count}");
            }
            else if (LibraryFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                StatusMessage = "Folder already in library";
            }
        }
    }
    
    /// <summary>
    /// Remove selected folder from the library folders list.
    /// </summary>
    private void RemoveFolderFromLibrary()
    {
        if (SelectedLibraryFolder != null)
        {
            LibraryFolders.Remove(SelectedLibraryFolder);
            SaveLibraryFolders();
            StatusMessage = "Folder removed from library";
        }
    }
    
    /// <summary>
    /// Scan all library folders and add tracks to the library.
    /// </summary>
    public async Task ScanAllLibraryFoldersAsync()
    {
        if (LibraryFolders.Count == 0)
        {
            StatusMessage = "No folders added to library. Click 'Add Folder' first.";
            return;
        }
        
        try
        {
            IsScanning = true;
            int totalTracksFound = 0;
            
            foreach (var folder in LibraryFolders.ToList())
            {
                if (!System.IO.Directory.Exists(folder))
                {
                    ScanStatus = $"Skipping missing folder: {folder}";
                    continue;
                }
                
                ScanStatus = $"Scanning: {System.IO.Path.GetFileName(folder)}...";
                
                await _libraryIndexService.ScanAndIndexDirectoryAsync(
                    folder,
                    recursive: _includeSubfolders,
                    onProgressChanged: (current, total, status) =>
                    {
                        ScanStatus = $"{status} ({current}/{total})";
                    });
            }
            
            // Reload all library tracks
            var index = _libraryIndexService.GetCurrentIndex();
            if (index?.Tracks != null)
            {
                _allLibraryTracks.Clear();
                foreach (var indexTrack in index.Tracks)
                {
                    var audioTrack = new AudioTrack
                    {
                        Title = indexTrack.DisplayTitle,
                        Artist = indexTrack.DisplayArtist,
                        Album = indexTrack.DisplayAlbum,
                        FilePath = indexTrack.FilePath,
                        Duration = TimeSpan.FromMilliseconds(indexTrack.DurationMs),
                        Genre = indexTrack.Genre ?? string.Empty,
                    };
                    _allLibraryTracks.Add(audioTrack);
                }
                totalTracksFound = _allLibraryTracks.Count;
                
                RebuildLibraryGroups();
                SyncFavoritesWithTracks();
                FilterLibraryTracks();
                RaisePropertyChanged(nameof(LibraryTrackCount));
                RaisePropertyChanged(nameof(LibraryArtistCount));
                RaisePropertyChanged(nameof(LibraryAlbumCount));
            }
            
            ScanStatus = $"Scan complete: {totalTracksFound} tracks";
            StatusMessage = $"Library updated: {totalTracksFound} tracks from {LibraryFolders.Count} folders";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning: {ex.Message}";
            ScanStatus = "Scan failed";
        }
        finally
        {
            IsScanning = false;
        }
    }
    
    /// <summary>
    /// Save library folders to settings file.
    /// </summary>
    private void SaveLibraryFolders()
    {
        try
        {
            var foldersPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "library_folders.json");
            
            var dir = System.IO.Path.GetDirectoryName(foldersPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            
            var json = System.Text.Json.JsonSerializer.Serialize(LibraryFolders.ToList());
            System.IO.File.WriteAllText(foldersPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving library folders: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Load library folders from settings file.
    /// </summary>
    private void LoadLibraryFolders()
    {
        try
        {
            var foldersPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "library_folders.json");
            
            if (System.IO.File.Exists(foldersPath))
            {
                var json = System.IO.File.ReadAllText(foldersPath);
                var folders = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
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
    
    /// <summary>
    /// Initialize user library data (favorites, playlists, etc.)
    /// </summary>
    private async Task InitializeUserLibraryAsync()
    {
        try
        {
            var data = await _userLibraryService.LoadAsync();
            System.Diagnostics.Debug.WriteLine($"User library loaded: {data.Favorites.Count} favorites, {data.Playlists.Count} playlists");
            
            // Sync favorite status into already loaded tracks
            SyncFavoritesWithTracks();
            
            RaisePropertyChanged(nameof(FavoriteCount));
            _userLibraryService.DataChanged += (s, e) =>
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                {
                    RaisePropertyChanged(nameof(FavoriteCount));
                    SyncFavoritesWithTracks();
                });
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error initializing user library: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sync favorite status from UserLibraryService to loaded tracks.
    /// </summary>
    private void SyncFavoritesWithTracks()
    {
        foreach (var track in _allLibraryTracks)
        {
            track.IsFavorite = _userLibraryService.IsFavorite(track.FilePath);
        }
        FilterLibraryTracks();
    }
    
    /// <summary>
    /// Toggle favorite status for a track.
    /// </summary>
    public async Task ToggleFavoriteAsync(AudioTrack track)
    {
        if (track == null) return;
        
        try
        {
            var isFavorite = await _userLibraryService.ToggleFavoriteAsync(track.FilePath);
            track.IsFavorite = isFavorite;
            
            RaisePropertyChanged(nameof(FavoriteCount));
            StatusMessage = isFavorite ? $"Added to favorites: {track.DisplayTitle}" : $"Removed from favorites: {track.DisplayTitle}";
            
            // Refresh the track in the LibraryTracks collection to update UI
            var index = LibraryTracks.IndexOf(track);
            if (index >= 0)
            {
                LibraryTracks.RemoveAt(index);
                LibraryTracks.Insert(index, track);
            }
            
            System.Diagnostics.Debug.WriteLine($"ToggleFavoriteAsync: {track.DisplayTitle} is now {(isFavorite ? "favorite" : "not favorite")}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error toggling favorite: {ex.Message}");
            StatusMessage = $"Error toggling favorite: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Check if a track is favorited.
    /// </summary>
    public bool IsTrackFavorite(AudioTrack track)
    {
        return _userLibraryService.IsFavorite(track?.FilePath ?? string.Empty);
    }
    
    /// <summary>
    /// Search library tracks.
    /// </summary>
    public void SearchLibrary(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            RebuildLibraryGroups();
            return;
        }
        
        var results = _libraryIndexService.Search(query, SearchType.All);
        _allLibraryTracks = new List<AudioTrack>();
        foreach (var indexTrack in results)
        {
            var audioTrack = new AudioTrack
            {
                Title = indexTrack.DisplayTitle,
                Artist = indexTrack.DisplayArtist,
                Album = indexTrack.DisplayAlbum,
                FilePath = indexTrack.FilePath,
                Duration = TimeSpan.FromMilliseconds(indexTrack.DurationMs),
                Genre = indexTrack.Genre ?? string.Empty,
            };
            _allLibraryTracks.Add(audioTrack);
        }
        
        RebuildLibraryGroups();
        SyncFavoritesWithTracks();
        ScanStatus = $"Found {results.Count} matches";
    }
    
    /// <summary>
    /// Rebuild library groups based on current organize mode.
    /// </summary>
    private void RebuildLibraryGroups()
    {
        _libraryGroups.Clear();
        
        if (_allLibraryTracks.Count == 0)
            return;
        
        var groupedTracks = _organizeModeIndex switch
        {
            1 => _allLibraryTracks.GroupBy(t => t.DisplayArtist).ToDictionary(g => g.Key, g => g.ToList()),
            2 => _allLibraryTracks.GroupBy(t => t.DisplayAlbum).ToDictionary(g => g.Key, g => g.ToList()),
            3 => _allLibraryTracks.GroupBy(t => t.Genre ?? "Unknown").ToDictionary(g => g.Key, g => g.ToList()),
            4 => _allLibraryTracks.GroupBy(t => System.IO.Path.GetDirectoryName(t.FilePath) ?? "Unknown").ToDictionary(g => g.Key, g => g.ToList()),
            5 => new Dictionary<string, List<AudioTrack>> { { "⭐ Favorites", _allLibraryTracks.Where(t => t.IsFavorite).ToList() } },
            _ => new Dictionary<string, List<AudioTrack>> { { "All Tracks", _allLibraryTracks } }
        };
        
        foreach (var group in groupedTracks.OrderBy(g => g.Key))
        {
            _libraryGroups.Add(new LibraryGroup
            {
                Name = group.Key,
                TrackCount = group.Value.Count,
                Tracks = group.Value,
            });
        }
        
        RaisePropertyChanged(nameof(LibraryGroups));
    }
    
    // Event handlers
    private void OnTrackChanged(object? sender, AudioTrack? track)
    {
        System.Diagnostics.Debug.WriteLine($"OnTrackChanged: {track?.DisplayTitle ?? "null"}");
        
        // Update on UI thread
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            CurrentTrack = track;
            RaisePropertyChanged(nameof(CurrentTrack));
            
            if (track != null)
            {
                Duration = _playerService.Duration;
                StatusMessage = $"Now Playing: {track.DisplayArtist} - {track.DisplayTitle}";
            }
        });
    }
    
    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        Position = position;
        if (Duration == TimeSpan.Zero && _playerService.Duration != TimeSpan.Zero)
            Duration = _playerService.Duration;
    }
    
    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        IsPlaying = isPlaying;
    }
    
    private void OnSpectrumDataUpdated(object? sender, double[] data)
    {
        SpectrumData = data;
    }
}

/// <summary>
/// Generic async relay command with parameter support.
/// </summary>
public class AsyncRelayCommand<T> : ICommand
{
    private readonly Func<T?, Task> _execute;
    private readonly Func<T?, bool>? _canExecute;
    
    public AsyncRelayCommand(Func<T?, Task> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }
    
    public bool CanExecute(object? parameter) => _canExecute?.Invoke((T?)parameter) ?? true;
    
    public async void Execute(object? parameter)
    {
        await _execute((T?)parameter);
    }
    
    public event EventHandler? CanExecuteChanged;
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Represents a group of audio tracks organized by a common property (Artist, Album, Genre, Folder).
/// </summary>
public class LibraryGroup
{
    public string Name { get; set; } = string.Empty;
    public int TrackCount { get; set; }
    public List<AudioTrack> Tracks { get; set; } = new();
    
    public string DisplayText => $"{Name} ({TrackCount})";
}
