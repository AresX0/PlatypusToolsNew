using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Represents a displayable item in the Continue Watching row.
    /// </summary>
    public class ContinueWatchingItem
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public double ProgressPercent { get; set; }
        public string TimeLeft { get; set; } = string.Empty;
        public string Icon { get; set; } = "🎬";
        public MediaType MediaType { get; set; }
    }

    /// <summary>
    /// Represents a TV series for display in the UI.
    /// </summary>
    public class TvSeriesDisplayItem
    {
        public string Name { get; set; } = string.Empty;
        public int SeasonCount { get; set; }
        public int EpisodeCount { get; set; }
        public string Summary { get; set; } = string.Empty;
        public TvSeries? Source { get; set; }
    }

    /// <summary>
    /// Represents a movie for display in the UI.
    /// </summary>
    public class MovieDisplayItem
    {
        public string Title { get; set; } = string.Empty;
        public int? Year { get; set; }
        public string Duration { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a playback history entry for display.
    /// </summary>
    public class HistoryDisplayItem
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string LastPlayed { get; set; } = string.Empty;
        public int PlayCount { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Icon { get; set; } = "🎬";
        public MediaType MediaType { get; set; }
    }

    /// <summary>
    /// Represents a playlist for display.
    /// </summary>
    public class PlaylistDisplayItem
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public string Duration { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents an episode for display in the series drill-down.
    /// </summary>
    public class EpisodeDisplayItem
    {
        public int EpisodeNumber { get; set; }
        public string? EpisodeTitle { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Duration { get; set; } = string.Empty;
        public string FileSize { get; set; } = string.Empty;
    }

    /// <summary>
    /// Represents a folder type designation for display.
    /// </summary>
    public class FolderDesignationItem
    {
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Persistent settings for the Media Hub (folder types, overrides, custom shows).
    /// Stored in %AppData%\PlatypusTools\media_hub_settings.json
    /// </summary>
    public class MediaHubSettings
    {
        [JsonPropertyName("tvShowFolders")]
        public List<string> TvShowFolders { get; set; } = new();

        [JsonPropertyName("movieFolders")]
        public List<string> MovieFolders { get; set; } = new();

        [JsonPropertyName("manualOverrides")]
        public Dictionary<string, string> ManualOverrides { get; set; } = new();

        [JsonPropertyName("customTvShows")]
        public Dictionary<string, List<string>> CustomTvShows { get; set; } = new();

        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "media_hub_settings.json");

        public static MediaHubSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<MediaHubSettings>(json) ?? new();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaHub] Error loading hub settings: {ex.Message}");
            }
            return new();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaHub] Error saving hub settings: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// ViewModel for the Media Hub view that surfaces Plex-like features:
    /// Continue Watching, TV Series, Movies, Playlists, and Playback History.
    /// Supports folder type designation, manual overrides, custom TV shows, and TMDb enrichment.
    /// </summary>
    public class MediaHubViewModel : BindableBase
    {
        private readonly ContentOrganizationService _contentService;
        private readonly PlaybackHistoryService _historyService;
        private readonly PlaylistService _playlistService;
        private readonly MetadataEnrichmentService _metadataService;
        private MediaHubSettings _hubSettings;
        private ContentLibrary? _contentLibrary;

        /// <summary>
        /// Raised when a file should be played in the in-app Video Player.
        /// The View subscribes and routes to PlayInVideoPlayerAsync.
        /// </summary>
        public event Action<string>? PlayFileRequested;

        public MediaHubViewModel()
        {
            _contentService = new ContentOrganizationService();
            _historyService = new PlaybackHistoryService();
            _playlistService = new PlaylistService();
            _metadataService = MetadataEnrichmentService.Instance;
            _hubSettings = MediaHubSettings.Load();

            // Initialize TMDb API key from app settings
            var tmdbKey = SettingsManager.Current?.TmdbApiKey;
            if (!string.IsNullOrWhiteSpace(tmdbKey))
                _metadataService.SetTmdbApiKey(tmdbKey);

            RefreshCommand = new RelayCommand(async _ => await LoadAllAsync(), _ => !IsLoading);
            PlayFileCommand = new RelayCommand(param => OnPlayFileRequested(param?.ToString()));
            OpenFolderCommand = new RelayCommand(param => OpenFolder(param?.ToString()));
            DrillIntoSeriesCommand = new RelayCommand(param => DrillIntoSeries(param as TvSeriesDisplayItem));
            DrillBackCommand = new RelayCommand(_ => DrillBack(), _ => IsSeriesDrillDown);
            ClearHistoryCommand = new RelayCommand(async _ => await ClearHistoryAsync());
            RemoveHistoryItemCommand = new RelayCommand(async param => await RemoveHistoryItemAsync(param as HistoryDisplayItem));

            // Content management commands
            AddTvShowFolderCommand = new RelayCommand(_ => AddTvShowFolder());
            RemoveTvShowFolderCommand = new RelayCommand(param => RemoveTvShowFolder(param as FolderDesignationItem), param => param is FolderDesignationItem);
            AddMovieFolderCommand = new RelayCommand(_ => AddMovieFolder());
            RemoveMovieFolderCommand = new RelayCommand(param => RemoveMovieFolder(param as FolderDesignationItem), param => param is FolderDesignationItem);
            MoveToTvShowCommand = new RelayCommand(param => MoveToTvShow(param as MovieDisplayItem), param => param is MovieDisplayItem);
            MoveToMoviesCommand = new RelayCommand(param => MoveToMovies(param?.ToString()));
            CreateNewTvShowCommand = new RelayCommand(_ => CreateNewTvShow());
            ToggleContentSettingsCommand = new RelayCommand(_ => ShowContentSettings = !ShowContentSettings);

            // TMDb enrichment commands
            EnrichMovieCommand = new RelayCommand(async param => await EnrichMovieAsync(param as MovieDisplayItem), param => param is MovieDisplayItem && IsTmdbConfigured);
            EnrichSeriesCommand = new RelayCommand(async param => await EnrichSeriesAsync(param as TvSeriesDisplayItem), param => param is TvSeriesDisplayItem && IsTmdbConfigured);

            // Load folder designations
            LoadFolderDesignations();

            // Load data on construction
            _ = LoadAllAsync();
        }

        #region Collections

        public ObservableCollection<ContinueWatchingItem> ContinueWatchingItems { get; } = new();
        public ObservableCollection<TvSeriesDisplayItem> TvSeriesList { get; } = new();
        public ObservableCollection<MovieDisplayItem> MoviesList { get; } = new();
        public ObservableCollection<HistoryDisplayItem> RecentHistory { get; } = new();
        public ObservableCollection<PlaylistDisplayItem> Playlists { get; } = new();
        public ObservableCollection<EpisodeDisplayItem> DrillDownEpisodes { get; } = new();
        public ObservableCollection<FolderDesignationItem> TvShowFolders { get; } = new();
        public ObservableCollection<FolderDesignationItem> MovieFolders { get; } = new();

        #endregion

        #region Properties

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                    ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
            }
        }

        private string _statusMessage = "Loading...";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _totalVideos;
        public int TotalVideos { get => _totalVideos; set => SetProperty(ref _totalVideos, value); }

        private int _totalSeries;
        public int TotalSeries { get => _totalSeries; set => SetProperty(ref _totalSeries, value); }

        private int _totalMovies;
        public int TotalMovies { get => _totalMovies; set => SetProperty(ref _totalMovies, value); }

        private bool _isSeriesDrillDown;
        public bool IsSeriesDrillDown
        {
            get => _isSeriesDrillDown;
            set
            {
                if (SetProperty(ref _isSeriesDrillDown, value))
                {
                    OnPropertyChanged(nameof(SeriesListVisibility));
                    OnPropertyChanged(nameof(DrillDownVisibility));
                    ((RelayCommand)DrillBackCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private string _drillDownTitle = string.Empty;
        public string DrillDownTitle { get => _drillDownTitle; set => SetProperty(ref _drillDownTitle, value); }

        public Visibility SeriesListVisibility => IsSeriesDrillDown ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DrillDownVisibility => IsSeriesDrillDown ? Visibility.Visible : Visibility.Collapsed;

        private bool _hasContinueWatching;
        public bool HasContinueWatching { get => _hasContinueWatching; set => SetProperty(ref _hasContinueWatching, value); }

        private bool _hasLibraryFolders;
        public bool HasLibraryFolders { get => _hasLibraryFolders; set => SetProperty(ref _hasLibraryFolders, value); }

        private bool _showContentSettings;
        public bool ShowContentSettings
        {
            get => _showContentSettings;
            set => SetProperty(ref _showContentSettings, value);
        }

        public bool IsTmdbConfigured => _metadataService.IsTmdbConfigured;

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand PlayFileCommand { get; }
        public ICommand OpenFolderCommand { get; }
        public ICommand DrillIntoSeriesCommand { get; }
        public ICommand DrillBackCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand RemoveHistoryItemCommand { get; }

        // Content management
        public ICommand AddTvShowFolderCommand { get; }
        public ICommand RemoveTvShowFolderCommand { get; }
        public ICommand AddMovieFolderCommand { get; }
        public ICommand RemoveMovieFolderCommand { get; }
        public ICommand MoveToTvShowCommand { get; }
        public ICommand MoveToMoviesCommand { get; }
        public ICommand CreateNewTvShowCommand { get; }
        public ICommand ToggleContentSettingsCommand { get; }

        // TMDb enrichment
        public ICommand EnrichMovieCommand { get; }
        public ICommand EnrichSeriesCommand { get; }

        #endregion

        #region Data Loading

        private async Task LoadAllAsync()
        {
            IsLoading = true;
            StatusMessage = "Scanning media library...";

            try
            {
                // Load all data in parallel
                var historyTask = LoadContinueWatchingAsync();
                var playlistTask = LoadPlaylistsAsync();
                var recentTask = LoadRecentHistoryAsync();

                // Scan video files from media library folders
                var videoFiles = await Task.Run(() => ScanVideoFiles());
                HasLibraryFolders = videoFiles.Count > 0;

                if (videoFiles.Count > 0)
                {
                    var videoFileInfos = videoFiles.Select(f => new VideoFileInfo
                    {
                        FilePath = f,
                        DurationSeconds = 0,
                        FileSizeBytes = new FileInfo(f).Length
                    });

                    // Build organization options from hub settings
                    var options = new ContentOrganizationOptions
                    {
                        TvShowFolders = _hubSettings.TvShowFolders,
                        MovieFolders = _hubSettings.MovieFolders,
                        ManualOverrides = _hubSettings.ManualOverrides,
                        CustomTvShows = _hubSettings.CustomTvShows
                    };

                    _contentLibrary = _contentService.OrganizeContent(videoFileInfos, options);
                    TotalVideos = videoFiles.Count;
                    TotalSeries = _contentLibrary.TvSeries.Count;
                    TotalMovies = _contentLibrary.Movies.Count;

                    LoadTvSeries(_contentLibrary);
                    LoadMovies(_contentLibrary);
                }
                else
                {
                    TotalVideos = 0;
                    TotalSeries = 0;
                    TotalMovies = 0;
                }

                await Task.WhenAll(historyTask, playlistTask, recentTask);

                StatusMessage = $"📊 {TotalVideos} videos · {TotalSeries} series · {TotalMovies} movies · {Playlists.Count} playlists";
                if (_hubSettings.TvShowFolders.Count > 0 || _hubSettings.MovieFolders.Count > 0)
                    StatusMessage += $" · 📁 {_hubSettings.TvShowFolders.Count} TV folders, {_hubSettings.MovieFolders.Count} movie folders";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                Debug.WriteLine($"[MediaHub] Error loading: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private List<string> ScanVideoFiles()
        {
            var folders = LoadMediaLibraryFolders();
            var videoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
                ".mpeg", ".mpg", ".m4v", ".3gp", ".ts", ".m2ts"
            };

            var files = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try
                {
                    files.AddRange(Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                        .Where(f => videoExts.Contains(Path.GetExtension(f))));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[MediaHub] Error scanning {folder}: {ex.Message}");
                }
            }

            return files;
        }

        private static List<string> LoadMediaLibraryFolders()
        {
            try
            {
                var settingsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools", "media_library_settings.json");

                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("LibraryFolders", out var foldersElement))
                    {
                        var folders = JsonSerializer.Deserialize<List<string>>(foldersElement.GetRawText());
                        if (folders != null && folders.Count > 0)
                            return folders;
                    }

                    if (doc.RootElement.TryGetProperty("PrimaryLibraryPath", out var pathElement))
                    {
                        var path = pathElement.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                            return new List<string> { path };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaHub] Error loading folders: {ex.Message}");
            }
            return new List<string>();
        }

        private void LoadTvSeries(ContentLibrary library)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                TvSeriesList.Clear();
                foreach (var series in library.TvSeries)
                {
                    TvSeriesList.Add(new TvSeriesDisplayItem
                    {
                        Name = series.Name,
                        SeasonCount = series.Seasons.Count,
                        EpisodeCount = series.TotalEpisodes,
                        Summary = $"{series.Seasons.Count} season{(series.Seasons.Count != 1 ? "s" : "")} · {series.TotalEpisodes} episode{(series.TotalEpisodes != 1 ? "s" : "")}",
                        Source = series
                    });
                }
            });
        }

        private void LoadMovies(ContentLibrary library)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                MoviesList.Clear();
                foreach (var movie in library.Movies)
                {
                    MoviesList.Add(new MovieDisplayItem
                    {
                        Title = movie.Title,
                        Year = movie.Year,
                        Duration = FormatDuration(movie.DurationSeconds),
                        FilePath = movie.FilePath,
                        FileName = movie.FileName,
                        FileSize = FormatFileSize(movie.FileSizeBytes)
                    });
                }
            });
        }

        private async Task LoadContinueWatchingAsync()
        {
            try
            {
                var inProgress = await _historyService.GetInProgressAsync();
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    ContinueWatchingItems.Clear();
                    foreach (var item in inProgress.Take(20))
                    {
                        var remaining = item.DurationSeconds - item.LastPositionSeconds;
                        var displayTitle = item.EpisodeNumber.HasValue
                            ? $"S{item.SeasonNumber ?? 0}E{item.EpisodeNumber} · {item.Title}"
                            : item.Title;

                        ContinueWatchingItems.Add(new ContinueWatchingItem
                        {
                            Title = displayTitle,
                            Subtitle = remaining > 0 ? $"{FormatDuration(remaining)} left" : "",
                            FilePath = item.FilePath,
                            ProgressPercent = item.ProgressPercent,
                            TimeLeft = remaining > 0 ? FormatDuration(remaining) : "",
                            Icon = item.MediaType == MediaType.Audio ? "🎵" : "🎬",
                            MediaType = item.MediaType
                        });
                    }
                    HasContinueWatching = ContinueWatchingItems.Count > 0;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaHub] Error loading continue watching: {ex.Message}");
            }
        }

        private async Task LoadPlaylistsAsync()
        {
            try
            {
                var all = await _playlistService.GetAllAsync();
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    Playlists.Clear();
                    foreach (var playlist in all)
                    {
                        Playlists.Add(new PlaylistDisplayItem
                        {
                            Id = playlist.Id,
                            Name = playlist.Name,
                            ItemCount = playlist.ItemCount,
                            Duration = FormatDuration(playlist.TotalDurationSeconds),
                            Type = playlist.Type.ToString()
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaHub] Error loading playlists: {ex.Message}");
            }
        }

        private async Task LoadRecentHistoryAsync()
        {
            try
            {
                var recent = await _historyService.GetRecentAsync(100);
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    RecentHistory.Clear();
                    foreach (var entry in recent)
                    {
                        RecentHistory.Add(new HistoryDisplayItem
                        {
                            Title = entry.Title,
                            Artist = entry.Artist,
                            LastPlayed = FormatRelativeTime(entry.LastPlayedAt),
                            PlayCount = entry.PlayCount,
                            FilePath = entry.FilePath,
                            Icon = entry.MediaType == MediaType.Audio ? "🎵" : "🎬",
                            MediaType = entry.MediaType
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaHub] Error loading history: {ex.Message}");
            }
        }

        #endregion

        #region Actions

        private void OnPlayFileRequested(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            PlayFileRequested?.Invoke(filePath);
        }

        private void OpenFolder(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select, \"{filePath}\"",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MediaHub] Error opening folder: {ex.Message}");
            }
        }

        private void DrillIntoSeries(TvSeriesDisplayItem? series)
        {
            if (series?.Source == null) return;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                DrillDownEpisodes.Clear();
                DrillDownTitle = series.Name;

                foreach (var season in series.Source.Seasons.OrderBy(s => s.SeasonNumber))
                {
                    foreach (var ep in season.Episodes.OrderBy(e => e.EpisodeNumber))
                    {
                        DrillDownEpisodes.Add(new EpisodeDisplayItem
                        {
                            EpisodeNumber = ep.EpisodeNumber,
                            EpisodeTitle = ep.EpisodeTitle ?? $"S{season.SeasonNumber:D2}E{ep.EpisodeNumber:D2}",
                            FilePath = ep.FilePath,
                            Duration = FormatDuration(ep.DurationSeconds),
                            FileSize = FormatFileSize(ep.FileSizeBytes)
                        });
                    }
                }

                IsSeriesDrillDown = true;
            });
        }

        private void DrillBack()
        {
            IsSeriesDrillDown = false;
            DrillDownTitle = string.Empty;
            DrillDownEpisodes.Clear();
        }

        private async Task ClearHistoryAsync()
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all playback history?",
                "Clear History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _historyService.ClearAsync();
                RecentHistory.Clear();
                ContinueWatchingItems.Clear();
                HasContinueWatching = false;
                StatusMessage = "History cleared.";
            }
        }

        private async Task RemoveHistoryItemAsync(HistoryDisplayItem? item)
        {
            if (item == null) return;
            await _historyService.RemoveAsync(item.FilePath);
            RecentHistory.Remove(item);
        }

        #endregion

        #region Folder Designation

        private void LoadFolderDesignations()
        {
            TvShowFolders.Clear();
            foreach (var folder in _hubSettings.TvShowFolders)
            {
                TvShowFolders.Add(new FolderDesignationItem
                {
                    FolderPath = folder,
                    FolderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                });
            }

            MovieFolders.Clear();
            foreach (var folder in _hubSettings.MovieFolders)
            {
                MovieFolders.Add(new FolderDesignationItem
                {
                    FolderPath = folder,
                    FolderName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                });
            }
        }

        private void AddTvShowFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a folder to designate as TV Shows"
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                var folderPath = dialog.FolderName;
                if (_hubSettings.TvShowFolders.Any(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("This folder is already designated as TV Shows.", "Already Added", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Remove from movie folders if present
                _hubSettings.MovieFolders.RemoveAll(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase));

                _hubSettings.TvShowFolders.Add(folderPath);
                _hubSettings.Save();
                LoadFolderDesignations();
                _ = LoadAllAsync(); // Re-scan with new settings
            }
        }

        private void RemoveTvShowFolder(FolderDesignationItem? item)
        {
            if (item == null) return;
            _hubSettings.TvShowFolders.RemoveAll(f => string.Equals(f, item.FolderPath, StringComparison.OrdinalIgnoreCase));
            _hubSettings.Save();
            LoadFolderDesignations();
            _ = LoadAllAsync();
        }

        private void AddMovieFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select a folder to designate as Movies"
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                var folderPath = dialog.FolderName;
                if (_hubSettings.MovieFolders.Any(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("This folder is already designated as Movies.", "Already Added", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Remove from TV show folders if present
                _hubSettings.TvShowFolders.RemoveAll(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase));

                _hubSettings.MovieFolders.Add(folderPath);
                _hubSettings.Save();
                LoadFolderDesignations();
                _ = LoadAllAsync();
            }
        }

        private void RemoveMovieFolder(FolderDesignationItem? item)
        {
            if (item == null) return;
            _hubSettings.MovieFolders.RemoveAll(f => string.Equals(f, item.FolderPath, StringComparison.OrdinalIgnoreCase));
            _hubSettings.Save();
            LoadFolderDesignations();
            _ = LoadAllAsync();
        }

        #endregion

        #region Move Between Categories

        private void MoveToTvShow(MovieDisplayItem? movie)
        {
            if (movie == null || string.IsNullOrEmpty(movie.FilePath)) return;

            // Prompt for series name
            var seriesName = PromptForInput("Move to TV Show",
                "Enter the TV series name for this video:",
                movie.Title);

            if (string.IsNullOrWhiteSpace(seriesName)) return;

            _hubSettings.ManualOverrides[movie.FilePath] = $"tv:{seriesName}";
            _hubSettings.Save();
            _ = LoadAllAsync();
        }

        private void MoveToMovies(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            _hubSettings.ManualOverrides[filePath] = "movie";
            _hubSettings.Save();
            _ = LoadAllAsync();
        }

        private void CreateNewTvShow()
        {
            var showName = PromptForInput("Create New TV Show",
                "Enter a name for the new TV show:",
                string.Empty);

            if (string.IsNullOrWhiteSpace(showName)) return;

            if (_hubSettings.CustomTvShows.ContainsKey(showName))
            {
                MessageBox.Show($"A custom TV show named '{showName}' already exists.", "Already Exists", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Let user pick files
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select video files for '{showName}'",
                Multiselect = true,
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.mpeg;*.mpg;*.m4v;*.3gp;*.ts;*.m2ts|All Files|*.*"
            };

            if (dialog.ShowDialog() == true && dialog.FileNames.Length > 0)
            {
                _hubSettings.CustomTvShows[showName] = new List<string>(dialog.FileNames);
                _hubSettings.Save();
                _ = LoadAllAsync();
            }
        }

        private static string? PromptForInput(string title, string prompt, string defaultValue)
        {
            // Use a simple input dialog - WPF doesn't have one built in, so we use an InputBox
            var win = new Window
            {
                Title = title,
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Owner = Application.Current?.MainWindow
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            stack.Children.Add(new System.Windows.Controls.TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var textBox = new System.Windows.Controls.TextBox { Text = defaultValue, Margin = new Thickness(0, 0, 0, 12) };
            textBox.SelectAll();
            stack.Children.Add(textBox);

            var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var okBtn = new System.Windows.Controls.Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", Width = 80, IsCancel = true };
            okBtn.Click += (s, e) => { win.DialogResult = true; win.Close(); };
            cancelBtn.Click += (s, e) => { win.DialogResult = false; win.Close(); };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            stack.Children.Add(btnPanel);

            win.Content = stack;
            textBox.Focus();

            return win.ShowDialog() == true ? textBox.Text?.Trim() : null;
        }

        #endregion

        #region TMDb Enrichment

        private async Task EnrichMovieAsync(MovieDisplayItem? movie)
        {
            if (movie == null || !_metadataService.IsTmdbConfigured) return;

            try
            {
                StatusMessage = $"🔍 Looking up '{movie.Title}' on TMDb...";
                var result = await _metadataService.SearchTmdbMovieAsync(movie.Title, movie.Year);
                if (result != null)
                {
                    StatusMessage = $"✅ Found: {result.Title} ({result.Year}) — {result.Genre} — ⭐ {result.Rating:F1}";
                    MessageBox.Show(
                        $"Title: {result.Title}\nYear: {result.Year}\nGenre: {result.Genre}\nRating: {result.Rating:F1}/10\nDirector: {result.Director}\n\nDescription:\n{result.Description}",
                        "TMDb Result",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = $"❌ No TMDb results for '{movie.Title}'";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"TMDb error: {ex.Message}";
                Debug.WriteLine($"[MediaHub] TMDb movie enrichment error: {ex}");
            }
        }

        private async Task EnrichSeriesAsync(TvSeriesDisplayItem? series)
        {
            if (series == null || !_metadataService.IsTmdbConfigured) return;

            try
            {
                StatusMessage = $"🔍 Looking up '{series.Name}' on TMDb...";
                var result = await _metadataService.SearchTmdbTvShowAsync(series.Name);
                if (result != null)
                {
                    StatusMessage = $"✅ Found: {result.SeriesName ?? result.Title} — {result.Genre} — ⭐ {result.Rating:F1}";
                    MessageBox.Show(
                        $"Series: {result.SeriesName ?? result.Title}\nGenre: {result.Genre}\nRating: {result.Rating:F1}/10\n\nDescription:\n{result.Description}",
                        "TMDb Result",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = $"❌ No TMDb results for '{series.Name}'";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"TMDb error: {ex.Message}";
                Debug.WriteLine($"[MediaHub] TMDb series enrichment error: {ex}");
            }
        }

        #endregion

        #region Formatters

        private static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds <= 0) return "";
            var ts = TimeSpan.FromSeconds(totalSeconds);
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}h {ts.Minutes}m"
                : $"{ts.Minutes}m {ts.Seconds}s";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return "";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private static string FormatRelativeTime(DateTime utcTime)
        {
            var diff = DateTime.UtcNow - utcTime;
            if (diff.TotalMinutes < 1) return "Just now";
            if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes}m ago";
            if (diff.TotalHours < 24) return $"{(int)diff.TotalHours}h ago";
            if (diff.TotalDays < 7) return $"{(int)diff.TotalDays}d ago";
            return utcTime.ToLocalTime().ToString("MMM d, yyyy");
        }

        #endregion
    }
}
