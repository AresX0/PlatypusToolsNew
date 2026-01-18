using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

/// <summary>
/// Helper class for Win32 window handle
/// </summary>
internal class Win32Window : System.Windows.Forms.IWin32Window
{
    public IntPtr Handle { get; }
    public Win32Window(IntPtr handle) => Handle = handle;
}

/// <summary>
/// Audio Player view with integrated visualizer supporting multiple visualization modes.
/// </summary>
public partial class AudioPlayerView : UserControl
{
    private AudioPlayerViewModel? _viewModel;
    private bool _isUserSeeking = false;
    private bool _servicesSubscribed = false;
    
    public AudioPlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        
        // Subscribe to critical events immediately in constructor
        // This ensures we don't miss any TrackChanged events
        SubscribeToServiceEvents();
    }
    
    /// <summary>
    /// Subscribe to service events. Can be called multiple times safely.
    /// </summary>
    private void SubscribeToServiceEvents()
    {
        if (_servicesSubscribed) return;
        
        // Subscribe to track changes from the service
        PlatypusTools.UI.Services.AudioPlayerService.Instance.TrackChanged += OnServiceTrackChanged;
        
        // Subscribe to spectrum data for visualizer
        PlatypusTools.UI.Services.AudioPlayerService.Instance.SpectrumDataUpdated += OnServiceSpectrumDataUpdated;
        
        // Subscribe to position changes for real-time updates
        PlatypusTools.UI.Services.AudioPlayerService.Instance.PositionChanged += OnServicePositionChanged;
        
        // Subscribe to media ready for duration updates
        PlatypusTools.UI.Services.AudioPlayerService.Instance.MediaReady += OnServiceMediaReady;
        
        _servicesSubscribed = true;
        System.Diagnostics.Debug.WriteLine("AudioPlayerView: Subscribed to all service events");
    }
    
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "debug_log.txt");
        try
        {
            var dir = System.IO.Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.AppendAllText(logPath, $"\n[{DateTime.Now}] ===== OnLoaded STARTED =====\n");
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] OnLoaded: DataContext type = {DataContext?.GetType().Name ?? "null"}\n");
            
            // Skip DataGrid column initialization - it causes exceptions and isn't needed
            // The XAML already sets proper column widths
            
            _viewModel = DataContext as AudioPlayerViewModel;
            System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] OnLoaded: Cast result _viewModel = {(_viewModel != null ? "SUCCESS" : "NULL")}\n");
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] OnLoaded OUTER EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n"); } catch { }
        }
        
        if (_viewModel != null)
        {
            try
            {
                var logPath2 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "debug_log.txt");
                System.IO.File.AppendAllText(logPath2, $"[{DateTime.Now}] OnLoaded: _viewModel assigned, subscribing to PropertyChanged\n");
                
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
                
                System.IO.File.AppendAllText(logPath2, $"[{DateTime.Now}] OnLoaded: Calling InitializeVisualizer\n");
                // Initialize audio visualizer
                InitializeVisualizer();
                
                System.IO.File.AppendAllText(logPath2, $"[{DateTime.Now}] OnLoaded: Calling LoadLibraryFoldersToUI\n");
                // Load library folders into the ListBox directly (bypass binding)
                LoadLibraryFoldersToUI();
                
                System.IO.File.AppendAllText(logPath2, $"[{DateTime.Now}] OnLoaded: LoadLibraryFoldersToUI completed, IsScanning={_viewModel.IsScanning}\n");
                
                // Wait for ViewModel to finish loading library (watch IsScanning)
                // The ViewModel's InitializeLibraryAsync sets IsScanning=true at start, false when done
                int waitAttempts = 0;
                const int maxWaitAttempts = 50; // 5 seconds max
                while (_viewModel.IsScanning && waitAttempts < maxWaitAttempts)
                {
                    await Task.Delay(100);
                    waitAttempts++;
                }
                
                System.IO.File.AppendAllText(logPath2, $"[{DateTime.Now}] OnLoaded: Wait complete after {waitAttempts * 100}ms, LibraryTracks={_viewModel.LibraryTracks.Count}\n");
                
                // Now refresh the library grid - ViewModel should have data loaded
                RefreshLibraryTrackGrid();
                
                System.IO.File.AppendAllText(logPath2, $"[{DateTime.Now}] OnLoaded: RefreshLibraryTrackGrid completed\n");
            }
            catch (Exception ex)
            {
                var logPath2 = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "debug_log.txt");
                System.IO.File.AppendAllText(logPath2, $"[{DateTime.Now}] OnLoaded: EXCEPTION - {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}\n");
            }
        }
        
        // Ensure events are subscribed (in case constructor didn't complete subscription)
        SubscribeToServiceEvents();
        
        // Start a timer to update position display
        StartPositionTimer();
        
        // Force initial update of visualizer settings
        ForceVisualizerUpdate();
        
        // Check if there's already a track playing and update UI
        var service = PlatypusTools.UI.Services.AudioPlayerService.Instance;
        if (service.CurrentTrack != null)
        {
            System.Diagnostics.Debug.WriteLine($"OnLoaded: Found existing track '{service.CurrentTrack.DisplayTitle}', updating UI");
            OnServiceTrackChanged(service, service.CurrentTrack);
        }
    }
    
    /// <summary>
    /// Forces an update of the visualizer with current settings.
    /// </summary>
    private void ForceVisualizerUpdate()
    {
        if (VisualizerControl == null) return;
        
        int modeIndex = _viewModel?.VisualizerModeIndex ?? 0;
        int barCount = _viewModel?.BarCount ?? 72;
        int colorIndex = _viewModel?.ColorSchemeIndex ?? 0;
        
        string mode = GetVisualizerModeName(modeIndex);
        VisualizerControl.SetColorScheme(colorIndex);
        VisualizerControl.UpdateSpectrumData(Array.Empty<double>(), mode, barCount);
        
        System.Diagnostics.Debug.WriteLine($"ForceVisualizerUpdate: mode={mode}, bars={barCount}, color={colorIndex}");
    }
    
    private void OnServiceTrackChanged(object? sender, PlatypusTools.Core.Models.Audio.AudioTrack? track)
    {
        Dispatcher.Invoke(() =>
        {
            System.Diagnostics.Debug.WriteLine($"OnServiceTrackChanged: Track = {track?.DisplayTitle ?? "null"}");
            
            if (track != null)
            {
                var service = PlatypusTools.UI.Services.AudioPlayerService.Instance;
                
                // Direct UI update - don't rely on bindings
                NowPlayingTitle.Text = track.DisplayTitle;
                NowPlayingArtist.Text = track.DisplayArtist;
                NowPlayingAlbum.Text = track.DisplayAlbum;
                
                // Update duration from service if available, otherwise from track
                var duration = service.Duration.TotalSeconds > 0 
                    ? service.Duration 
                    : track.Duration;
                    
                if (duration.TotalSeconds > 0)
                {
                    NowPlayingDuration.Text = duration.ToString(@"m\:ss");
                    NowPlayingProgress.Maximum = duration.TotalSeconds;
                }
                
                // Update ViewModel's CurrentTrack too
                var vm = GetViewModel();
                if (vm != null)
                {
                    vm.CurrentTrack = track;
                }
                
                // Load album art
                LoadAlbumArt(track);
                
                // Update queue count
                UpdateQueueCount();
                
                System.Diagnostics.Debug.WriteLine($"OnServiceTrackChanged: Updated Now Playing to '{track.DisplayTitle}', Duration: {duration}");
            }
            else
            {
                NowPlayingTitle.Text = "No track";
                NowPlayingArtist.Text = "Unknown";
                NowPlayingAlbum.Text = "Unknown";
                NowPlayingPosition.Text = "0:00";
                NowPlayingDuration.Text = "0:00";
                NowPlayingProgress.Value = 0;
                NowPlayingProgress.Maximum = 100;
                AlbumArtImage.Source = null;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
            }
        });
    }
    
    private void OnServicePositionChanged(object? sender, TimeSpan position)
    {
        // Don't use Invoke for high-frequency updates - use BeginInvoke
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isUserSeeking)
            {
                NowPlayingPosition.Text = position.ToString(@"m\:ss");
                
                var service = PlatypusTools.UI.Services.AudioPlayerService.Instance;
                if (service.Duration.TotalSeconds > 0)
                {
                    NowPlayingProgress.Value = position.TotalSeconds;
                }
            }
        });
    }
    
    private void OnServiceMediaReady(object? sender, TimeSpan duration)
    {
        Dispatcher.Invoke(() =>
        {
            if (duration.TotalSeconds > 0)
            {
                NowPlayingDuration.Text = duration.ToString(@"m\:ss");
                NowPlayingProgress.Maximum = duration.TotalSeconds;
                System.Diagnostics.Debug.WriteLine($"OnServiceMediaReady: Duration = {duration}");
            }
        });
    }
    
    private void OnServiceSpectrumDataUpdated(object? sender, double[] data)
    {
        // Use BeginInvoke for better performance with frequent updates
        Dispatcher.BeginInvoke(() =>
        {
            if (VisualizerControl != null && data != null && data.Length > 0)
            {
                var vm = GetViewModel();
                string mode = GetVisualizerModeName(vm?.VisualizerModeIndex ?? 0);
                int barCount = vm?.BarCount ?? 72;
                int colorIndex = vm?.ColorSchemeIndex ?? 0;
                
                VisualizerControl.SetColorScheme(colorIndex);
                VisualizerControl.UpdateSpectrumData(data, mode, barCount);
            }
        });
    }
    
    /// <summary>
    /// Gets the visualizer mode name from the mode index.
    /// </summary>
    private static string GetVisualizerModeName(int index) => index switch
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
    
    private System.Windows.Threading.DispatcherTimer? _positionTimer;
    
    /// <summary>
    /// Starts a timer to update the position display every 250ms.
    /// </summary>
    private void StartPositionTimer()
    {
        _positionTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _positionTimer.Tick += (s, e) =>
        {
            try
            {
                var service = PlatypusTools.UI.Services.AudioPlayerService.Instance;
                var track = service.CurrentTrack;
                var isPlaying = service.IsPlaying;
                var queueCount = service.Queue.Count;
                
                // Debug: Log state every few seconds
                if (DateTime.Now.Second % 5 == 0 && DateTime.Now.Millisecond < 300)
                {
                    System.Diagnostics.Debug.WriteLine($"PositionTimer: CurrentTrack={track?.DisplayTitle ?? "NULL"}, IsPlaying={isPlaying}, QueueCount={queueCount}, CurrentIndex={service.CurrentIndex}");
                }
                
                // Always update position if there's a current track
                if (track != null)
                {
                    var position = service.Position;
                    var duration = service.Duration;
                    
                    // Update track info if it doesn't match
                    if (NowPlayingTitle.Text != track.DisplayTitle)
                    {
                        System.Diagnostics.Debug.WriteLine($"PositionTimer: Updating Now Playing to '{track.DisplayTitle}'");
                        NowPlayingTitle.Text = track.DisplayTitle;
                        NowPlayingArtist.Text = track.DisplayArtist;
                        NowPlayingAlbum.Text = track.DisplayAlbum;
                        LoadAlbumArt(track);
                    }
                    
                    // Update position display
                    NowPlayingPosition.Text = position.ToString(@"m\:ss");
                    
                    // Update duration and slider if available (and user isn't seeking)
                    if (duration.TotalSeconds > 0)
                    {
                        NowPlayingDuration.Text = duration.ToString(@"m\:ss");
                        NowPlayingProgress.Maximum = duration.TotalSeconds;
                        
                        // Only update slider position if user is not dragging
                        if (!_isUserSeeking)
                        {
                            NowPlayingProgress.Value = position.TotalSeconds;
                        }
                    }
                }
                else if (isPlaying && queueCount > 0)
                {
                    // This is a problem state: playing but no current track
                    // Try to get the first track from the queue as a fallback
                    System.Diagnostics.Debug.WriteLine($"PositionTimer WARNING: IsPlaying={isPlaying} but CurrentTrack is null with {queueCount} items in queue! CurrentIndex={service.CurrentIndex}");
                    
                    // Attempt recovery: get first track from queue
                    var firstTrack = service.Queue.FirstOrDefault();
                    if (firstTrack != null && NowPlayingTitle.Text == "No track")
                    {
                        System.Diagnostics.Debug.WriteLine($"PositionTimer: Attempting recovery with first queue track '{firstTrack.DisplayTitle}'");
                        NowPlayingTitle.Text = firstTrack.DisplayTitle;
                        NowPlayingArtist.Text = firstTrack.DisplayArtist;
                        NowPlayingAlbum.Text = firstTrack.DisplayAlbum;
                        LoadAlbumArt(firstTrack);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Position timer error: {ex.Message}");
            }
        };
        _positionTimer.Start();
        System.Diagnostics.Debug.WriteLine("Position timer started");
    }
    
    /// <summary>
    /// Loads library folders from file and updates the ListBox directly.
    /// </summary>
    private void LoadLibraryFoldersToUI()
    {
        try
        {
            var foldersPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools",
                "library_folders.json");
            
            var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "debug_log.txt");
            System.IO.File.AppendAllText(logPath, $"\n[{DateTime.Now}] LoadLibraryFoldersToUI: Looking for {foldersPath}, exists={System.IO.File.Exists(foldersPath)}\n");
            
            if (System.IO.File.Exists(foldersPath))
            {
                var json = System.IO.File.ReadAllText(foldersPath);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] LoadLibraryFoldersToUI: JSON length = {json.Length}\n");
                var folders = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] LoadLibraryFoldersToUI: Deserialized {folders?.Count ?? 0} folders\n");
                if (folders != null && folders.Count > 0)
                {
                    var vm = GetViewModel();
                    System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] LoadLibraryFoldersToUI: vm={vm != null}, LibraryFoldersListBox={LibraryFoldersListBox != null}\n");
                    if (vm != null)
                    {
                        vm.LibraryFolders.Clear();
                        foreach (var folder in folders)
                            vm.LibraryFolders.Add(folder);
                        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] LoadLibraryFoldersToUI: Added {folders.Count} folders to ViewModel\n");
                    }
                    
                    // Set ItemsSource directly
                    if (LibraryFoldersListBox != null)
                    {
                        LibraryFoldersListBox.ItemsSource = folders;
                        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] LoadLibraryFoldersToUI: Set ItemsSource with {folders.Count} folders, Items.Count={LibraryFoldersListBox.Items.Count}\n");
                    }
                }
            }
            else
            {
                System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] LoadLibraryFoldersToUI: File does not exist\n");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading library folders: {ex.Message}");
        }
    }
    
    private void InitializeVisualizer()
    {
        if (VisualizerControl != null)
        {
            // No special initialization needed - visualizer renders on its own
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioPlayerViewModel.SpectrumData))
        {
            // Feed spectrum data to the native visualizer
            if (VisualizerControl != null && _viewModel?.SpectrumData != null)
            {
                var specData = _viewModel.SpectrumData;
                string mode = GetVisualizerModeName(_viewModel.VisualizerModeIndex);
                
                VisualizerControl.UpdateSpectrumData(specData, mode, _viewModel.BarCount);
            }
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.VisualizerModeIndex) ||
                 e.PropertyName == nameof(AudioPlayerViewModel.BarCount))
        {
            // Mode or bar count changed - update visualizer with current mode/barcount
            if (VisualizerControl != null && _viewModel != null)
            {
                string mode = GetVisualizerModeName(_viewModel.VisualizerModeIndex);
                System.Diagnostics.Debug.WriteLine($"Mode changed to: {mode} (index {_viewModel.VisualizerModeIndex})");
                
                // Update with empty data to trigger mode/bar change
                VisualizerControl.UpdateSpectrumData(Array.Empty<double>(), mode, _viewModel.BarCount);
            }
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.CurrentTrack))
        {
            // Track changed - update Now Playing panel directly
            UpdateNowPlayingPanel();
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.Queue))
        {
            // Queue changed - update count
            UpdateQueueCount();
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.Position))
        {
            UpdatePositionDisplay();
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.StatusMessage))
        {
            if (StatusText != null && _viewModel != null)
                StatusText.Text = _viewModel.StatusMessage;
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.LibraryTracks) ||
                 e.PropertyName == nameof(AudioPlayerViewModel.LibraryTrackCount))
        {
            // Library tracks changed - refresh the grid
            Dispatcher.InvokeAsync(() => RefreshLibraryTrackGrid());
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.IsScanning))
        {
            // When scanning finishes, refresh the grid
            if (_viewModel != null && !_viewModel.IsScanning)
            {
                Dispatcher.InvokeAsync(() => RefreshLibraryTrackGrid());
            }
        }
    }
    
    /// <summary>
    /// Updates the Now Playing panel with current track info (bypasses binding issues).
    /// </summary>
    private void UpdateNowPlayingPanel()
    {
        Dispatcher.Invoke(() =>
        {
            // Always get track directly from the service
            var track = PlatypusTools.UI.Services.AudioPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                NowPlayingTitle.Text = track.DisplayTitle;
                NowPlayingArtist.Text = track.DisplayArtist;
                NowPlayingAlbum.Text = track.DisplayAlbum;
                
                // Get duration from service if track duration is 0
                var duration = track.Duration.TotalSeconds > 0 
                    ? track.Duration 
                    : PlatypusTools.UI.Services.AudioPlayerService.Instance.Duration;
                    
                NowPlayingDuration.Text = duration.TotalSeconds > 0 
                    ? duration.ToString(@"m\:ss") 
                    : track.DurationFormatted;
                NowPlayingProgress.Maximum = duration.TotalSeconds > 0 ? duration.TotalSeconds : 300;
                System.Diagnostics.Debug.WriteLine($"UpdateNowPlayingPanel: Set title to '{track.DisplayTitle}'");
                
                // Load album art
                LoadAlbumArt(track);
            }
            else
            {
                NowPlayingTitle.Text = "No track";
                NowPlayingArtist.Text = "Unknown";
                NowPlayingAlbum.Text = "Unknown";
                NowPlayingDuration.Text = "0:00";
                AlbumArtImage.Source = null;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
            }
        });
    }
    
    /// <summary>
    /// Updates the position display.
    /// </summary>
    private void UpdatePositionDisplay()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        Dispatcher.Invoke(() =>
        {
            NowPlayingPosition.Text = vm.PositionDisplay;
            NowPlayingProgress.Value = vm.PositionSeconds;
        });
    }
    
    /// <summary>
    /// Updates the queue count display.
    /// </summary>
    private void UpdateQueueCount()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        Dispatcher.Invoke(() =>
        {
            QueueCountText.Text = $"Queue: {vm.Queue.Count} tracks";
        });
    }
    
    /// <summary>
    /// Refreshes the queue display by syncing ViewModel Queue with service Queue.
    /// This updates through the binding rather than breaking it.
    /// </summary>
    private void RefreshQueueDisplay()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        Dispatcher.Invoke(() =>
        {
            // Sync ViewModel queue with service queue
            vm.Queue.Clear();
            foreach (var track in PlatypusTools.UI.Services.AudioPlayerService.Instance.Queue)
                vm.Queue.Add(track);
            
            // Ensure binding is active (restore if it was broken)
            if (QueueListBox.ItemsSource != vm.Queue)
            {
                QueueListBox.ItemsSource = vm.Queue;
            }
            
            UpdateQueueCount();
        });
    }
    
    // ===== Seek Slider Handlers =====
    
    private void OnSeekSliderMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _isUserSeeking = true;
    }
    
    private void OnSeekSliderMouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_isUserSeeking)
        {
            var slider = sender as Slider;
            if (slider != null)
            {
                var newPosition = TimeSpan.FromSeconds(slider.Value);
                PlatypusTools.UI.Services.AudioPlayerService.Instance.Seek(newPosition);
            }
            _isUserSeeking = false;
        }
    }
    
    private void OnSeekSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Only seek if user is dragging (not programmatic update)
        // The actual seek happens on mouse up
    }
    
    /// <summary>
    /// Loads album art from the audio file if available.
    /// </summary>
    private void LoadAlbumArt(PlatypusTools.Core.Models.Audio.AudioTrack? track)
    {
        Dispatcher.Invoke(() =>
        {
            if (track == null || string.IsNullOrEmpty(track.FilePath))
            {
                AlbumArtImage.Source = null;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
                return;
            }
            
            try
            {
                // Try to load album art from the audio file using TagLib
                var file = TagLib.File.Create(track.FilePath);
                if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                {
                    var pic = file.Tag.Pictures[0];
                    using (var ms = new System.IO.MemoryStream(pic.Data.Data))
                    {
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = ms;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        
                        AlbumArtImage.Source = bitmap;
                        AlbumArtPlaceholder.Visibility = Visibility.Collapsed;
                    }
                }
                else
                {
                    AlbumArtImage.Source = null;
                    AlbumArtPlaceholder.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                AlbumArtImage.Source = null;
                AlbumArtPlaceholder.Visibility = Visibility.Visible;
            }
        });
    }

// Old visualizer drawing methods replaced by integrated AudioVisualizerView
    // These methods are deprecated and kept only for reference
    
    // Helper to get ViewModel safely
    private AudioPlayerViewModel? GetViewModel() => DataContext as AudioPlayerViewModel ?? _viewModel;
    
    // ===== Toolbar Click Handlers =====
    
    private async void OnOpenFilesClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        System.Diagnostics.Debug.WriteLine($"OnOpenFilesClick: ViewModel = {(vm != null ? "OK" : "NULL")}");
        
        if (vm == null) 
        {
            MessageBox.Show("Error: Audio player not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open Audio File",
            Filter = "Audio Files|*.mp3;*.wav;*.wma;*.aac;*.m4a;*.flac;*.ogg;*.opus|All Files|*.*",
            Multiselect = true
        };
        
        if (dialog.ShowDialog() == true)
        {
            vm.StatusMessage = "Loading files...";
            int loadedCount = 0;
            
            System.Diagnostics.Debug.WriteLine($"OnOpenFilesClick: Selected {dialog.FileNames.Length} files");
            
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    var track = await PlatypusTools.UI.Services.AudioPlayerService.Instance.LoadTrackAsync(file);
                    if (track != null)
                    {
                        PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(track);
                        loadedCount++;
                        System.Diagnostics.Debug.WriteLine($"OnOpenFilesClick: Loaded track: {track.DisplayTitle}");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading {file}: {ex.Message}");
                }
            }
            
            System.Diagnostics.Debug.WriteLine($"OnOpenFilesClick: Loaded {loadedCount} tracks total");
            
            // Refresh queue display on UI thread - use helper to preserve binding
            await Dispatcher.InvokeAsync(() =>
            {
                RefreshQueueDisplay();
                System.Diagnostics.Debug.WriteLine($"OnOpenFilesClick: Queue now has {vm.Queue.Count} tracks, ListBox has {QueueListBox.Items.Count} items");
            });
            
            // Auto-play first track if nothing is playing
            if (loadedCount > 0)
            {
                var firstTrack = PlatypusTools.UI.Services.AudioPlayerService.Instance.Queue.FirstOrDefault();
                if (firstTrack != null)
                {
                    System.Diagnostics.Debug.WriteLine($"OnOpenFilesClick: Playing first track: {firstTrack.DisplayTitle}");
                    
                    // PlayTrackAsync fires TrackChanged event which updates CurrentTrack
                    await PlatypusTools.UI.Services.AudioPlayerService.Instance.PlayTrackAsync(firstTrack);
                    
                    // Explicitly update CurrentTrack and Now Playing panel on UI thread
                    await Dispatcher.InvokeAsync(() =>
                    {
                        vm.CurrentTrack = firstTrack;
                        
                        // DIRECT UI UPDATE - bypass all binding issues
                        NowPlayingTitle.Text = firstTrack.DisplayTitle;
                        NowPlayingArtist.Text = firstTrack.DisplayArtist;
                        NowPlayingAlbum.Text = firstTrack.DisplayAlbum;
                        NowPlayingDuration.Text = firstTrack.DurationFormatted;
                        
                        System.Diagnostics.Debug.WriteLine($"OnOpenFilesClick: DIRECT UI UPDATE - Title: {firstTrack.DisplayTitle}");
                    });
                }
            }
            
            // Update status text directly
            StatusText.Text = $"Loaded {loadedCount} file(s). Queue: {vm.Queue.Count} tracks";
            vm.StatusMessage = $"Loaded {loadedCount} file(s). Queue: {vm.Queue.Count} tracks";
        }
    }
    
    private async void OnScanFolderClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) 
        {
            MessageBox.Show("Error: Audio player not initialized", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder with audio files"
        };
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            vm.StatusMessage = "Scanning folder...";
            StatusText.Text = "Scanning folder...";
            
            var tracks = await PlatypusTools.UI.Services.AudioPlayerService.Instance.ScanFolderAsync(
                dialog.SelectedPath, vm.IncludeSubfolders);
            
            if (tracks.Count > 0)
            {
                PlatypusTools.UI.Services.AudioPlayerService.Instance.SetQueue(tracks);
                
                // Refresh queue display on UI thread - use helper to preserve binding
                await Dispatcher.InvokeAsync(() =>
                {
                    RefreshQueueDisplay();
                });
                
                var firstTrack = tracks.FirstOrDefault();
                if (firstTrack != null)
                {
                    await PlatypusTools.UI.Services.AudioPlayerService.Instance.PlayTrackAsync(firstTrack);
                    
                    // Force update UI directly
                    await Dispatcher.InvokeAsync(() =>
                    {
                        vm.CurrentTrack = firstTrack;
                        
                        // DIRECT UI UPDATE
                        NowPlayingTitle.Text = firstTrack.DisplayTitle;
                        NowPlayingArtist.Text = firstTrack.DisplayArtist;
                        NowPlayingAlbum.Text = firstTrack.DisplayAlbum;
                        NowPlayingDuration.Text = firstTrack.DurationFormatted;
                    });
                }
                
                StatusText.Text = $"Loaded {tracks.Count} tracks";
                vm.StatusMessage = $"Loaded {tracks.Count} tracks";
            }
            else
            {
                StatusText.Text = "No audio files found in folder";
                vm.StatusMessage = "No audio files found in folder";
            }
        }
    }
    
    private void OnAddFolderToLibraryClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null)
        {
            System.Diagnostics.Debug.WriteLine("OnAddFolderToLibraryClick: ViewModel is NULL");
            MessageBox.Show("ViewModel is null - please try again", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"OnAddFolderToLibraryClick: Starting. Current LibraryFolders count: {vm.LibraryFolders.Count}");
        
        // Use WinForms FolderBrowserDialog with proper owner window
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to add to library",
            ShowNewFolderButton = false,
            UseDescriptionForTitle = true
        };
        
        // Get window handle for proper dialog parenting
        var window = Window.GetWindow(this);
        var hwnd = window != null ? new System.Windows.Interop.WindowInteropHelper(window).Handle : IntPtr.Zero;
        System.Diagnostics.Debug.WriteLine($"OnAddFolderToLibraryClick: Window handle = {hwnd}");
        
        var result = dialog.ShowDialog(hwnd != IntPtr.Zero ? new Win32Window(hwnd) : null);
        
        System.Diagnostics.Debug.WriteLine($"OnAddFolderToLibraryClick: Dialog result = {result}, SelectedPath = '{dialog.SelectedPath}'");
        
        if (result == System.Windows.Forms.DialogResult.OK && !string.IsNullOrEmpty(dialog.SelectedPath))
        {
            var folder = dialog.SelectedPath;
            
            // Check if already exists (case insensitive)
            bool exists = vm.LibraryFolders.Any(f => f.Equals(folder, StringComparison.OrdinalIgnoreCase));
            System.Diagnostics.Debug.WriteLine($"OnAddFolderToLibraryClick: Folder exists in list: {exists}");
            
            if (!exists)
            {
                // Add to the ObservableCollection
                vm.LibraryFolders.Add(folder);
                System.Diagnostics.Debug.WriteLine($"OnAddFolderToLibraryClick: Added folder. New count: {vm.LibraryFolders.Count}");
                
                // Save to persistent storage
                SaveLibraryFoldersToFile(vm.LibraryFolders.ToList());
                
                // FORCE UI UPDATE - bypass binding issues completely
                if (LibraryFoldersListBox != null)
                {
                    // Create a new list and set it as ItemsSource
                    var folderList = vm.LibraryFolders.ToList();
                    LibraryFoldersListBox.ItemsSource = null;
                    LibraryFoldersListBox.ItemsSource = folderList;
                    System.Diagnostics.Debug.WriteLine($"OnAddFolderToLibraryClick: Forced ItemsSource refresh. Items: {LibraryFoldersListBox.Items.Count}");
                }
                
                // Update status directly
                StatusText.Text = $"Added folder: {System.IO.Path.GetFileName(folder)}";
                vm.StatusMessage = $"Added folder: {System.IO.Path.GetFileName(folder)}";
                
                // Show confirmation
                MessageBox.Show($"Folder added to library:\n{folder}\n\nClick 'Scan All Folders' to index the music files.", 
                    "Folder Added", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                vm.StatusMessage = "Folder already in library";
                MessageBox.Show("This folder is already in your library.", "Already Added", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("OnAddFolderToLibraryClick: Dialog cancelled or no folder selected");
        }
    }
    
    private void SaveLibraryFoldersToFile(List<string> folders)
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
            
            var json = System.Text.Json.JsonSerializer.Serialize(folders);
            System.IO.File.WriteAllText(foldersPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving library folders: {ex.Message}");
        }
    }
    
    private void OnRemoveFolderFromLibraryClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        // Get selected folder from ListBox
        if (LibraryFoldersListBox?.SelectedItem is string selectedFolder)
        {
            vm.LibraryFolders.Remove(selectedFolder);
            SaveLibraryFoldersToFile(vm.LibraryFolders.ToList());
            
            // Force refresh
            var folderList = vm.LibraryFolders.ToList();
            LibraryFoldersListBox.ItemsSource = folderList;
            
            StatusText.Text = "Folder removed from library";
            vm.StatusMessage = "Folder removed from library";
        }
        else
        {
            MessageBox.Show("Please select a folder to remove.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    
    /// <summary>
    /// Plays a specific track from the queue when its play button is clicked.
    /// </summary>
    private async void OnPlayTrackInQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        if (sender is Button btn && btn.Tag is PlatypusTools.Core.Models.Audio.AudioTrack track)
        {
            await PlatypusTools.UI.Services.AudioPlayerService.Instance.PlayTrackAsync(track);
            vm.StatusMessage = $"Playing: {track.DisplayTitle}";
        }
    }
    
    /// <summary>
    /// Removes a specific track from the queue when its remove button is clicked.
    /// </summary>
    private void OnRemoveTrackFromQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        if (sender is Button btn && btn.Tag is PlatypusTools.Core.Models.Audio.AudioTrack track)
        {
            PlatypusTools.UI.Services.AudioPlayerService.Instance.RemoveFromQueue(track);
            vm.Queue.Remove(track);
            vm.StatusMessage = $"Removed: {track.DisplayTitle}";
        }
    }
    
    private async void OnScanAllFoldersClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        // Get folders from the ListBox (since binding might not work)
        var folders = LibraryFoldersListBox?.ItemsSource as IEnumerable<string>;
        if (folders == null || !folders.Any())
        {
            MessageBox.Show("No folders in library. Click 'Add Folder' first.", "No Folders", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var folderList = folders.ToList();
        StatusText.Text = $"Scanning {folderList.Count} folders...";
        
        // Use the ViewModel's scan method which uses LibraryIndexService for persistence
        await vm.ScanAllLibraryFoldersAsync();
        
        // Show completion message
        await Dispatcher.InvokeAsync(() =>
        {
            StatusText.Text = $"Library scan complete: {vm.LibraryTrackCount} tracks from {folderList.Count} folders";
            MessageBox.Show($"Scan complete!\n\nFound {vm.LibraryTrackCount} tracks in {folderList.Count} folders.",
                "Scan Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }
    
    private async void OnAddToQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select folder to add to queue"
        };
        
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            vm.StatusMessage = "Scanning folder...";
            
            var tracks = await PlatypusTools.UI.Services.AudioPlayerService.Instance.ScanFolderAsync(
                dialog.SelectedPath, vm.IncludeSubfolders);
            
            foreach (var track in tracks)
            {
                PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(track);
            }
            
            // Refresh queue display
            await Dispatcher.InvokeAsync(() =>
            {
                RefreshQueueListBox();
            });
            
            vm.StatusMessage = $"Added {tracks.Count} tracks to queue";
        }
    }
    
    private void OnToggleLibraryClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.ViewModeIndex = vm.ViewModeIndex == 0 ? 1 : 0;
    }
    
    // ===== Library Click Handlers =====
    
    private async void OnPlaySelectedLibraryClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        // Try to get selection from ViewModel, fallback to DataGrid directly
        var selectedTrack = vm.SelectedLibraryTrack;
        if (selectedTrack == null && LibraryTrackGrid != null)
        {
            selectedTrack = LibraryTrackGrid.SelectedItem as PlatypusTools.Core.Models.Audio.AudioTrack;
        }
        
        if (selectedTrack == null)
        {
            MessageBox.Show("Please select a track from the library first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(selectedTrack);
        vm.Queue.Add(selectedTrack);
        await PlatypusTools.UI.Services.AudioPlayerService.Instance.PlayTrackAsync(selectedTrack);
        RefreshQueueListBox();
        UpdateQueueCount();
        UpdateNowPlayingPanel();
    }
    
    private async void OnLibraryPlayClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        // Try to get selection from DataGrid first
        var selectedTrack = vm.SelectedLibraryTrack;
        if (selectedTrack == null && LibraryTrackGrid != null)
        {
            selectedTrack = LibraryTrackGrid.SelectedItem as PlatypusTools.Core.Models.Audio.AudioTrack;
        }
        
        if (selectedTrack == null)
        {
            MessageBox.Show("Please select a track from the library first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(selectedTrack);
        vm.Queue.Add(selectedTrack); // Also add to ViewModel queue for UI
        await PlatypusTools.UI.Services.AudioPlayerService.Instance.PlayTrackAsync(selectedTrack);
        RefreshQueueListBox();
        UpdateQueueCount();
        UpdateNowPlayingPanel();
    }
    
    private void OnAddSelectedToQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        // Try to get selection from DataGrid first
        var selectedTrack = vm.SelectedLibraryTrack;
        if (selectedTrack == null && LibraryTrackGrid != null)
        {
            selectedTrack = LibraryTrackGrid.SelectedItem as PlatypusTools.Core.Models.Audio.AudioTrack;
        }
        
        if (selectedTrack == null)
        {
            MessageBox.Show("Please select a track from the library first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(selectedTrack);
        vm.Queue.Add(selectedTrack); // Also add to ViewModel queue for UI
        RefreshQueueListBox();
        UpdateQueueCount();
        vm.StatusMessage = $"Added '{selectedTrack.DisplayTitle}' to queue";
        StatusText.Text = vm.StatusMessage;
    }
    
    private async void OnToggleFavoriteClick(object sender, RoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine("OnToggleFavoriteClick called");
        
        var vm = GetViewModel();
        if (vm == null)
        {
            System.Diagnostics.Debug.WriteLine("OnToggleFavoriteClick: ViewModel is null");
            return;
        }
        
        // Try to get selection from DataGrid first
        var selectedTrack = vm.SelectedLibraryTrack;
        System.Diagnostics.Debug.WriteLine($"OnToggleFavoriteClick: SelectedLibraryTrack = {selectedTrack?.DisplayTitle ?? "null"}");
        
        if (selectedTrack == null && LibraryTrackGrid != null)
        {
            selectedTrack = LibraryTrackGrid.SelectedItem as PlatypusTools.Core.Models.Audio.AudioTrack;
            System.Diagnostics.Debug.WriteLine($"OnToggleFavoriteClick: DataGrid.SelectedItem = {selectedTrack?.DisplayTitle ?? "null"}");
        }
        
        if (selectedTrack != null)
        {
            System.Diagnostics.Debug.WriteLine($"OnToggleFavoriteClick: Toggling favorite for {selectedTrack.DisplayTitle}");
            await vm.ToggleFavoriteAsync(selectedTrack);
            // Refresh the DataGrid to show updated star
            LibraryTrackGrid?.Items.Refresh();
            System.Diagnostics.Debug.WriteLine($"OnToggleFavoriteClick: IsFavorite is now {selectedTrack.IsFavorite}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine("OnToggleFavoriteClick: No track selected");
        }
    }
    
    private void OnAddAllToQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        // Get tracks from the DataGrid ItemsSource or ViewModel
        var tracks = vm.LibraryTracks.ToList();
        if (tracks.Count == 0 && LibraryTrackGrid?.ItemsSource != null)
        {
            tracks = (LibraryTrackGrid.ItemsSource as IEnumerable<PlatypusTools.Core.Models.Audio.AudioTrack>)?.ToList() 
                     ?? new List<PlatypusTools.Core.Models.Audio.AudioTrack>();
        }
        
        if (tracks.Count == 0)
        {
            MessageBox.Show("No tracks in library. Please scan a folder first.", "Empty Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        foreach (var track in tracks)
        {
            PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(track);
            vm.Queue.Add(track); // Also add to ViewModel queue for UI
        }
        
        RefreshQueueListBox();
        UpdateQueueCount();
        vm.StatusMessage = $"Added {tracks.Count} tracks to queue";
        StatusText.Text = vm.StatusMessage;
    }
    
    private void OnOrganizeByChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        // Force refresh the library grid after organize mode changes
        if (sender is System.Windows.Controls.ComboBox combo)
        {
            vm.OrganizeModeIndex = combo.SelectedIndex;
            RefreshLibraryTrackGrid();
        }
    }
    
    // ===== Playback Control Click Handlers =====
    
    private void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.PlayPauseCommand.Execute(null);
    }
    
    private void OnPreviousClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.PreviousCommand.Execute(null);
    }
    
    private void OnNextClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.NextCommand.Execute(null);
    }
    
    private void OnStopClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.StopCommand.Execute(null);
    }
    
    private void OnCycleRepeatClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.RepeatMode = (vm.RepeatMode + 1) % 3;
    }
    
    private void OnToggleMuteClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.IsMuted = !vm.IsMuted;
    }
    
    // ===== Visualizer Control Handlers =====
    
    private void OnVisualizerModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VisualizerControl == null) return;
        
        var combo = sender as ComboBox;
        if (combo == null) return;
        
        int modeIndex = combo.SelectedIndex;
        int barCount = _viewModel?.BarCount ?? 72;
        int colorIndex = _viewModel?.ColorSchemeIndex ?? 0;
        
        string mode = GetVisualizerModeName(modeIndex);
        VisualizerControl.SetColorScheme(colorIndex);
        VisualizerControl.UpdateSpectrumData(Array.Empty<double>(), mode, barCount);
        
        System.Diagnostics.Debug.WriteLine($"OnVisualizerModeChanged: mode={mode}");
    }
    
    private void OnBarCountChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (VisualizerControl == null) return;
        
        int modeIndex = _viewModel?.VisualizerModeIndex ?? 0;
        int barCount = (int)e.NewValue;
        int colorIndex = _viewModel?.ColorSchemeIndex ?? 0;
        
        string mode = GetVisualizerModeName(modeIndex);
        VisualizerControl.SetColorScheme(colorIndex);
        VisualizerControl.UpdateSpectrumData(Array.Empty<double>(), mode, barCount);
        
        System.Diagnostics.Debug.WriteLine($"OnBarCountChanged: bars={barCount}");
    }
    
    private void OnColorSchemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VisualizerControl == null) return;
        
        var combo = sender as ComboBox;
        if (combo == null) return;
        
        int modeIndex = _viewModel?.VisualizerModeIndex ?? 0;
        int barCount = _viewModel?.BarCount ?? 72;
        int colorIndex = combo.SelectedIndex;
        
        string mode = GetVisualizerModeName(modeIndex);
        VisualizerControl.SetColorScheme(colorIndex);
        VisualizerControl.UpdateSpectrumData(Array.Empty<double>(), mode, barCount);
        
        System.Diagnostics.Debug.WriteLine($"OnColorSchemeChanged: color={colorIndex}");
    }
    
    // ===== EQ Controls =====
    
    private void OnEqBassChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // EQ changes are handled by ViewModel binding
        System.Diagnostics.Debug.WriteLine($"EQ Bass changed to {e.NewValue} dB");
    }
    
    private void OnEqMidChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // EQ changes are handled by ViewModel binding
        System.Diagnostics.Debug.WriteLine($"EQ Mid changed to {e.NewValue} dB");
    }
    
    private void OnEqTrebleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // EQ changes are handled by ViewModel binding
        System.Diagnostics.Debug.WriteLine($"EQ Treble changed to {e.NewValue} dB");
    }
    
    private void OnEqResetClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm != null)
        {
            vm.EqBass = 0;
            vm.EqMid = 0;
            vm.EqTreble = 0;
        }
        PlatypusTools.UI.Services.AudioPlayerService.Instance.ResetEq();
        System.Diagnostics.Debug.WriteLine("EQ Reset to flat");
    }
    
    private void OnCrossfadeEnabledChanged(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm != null)
        {
            System.Diagnostics.Debug.WriteLine($"Crossfade enabled: {vm.CrossfadeEnabled}");
        }
    }
    
    private void OnCrossfadeDurationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        System.Diagnostics.Debug.WriteLine($"Crossfade duration changed to {e.NewValue:F1}s");
    }
    
    // ===== Fullscreen Visualizer Support =====
    
    private bool _isVisualizerFullscreen = false;
    private Window? _fullscreenWindow;
    
    private void OnToggleFullscreenVisualizerClick(object sender, RoutedEventArgs e)
    {
        if (_isVisualizerFullscreen)
        {
            ExitFullscreenVisualizer();
        }
        else
        {
            EnterFullscreenVisualizer();
        }
    }
    
    private void EnterFullscreenVisualizer()
    {
        try
        {
            // Create a new fullscreen window for the visualizer
            _fullscreenWindow = new Window
            {
                Title = "PlatypusTools Visualizer",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None,
                Background = new SolidColorBrush(Color.FromRgb(10, 14, 39)),
                Topmost = true
            };
            
            // Create a new visualizer for fullscreen
            var fullscreenVisualizer = new AudioVisualizerView();
            
            // Set initial color scheme
            var vm = GetViewModel();
            fullscreenVisualizer.SetColorScheme(vm?.ColorSchemeIndex ?? 0);
            
            _fullscreenWindow.Content = fullscreenVisualizer;
            
            // Subscribe to spectrum data for fullscreen visualizer
            void OnSpectrumData(object? s, double[] data)
            {
                var viewModel = GetViewModel();
                string mode = GetVisualizerModeName(viewModel?.VisualizerModeIndex ?? 0);
                int barCount = viewModel?.BarCount ?? 72;
                int colorIndex = viewModel?.ColorSchemeIndex ?? 0;
                fullscreenVisualizer.Dispatcher.Invoke(() => 
                {
                    fullscreenVisualizer.SetColorScheme(colorIndex);
                    fullscreenVisualizer.UpdateSpectrumData(data, mode, barCount);
                });
            }
            
            PlatypusTools.UI.Services.AudioPlayerService.Instance.SpectrumDataUpdated += OnSpectrumData;
            
            // Close on Escape or click
            _fullscreenWindow.KeyDown += (s, args) =>
            {
                if (args.Key == System.Windows.Input.Key.Escape)
                {
                    PlatypusTools.UI.Services.AudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumData;
                    ExitFullscreenVisualizer();
                }
            };
            
            _fullscreenWindow.MouseDoubleClick += (s, args) =>
            {
                PlatypusTools.UI.Services.AudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumData;
                ExitFullscreenVisualizer();
            };
            
            _fullscreenWindow.Closed += (s, args) =>
            {
                PlatypusTools.UI.Services.AudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumData;
                _isVisualizerFullscreen = false;
                _fullscreenWindow = null;
            };
            
            _fullscreenWindow.Show();
            _isVisualizerFullscreen = true;
            
            if (FullscreenToggleBtn != null)
                FullscreenToggleBtn.Content = "⛶"; // Change icon to indicate exit fullscreen
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error entering fullscreen: {ex.Message}");
            MessageBox.Show($"Could not enter fullscreen mode: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void ExitFullscreenVisualizer()
    {
        try
        {
            if (_fullscreenWindow != null)
            {
                _fullscreenWindow.Close();
                _fullscreenWindow = null;
            }
            
            _isVisualizerFullscreen = false;
            
            if (FullscreenToggleBtn != null)
                FullscreenToggleBtn.Content = "⛶"; // Reset icon
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error exiting fullscreen: {ex.Message}");
        }
    }

    /// <summary>
    /// Removes all selected tracks from the queue (multi-select support).
    /// </summary>
    private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null)
        {
            System.Diagnostics.Debug.WriteLine("OnRemoveSelectedClick: ViewModel is null");
            return;
        }
        
        var selectedTracks = QueueListBox.SelectedItems
            .Cast<PlatypusTools.Core.Models.Audio.AudioTrack>()
            .ToList();
        
        System.Diagnostics.Debug.WriteLine($"OnRemoveSelectedClick: {selectedTracks.Count} tracks selected");
        
        if (selectedTracks.Count == 0)
        {
            MessageBox.Show("No tracks selected. Use Ctrl+Click or Shift+Click to select multiple tracks.",
                            "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        foreach (var track in selectedTracks)
        {
            vm.Queue.Remove(track);
            PlatypusTools.UI.Services.AudioPlayerService.Instance.RemoveFromQueue(track);
        }
        
        // Refresh the queue display
        RefreshQueueListBox();
        UpdateQueueCount();
        
        StatusText.Text = $"Removed {selectedTracks.Count} track(s) from queue";
        vm.StatusMessage = $"Removed {selectedTracks.Count} track(s) from queue";
    }
    
    /// <summary>
    /// Clears the entire queue.
    /// </summary>
    private void OnClearQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null)
        {
            System.Diagnostics.Debug.WriteLine("OnClearQueueClick: ViewModel is null");
            return;
        }
        
        System.Diagnostics.Debug.WriteLine($"OnClearQueueClick: Queue has {vm.Queue.Count} tracks");
        
        if (vm.Queue.Count == 0)
        {
            MessageBox.Show("Queue is already empty.", "Clear Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var result = MessageBox.Show($"Remove all {vm.Queue.Count} tracks from the queue?",
                                     "Clear Queue", MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            vm.Queue.Clear();
            PlatypusTools.UI.Services.AudioPlayerService.Instance.ClearQueue();
            
            // Refresh the queue display
            RefreshQueueListBox();
            UpdateQueueCount();
            
            // Reset Now Playing
            NowPlayingTitle.Text = "No track";
            NowPlayingArtist.Text = "Unknown";
            NowPlayingAlbum.Text = "Unknown";
            NowPlayingPosition.Text = "0:00";
            NowPlayingDuration.Text = "0:00";
            NowPlayingProgress.Value = 0;
            
            StatusText.Text = "Queue cleared";
            vm.StatusMessage = "Queue cleared";
        }
    }
    
    /// <summary>
    /// Syncs the ViewModel's Queue collection with the service's queue.
    /// Also ensures XAML binding is active (in case it was broken previously).
    /// </summary>
    private void RefreshQueueListBox()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        var service = PlatypusTools.UI.Services.AudioPlayerService.Instance;
        
        // Sync ViewModel Queue with service queue
        vm.Queue.Clear();
        foreach (var track in service.Queue)
        {
            vm.Queue.Add(track);
        }
        
        // Ensure binding is active (restore if it was broken)
        if (QueueListBox.ItemsSource != vm.Queue)
        {
            QueueListBox.ItemsSource = vm.Queue;
        }
        
        // Update queue count display
        UpdateQueueCount();
    }
    
    /// <summary>
    /// Refreshes the Library track grid by re-binding to ViewModel's LibraryTracks.
    /// </summary>
    private void RefreshLibraryTrackGrid()
    {
        var logPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "debug_log.txt");
        var vm = GetViewModel();
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] RefreshLibraryTrackGrid: vm={vm != null}, LibraryTrackGrid={LibraryTrackGrid != null}\n");
        if (vm == null || LibraryTrackGrid == null) return;
        
        // Force re-bind to LibraryTracks collection
        var tracks = vm.LibraryTracks.ToList();
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] RefreshLibraryTrackGrid: LibraryTracks has {tracks.Count} items, LibraryTrackCount={vm.LibraryTrackCount}\n");
        LibraryTrackGrid.ItemsSource = null;
        LibraryTrackGrid.ItemsSource = tracks;
        System.IO.File.AppendAllText(logPath, $"[{DateTime.Now}] RefreshLibraryTrackGrid: Set ItemsSource, Grid.Items.Count={LibraryTrackGrid.Items.Count}\n");
        
        // Update track count display
        if (FindName("LibraryTrackCountText") is TextBlock countText)
        {
            countText.Text = $"{tracks.Count} tracks";
        }
    }
    
    #region Drag and Drop Queue Reordering
    
    private Point _dragStartPoint;
    private bool _isDragging;
    private object? _draggedItem;
    
    private void QueueListBox_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
        
        // Get the item being clicked
        var listBox = sender as ListBox;
        if (listBox == null) return;
        
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element != listBox)
        {
            if (element is ListBoxItem item)
            {
                _draggedItem = item.DataContext;
                break;
            }
            element = VisualTreeHelper.GetParent(element);
        }
    }
    
    private void QueueListBox_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _draggedItem == null)
            return;
        
        Point position = e.GetPosition(null);
        
        // Check if we've moved enough to start a drag
        if (Math.Abs(position.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(position.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (!_isDragging)
            {
                _isDragging = true;
                var listBox = sender as ListBox;
                if (listBox != null && _draggedItem != null)
                {
                    DragDrop.DoDragDrop(listBox, _draggedItem, DragDropEffects.Move);
                }
                _isDragging = false;
                _draggedItem = null;
            }
        }
    }
    
    private void QueueListBox_DragOver(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(PlatypusTools.Core.Models.Audio.AudioTrack)))
        {
            e.Effects = DragDropEffects.None;
        }
        else
        {
            e.Effects = DragDropEffects.Move;
        }
        e.Handled = true;
    }
    
    private void QueueListBox_Drop(object sender, DragEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        if (!e.Data.GetDataPresent(typeof(PlatypusTools.Core.Models.Audio.AudioTrack)))
            return;
        
        var droppedTrack = e.Data.GetData(typeof(PlatypusTools.Core.Models.Audio.AudioTrack)) as PlatypusTools.Core.Models.Audio.AudioTrack;
        if (droppedTrack == null) return;
        
        // Find the target item
        var listBox = sender as ListBox;
        if (listBox == null) return;
        
        // Get the item at the drop position
        var targetElement = e.OriginalSource as DependencyObject;
        PlatypusTools.Core.Models.Audio.AudioTrack? targetTrack = null;
        
        while (targetElement != null && targetElement != listBox)
        {
            if (targetElement is ListBoxItem item)
            {
                targetTrack = item.DataContext as PlatypusTools.Core.Models.Audio.AudioTrack;
                break;
            }
            targetElement = VisualTreeHelper.GetParent(targetElement);
        }
        
        if (targetTrack == null || targetTrack.Id == droppedTrack.Id)
            return;
        
        // Get indices
        var fromIndex = vm.Queue.ToList().FindIndex(t => t.Id == droppedTrack.Id);
        var toIndex = vm.Queue.ToList().FindIndex(t => t.Id == targetTrack.Id);
        
        if (fromIndex >= 0 && toIndex >= 0 && fromIndex != toIndex)
        {
            // Move in the service
            PlatypusTools.UI.Services.AudioPlayerService.Instance.MoveInQueue(fromIndex, toIndex);
            
            // Refresh the display
            RefreshQueueListBox();
            UpdateQueueCount();
            
            vm.StatusMessage = $"Moved '{droppedTrack.DisplayTitle}'";
        }
    }
    
    #endregion
}