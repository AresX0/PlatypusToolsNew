using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;

namespace PlatypusTools.UI.Views
{
    public partial class NativeVideoPlayerView : UserControl
    {
        private DispatcherTimer? _timer;
        private string? _currentFilePath;

        public NativeVideoPlayerView()
        {
            InitializeComponent();
            InitializeTimer();
        }

        private void InitializeTimer()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _timer.Tick += Timer_Tick;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = VideoPlayer.NaturalDuration.TimeSpan;
                var position = VideoPlayer.Position;
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
                VideoPlayer.Source = new Uri(dialog.FileName);
                VideoPlayer.Volume = VolumeSlider.Value;
            }
        }

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var duration = VideoPlayer.NaturalDuration.TimeSpan;
                SeekSlider.Maximum = duration.TotalSeconds;
                DurationText.Text = duration.ToString(@"hh\:mm\:ss");
            }
            _timer?.Start();
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _timer?.Stop();
            VideoPlayer.Position = TimeSpan.Zero;
            SeekSlider.Value = 0;
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Play();
            _timer?.Start();
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Pause();
        }

        private void Stop_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Stop();
            _timer?.Stop();
            SeekSlider.Value = 0;
            PositionText.Text = "00:00:00";
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            var newPosition = VideoPlayer.Position - TimeSpan.FromSeconds(10);
            if (newPosition < TimeSpan.Zero) newPosition = TimeSpan.Zero;
            VideoPlayer.Position = newPosition;
        }

        private void Forward_Click(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                var newPosition = VideoPlayer.Position + TimeSpan.FromSeconds(10);
                if (newPosition > VideoPlayer.NaturalDuration.TimeSpan)
                    newPosition = VideoPlayer.NaturalDuration.TimeSpan;
                VideoPlayer.Position = newPosition;
            }
        }

        private void SeekSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!VideoPlayer.NaturalDuration.HasTimeSpan) return;
            var difference = Math.Abs(VideoPlayer.Position.TotalSeconds - e.NewValue);
            if (difference > 0.5)
            {
                VideoPlayer.Position = TimeSpan.FromSeconds(e.NewValue);
            }
        }

        private void Volume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (VideoPlayer != null)
            {
                VideoPlayer.Volume = e.NewValue;
            }
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            VideoPlayer.IsMuted = !VideoPlayer.IsMuted;
        }

        private void Fullscreen_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentFilePath))
            {
                MessageBox.Show("Please select a video file first.", "No Video", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var currentPosition = VideoPlayer.Position;
            var wasPlaying = _timer?.IsEnabled ?? false;
            VideoPlayer.Pause();

            var fullscreenWindow = new Window
            {
                Title = "Video Player - Fullscreen",
                WindowState = WindowState.Maximized,
                WindowStyle = WindowStyle.None,
                Background = System.Windows.Media.Brushes.Black,
                ResizeMode = System.Windows.ResizeMode.NoResize
            };

            // Create main grid
            var mainGrid = new Grid();
            
            // Media element
            var fullscreenPlayer = new MediaElement
            {
                LoadedBehavior = MediaState.Manual,
                UnloadedBehavior = MediaState.Stop,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Source = new Uri(_currentFilePath),
                Volume = VolumeSlider.Value
            };
            mainGrid.Children.Add(fullscreenPlayer);

            // Control panel overlay (initially hidden)
            var controlPanel = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 30, 30, 30)),
                Height = 100,
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                CornerRadius = new CornerRadius(8, 8, 0, 0),
                Margin = new Thickness(50, 0, 50, 0),
                Visibility = Visibility.Collapsed
            };

            var controlStack = new StackPanel { Margin = new Thickness(20, 10, 20, 10) };
            
            // Seek slider row
            var seekGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            seekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            
            var posText = new TextBlock { Text = "00:00:00", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            Grid.SetColumn(posText, 0);
            var seekSlider = new Slider { Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
            Grid.SetColumn(seekSlider, 1);
            var durText = new TextBlock { Text = "00:00:00", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            Grid.SetColumn(durText, 2);
            seekGrid.Children.Add(posText);
            seekGrid.Children.Add(seekSlider);
            seekGrid.Children.Add(durText);
            controlStack.Children.Add(seekGrid);

            // Buttons row
            var buttonPanel = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center };
            var btnStyle = new Style(typeof(Button));
            
            var playBtn = new Button { Content = "▶ Play", Width = 80, Height = 35, Margin = new Thickness(5) };
            var pauseBtn = new Button { Content = "⏸ Pause", Width = 80, Height = 35, Margin = new Thickness(5) };
            var stopBtn = new Button { Content = "⏹ Stop", Width = 80, Height = 35, Margin = new Thickness(5) };
            var backBtn = new Button { Content = "⏪ -10s", Width = 70, Height = 35, Margin = new Thickness(5) };
            var fwdBtn = new Button { Content = "⏩ +10s", Width = 70, Height = 35, Margin = new Thickness(5) };
            var exitBtn = new Button { Content = "✕ Exit", Width = 70, Height = 35, Margin = new Thickness(5), Background = System.Windows.Media.Brushes.IndianRed };
            
            var volLabel = new TextBlock { Text = "Vol:", Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 5, 0) };
            var volSlider = new Slider { Minimum = 0, Maximum = 1, Value = VolumeSlider.Value, Width = 100, VerticalAlignment = VerticalAlignment.Center };
            var muteBtn = new Button { Content = "🔇", Width = 40, Height = 35, Margin = new Thickness(5) };

            buttonPanel.Children.Add(playBtn);
            buttonPanel.Children.Add(pauseBtn);
            buttonPanel.Children.Add(stopBtn);
            buttonPanel.Children.Add(backBtn);
            buttonPanel.Children.Add(fwdBtn);
            buttonPanel.Children.Add(volLabel);
            buttonPanel.Children.Add(volSlider);
            buttonPanel.Children.Add(muteBtn);
            buttonPanel.Children.Add(exitBtn);
            controlStack.Children.Add(buttonPanel);

            controlPanel.Child = controlStack;
            mainGrid.Children.Add(controlPanel);

            fullscreenWindow.Content = mainGrid;

            // Timer for position updates
            var fsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            fsTimer.Tick += (s, args) =>
            {
                if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                {
                    var dur = fullscreenPlayer.NaturalDuration.TimeSpan;
                    var pos = fullscreenPlayer.Position;
                    seekSlider.Maximum = dur.TotalSeconds;
                    seekSlider.Value = pos.TotalSeconds;
                    posText.Text = pos.ToString(@"hh\:mm\:ss");
                    durText.Text = dur.ToString(@"hh\:mm\:ss");
                }
            };

            // Show/hide control panel on mouse movement
            DateTime lastMouseMove = DateTime.Now;
            DispatcherTimer hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            hideTimer.Tick += (s, args) =>
            {
                if ((DateTime.Now - lastMouseMove).TotalSeconds > 2.5)
                {
                    controlPanel.Visibility = Visibility.Collapsed;
                    fullscreenWindow.Cursor = System.Windows.Input.Cursors.None;
                }
            };
            hideTimer.Start();

            mainGrid.MouseMove += (s, args) =>
            {
                lastMouseMove = DateTime.Now;
                fullscreenWindow.Cursor = System.Windows.Input.Cursors.Arrow;
                var pos = args.GetPosition(mainGrid);
                // Show controls when mouse is in bottom 150px
                if (pos.Y > mainGrid.ActualHeight - 150)
                {
                    controlPanel.Visibility = Visibility.Visible;
                }
            };

            // Button handlers
            playBtn.Click += (s, args) => { fullscreenPlayer.Play(); fsTimer.Start(); };
            pauseBtn.Click += (s, args) => fullscreenPlayer.Pause();
            stopBtn.Click += (s, args) => { fullscreenPlayer.Stop(); fsTimer.Stop(); };
            backBtn.Click += (s, args) =>
            {
                var newPos = fullscreenPlayer.Position - TimeSpan.FromSeconds(10);
                if (newPos < TimeSpan.Zero) newPos = TimeSpan.Zero;
                fullscreenPlayer.Position = newPos;
            };
            fwdBtn.Click += (s, args) =>
            {
                if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                {
                    var newPos = fullscreenPlayer.Position + TimeSpan.FromSeconds(10);
                    if (newPos > fullscreenPlayer.NaturalDuration.TimeSpan)
                        newPos = fullscreenPlayer.NaturalDuration.TimeSpan;
                    fullscreenPlayer.Position = newPos;
                }
            };
            volSlider.ValueChanged += (s, args) => fullscreenPlayer.Volume = args.NewValue;
            muteBtn.Click += (s, args) => fullscreenPlayer.IsMuted = !fullscreenPlayer.IsMuted;
            
            Action exitFullscreen = () =>
            {
                var exitPosition = fullscreenPlayer.Position;
                fsTimer.Stop();
                hideTimer.Stop();
                fullscreenPlayer.Stop();
                fullscreenWindow.Close();
                VideoPlayer.Position = exitPosition;
                if (wasPlaying) VideoPlayer.Play();
                _timer?.Start();
            };
            exitBtn.Click += (s, args) => exitFullscreen();

            seekSlider.ValueChanged += (s, args) =>
            {
                if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                {
                    var diff = Math.Abs(fullscreenPlayer.Position.TotalSeconds - args.NewValue);
                    if (diff > 1)
                        fullscreenPlayer.Position = TimeSpan.FromSeconds(args.NewValue);
                }
            };

            fullscreenPlayer.MediaOpened += (s, args) =>
            {
                fullscreenPlayer.Position = currentPosition;
                fullscreenPlayer.Play();
                fsTimer.Start();
            };

            fullscreenPlayer.MediaEnded += (s, args) =>
            {
                fullscreenPlayer.Position = TimeSpan.Zero;
                fullscreenPlayer.Play();
            };

            fullscreenWindow.KeyDown += (s, args) =>
            {
                switch (args.Key)
                {
                    case System.Windows.Input.Key.Escape:
                        exitFullscreen();
                        break;
                    case System.Windows.Input.Key.Space:
                        if (fullscreenPlayer.CanPause)
                        {
                            var pos = fullscreenPlayer.Position;
                            fullscreenPlayer.Pause();
                            fullscreenPlayer.Position = pos;
                        }
                        break;
                    case System.Windows.Input.Key.Left:
                        var backPos = fullscreenPlayer.Position - TimeSpan.FromSeconds(10);
                        if (backPos < TimeSpan.Zero) backPos = TimeSpan.Zero;
                        fullscreenPlayer.Position = backPos;
                        break;
                    case System.Windows.Input.Key.Right:
                        if (fullscreenPlayer.NaturalDuration.HasTimeSpan)
                        {
                            var fwdPos = fullscreenPlayer.Position + TimeSpan.FromSeconds(10);
                            if (fwdPos > fullscreenPlayer.NaturalDuration.TimeSpan)
                                fwdPos = fullscreenPlayer.NaturalDuration.TimeSpan;
                            fullscreenPlayer.Position = fwdPos;
                        }
                        break;
                    case System.Windows.Input.Key.Up:
                        fullscreenPlayer.Volume = Math.Min(1.0, fullscreenPlayer.Volume + 0.1);
                        volSlider.Value = fullscreenPlayer.Volume;
                        break;
                    case System.Windows.Input.Key.Down:
                        fullscreenPlayer.Volume = Math.Max(0.0, fullscreenPlayer.Volume - 0.1);
                        volSlider.Value = fullscreenPlayer.Volume;
                        break;
                    case System.Windows.Input.Key.M:
                        fullscreenPlayer.IsMuted = !fullscreenPlayer.IsMuted;
                        break;
                }
            };

            fullscreenPlayer.MouseLeftButtonDown += (s, args) =>
            {
                if (args.ClickCount == 2)
                    exitFullscreen();
            };

            fullscreenWindow.ShowDialog();
        }
    }
}
