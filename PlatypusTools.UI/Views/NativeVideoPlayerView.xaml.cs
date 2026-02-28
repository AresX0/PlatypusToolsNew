using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;
using LibVLCSharp.Shared;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Converter to show bold font for currently playing item
    /// </summary>
    public class BoolToFontWeightConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is bool b && b ? FontWeights.Bold : FontWeights.Normal;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Represents a video file in the queue
    /// </summary>
    public class QueueItem : BindableBase
    {
        private bool _isPlaying;
        private int _index;

        public string FullPath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FullPath);

        public int Index
        {
            get => _index;
            set { _index = value; OnPropertyChanged(nameof(Index)); }
        }

        public bool IsPlaying
        {
            get => _isPlaying;
            set { _isPlaying = value; OnPropertyChanged(nameof(IsPlaying)); }
        }
    }

    public partial class NativeVideoPlayerView : UserControl
    {
        private DispatcherTimer? _timer;
        private string? _currentFilePath;
        
        // LibVLC for universal video playback
        private static LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;
        private bool _isInitialized = false;
        private bool _isInitializing = false;

        // Queue
        private readonly ObservableCollection<QueueItem> _queue = new();
        private int _currentQueueIndex = -1;
        
        // Pending file to play once LibVLC is initialized
        private string? _pendingPlayFile;
        
        // Logging
        private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "vlc_debug.log");

        private static void Log(string message)
        {
            try
            {
                var line = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine($"[VLC] {message}");
            }
            catch { }
        }

        public NativeVideoPlayerView()
        {
            InitializeComponent();
            Log("NativeVideoPlayerView constructor starting");
            // Defer heavy initialization to Loaded event to avoid UI freeze
            InitializeTimer();
            
            // Initialize queue
            QueueListBox.ItemsSource = _queue;
            UpdateQueueInfo();
            
            Loaded += NativeVideoPlayerView_Loaded;
            Unloaded += NativeVideoPlayerView_Unloaded;
            Log("NativeVideoPlayerView constructor complete (deferred init)");
        }
        
        private async void InitializeLibVLCAsync()
        {
            if (_isInitialized || _isInitializing) return;
            _isInitializing = true;
            
            Log("InitializeLibVLCAsync starting on background thread");
            
            try
            {
                // Run heavy native library loading on background thread
                await System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        if (_libVLC == null)
                        {
                            // Determine the path to libvlc native libraries
                            var baseDir = AppContext.BaseDirectory;
                            var libvlcPath = Path.Combine(baseDir, "libvlc", "win-x64");
                            
                            Log($"Looking for libvlc at: {libvlcPath}");
                            Log($"libvlc.dll exists: {File.Exists(Path.Combine(libvlcPath, "libvlc.dll"))}");
                            Log($"libvlccore.dll exists: {File.Exists(Path.Combine(libvlcPath, "libvlccore.dll"))}");
                            
                            if (!File.Exists(Path.Combine(libvlcPath, "libvlc.dll")))
                            {
                                Log("ERROR: libvlc.dll not found!");
                                return;
                            }
                            
                            Log("Calling Core.Initialize() with libvlc path...");
                            LibVLCSharp.Shared.Core.Initialize(libvlcPath);
                            Log("Core.Initialize() succeeded, creating LibVLC instance...");
                            _libVLC = new LibVLC("--no-video-title-show", "--quiet");
                            Log($"LibVLC created: {_libVLC != null}");
                        }
                        else
                        {
                            Log("LibVLC already initialized (static)");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"InitializeLibVLCAsync background FAILED: {ex.GetType().Name}: {ex.Message}");
                        Log($"Stack: {ex.StackTrace}");
                    }
                });
                
                // Back on UI thread - create MediaPlayer and attach to VideoView
                if (_libVLC != null)
                {
                    Log("Creating MediaPlayer on UI thread...");
                    _mediaPlayer = new MediaPlayer(_libVLC);
                    Log($"MediaPlayer created: {_mediaPlayer != null}");
                    
                    // Hook up end reached event for auto-play next
                    if (_mediaPlayer != null)
                    {
                        _mediaPlayer.EndReached += MediaPlayer_EndReached;
                    }
                    
                    if (_mediaPlayer != null && VlcVideoView.MediaPlayer == null)
                    {
                        Log("Attaching MediaPlayer to VlcVideoView...");
                        VlcVideoView.MediaPlayer = _mediaPlayer;
                        Log("MediaPlayer attached to VlcVideoView");
                    }
                    
                    _isInitialized = true;
                    
                    // Play any file that was requested before init completed
                    if (!string.IsNullOrEmpty(_pendingPlayFile))
                    {
                        var pending = _pendingPlayFile;
                        _pendingPlayFile = null;
                        Log($"Playing pending file after init: {pending}");
                        PlayFromMediaLibrary(pending);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"InitializeLibVLCAsync FAILED: {ex.GetType().Name}: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
            }
            finally
            {
                _isInitializing = false;
            }
        }
        
        private void NativeVideoPlayerView_Loaded(object sender, RoutedEventArgs e)
        {
            Log("NativeVideoPlayerView_Loaded fired");
            
            // Start async initialization when view is loaded
            if (!_isInitialized && !_isInitializing)
            {
                InitializeLibVLCAsync();
            }
            else if (_isInitialized)
            {
                // Already initialized, just ensure MediaPlayer is attached
                if (_mediaPlayer == null && _libVLC != null)
                {
                    Log("MediaPlayer was null, recreating...");
                    _mediaPlayer = new MediaPlayer(_libVLC);
                }
                
                if (_mediaPlayer != null && VlcVideoView.MediaPlayer == null)
                {
                    Log("Re-attaching MediaPlayer to VlcVideoView...");
                    VlcVideoView.MediaPlayer = _mediaPlayer;
                }
            }
            
            Log($"State: _libVLC={_libVLC != null}, _mediaPlayer={_mediaPlayer != null}, _isInitialized={_isInitialized}");
        }
        
        private void NativeVideoPlayerView_Unloaded(object sender, RoutedEventArgs e)
        {
            // Just pause, don't dispose - we might come back to this tab
            _timer?.Stop();
            _mediaPlayer?.Pause();
        }

        private void InitializeTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (_mediaPlayer != null && _mediaPlayer.Length > 0)
            {
                var duration = TimeSpan.FromMilliseconds(_mediaPlayer.Length);
                var position = TimeSpan.FromMilliseconds(_mediaPlayer.Time);
                SeekSlider.Maximum = duration.TotalSeconds;
                SeekSlider.Value = position.TotalSeconds;
                PositionText.Text = position.ToString(@"hh\:mm\:ss");
                DurationText.Text = duration.ToString(@"hh\:mm\:ss");
            }
        }

        private void MediaPlayer_EndReached(object? sender, EventArgs e)
        {
            // EndReached is called from a VLC thread, need to dispatch to UI thread
            Dispatcher.BeginInvoke(() =>
            {
                Log("MediaPlayer EndReached event fired");
                
                // Clear current playing indicator
                if (_currentQueueIndex >= 0 && _currentQueueIndex < _queue.Count)
                {
                    _queue[_currentQueueIndex].IsPlaying = false;
                }
                
                if (AutoPlayCheckBox.IsChecked == true && _queue.Count > 0)
                {
                    // If not currently in queue, start from the beginning
                    if (_currentQueueIndex < 0)
                    {
                        Log("Auto-playing first item in queue");
                        PlayFromQueue(0);
                    }
                    else
                    {
                        Log("Auto-playing next in queue");
                        PlayNextInQueue();
                    }
                }
            });
        }

        private void BrowseVideo_Click(object sender, RoutedEventArgs e)
        {
            Log("BrowseVideo_Click");
            var dialog = new OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.vob;*.ogv;*.3gp;*.3g2;*.divx;*.xvid;*.asf;*.rm;*.rmvb|All Files|*.*",
                Title = "Select Video File",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                _currentFilePath = dialog.FileName;
                VideoFilePathBox.Text = dialog.FileName;
                Log($"Selected file: {dialog.FileName}");
                LoadVideo(dialog.FileName);
            }
        }
        
        private async void LoadVideo(string path)
        {
            Log($"LoadVideo called with: {path}");
            Log($"File exists: {File.Exists(path)}");
            
            if (_libVLC == null)
            {
                Log("ERROR: _libVLC is null, cannot load video");
                return;
            }
            
            // Ensure MediaPlayer exists and is connected to VideoView
            if (_mediaPlayer == null)
            {
                Log("MediaPlayer is null, creating new one...");
                _mediaPlayer = new MediaPlayer(_libVLC);
                _mediaPlayer.EndReached += MediaPlayer_EndReached;
            }
            
            Log($"MediaPlayer state: {_mediaPlayer?.State}");
            
            // Ensure MediaPlayer is attached to the VideoView
            if (VlcVideoView.MediaPlayer != _mediaPlayer)
            {
                Log("Attaching MediaPlayer to VlcVideoView in LoadVideo...");
                VlcVideoView.MediaPlayer = _mediaPlayer;
            }
            
            try
            {
                // Dispose previous media
                _currentMedia?.Dispose();
                Log("Previous media disposed");
                
                // Create new media
                Log("Creating Media object...");
                _currentMedia = new Media(_libVLC, path, FromType.FromPath);
                Log($"Media created, Duration (before parse): {_currentMedia.Duration}");
                
                _mediaPlayer!.Media = _currentMedia;
                Log("Media assigned to player");
                
                // Set volume
                _mediaPlayer!.Volume = (int)(VolumeSlider.Value * 100);
                Log($"Volume set to: {_mediaPlayer.Volume}");
                
                // Parse to get duration
                Log("Parsing media...");
                await _currentMedia.Parse(MediaParseOptions.ParseLocal);
                Log($"Parse complete, Duration: {_currentMedia.Duration}ms");
                
                if (_currentMedia.Duration > 0)
                {
                    var duration = TimeSpan.FromMilliseconds(_currentMedia.Duration);
                    SeekSlider.Maximum = duration.TotalSeconds;
                    DurationText.Text = duration.ToString(@"hh\:mm\:ss");
                    Log($"Duration set: {duration}");
                }
                
                _timer?.Start();
                Log("Timer started");
                
                // Auto-play when video is loaded
                Log("Calling Play()...");
                var playResult = _mediaPlayer.Play();
                Log($"Play() returned: {playResult}, State after: {_mediaPlayer.State}");
            }
            catch (Exception ex)
            {
                Log($"ERROR in LoadVideo: {ex.GetType().Name}: {ex.Message}");
                Log($"Stack: {ex.StackTrace}");
                MessageBox.Show($"Error loading video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            Log($"Play_Click: _mediaPlayer={_mediaPlayer != null}, State={_mediaPlayer?.State}");
            
            // If no video is loaded but queue has items, start playing from queue
            if ((_mediaPlayer == null || _mediaPlayer.State == VLCState.NothingSpecial || _mediaPlayer.State == VLCState.Stopped) 
                && _queue.Count > 0 && _currentQueueIndex < 0)
            {
                PlayFromQueue(0);
                return;
            }
            
            var result = _mediaPlayer?.Play();
            Log($"Play returned: {result}");
            _timer?.Start();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            Log($"Pause_Click: State={_mediaPlayer?.State}");
            _mediaPlayer?.Pause();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            Log($"Stop_Click: State={_mediaPlayer?.State}");
            _mediaPlayer?.Stop();
            _timer?.Stop();
            SeekSlider.Value = 0;
            PositionText.Text = "00:00:00";
            
            // Clear queue playing indicator
            if (_currentQueueIndex >= 0 && _currentQueueIndex < _queue.Count)
            {
                _queue[_currentQueueIndex].IsPlaying = false;
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null) return;
            var newTime = _mediaPlayer.Time - 10000; // 10 seconds in ms
            if (newTime < 0) newTime = 0;
            _mediaPlayer.Time = newTime;
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;
            var newTime = _mediaPlayer.Time + 10000; // 10 seconds in ms
            if (newTime > _mediaPlayer.Length) newTime = _mediaPlayer.Length;
            _mediaPlayer.Time = newTime;
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;
            var currentSeconds = _mediaPlayer.Time / 1000.0;
            var difference = Math.Abs(currentSeconds - e.NewValue);
            if (difference > 0.5)
            {
                _mediaPlayer.Time = (long)(e.NewValue * 1000);
            }
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Volume = (int)(e.NewValue * 100);
            }
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaPlayer != null)
            {
                _mediaPlayer.Mute = !_mediaPlayer.Mute;
            }
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath) || _libVLC == null)
            {
                MessageBox.Show("Please select a video file first.", "No Video", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var currentTime = _mediaPlayer?.Time ?? 0;
            var wasPlaying = _mediaPlayer?.IsPlaying ?? false;
            _mediaPlayer?.Pause();

            // Create fullscreen window with VLC
            var fullscreenWindow = new Window
            {
                Title = "Fullscreen - ESC to exit, Space=Play/Pause, Arrows=Seek",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None,
                Background = System.Windows.Media.Brushes.Black,
                ResizeMode = ResizeMode.NoResize
            };

            // Create VLC video view for fullscreen
            var fsVlcView = new LibVLCSharp.WPF.VideoView { Background = System.Windows.Media.Brushes.Black };
#pragma warning disable CA2000 // Dispose properly handled in cleanupFullscreen
            MediaPlayer? fsMediaPlayer = new MediaPlayer(_libVLC);
#pragma warning restore CA2000
            Media? fsMedia = null;
            bool disposed = false;
            
            Action cleanupFullscreen = () =>
            {
                if (disposed) return;
                disposed = true;
                
                fsMedia?.Dispose();
                fsMedia = null;
                
                if (fsMediaPlayer != null)
                {
                    fsMediaPlayer.Stop();
                    fsMediaPlayer.Dispose();
                    fsMediaPlayer = null;
                }
            };
            
            fullscreenWindow.Closed += (s, args) => cleanupFullscreen();

            var mainGrid = new Grid();
            mainGrid.Children.Add(fsVlcView);
            fullscreenWindow.Content = mainGrid;
            
            // Set up VLC and controls window when fullscreen is loaded
            fullscreenWindow.Loaded += (s, args) =>
            {
                if (fsMediaPlayer == null) return;
                
                // Attach media player and start playback
                fsVlcView.MediaPlayer = fsMediaPlayer;
                fsMedia = new Media(_libVLC, _currentFilePath, FromType.FromPath);
                fsMediaPlayer.Media = fsMedia;
                fsMediaPlayer.Volume = (int)(VolumeSlider.Value * 100);
                fsMediaPlayer.Play();
                fsMediaPlayer.Time = currentTime;
                
                // Create overlay window for controls (due to WPF airspace issues)
                var controlsWindow = new Window
                {
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true,
                    Owner = fullscreenWindow,
                    WindowState = WindowState.Maximized
                };
                
                var controlsPanel = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 30, 30, 30)),
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    CornerRadius = new CornerRadius(10, 10, 0, 0),
                    Padding = new Thickness(25, 15, 25, 15)
                };
                
                var controlsStack = new StackPanel();
                
                // Time display and progress
                var timeRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) };
                var fsPositionText = new TextBlock { Text = "00:00:00", Foreground = System.Windows.Media.Brushes.White, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(0, 0, 10, 0) };
                var fsSeekSlider = new Slider { Minimum = 0, Maximum = 100, Width = 400, VerticalAlignment = VerticalAlignment.Center };
                var fsDurationText = new TextBlock { Text = "00:00:00", Foreground = System.Windows.Media.Brushes.White, FontFamily = new System.Windows.Media.FontFamily("Consolas"), Margin = new Thickness(10, 0, 0, 0) };
                timeRow.Children.Add(fsPositionText);
                timeRow.Children.Add(fsSeekSlider);
                timeRow.Children.Add(fsDurationText);
                controlsStack.Children.Add(timeRow);
                
                // Buttons row
                var buttonRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 0) };
                var backBtn = new Button { Content = "⏪", MinWidth = 45, Height = 35, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4), FontSize = 16, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var playBtn = new Button { Content = "▶", MinWidth = 50, Height = 40, Margin = new Thickness(3), Padding = new Thickness(10, 6, 10, 6), FontSize = 18, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var pauseBtn = new Button { Content = "⏸", MinWidth = 50, Height = 40, Margin = new Thickness(3), Padding = new Thickness(10, 6, 10, 6), FontSize = 18, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var fwdBtn = new Button { Content = "⏩", MinWidth = 45, Height = 35, Margin = new Thickness(3), Padding = new Thickness(8, 4, 8, 4), FontSize = 16, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var exitBtn = new Button { Content = "✕ Exit", MinWidth = 60, Height = 35, Margin = new Thickness(15, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), FontSize = 14, Background = System.Windows.Media.Brushes.DarkRed, Foreground = System.Windows.Media.Brushes.White };
                
                playBtn.Click += (s2, a2) => fsMediaPlayer?.Play();
                pauseBtn.Click += (s2, a2) => fsMediaPlayer?.Pause();
                backBtn.Click += (s2, a2) => { if (fsMediaPlayer != null) fsMediaPlayer.Time = Math.Max(0, fsMediaPlayer.Time - 10000); };
                fwdBtn.Click += (s2, a2) => { if (fsMediaPlayer != null) fsMediaPlayer.Time = Math.Min(fsMediaPlayer.Length, fsMediaPlayer.Time + 10000); };
                exitBtn.Click += (s2, a2) => fullscreenWindow.Close();
                
                buttonRow.Children.Add(backBtn);
                buttonRow.Children.Add(playBtn);
                buttonRow.Children.Add(pauseBtn);
                buttonRow.Children.Add(fwdBtn);
                buttonRow.Children.Add(exitBtn);
                controlsStack.Children.Add(buttonRow);
                
                controlsPanel.Child = controlsStack;
                var overlayGrid = new Grid { Background = System.Windows.Media.Brushes.Transparent };
                overlayGrid.Children.Add(controlsPanel);
                controlsWindow.Content = overlayGrid;
                
                // Auto-hide timer
                DateTime lastMove = DateTime.Now;
                var hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                hideTimer.Tick += (s2, a2) =>
                {
                    if ((DateTime.Now - lastMove).TotalSeconds > 5)
                    {
                        controlsPanel.Visibility = Visibility.Collapsed;
                        fullscreenWindow.Cursor = System.Windows.Input.Cursors.None;
                    }
                };
                hideTimer.Start();
                
                // Show controls on mouse move
                overlayGrid.MouseMove += (s2, a2) =>
                {
                    lastMove = DateTime.Now;
                    fullscreenWindow.Cursor = System.Windows.Input.Cursors.Arrow;
                    controlsPanel.Visibility = Visibility.Visible;
                };
                
                // Seeking - handle user drag on slider
                bool isUserSeeking = false;
                fsSeekSlider.PreviewMouseDown += (s2, a2) => { isUserSeeking = true; };
                fsSeekSlider.PreviewMouseUp += (s2, a2) => 
                { 
                    if (fsMediaPlayer != null && isUserSeeking)
                    {
                        fsMediaPlayer.Time = (long)(fsSeekSlider.Value * 1000);
                    }
                    isUserSeeking = false; 
                };
                fsSeekSlider.ValueChanged += (s2, a2) =>
                {
                    if (isUserSeeking && fsMediaPlayer != null)
                    {
                        fsMediaPlayer.Time = (long)(fsSeekSlider.Value * 1000);
                    }
                };
                
                // Timer to update position
                var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                updateTimer.Tick += (s2, a2) =>
                {
                    if (fsMediaPlayer != null && fsMediaPlayer.Length > 0 && !isUserSeeking)
                    {
                        fsSeekSlider.Maximum = fsMediaPlayer.Length / 1000.0;
                        fsSeekSlider.Value = fsMediaPlayer.Time / 1000.0;
                        fsPositionText.Text = TimeSpan.FromMilliseconds(fsMediaPlayer.Time).ToString(@"hh\\:mm\\:ss");
                        fsDurationText.Text = TimeSpan.FromMilliseconds(fsMediaPlayer.Length).ToString(@"hh\\:mm\\:ss");
                    }
                };
                updateTimer.Start();
                
                fullscreenWindow.Closed += (s2, a2) => { updateTimer.Stop(); hideTimer.Stop(); controlsWindow.Close(); };
                controlsWindow.Show();
            };

            Action exitFullscreen = () =>
            {
                var exitTime = fsMediaPlayer?.Time ?? 0;
                cleanupFullscreen();
                fullscreenWindow.Close();
                
                // Resume main player
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.Time = exitTime;
                    if (wasPlaying) _mediaPlayer.Play();
                }
            };

            fullscreenWindow.KeyDown += (s, args) =>
            {
                switch (args.Key)
                {
                    case System.Windows.Input.Key.Escape:
                        exitFullscreen();
                        break;
                    case System.Windows.Input.Key.Space:
                        if (fsMediaPlayer != null)
                        {
                            if (fsMediaPlayer.IsPlaying) fsMediaPlayer.Pause();
                            else fsMediaPlayer.Play();
                        }
                        break;
                    case System.Windows.Input.Key.Left:
                        if (fsMediaPlayer != null) fsMediaPlayer.Time = Math.Max(0, fsMediaPlayer.Time - 10000);
                        break;
                    case System.Windows.Input.Key.Right:
                        if (fsMediaPlayer != null) fsMediaPlayer.Time = Math.Min(fsMediaPlayer.Length, fsMediaPlayer.Time + 10000);
                        break;
                }
            };

            // Double-click to exit
            fsVlcView.MouseLeftButtonDown += (s, args) =>
            {
                if (args.ClickCount == 2)
                    exitFullscreen();
            };

            fullscreenWindow.ShowDialog();
        }

        /// <summary>
        /// Play a video file from an external source (e.g., Media Library / Media Hub).
        /// Adds to queue and handles the case where LibVLC is still initializing.
        /// </summary>
        public void PlayFromMediaLibrary(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;
            
            _currentFilePath = filePath;
            VideoFilePathBox.Text = filePath;
            Log($"PlayFromMediaLibrary: {filePath}, _libVLC={_libVLC != null}, _isInitialized={_isInitialized}");
            
            // Add to queue if not already there
            if (!_queue.Any(q => q.FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
            {
                _queue.Add(new QueueItem { FullPath = filePath });
                UpdateQueueInfo();
                Log($"Added to queue: {Path.GetFileName(filePath)}");
            }
            
            if (_libVLC == null || !_isInitialized)
            {
                // LibVLC not ready yet — store for deferred playback
                _pendingPlayFile = filePath;
                Log($"LibVLC not ready, stored as pending: {filePath}");
                return;
            }
            
            // Find the item in the queue and play via queue
            var queueIndex = -1;
            for (int i = 0; i < _queue.Count; i++)
            {
                if (_queue[i].FullPath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    queueIndex = i;
                    break;
                }
            }
            
            if (queueIndex >= 0)
            {
                PlayFromQueue(queueIndex);
            }
            else
            {
                LoadVideo(filePath);
            }
        }

        #region Queue Management

        private void UpdateQueueInfo()
        {
            QueueInfoText.Text = $"{_queue.Count} item{(_queue.Count == 1 ? "" : "s")} in queue";
            // Update indices
            for (int i = 0; i < _queue.Count; i++)
            {
                _queue[i].Index = i + 1;
            }
        }

        private void AddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg;*.ts;*.mts;*.m2ts;*.vob;*.ogv;*.3gp;*.3g2;*.divx;*.xvid;*.asf;*.rm;*.rmvb|All Files|*.*",
                Title = "Add Videos to Queue",
                Multiselect = true
            };
            if (dialog.ShowDialog() == true)
            {
                foreach (var file in dialog.FileNames)
                {
                    _queue.Add(new QueueItem { FullPath = file });
                }
                UpdateQueueInfo();
                Log($"Added {dialog.FileNames.Length} files to queue");
            }
        }

        private void ClearQueue_Click(object sender, RoutedEventArgs e)
        {
            _queue.Clear();
            _currentQueueIndex = -1;
            UpdateQueueInfo();
            Log("Queue cleared");
        }

        private void ShuffleQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_queue.Count <= 1) return;

            var random = new Random();
            var items = _queue.ToList();
            
            // Fisher-Yates shuffle
            for (int i = items.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (items[i], items[j]) = (items[j], items[i]);
            }

            _queue.Clear();
            foreach (var item in items)
            {
                _queue.Add(item);
            }
            
            _currentQueueIndex = -1;
            UpdateQueueInfo();
            Log("Queue shuffled");
        }

        private void RemoveFromQueue_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is QueueItem item)
            {
                int idx = _queue.IndexOf(item);
                _queue.Remove(item);
                
                // Adjust current index if needed
                if (idx < _currentQueueIndex)
                    _currentQueueIndex--;
                else if (idx == _currentQueueIndex)
                    _currentQueueIndex = -1;
                    
                UpdateQueueInfo();
            }
        }

        private void QueueItem_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (QueueListBox.SelectedItem is QueueItem item)
            {
                PlayFromQueue(_queue.IndexOf(item));
            }
        }

        private void PlayFromQueue(int index)
        {
            if (index < 0 || index >= _queue.Count) return;

            // Clear previous playing state
            foreach (var item in _queue)
                item.IsPlaying = false;

            _currentQueueIndex = index;
            var queueItem = _queue[index];
            queueItem.IsPlaying = true;

            _currentFilePath = queueItem.FullPath;
            VideoFilePathBox.Text = queueItem.FullPath;
            LoadVideo(queueItem.FullPath);
            
            Log($"Playing from queue index {index}: {queueItem.FileName}");
        }

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (_queue.Count == 0) return;

            int newIndex;
            if (_currentQueueIndex <= 0)
            {
                // Wrap to end if repeat is enabled, otherwise stay at start
                newIndex = RepeatCheckBox.IsChecked == true ? _queue.Count - 1 : 0;
            }
            else
            {
                newIndex = _currentQueueIndex - 1;
            }

            PlayFromQueue(newIndex);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            PlayNextInQueue();
        }

        private void PlayNextInQueue()
        {
            if (_queue.Count == 0) return;

            int newIndex;
            if (_currentQueueIndex >= _queue.Count - 1)
            {
                // Wrap to beginning if repeat is enabled
                newIndex = RepeatCheckBox.IsChecked == true ? 0 : -1;
            }
            else
            {
                newIndex = _currentQueueIndex + 1;
            }

            if (newIndex >= 0)
                PlayFromQueue(newIndex);
        }

        private void QueueListBox_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void QueueListBox_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var videoExtensions = new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".mts", ".m2ts", ".vob", ".ogv", ".3gp", ".divx" };
                
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (videoExtensions.Contains(ext) || File.Exists(file))
                    {
                        _queue.Add(new QueueItem { FullPath = file });
                    }
                }
                UpdateQueueInfo();
                Log($"Dropped {files.Length} files to queue");
            }
        }

        #endregion
    }
}
