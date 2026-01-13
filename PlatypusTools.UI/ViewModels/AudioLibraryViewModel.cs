using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// View mode for library navigation.
/// </summary>
public enum LibraryViewMode
{
    AllTracks,
    Artists,
    Albums,
    Genres,
    RecentlyAdded,
    RecentlyPlayed,
    MostPlayed,
    TopRated,
    Playlist
}

/// <summary>
/// ViewModel for the Audio Library browser with navigation, search, and organization.
/// </summary>
public class AudioLibraryViewModel : BindableBase
{
    private readonly AudioLibraryService _libraryService;
    private readonly AudioPlayerService _playerService;
    private CancellationTokenSource? _scanCts;
    
    private LibraryViewMode _viewMode = LibraryViewMode.AllTracks;
    private string _searchQuery = string.Empty;
    private bool _isScanning;
    private int _scanProgress;
    private string? _selectedWatchFolder;
    private Artist? _selectedArtist;
    private Album? _selectedAlbum;
    private Playlist? _selectedPlaylist;
    private AudioTrack? _selectedTrack;
    
    public AudioLibraryViewModel()
    {
        _libraryService = AudioLibraryService.Instance;
        _playerService = AudioPlayerService.Instance;
        
        Tracks = new ObservableCollection<AudioTrack>();
        Artists = new ObservableCollection<Artist>();
        Albums = new ObservableCollection<Album>();
        Genres = new ObservableCollection<string>();
        Playlists = new ObservableCollection<Playlist>();
        WatchFolders = new ObservableCollection<string>();
        
        // Commands
        ScanFolderCommand = new AsyncRelayCommand(ScanFolderAsync);
        AddFolderCommand = new AsyncRelayCommand(AddFolderAsync);
        RemoveFolderCommand = new RelayCommand(_ => RemoveFolder(), _ => !string.IsNullOrEmpty(SelectedWatchFolder));
        CreatePlaylistCommand = new AsyncRelayCommand(CreatePlaylistAsync);
        SelectPlaylistCommand = new RelayCommand(SelectPlaylist);
        PlayTrackCommand = new AsyncRelayCommand<AudioTrack>(PlayTrackAsync);
        PlayAllCommand = new AsyncRelayCommand(PlayAllAsync);
        AddToQueueCommand = new RelayCommand(AddToQueue);
        RefreshCommand = new RelayCommand(_ => RefreshView());
        CancelScanCommand = new RelayCommand(_ => CancelScan(), _ => IsScanning);
        
        // Subscribe to events
        _libraryService.LibraryUpdated += OnLibraryUpdated;
        _libraryService.ScanProgressChanged += OnScanProgressChanged;
        
        // Load library on start
        _ = LoadLibraryAsync();
    }
    
    // Properties
    public LibraryViewMode ViewMode
    {
        get => _viewMode;
        set
        {
            if (SetProperty(ref _viewMode, value))
            {
                RefreshView();
            }
        }
    }
    
    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                RefreshView();
            }
        }
    }
    
    public bool IsScanning
    {
        get => _isScanning;
        set => SetProperty(ref _isScanning, value);
    }
    
    public int ScanProgress
    {
        get => _scanProgress;
        set => SetProperty(ref _scanProgress, value);
    }
    
    public string? SelectedWatchFolder
    {
        get => _selectedWatchFolder;
        set => SetProperty(ref _selectedWatchFolder, value);
    }
    
    public Artist? SelectedArtist
    {
        get => _selectedArtist;
        set
        {
            if (SetProperty(ref _selectedArtist, value))
            {
                if (value != null)
                {
                    LoadArtistTracks(value);
                }
            }
        }
    }
    
    public Album? SelectedAlbum
    {
        get => _selectedAlbum;
        set
        {
            if (SetProperty(ref _selectedAlbum, value))
            {
                if (value != null)
                {
                    LoadAlbumTracks(value);
                }
            }
        }
    }
    
    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set => SetProperty(ref _selectedPlaylist, value);
    }
    
    public AudioTrack? SelectedTrack
    {
        get => _selectedTrack;
        set => SetProperty(ref _selectedTrack, value);
    }
    
    public int TrackCount => _libraryService.TrackCount;
    public string TotalDuration => FormatDuration(_libraryService.GetTotalDuration());
    
    // Collections
    public ObservableCollection<AudioTrack> Tracks { get; }
    public ObservableCollection<Artist> Artists { get; }
    public ObservableCollection<Album> Albums { get; }
    public ObservableCollection<string> Genres { get; }
    public ObservableCollection<Playlist> Playlists { get; }
    public ObservableCollection<string> WatchFolders { get; }
    
    // Commands
    public ICommand ScanFolderCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveFolderCommand { get; }
    public ICommand CreatePlaylistCommand { get; }
    public ICommand SelectPlaylistCommand { get; }
    public ICommand PlayTrackCommand { get; }
    public ICommand PlayAllCommand { get; }
    public ICommand AddToQueueCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CancelScanCommand { get; }
    
    // Methods
    private async Task LoadLibraryAsync()
    {
        await _libraryService.LoadLibraryAsync();
        RefreshView();
        UpdateWatchFolders();
        UpdatePlaylists();
    }
    
    private async Task ScanFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to scan for audio files"
        };
        
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        
        IsScanning = true;
        ScanProgress = 0;
        _scanCts = new CancellationTokenSource();
        StatusBarViewModel.Instance.StartOperation("Scanning audio library...", isCancellable: true);
        
        try
        {
            var count = await _libraryService.ScanFolderAsync(
                dialog.SelectedPath, 
                true, 
                _scanCts.Token);
            
            await _libraryService.SaveLibraryAsync();
            RefreshView();
        }
        finally
        {
            IsScanning = false;
            StatusBarViewModel.Instance.CompleteOperation("Library scan complete");
            _scanCts?.Dispose();
            _scanCts = null;
        }
    }
    
    private async Task AddFolderAsync()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to watch for audio files"
        };
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _libraryService.AddWatchFolder(dialog.SelectedPath);
            UpdateWatchFolders();
            await _libraryService.SaveLibraryAsync();
        }
    }
    
    private void RemoveFolder()
    {
        if (!string.IsNullOrEmpty(SelectedWatchFolder))
        {
            _libraryService.RemoveWatchFolder(SelectedWatchFolder);
            UpdateWatchFolders();
            _ = _libraryService.SaveLibraryAsync();
        }
    }
    
    private void CancelScan()
    {
        _scanCts?.Cancel();
    }
    
    private async Task CreatePlaylistAsync()
    {
        // Simple input dialog simulation - in real app use proper dialog
        var playlist = _libraryService.CreatePlaylist($"New Playlist {DateTime.Now:HH:mm}");
        UpdatePlaylists();
        await _libraryService.SaveLibraryAsync();
    }
    
    private void SelectPlaylist(object? parameter)
    {
        if (parameter is Playlist playlist)
        {
            SelectedPlaylist = playlist;
            ViewMode = LibraryViewMode.Playlist;
        }
    }
    
    private async Task PlayTrackAsync(AudioTrack? track)
    {
        if (track == null) return;
        
        // Set current view as queue
        _playerService.SetQueue(Tracks.ToList());
        await _playerService.PlayTrackAsync(track);
    }
    
    private async Task PlayAllAsync()
    {
        if (Tracks.Count == 0) return;
        
        _playerService.SetQueue(Tracks.ToList());
        await _playerService.PlayTrackAsync(Tracks[0]);
    }
    
    private void AddToQueue(object? parameter)
    {
        if (parameter is AudioTrack track)
        {
            _playerService.AddToQueue(track);
        }
    }
    
    private void RefreshView()
    {
        Tracks.Clear();
        
        var tracks = ViewMode switch
        {
            LibraryViewMode.AllTracks => _libraryService.SearchTracks(SearchQuery),
            LibraryViewMode.Artists => LoadArtistView(),
            LibraryViewMode.Albums => LoadAlbumView(),
            LibraryViewMode.Genres => LoadGenreView(),
            LibraryViewMode.RecentlyAdded => _libraryService.GetRecentlyAdded(),
            LibraryViewMode.RecentlyPlayed => _libraryService.GetRecentlyPlayed(),
            LibraryViewMode.MostPlayed => _libraryService.GetMostPlayed(),
            LibraryViewMode.TopRated => _libraryService.GetTopRated(),
            LibraryViewMode.Playlist => SelectedPlaylist != null 
                ? _libraryService.GetPlaylistTracks(SelectedPlaylist.Id) 
                : Enumerable.Empty<AudioTrack>(),
            _ => _libraryService.AllTracks
        };
        
        foreach (var track in tracks)
        {
            Tracks.Add(track);
        }
        
        RaisePropertyChanged(nameof(TrackCount));
        RaisePropertyChanged(nameof(TotalDuration));
    }
    
    private IEnumerable<AudioTrack> LoadArtistView()
    {
        Artists.Clear();
        foreach (var artist in _libraryService.AllArtists.OrderBy(a => a.Name))
        {
            Artists.Add(artist);
        }
        
        return SelectedArtist != null 
            ? _libraryService.GetTracksByArtist(SelectedArtist.Name)
            : Enumerable.Empty<AudioTrack>();
    }
    
    private IEnumerable<AudioTrack> LoadAlbumView()
    {
        Albums.Clear();
        foreach (var album in _libraryService.AllAlbums.OrderBy(a => a.Name))
        {
            Albums.Add(album);
        }
        
        return SelectedAlbum != null 
            ? _libraryService.GetTracksByAlbum($"{SelectedAlbum.Name}|{SelectedAlbum.DisplayArtist}")
            : Enumerable.Empty<AudioTrack>();
    }
    
    private IEnumerable<AudioTrack> LoadGenreView()
    {
        Genres.Clear();
        foreach (var genre in _libraryService.GetAllGenres())
        {
            Genres.Add(genre);
        }
        
        return Enumerable.Empty<AudioTrack>();
    }
    
    private void LoadArtistTracks(Artist artist)
    {
        Tracks.Clear();
        foreach (var track in _libraryService.GetTracksByArtist(artist.Name)
            .OrderBy(t => t.Album).ThenBy(t => t.TrackNumber))
        {
            Tracks.Add(track);
        }
    }
    
    private void LoadAlbumTracks(Album album)
    {
        Tracks.Clear();
        var albumKey = $"{album.Name}|{album.DisplayArtist}";
        foreach (var track in _libraryService.GetTracksByAlbum(albumKey)
            .OrderBy(t => t.DiscNumber).ThenBy(t => t.TrackNumber))
        {
            Tracks.Add(track);
        }
    }
    
    private void UpdateWatchFolders()
    {
        WatchFolders.Clear();
        foreach (var folder in _libraryService.WatchFolders)
        {
            WatchFolders.Add(folder);
        }
    }
    
    private void UpdatePlaylists()
    {
        Playlists.Clear();
        foreach (var playlist in _libraryService.AllPlaylists.Where(p => p.Type == PlaylistType.User))
        {
            Playlists.Add(playlist);
        }
    }
    
    private void OnLibraryUpdated(object? sender, EventArgs e)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            RefreshView();
            RaisePropertyChanged(nameof(TrackCount));
            RaisePropertyChanged(nameof(TotalDuration));
        });
    }
    
    private void OnScanProgressChanged(object? sender, int progress)
    {
        ScanProgress = progress;
    }
    
    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalDays >= 1)
            return $"{(int)duration.TotalDays}d {duration.Hours}h";
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        return $"{duration.Minutes}m {duration.Seconds}s";
    }
}

/// <summary>
/// Wrapper for AudioTrack with selection support.
/// </summary>
public class AudioTrackViewModel : BindableBase
{
    private bool _isSelected;
    
    public AudioTrack Track { get; }
    
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
    
    public AudioTrackViewModel(AudioTrack track)
    {
        Track = track;
    }
}

