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
    
    public AudioPlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize DataGrid columns for proper resizing
        if (this.FindName("LibraryTrackGrid") is DataGrid grid)
        {
            grid.UpdateLayout();
            foreach (var column in grid.Columns)
            {
                column.Width = double.NaN; // Auto-size
            }
            grid.UpdateLayout();
        }
        
        _viewModel = DataContext as AudioPlayerViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Initialize audio visualizer
            InitializeVisualizer();
            
            // Load library folders into the ListBox directly (bypass binding)
            LoadLibraryFoldersToUI();
            
            // Start a timer to update position display
            StartPositionTimer();
        }
    }
    
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
                
                // Always update position if there's a current track
                if (service.CurrentTrack != null)
                {
                    var position = service.Position;
                    var duration = service.Duration;
                    
                    // Update position display
                    NowPlayingPosition.Text = position.ToString(@"m\:ss");
                    
                    // Update duration if available
                    if (duration.TotalSeconds > 0)
                    {
                        NowPlayingDuration.Text = duration.ToString(@"m\:ss");
                        NowPlayingProgress.Maximum = duration.TotalSeconds;
                        NowPlayingProgress.Value = position.TotalSeconds;
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
            
            if (System.IO.File.Exists(foldersPath))
            {
                var json = System.IO.File.ReadAllText(foldersPath);
                var folders = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                if (folders != null && folders.Count > 0)
                {
                    var vm = GetViewModel();
                    if (vm != null)
                    {
                        vm.LibraryFolders.Clear();
                        foreach (var folder in folders)
                            vm.LibraryFolders.Add(folder);
                    }
                    
                    // Set ItemsSource directly
                    if (LibraryFoldersListBox != null)
                    {
                        LibraryFoldersListBox.ItemsSource = folders;
                        System.Diagnostics.Debug.WriteLine($"LoadLibraryFoldersToUI: Loaded {folders.Count} folders");
                    }
                }
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
                string mode = _viewModel.VisualizerModeIndex switch
                {
                    0 => "Bars",
                    1 => "Mirror",
                    2 => "Waveform",
                    3 => "Circular",
                    _ => "Bars"
                };
                
                VisualizerControl.UpdateSpectrumData(specData, mode, _viewModel.BarCount);
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
    }
    
    /// <summary>
    /// Updates the Now Playing panel with current track info (bypasses binding issues).
    /// </summary>
    private void UpdateNowPlayingPanel()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        Dispatcher.Invoke(() =>
        {
            var track = vm.CurrentTrack;
            if (track != null)
            {
                NowPlayingTitle.Text = track.DisplayTitle;
                NowPlayingArtist.Text = track.DisplayArtist;
                NowPlayingAlbum.Text = track.DisplayAlbum;
                NowPlayingDuration.Text = track.DurationFormatted;
                NowPlayingProgress.Maximum = track.Duration.TotalSeconds > 0 ? track.Duration.TotalSeconds : 100;
                System.Diagnostics.Debug.WriteLine($"UpdateNowPlayingPanel: Set title to '{track.DisplayTitle}'");
            }
            else
            {
                NowPlayingTitle.Text = "No track";
                NowPlayingArtist.Text = "Unknown";
                NowPlayingAlbum.Text = "Unknown";
                NowPlayingDuration.Text = "0:00";
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
            
            // Refresh queue display on UI thread
            await Dispatcher.InvokeAsync(() =>
            {
                vm.Queue.Clear();
                foreach (var track in PlatypusTools.UI.Services.AudioPlayerService.Instance.Queue)
                    vm.Queue.Add(track);
                
                // FORCE ItemsSource update - bypass binding
                var queueList = PlatypusTools.UI.Services.AudioPlayerService.Instance.Queue.ToList();
                QueueListBox.ItemsSource = queueList;
                    
                // Update queue count display directly
                UpdateQueueCount();
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
                
                // Refresh queue display on UI thread
                await Dispatcher.InvokeAsync(() =>
                {
                    vm.Queue.Clear();
                    foreach (var track in tracks)
                        vm.Queue.Add(track);
                    
                    // FORCE ItemsSource update
                    QueueListBox.ItemsSource = tracks.ToList();
                    
                    // Update queue count
                    UpdateQueueCount();
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
        
        var allTracks = new List<PlatypusTools.Core.Models.Audio.AudioTrack>();
        
        foreach (var folder in folderList)
        {
            if (!System.IO.Directory.Exists(folder))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping missing folder: {folder}");
                continue;
            }
            
            StatusText.Text = $"Scanning: {System.IO.Path.GetFileName(folder)}...";
            
            try
            {
                var tracks = await PlatypusTools.UI.Services.AudioPlayerService.Instance.ScanFolderAsync(
                    folder, vm.IncludeSubfolders);
                allTracks.AddRange(tracks);
                System.Diagnostics.Debug.WriteLine($"Found {tracks.Count} tracks in {folder}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error scanning {folder}: {ex.Message}");
            }
        }
        
        // Update UI
        await Dispatcher.InvokeAsync(() =>
        {
            // Update Library grid
            vm.LibraryTracks.Clear();
            foreach (var track in allTracks)
                vm.LibraryTracks.Add(track);
            
            // Force ItemsSource update on the DataGrid
            if (this.FindName("LibraryTrackGrid") is DataGrid grid)
            {
                grid.ItemsSource = allTracks;
                System.Diagnostics.Debug.WriteLine($"LibraryTrackGrid ItemsSource set to {allTracks.Count} tracks");
            }
            
            StatusText.Text = $"Library scan complete: {allTracks.Count} tracks from {folderList.Count} folders";
            vm.StatusMessage = $"Library scan complete: {allTracks.Count} tracks";
            
            // Update stats display
            MessageBox.Show($"Scan complete!\n\nFound {allTracks.Count} tracks in {folderList.Count} folders.",
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
                vm.Queue.Clear();
                foreach (var track in PlatypusTools.UI.Services.AudioPlayerService.Instance.Queue)
                    vm.Queue.Add(track);
                    
                QueueListBox.ItemsSource = null;
                QueueListBox.ItemsSource = vm.Queue;
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
        
        if (vm.SelectedLibraryTrack == null)
        {
            MessageBox.Show("Please select a track from the library first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(vm.SelectedLibraryTrack);
        vm.Queue.Add(vm.SelectedLibraryTrack);
        await PlatypusTools.UI.Services.AudioPlayerService.Instance.PlayTrackAsync(vm.SelectedLibraryTrack);
    }
    
    private void OnAddSelectedToQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        if (vm.SelectedLibraryTrack == null)
        {
            MessageBox.Show("Please select a track from the library first.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(vm.SelectedLibraryTrack);
        vm.Queue.Add(vm.SelectedLibraryTrack);
        vm.StatusMessage = $"Added '{vm.SelectedLibraryTrack.DisplayTitle}' to queue";
    }
    
    private void OnAddAllToQueueClick(object sender, RoutedEventArgs e)
    {
        var vm = GetViewModel();
        if (vm == null) return;
        
        if (vm.LibraryTracks.Count == 0)
        {
            MessageBox.Show("No tracks in library. Please scan a folder first.", "Empty Library", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        foreach (var track in vm.LibraryTracks)
        {
            PlatypusTools.UI.Services.AudioPlayerService.Instance.AddToQueue(track);
            vm.Queue.Add(track);
        }
        
        vm.StatusMessage = $"Added {vm.LibraryTracks.Count} tracks to queue";
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
    /// Refreshes the queue ListBox by re-setting its ItemsSource.
    /// </summary>
    private void RefreshQueueListBox()
    {
        var vm = GetViewModel();
        if (vm == null || QueueListBox == null) return;
        
        var queueItems = vm.Queue.ToList();
        QueueListBox.ItemsSource = null;
        QueueListBox.ItemsSource = queueItems;
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