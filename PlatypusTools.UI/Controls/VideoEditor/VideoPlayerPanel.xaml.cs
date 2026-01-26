using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PlatypusTools.UI.Controls.VideoEditor
{
    /// <summary>
    /// Video player panel with transport controls.
    /// Modeled after Shotcut's Player class.
    /// </summary>
    public partial class VideoPlayerPanel : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty SourceProperty =
            DependencyProperty.Register(nameof(Source), typeof(Uri), typeof(VideoPlayerPanel),
                new PropertyMetadata(null, OnSourceChanged));

        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(nameof(Position), typeof(TimeSpan), typeof(VideoPlayerPanel),
                new PropertyMetadata(TimeSpan.Zero, OnPositionChanged));

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(VideoPlayerPanel),
                new PropertyMetadata(TimeSpan.Zero));

        public static readonly DependencyProperty InPointProperty =
            DependencyProperty.Register(nameof(InPoint), typeof(TimeSpan?), typeof(VideoPlayerPanel),
                new PropertyMetadata(null));

        public static readonly DependencyProperty OutPointProperty =
            DependencyProperty.Register(nameof(OutPoint), typeof(TimeSpan?), typeof(VideoPlayerPanel),
                new PropertyMetadata(null));

        public static readonly DependencyProperty IsPlayingProperty =
            DependencyProperty.Register(nameof(IsPlaying), typeof(bool), typeof(VideoPlayerPanel),
                new PropertyMetadata(false));

        public Uri? Source
        {
            get => (Uri?)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        public TimeSpan Position
        {
            get => (TimeSpan)GetValue(PositionProperty);
            set => SetValue(PositionProperty, value);
        }

        public TimeSpan Duration
        {
            get => (TimeSpan)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public TimeSpan? InPoint
        {
            get => (TimeSpan?)GetValue(InPointProperty);
            set => SetValue(InPointProperty, value);
        }

        public TimeSpan? OutPoint
        {
            get => (TimeSpan?)GetValue(OutPointProperty);
            set => SetValue(OutPointProperty, value);
        }

        public bool IsPlaying
        {
            get => (bool)GetValue(IsPlayingProperty);
            set => SetValue(IsPlayingProperty, value);
        }

        #endregion

        #region Events

        public event EventHandler<TimeSpan>? Seeked;
        public event EventHandler<TimeSpan>? InPointChanged;
        public event EventHandler<TimeSpan>? OutPointChanged;
        public event EventHandler? Played;
        public event EventHandler? Paused;

        #endregion

        private readonly DispatcherTimer _positionTimer;
        private double _playbackSpeed = 1.0;
        private bool _isMuted;
        private double _previousVolume = 1.0;
        private bool _isExternalPositionUpdate;

        public VideoPlayerPanel()
        {
            InitializeComponent();

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _positionTimer.Tick += PositionTimer_Tick;
        }

        private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoPlayerPanel player)
            {
                player.LoadMedia((Uri?)e.NewValue);
            }
        }

        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoPlayerPanel player && !player._isExternalPositionUpdate)
            {
                player.SeekTo((TimeSpan)e.NewValue);
            }
        }

        #region Media Control

        public void LoadMedia(Uri? uri)
        {
            if (uri == null)
            {
                VideoPlayer.Source = null;
                NoVideoOverlay.Visibility = Visibility.Visible;
                Duration = TimeSpan.Zero;
                Position = TimeSpan.Zero;
                return;
            }

            try
            {
                VideoPlayer.Source = uri;
                NoVideoOverlay.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load media: {ex.Message}");
            }
        }

        public void LoadMedia(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                LoadMedia((Uri?)null);
                return;
            }

            try
            {
                // Use UriKind.Absolute for file paths to ensure proper parsing
                var uri = new Uri(path, UriKind.Absolute);
                LoadMedia(uri);
                
                // Auto-play when media is loaded from double-click
                // MediaOpened will be called when ready, then we play
                VideoPlayer.MediaOpened -= AutoPlayOnOpen;
                VideoPlayer.MediaOpened += AutoPlayOnOpen;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create URI from path: {path}, Error: {ex.Message}");
                LoadMedia((Uri?)null);
            }
        }

        private void AutoPlayOnOpen(object sender, RoutedEventArgs e)
        {
            VideoPlayer.MediaOpened -= AutoPlayOnOpen;
            Play();
        }

        public void Play(double speed = 1.0)
        {
            _playbackSpeed = speed;
            VideoPlayer.SpeedRatio = Math.Abs(speed);
            VideoPlayer.Play();
            IsPlaying = true;
            _positionTimer.Start();
            PlayPauseIcon.Text = "⏸";
            Played?.Invoke(this, EventArgs.Empty);
        }

        public void Pause()
        {
            VideoPlayer.Pause();
            IsPlaying = false;
            _positionTimer.Stop();
            PlayPauseIcon.Text = "▶";
            Paused?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            VideoPlayer.Stop();
            IsPlaying = false;
            _positionTimer.Stop();
            Position = TimeSpan.Zero;
            PlayPauseIcon.Text = "▶";
        }

        public void SeekTo(TimeSpan position)
        {
            if (position < TimeSpan.Zero) position = TimeSpan.Zero;
            if (position > Duration) position = Duration;

            VideoPlayer.Position = position;
            UpdatePositionDisplay(position);
        }

        public void SeekByFrames(int frames)
        {
            // Assume 30fps if we don't know
            double fps = 30;
            var frameTime = TimeSpan.FromSeconds(frames / fps);
            SeekTo(Position + frameTime);
        }

        #endregion

        #region Event Handlers

        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            if (VideoPlayer.NaturalDuration.HasTimeSpan)
            {
                Duration = VideoPlayer.NaturalDuration.TimeSpan;
                PlayerScrubBar.Duration = Duration;
                DurationText.Text = FormatTimecode(Duration);
            }
            NoVideoOverlay.Visibility = Visibility.Collapsed;
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            if (LoopButton.IsChecked == true)
            {
                VideoPlayer.Position = InPoint ?? TimeSpan.Zero;
                VideoPlayer.Play();
            }
            else
            {
                Pause();
            }
        }

        private void VideoPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"Media failed: {e.ErrorException?.Message}");
            NoVideoOverlay.Visibility = Visibility.Visible;
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            _isExternalPositionUpdate = true;
            Position = VideoPlayer.Position;
            PlayerScrubBar.Position = Position;
            UpdatePositionDisplay(Position);
            _isExternalPositionUpdate = false;

            // Check for out point
            if (OutPoint.HasValue && Position >= OutPoint.Value)
            {
                if (LoopButton.IsChecked == true)
                {
                    SeekTo(InPoint ?? TimeSpan.Zero);
                }
                else
                {
                    Pause();
                }
            }
        }

        private void PlayerScrubBar_Seeked(object? sender, TimeSpan e)
        {
            SeekTo(e);
            Seeked?.Invoke(this, e);
        }

        private void UpdatePositionDisplay(TimeSpan position)
        {
            var timecode = FormatTimecode(position);
            PositionText.Text = timecode;
            TimecodeDisplay.Text = timecode;
        }

        private string FormatTimecode(TimeSpan time)
        {
            return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds / 10:00}";
        }

        #endregion

        #region Transport Button Handlers

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (IsPlaying)
                Pause();
            else
                Play();
        }

        private void SkipToStart_Click(object sender, RoutedEventArgs e)
        {
            SeekTo(InPoint ?? TimeSpan.Zero);
        }

        private void SkipToEnd_Click(object sender, RoutedEventArgs e)
        {
            SeekTo(OutPoint ?? Duration);
        }

        private void PreviousFrame_Click(object sender, RoutedEventArgs e)
        {
            Pause();
            SeekByFrames(-1);
        }

        private void NextFrame_Click(object sender, RoutedEventArgs e)
        {
            Pause();
            SeekByFrames(1);
        }

        private void Rewind_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackSpeed > 0)
                _playbackSpeed = -1;
            else
                _playbackSpeed *= 2;

            _playbackSpeed = Math.Max(_playbackSpeed, -8);
            
            // MediaElement doesn't support reverse playback well,
            // so we simulate it with frame stepping
            Pause();
            SeekByFrames(-10);
        }

        private void FastForward_Click(object sender, RoutedEventArgs e)
        {
            if (_playbackSpeed < 0)
                _playbackSpeed = 1;
            else
                _playbackSpeed *= 2;

            _playbackSpeed = Math.Min(_playbackSpeed, 8);
            Play(_playbackSpeed);
        }

        private void SetInPoint_Click(object sender, RoutedEventArgs e)
        {
            InPoint = Position;
            PlayerScrubBar.InPoint = Position;
            InPointChanged?.Invoke(this, Position);
        }

        private void SetOutPoint_Click(object sender, RoutedEventArgs e)
        {
            OutPoint = Position;
            PlayerScrubBar.OutPoint = Position;
            OutPointChanged?.Invoke(this, Position);
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            _isMuted = !_isMuted;
            if (_isMuted)
            {
                _previousVolume = VideoPlayer.Volume;
                VideoPlayer.Volume = 0;
                VolumeIcon.Text = "🔇";
            }
            else
            {
                VideoPlayer.Volume = _previousVolume;
                VolumeIcon.Text = _previousVolume > 0.5 ? "🔊" : "🔉";
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            VideoPlayer.Volume = e.NewValue;
            _isMuted = false;
            VolumeIcon.Text = e.NewValue > 0.5 ? "🔊" : e.NewValue > 0 ? "🔉" : "🔇";
        }

        #endregion
    }
}
