using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// ViewModel for the integrated Audio Player with visualizer, library organization, and queue management.
/// </summary>
public class AudioPlayerViewModel : BindableBase
{
    private readonly AudioPlayerService _playerService;
    
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
    private AudioTrack? _selectedLibraryTrack;
    private LibraryGroup? _selectedGroup;
    
    // Library data
    private List<AudioTrack> _allLibraryTracks = new();
    private List<LibraryGroup> _libraryGroups = new();
    
    // Properties
    public AudioTrack? CurrentTrack
    {
        get => _currentTrack;
        set => SetProperty(ref _currentTrack, value);
    }
    
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
        set => SetProperty(ref _visualizerModeIndex, value);
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
    
    public int LibraryTrackCount => _allLibraryTracks.Count;
    
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
    public ICommand ClearQueueCommand { get; }
    
    // New commands
    public ICommand ToggleLibraryCommand { get; }
    public ICommand PlaySelectedCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand AddAllToQueueCommand { get; }
    
    public AudioPlayerViewModel()
    {
        _playerService = AudioPlayerService.Instance;
        _selectedPreset = EQPreset.Flat;
        
        // Initialize EQ presets
        foreach (var preset in EQPreset.AllPresets)
            EQPresets.Add(preset);
        
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
        ClearQueueCommand = new RelayCommand(_ =>
        {
            _playerService.ClearQueue();
            UpdateQueue();
        });
        
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
        
        // Set initial volume
        _playerService.Volume = _volume;
    }
    
    private void PlayPause()
    {
        if (IsPlaying)
            _playerService.Pause();
        else
            _playerService.Play();
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
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            IsScanning = true;
            ScanStatus = "Scanning folder...";
            
            var tracks = await _playerService.ScanFolderAsync(dialog.SelectedPath, IncludeSubfolders);
            
            if (tracks.Count > 0)
            {
                // Add to library
                _allLibraryTracks.AddRange(tracks);
                UpdateLibraryGroups();
                FilterLibraryTracks();
                RaisePropertyChanged(nameof(LibraryTrackCount));
                
                // Set as queue and play
                _playerService.SetQueue(tracks);
                UpdateQueue();
                await _playerService.PlayTrackAsync(tracks[0]);
                StatusMessage = $"Loaded {tracks.Count} tracks";
                ScanStatus = $"Found {tracks.Count} audio files";
            }
            else
            {
                StatusMessage = "No audio files found";
                ScanStatus = "No audio files found in folder";
            }
            
            IsScanning = false;
        }
    }
    
    private void UpdateLibraryGroups()
    {
        LibraryGroups.Clear();
        
        IEnumerable<IGrouping<string, AudioTrack>> groups = OrganizeModeIndex switch
        {
            1 => _allLibraryTracks.GroupBy(t => t.DisplayArtist),
            2 => _allLibraryTracks.GroupBy(t => t.DisplayAlbum),
            3 => _allLibraryTracks.GroupBy(t => string.IsNullOrEmpty(t.Genre) ? "Unknown" : t.Genre),
            4 => _allLibraryTracks.GroupBy(t => System.IO.Path.GetDirectoryName(t.FilePath) ?? "Unknown"),
            _ => Enumerable.Empty<IGrouping<string, AudioTrack>>()
        };
        
        foreach (var group in groups.OrderBy(g => g.Key))
        {
            LibraryGroups.Add(new LibraryGroup 
            { 
                Name = group.Key, 
                TrackCount = group.Count(),
                Tracks = group.ToList()
            });
        }
    }
    
    private void FilterLibraryTracks()
    {
        LibraryTracks.Clear();
        
        IEnumerable<AudioTrack> source = _allLibraryTracks;
        
        // Filter by selected group
        if (SelectedGroup != null && ShowGroups)
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
    }
    
    // Event handlers
    private void OnTrackChanged(object? sender, AudioTrack? track)
    {
        CurrentTrack = track;
        if (track != null)
        {
            Duration = _playerService.Duration;
            StatusMessage = $"Now Playing: {track.DisplayArtist} - {track.DisplayTitle}";
        }
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
