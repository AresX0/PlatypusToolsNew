using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using LibVLCSharp.Shared;

namespace PlatypusTools.UI.Views
{
    public partial class NativeVideoPlayerView : UserControl
    {
        private DispatcherTimer? _timer;
        private string? _currentFilePath;
        
        // LibVLC for universal video playback
        private static LibVLC? _libVLC;
        private MediaPlayer? _mediaPlayer;
        private Media? _currentMedia;

        public NativeVideoPlayerView()
        {
            InitializeComponent();
            InitializeLibVLC();
            InitializeTimer();
            
            Loaded += NativeVideoPlayerView_Loaded;
            Unloaded += NativeVideoPlayerView_Unloaded;
        }
        
        private void InitializeLibVLC()
        {
            try
            {
                if (_libVLC == null)
                {
                    LibVLCSharp.Shared.Core.Initialize();
                    _libVLC = new LibVLC("--no-video-title-show", "--quiet");
                }
                
                _mediaPlayer = new MediaPlayer(_libVLC);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VLC] Init failed: {ex.Message}");
            }
        }
        
        private void NativeVideoPlayerView_Loaded(object sender, RoutedEventArgs e)
        {
            // Ensure we have a MediaPlayer (recreate if was disposed)
            if (_mediaPlayer == null && _libVLC != null)
            {
                _mediaPlayer = new MediaPlayer(_libVLC);
            }
            
            // Attach media player to VideoView after control is loaded
            if (_mediaPlayer != null && VlcVideoView.MediaPlayer == null)
            {
                VlcVideoView.MediaPlayer = _mediaPlayer;
            }
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

        private void BrowseVideo_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Video Files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.mpg;*.mpeg|All Files|*.*",
                Title = "Select Video File"
            };
            if (dialog.ShowDialog() == true)
            {
                _currentFilePath = dialog.FileName;
                VideoFilePathBox.Text = dialog.FileName;
                LoadVideo(dialog.FileName);
            }
        }
        
        private async void LoadVideo(string path)
        {
            if (_libVLC == null || _mediaPlayer == null) return;
            
            try
            {
                // Dispose previous media
                _currentMedia?.Dispose();
                
                // Create new media
                _currentMedia = new Media(_libVLC, path, FromType.FromPath);
                _mediaPlayer.Media = _currentMedia;
                
                // Set volume
                _mediaPlayer.Volume = (int)(VolumeSlider.Value * 100);
                
                // Parse to get duration
                await _currentMedia.Parse(MediaParseOptions.ParseLocal);
                
                if (_currentMedia.Duration > 0)
                {
                    var duration = TimeSpan.FromMilliseconds(_currentMedia.Duration);
                    SeekSlider.Maximum = duration.TotalSeconds;
                    DurationText.Text = duration.ToString(@"hh\:mm\:ss");
                }
                
                _timer?.Start();
                
                // Auto-play when video is loaded
                _mediaPlayer.Play();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading video: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer?.Play();
            _timer?.Start();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer?.Pause();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            _mediaPlayer?.Stop();
            _timer?.Stop();
            SeekSlider.Value = 0;
            PositionText.Text = "00:00:00";
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
                var backBtn = new Button { Content = "⏪", Width = 45, Height = 35, Margin = new Thickness(3), FontSize = 16, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var playBtn = new Button { Content = "▶", Width = 50, Height = 40, Margin = new Thickness(3), FontSize = 18, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var pauseBtn = new Button { Content = "⏸", Width = 50, Height = 40, Margin = new Thickness(3), FontSize = 18, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var fwdBtn = new Button { Content = "⏩", Width = 45, Height = 35, Margin = new Thickness(3), FontSize = 16, Background = System.Windows.Media.Brushes.DimGray, Foreground = System.Windows.Media.Brushes.White };
                var exitBtn = new Button { Content = "✕ Exit", Width = 60, Height = 35, Margin = new Thickness(15, 0, 0, 0), FontSize = 14, Background = System.Windows.Media.Brushes.DarkRed, Foreground = System.Windows.Media.Brushes.White };
                
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
                
                // Timer to update position
                var updateTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                updateTimer.Tick += (s2, a2) =>
                {
                    if (fsMediaPlayer != null && fsMediaPlayer.Length > 0)
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
    }
}
