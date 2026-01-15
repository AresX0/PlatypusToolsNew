using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Native audio visualizer with spectrum bars and waveform rendering.
    /// Supports multiple visualization modes: Bars, Mirror, Waveform, Circular.
    /// </summary>
    public partial class AudioVisualizerView : UserControl
    {
        private double[] _spectrumData = Array.Empty<double>();
        private double[] _previousSpectrumData = Array.Empty<double>();
        private DispatcherTimer? _renderTimer;
        private int _barCount = 32;
        private string _visualizationMode = "Bars"; // Bars, Mirror, Waveform, Circular

        public AudioVisualizerView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            SizeChanged += OnSizeChanged;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Start render loop
            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) }; // ~30fps
            _renderTimer.Tick += (s, e) => RenderVisualization();
            _renderTimer.Start();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Redraw when canvas resizes
            RenderVisualization();
        }

        public void UpdateSpectrumData(double[] spectrumData, string mode = "Bars", int barCount = 32)
        {
            _spectrumData = spectrumData ?? Array.Empty<double>();
            _visualizationMode = mode;
            _barCount = barCount;
        }

        private void RenderVisualization()
        {
            var canvas = VisualizerCanvas;
            if (canvas == null || _spectrumData.Length == 0) return;

            canvas.Children.Clear();

            // Draw background
            var bg = new Rectangle
            {
                Width = canvas.ActualWidth,
                Height = canvas.ActualHeight,
                Fill = new SolidColorBrush(Color.FromRgb(10, 14, 39))
            };
            canvas.Children.Add(bg);

            switch (_visualizationMode)
            {
                case "Mirror":
                    RenderMirrorBars(canvas);
                    break;
                case "Waveform":
                    RenderWaveform(canvas);
                    break;
                case "Circular":
                    RenderCircular(canvas);
                    break;
                default: // Bars
                    RenderBars(canvas);
                    break;
            }

            // Smooth transition
            _previousSpectrumData = (double[])_spectrumData.Clone();
        }

        private void RenderBars(Canvas canvas)
        {
            int numBars = Math.Min(_barCount, _spectrumData.Length);
            double barWidth = canvas.ActualWidth / numBars;
            double maxHeight = canvas.ActualHeight;

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(30, 144, 255), 0),   // Dodger Blue
                    new GradientStop(Color.FromRgb(0, 255, 127), 1)     // Spring Green
                }
            };

            for (int i = 0; i < numBars; i++)
            {
                double value = Math.Min(1.0, _spectrumData[i] * 2); // Clamp and amplify
                double barHeight = value * maxHeight;

                var bar = new Rectangle
                {
                    Width = barWidth - 2,
                    Height = barHeight,
                    Fill = brush,
                    Opacity = 0.8
                };

                Canvas.SetLeft(bar, i * barWidth + 1);
                Canvas.SetTop(bar, maxHeight - barHeight);
                canvas.Children.Add(bar);
            }
        }

        private void RenderMirrorBars(Canvas canvas)
        {
            int numBars = Math.Min(_barCount, _spectrumData.Length);
            double barWidth = canvas.ActualWidth / numBars;
            double maxHeight = canvas.ActualHeight / 2;

            var brush = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(255, 0, 127), 0),    // Red
                    new GradientStop(Color.FromRgb(30, 144, 255), 0.5),  // Blue
                    new GradientStop(Color.FromRgb(0, 255, 127), 1)      // Green
                }
            };

            double centerY = canvas.ActualHeight / 2;

            for (int i = 0; i < numBars; i++)
            {
                double value = Math.Min(1.0, _spectrumData[i] * 2);
                double barHeight = value * maxHeight;

                // Top bar
                var barTop = new Rectangle
                {
                    Width = barWidth - 2,
                    Height = barHeight,
                    Fill = brush,
                    Opacity = 0.8
                };
                Canvas.SetLeft(barTop, i * barWidth + 1);
                Canvas.SetTop(barTop, centerY - barHeight);
                canvas.Children.Add(barTop);

                // Bottom bar (mirrored)
                var barBottom = new Rectangle
                {
                    Width = barWidth - 2,
                    Height = barHeight,
                    Fill = brush,
                    Opacity = 0.8
                };
                Canvas.SetLeft(barBottom, i * barWidth + 1);
                Canvas.SetTop(barBottom, centerY);
                canvas.Children.Add(barBottom);
            }
        }

        private void RenderWaveform(Canvas canvas)
        {
            if (_spectrumData.Length < 2) return;

            var polyline = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0, 255, 127)),
                StrokeThickness = 2,
                Opacity = 0.9
            };

            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double centerY = height / 2;
            double pointSpacing = width / _spectrumData.Length;

            for (int i = 0; i < _spectrumData.Length; i++)
            {
                double x = i * pointSpacing;
                double y = centerY - (_spectrumData[i] * centerY * 2);
                polyline.Points.Add(new Point(x, y));
            }

            canvas.Children.Add(polyline);
        }

        private void RenderCircular(Canvas canvas)
        {
            double centerX = canvas.ActualWidth / 2;
            double centerY = canvas.ActualHeight / 2;
            double radius = Math.Min(centerX, centerY) - 20;

            if (_spectrumData.Length == 0) return;

            for (int i = 0; i < _spectrumData.Length; i++)
            {
                double angle = (i / (double)_spectrumData.Length) * Math.PI * 2;
                double value = Math.Min(1.0, _spectrumData[i] * 2);
                double barRadius = radius * value;

                double startX = centerX + Math.Cos(angle) * radius;
                double startY = centerY + Math.Sin(angle) * radius;
                double endX = centerX + Math.Cos(angle) * (radius - barRadius);
                double endY = centerY + Math.Sin(angle) * (radius - barRadius);

                var line = new Line
                {
                    X1 = startX,
                    Y1 = startY,
                    X2 = endX,
                    Y2 = endY,
                    Stroke = new SolidColorBrush(Color.FromRgb((byte)(30 + (int)(value * 225)), 144, 255)),
                    StrokeThickness = 3,
                    Opacity = 0.8
                };

                canvas.Children.Add(line);
            }
        }
    }
}
