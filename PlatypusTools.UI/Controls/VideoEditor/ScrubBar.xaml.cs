using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PlatypusTools.UI.Controls.VideoEditor
{
    /// <summary>
    /// A scrub bar control for video timeline navigation.
    /// Modeled after Shotcut's ScrubBar (scrubbar.h/cpp)
    /// </summary>
    public partial class ScrubBar : UserControl
    {
        #region Dependency Properties

        public static readonly DependencyProperty PositionProperty =
            DependencyProperty.Register(nameof(Position), typeof(TimeSpan), typeof(ScrubBar),
                new PropertyMetadata(TimeSpan.Zero, OnPositionChanged));

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(nameof(Duration), typeof(TimeSpan), typeof(ScrubBar),
                new PropertyMetadata(TimeSpan.FromSeconds(60), OnDurationChanged));

        public static readonly DependencyProperty InPointProperty =
            DependencyProperty.Register(nameof(InPoint), typeof(TimeSpan?), typeof(ScrubBar),
                new PropertyMetadata(null, OnMarkersChanged));

        public static readonly DependencyProperty OutPointProperty =
            DependencyProperty.Register(nameof(OutPoint), typeof(TimeSpan?), typeof(ScrubBar),
                new PropertyMetadata(null, OnMarkersChanged));

        public static readonly DependencyProperty LoopStartProperty =
            DependencyProperty.Register(nameof(LoopStart), typeof(TimeSpan?), typeof(ScrubBar),
                new PropertyMetadata(null, OnMarkersChanged));

        public static readonly DependencyProperty LoopEndProperty =
            DependencyProperty.Register(nameof(LoopEnd), typeof(TimeSpan?), typeof(ScrubBar),
                new PropertyMetadata(null, OnMarkersChanged));

        public static readonly DependencyProperty FrameRateProperty =
            DependencyProperty.Register(nameof(FrameRate), typeof(double), typeof(ScrubBar),
                new PropertyMetadata(30.0));

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

        public TimeSpan? LoopStart
        {
            get => (TimeSpan?)GetValue(LoopStartProperty);
            set => SetValue(LoopStartProperty, value);
        }

        public TimeSpan? LoopEnd
        {
            get => (TimeSpan?)GetValue(LoopEndProperty);
            set => SetValue(LoopEndProperty, value);
        }

        public double FrameRate
        {
            get => (double)GetValue(FrameRateProperty);
            set => SetValue(FrameRateProperty, value);
        }

        #endregion

        #region Events

        public event EventHandler<TimeSpan>? Seeked;
        public event EventHandler<TimeSpan>? Paused;

        #endregion

        private bool _isDragging;
        private readonly List<TextBlock> _timecodeLabels = new();
        private readonly List<Line> _tickLines = new();

        public ScrubBar()
        {
            InitializeComponent();
        }

        private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrubBar scrubBar)
            {
                scrubBar.UpdatePlayhead();
            }
        }

        private static void OnDurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrubBar scrubBar)
            {
                scrubBar.RedrawTimeline();
            }
        }

        private static void OnMarkersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrubBar scrubBar)
            {
                scrubBar.UpdateMarkers();
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            RedrawTimeline();
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            CaptureMouse();
            SeekToMouse(e.GetPosition(ScrubCanvas));
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                Paused?.Invoke(this, Position);
            }
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                SeekToMouse(e.GetPosition(ScrubCanvas));
            }
        }

        private void SeekToMouse(Point mousePos)
        {
            if (Duration.TotalSeconds <= 0 || ActualWidth <= 0)
                return;

            double ratio = Math.Clamp(mousePos.X / ActualWidth, 0, 1);
            var newPosition = TimeSpan.FromSeconds(Duration.TotalSeconds * ratio);
            Position = newPosition;
            Seeked?.Invoke(this, newPosition);
        }

        private void UpdatePlayhead()
        {
            if (Duration.TotalSeconds <= 0 || ActualWidth <= 0)
                return;

            double x = (Position.TotalSeconds / Duration.TotalSeconds) * ActualWidth;
            Canvas.SetLeft(PlayheadCanvas, x);
            PlayheadLine.Height = ActualHeight - 8;
        }

        private void UpdateMarkers()
        {
            if (Duration.TotalSeconds <= 0 || ActualWidth <= 0)
                return;

            // In point
            if (InPoint.HasValue)
            {
                double x = (InPoint.Value.TotalSeconds / Duration.TotalSeconds) * ActualWidth;
                Canvas.SetLeft(InPointMarker, x);
                Canvas.SetTop(InPointMarker, (ActualHeight - 16) / 2);
                InPointMarker.Visibility = Visibility.Visible;
            }
            else
            {
                InPointMarker.Visibility = Visibility.Collapsed;
            }

            // Out point
            if (OutPoint.HasValue)
            {
                double x = (OutPoint.Value.TotalSeconds / Duration.TotalSeconds) * ActualWidth;
                Canvas.SetLeft(OutPointMarker, x - 8);
                Canvas.SetTop(OutPointMarker, (ActualHeight - 16) / 2);
                OutPointMarker.Visibility = Visibility.Visible;
            }
            else
            {
                OutPointMarker.Visibility = Visibility.Collapsed;
            }

            // Loop region
            if (LoopStart.HasValue && LoopEnd.HasValue)
            {
                double startX = (LoopStart.Value.TotalSeconds / Duration.TotalSeconds) * ActualWidth;
                double endX = (LoopEnd.Value.TotalSeconds / Duration.TotalSeconds) * ActualWidth;
                Canvas.SetLeft(LoopRegion, startX);
                LoopRegion.Width = Math.Max(0, endX - startX);
                LoopRegion.Visibility = Visibility.Visible;
            }
            else
            {
                LoopRegion.Visibility = Visibility.Collapsed;
            }
        }

        private void RedrawTimeline()
        {
            // Clear old elements
            foreach (var label in _timecodeLabels)
                ScrubCanvas.Children.Remove(label);
            foreach (var line in _tickLines)
                ScrubCanvas.Children.Remove(line);
            _timecodeLabels.Clear();
            _tickLines.Clear();

            if (Duration.TotalSeconds <= 0 || ActualWidth <= 0)
                return;

            // Calculate tick interval (like Shotcut)
            double pixelsPerSecond = ActualWidth / Duration.TotalSeconds;
            int secondsPerTick = CalculateTickInterval(pixelsPerSecond);

            // Draw ticks and timecodes
            for (double seconds = 0; seconds <= Duration.TotalSeconds; seconds += secondsPerTick)
            {
                double x = (seconds / Duration.TotalSeconds) * ActualWidth;

                // Major tick line
                var tickLine = new Line
                {
                    X1 = x,
                    Y1 = ActualHeight - 15,
                    X2 = x,
                    Y2 = ActualHeight,
                    Stroke = FindResource("ForegroundDimBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                    StrokeThickness = 1
                };
                ScrubCanvas.Children.Add(tickLine);
                _tickLines.Add(tickLine);

                // Timecode label
                var timeSpan = TimeSpan.FromSeconds(seconds);
                string timecode = FormatTimecode(timeSpan);
                var label = new TextBlock
                {
                    Text = timecode,
                    Foreground = FindResource("ForegroundDimBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                    FontSize = 10,
                    FontFamily = new FontFamily("Consolas")
                };
                Canvas.SetLeft(label, x + 2);
                Canvas.SetTop(label, 2);
                ScrubCanvas.Children.Add(label);
                _timecodeLabels.Add(label);

                // Minor ticks
                if (secondsPerTick >= 5)
                {
                    for (int i = 1; i < 5; i++)
                    {
                        double minorSeconds = seconds + (secondsPerTick / 5.0 * i);
                        if (minorSeconds > Duration.TotalSeconds) break;
                        double minorX = (minorSeconds / Duration.TotalSeconds) * ActualWidth;
                        var minorTick = new Line
                        {
                            X1 = minorX,
                            Y1 = ActualHeight - 8,
                            X2 = minorX,
                            Y2 = ActualHeight,
                            Stroke = FindResource("ControlBorderBrush") as Brush ?? new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                            StrokeThickness = 1
                        };
                        ScrubCanvas.Children.Add(minorTick);
                        _tickLines.Add(minorTick);
                    }
                }
            }

            UpdatePlayhead();
            UpdateMarkers();
        }

        private int CalculateTickInterval(double pixelsPerSecond)
        {
            // Similar to Shotcut's logic
            if (pixelsPerSecond >= 100) return 1;
            if (pixelsPerSecond >= 50) return 2;
            if (pixelsPerSecond >= 20) return 5;
            if (pixelsPerSecond >= 10) return 10;
            if (pixelsPerSecond >= 4) return 30;
            if (pixelsPerSecond >= 2) return 60;
            return 300; // 5 minutes
        }

        private string FormatTimecode(TimeSpan time)
        {
            if (Duration.TotalHours >= 1)
                return time.ToString(@"h\:mm\:ss");
            if (Duration.TotalMinutes >= 1)
                return time.ToString(@"m\:ss");
            return time.ToString(@"s\.f");
        }
    }
}
