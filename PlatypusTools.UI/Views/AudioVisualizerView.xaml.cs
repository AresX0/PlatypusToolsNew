using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Color scheme for the visualizer
    /// </summary>
    public enum VisualizerColorScheme
    {
        BlueGreen,      // Default blue to green gradient
        Rainbow,        // Full rainbow spectrum
        Fire,           // Red, orange, yellow
        Purple,         // Purple to pink
        Neon,           // Bright neon colors
        Ocean,          // Deep blues and teals
        Sunset,         // Orange, pink, purple
        Monochrome      // White/gray
    }
    
    /// <summary>
    /// Native audio visualizer with spectrum bars and waveform rendering.
    /// Supports multiple visualization modes: Bars, Mirror, Waveform, Circular.
    /// Also supports advanced modes: Radial Spectrum, Particle Field, Aurora, Wave Grid.
    /// </summary>
    public partial class AudioVisualizerView : UserControl
    {
        private double[] _spectrumData = new double[128]; // Always have data
        private double[] _smoothedData = new double[128]; // Smoothed for display
        private DispatcherTimer? _renderTimer;
        private int _barCount = 72;
        private string _visualizationMode = "Bars"; // Bars, Mirror, Waveform, Circular, Radial, Particles, Aurora, WaveGrid
        private bool _hasExternalData = false;
        private DateTime _lastExternalUpdate = DateTime.MinValue;
        private static readonly Random _random = new Random();
        private double _animationPhase = 0; // For smooth idle animation
        private bool _timerStarted = false;
        private bool _subscribedToService = false;
        
        // Color scheme
        private VisualizerColorScheme _colorScheme = VisualizerColorScheme.BlueGreen;
        
        // For advanced visualizations
        private readonly List<Particle> _particles = new();
        private double _auroraPhase = 0;
        private double[] _previousSmoothed = new double[128];

        public AudioVisualizerView()
        {
            InitializeComponent();
            
            // Initialize with default data
            for (int i = 0; i < _spectrumData.Length; i++)
            {
                _spectrumData[i] = 0.1;
                _smoothedData[i] = 0.1;
                _previousSmoothed[i] = 0.1;
            }
            
            // Initialize particles for particle field mode
            for (int i = 0; i < 100; i++)
            {
                _particles.Add(new Particle
                {
                    X = _random.NextDouble(),
                    Y = _random.NextDouble(),
                    VelocityX = (_random.NextDouble() - 0.5) * 0.02,
                    VelocityY = (_random.NextDouble() - 0.5) * 0.02,
                    Size = 2 + _random.NextDouble() * 4,
                    Hue = _random.NextDouble() * 360
                });
            }
            
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnSizeChanged;
            IsVisibleChanged += OnIsVisibleChanged;
            
            // Subscribe immediately in constructor as backup
            SubscribeToService();
            StartRenderTimer();
        }
        
        /// <summary>
        /// Subscribes to the audio service for spectrum data.
        /// </summary>
        private void SubscribeToService()
        {
            if (!_subscribedToService)
            {
                try
                {
                    PlatypusTools.UI.Services.AudioPlayerService.Instance.SpectrumDataUpdated += OnSpectrumDataFromService;
                    _subscribedToService = true;
                    System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Subscribed to SpectrumDataUpdated (from constructor/init)");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioVisualizerView: Failed to subscribe: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Starts the render timer if not already started.
        /// </summary>
        private void StartRenderTimer()
        {
            if (!_timerStarted)
            {
                _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
                _renderTimer.Tick += OnRenderTick;
                _renderTimer.Start();
                _timerStarted = true;
                System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Render timer started (from constructor/init)");
            }
        }
        
        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue)
            {
                // When becoming visible, ensure we're subscribed and running
                SubscribeToService();
                StartRenderTimer();
                System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Became visible, ensuring subscription");
            }
        }
        
        /// <summary>
        /// Gets or sets the color scheme for the visualizer.
        /// </summary>
        public VisualizerColorScheme ColorScheme
        {
            get => _colorScheme;
            set
            {
                _colorScheme = value;
                System.Diagnostics.Debug.WriteLine($"AudioVisualizerView: ColorScheme set to {value}");
            }
        }
        
        /// <summary>
        /// Sets the color scheme by index (for ComboBox binding).
        /// </summary>
        public void SetColorScheme(int index)
        {
            if (Enum.IsDefined(typeof(VisualizerColorScheme), index))
            {
                _colorScheme = (VisualizerColorScheme)index;
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine($"AudioVisualizerView.OnLoaded called, ActualWidth={ActualWidth}, ActualHeight={ActualHeight}");
            
            // Ensure subscription and timer are running
            SubscribeToService();
            StartRenderTimer();
        }
        
        private void OnSpectrumDataFromService(object? sender, double[] data)
        {
            // Update spectrum data on UI thread
            Dispatcher.BeginInvoke(() =>
            {
                if (data != null && data.Length > 0)
                {
                    _spectrumData = ResampleSpectrum(data, _barCount);
                    _hasExternalData = true;
                    _lastExternalUpdate = DateTime.Now;
                }
            });
        }
        
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe from service when unloaded
            if (_subscribedToService)
            {
                try
                {
                    PlatypusTools.UI.Services.AudioPlayerService.Instance.SpectrumDataUpdated -= OnSpectrumDataFromService;
                    _subscribedToService = false;
                    System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Unsubscribed from SpectrumDataUpdated");
                }
                catch { }
            }
            System.Diagnostics.Debug.WriteLine("AudioVisualizerView.OnUnloaded");
        }
        
        private void OnRenderTick(object? sender, EventArgs e)
        {
            RenderVisualization();
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Redraw when canvas resizes
            RenderVisualization();
        }

        public void UpdateSpectrumData(double[] spectrumData, string mode = "Bars", int barCount = 72)
        {
            // Always update mode and bar count
            _visualizationMode = mode;
            _barCount = Math.Max(barCount, 8);
            
            if (spectrumData != null && spectrumData.Length > 0)
            {
                // Resample input data to match our bar count
                _spectrumData = ResampleSpectrum(spectrumData, _barCount);
                _hasExternalData = true;
                _lastExternalUpdate = DateTime.Now;
            }
            
            System.Diagnostics.Debug.WriteLine($"UpdateSpectrumData: mode={mode}, barCount={barCount}, hasData={spectrumData?.Length ?? 0}");
        }
        
        /// <summary>
        /// Updates spectrum data with color scheme.
        /// </summary>
        public void UpdateSpectrumData(double[] spectrumData, string mode, int barCount, int colorSchemeIndex)
        {
            SetColorScheme(colorSchemeIndex);
            UpdateSpectrumData(spectrumData, mode, barCount);
        }
        
        /// <summary>
        /// Gets the gradient brush for the current color scheme.
        /// </summary>
        private Brush GetColorSchemeBrush(bool vertical = true)
        {
            GradientStopCollection stops = _colorScheme switch
            {
                VisualizerColorScheme.Rainbow => new GradientStopCollection
                {
                    new GradientStop(Colors.Red, 0),
                    new GradientStop(Colors.Orange, 0.17),
                    new GradientStop(Colors.Yellow, 0.33),
                    new GradientStop(Colors.Lime, 0.5),
                    new GradientStop(Colors.Cyan, 0.67),
                    new GradientStop(Colors.Blue, 0.83),
                    new GradientStop(Colors.Magenta, 1)
                },
                VisualizerColorScheme.Fire => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(255, 50, 0), 0),
                    new GradientStop(Color.FromRgb(255, 150, 0), 0.5),
                    new GradientStop(Color.FromRgb(255, 255, 50), 1)
                },
                VisualizerColorScheme.Purple => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(75, 0, 130), 0),
                    new GradientStop(Color.FromRgb(148, 0, 211), 0.5),
                    new GradientStop(Color.FromRgb(255, 105, 180), 1)
                },
                VisualizerColorScheme.Neon => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0, 255, 255), 0),
                    new GradientStop(Color.FromRgb(255, 0, 255), 0.5),
                    new GradientStop(Color.FromRgb(0, 255, 128), 1)
                },
                VisualizerColorScheme.Ocean => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0, 30, 60), 0),
                    new GradientStop(Color.FromRgb(0, 100, 150), 0.5),
                    new GradientStop(Color.FromRgb(0, 200, 200), 1)
                },
                VisualizerColorScheme.Sunset => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(255, 100, 0), 0),
                    new GradientStop(Color.FromRgb(255, 50, 100), 0.5),
                    new GradientStop(Color.FromRgb(150, 0, 150), 1)
                },
                VisualizerColorScheme.Monochrome => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(100, 100, 100), 0),
                    new GradientStop(Color.FromRgb(200, 200, 200), 0.5),
                    new GradientStop(Colors.White, 1)
                },
                _ => new GradientStopCollection // BlueGreen (default)
                {
                    new GradientStop(Color.FromRgb(30, 144, 255), 0),
                    new GradientStop(Color.FromRgb(0, 255, 127), 1)
                }
            };
            
            return new LinearGradientBrush
            {
                StartPoint = vertical ? new Point(0, 1) : new Point(0, 0),
                EndPoint = vertical ? new Point(0, 0) : new Point(1, 0),
                GradientStops = stops
            };
        }
        
        /// <summary>
        /// Gets a color from the current scheme based on a value (0-1).
        /// </summary>
        private Color GetColorFromScheme(double value)
        {
            value = Math.Clamp(value, 0, 1);
            
            return _colorScheme switch
            {
                VisualizerColorScheme.Rainbow => HslToRgb(value * 300, 1, 0.5),
                VisualizerColorScheme.Fire => Color.FromRgb(255, (byte)(50 + value * 200), (byte)(value * 50)),
                VisualizerColorScheme.Purple => Color.FromRgb((byte)(75 + value * 180), (byte)(value * 105), (byte)(130 + value * 80)),
                VisualizerColorScheme.Neon => HslToRgb(180 + value * 120, 1, 0.6),
                VisualizerColorScheme.Ocean => Color.FromRgb(0, (byte)(30 + value * 170), (byte)(60 + value * 140)),
                VisualizerColorScheme.Sunset => Color.FromRgb(255, (byte)(100 - value * 50), (byte)(value * 150)),
                VisualizerColorScheme.Monochrome => Color.FromRgb((byte)(100 + value * 155), (byte)(100 + value * 155), (byte)(100 + value * 155)),
                _ => Color.FromRgb((byte)(30 + value * 0), (byte)(144 + value * 111), (byte)(255 - value * 128))
            };
        }
        
        private double[] ResampleSpectrum(double[] source, int targetLength)
        {
            var result = new double[targetLength];
            double step = (double)source.Length / targetLength;
            for (int i = 0; i < targetLength; i++)
            {
                int srcIdx = Math.Min((int)(i * step), source.Length - 1);
                result[i] = source[srcIdx];
            }
            return result;
        }

        private void RenderVisualization()
        {
            var canvas = VisualizerCanvas;
            if (canvas == null || canvas.ActualWidth < 1 || canvas.ActualHeight < 1) return;

            canvas.Children.Clear();

            // Draw background
            var bg = new Rectangle
            {
                Width = canvas.ActualWidth,
                Height = canvas.ActualHeight,
                Fill = new SolidColorBrush(Color.FromRgb(10, 14, 39))
            };
            canvas.Children.Add(bg);

            // Advance animation phase
            _animationPhase += 0.05;

            // If no external data for 300ms, generate animated idle data
            if (!_hasExternalData || (DateTime.Now - _lastExternalUpdate).TotalMilliseconds > 300)
            {
                GenerateIdleAnimation();
            }

            // Apply smoothing for fluid animation
            ApplySmoothing();

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
                case "Radial":
                    RenderRadialSpectrum(canvas);
                    break;
                case "Particles":
                    RenderParticleField(canvas);
                    break;
                case "Aurora":
                    RenderAurora(canvas);
                    break;
                case "WaveGrid":
                    RenderWaveGrid(canvas);
                    break;
                default: // Bars
                    RenderBars(canvas);
                    break;
            }
        }
        
        private void ApplySmoothing()
        {
            // Ensure smoothed array matches current bar count
            if (_smoothedData.Length != _barCount)
            {
                _smoothedData = new double[_barCount];
            }
            
            // Expand or resample spectrum data if needed
            if (_spectrumData.Length != _barCount)
            {
                _spectrumData = ResampleSpectrum(_spectrumData, _barCount);
            }
            
            // Smooth transitions (lerp toward target)
            const double smoothFactor = 0.25;
            for (int i = 0; i < _barCount; i++)
            {
                double target = i < _spectrumData.Length ? _spectrumData[i] : 0.1;
                _smoothedData[i] += (target - _smoothedData[i]) * smoothFactor;
            }
        }

        private void RenderBars(Canvas canvas)
        {
            int numBars = _barCount;
            if (numBars == 0) numBars = 72;
            
            double barWidth = canvas.ActualWidth / numBars;
            double maxHeight = canvas.ActualHeight;

            var brush = GetColorSchemeBrush(true);

            for (int i = 0; i < numBars && i < _smoothedData.Length; i++)
            {
                double value = Math.Min(1.0, _smoothedData[i] * 2); // Clamp and amplify
                double barHeight = Math.Max(4, value * maxHeight); // Minimum height of 4px

                var bar = new Rectangle
                {
                    Width = Math.Max(2, barWidth - 2),
                    Height = barHeight,
                    Fill = brush,
                    Opacity = 0.85,
                    RadiusX = 2,
                    RadiusY = 2
                };

                Canvas.SetLeft(bar, i * barWidth + 1);
                Canvas.SetTop(bar, maxHeight - barHeight);
                canvas.Children.Add(bar);
            }
        }

        private void RenderMirrorBars(Canvas canvas)
        {
            int numBars = _barCount;
            if (numBars == 0) numBars = 72;
            
            double barWidth = canvas.ActualWidth / numBars;
            double maxHeight = canvas.ActualHeight / 2;

            var brush = GetColorSchemeBrush(false);

            double centerY = canvas.ActualHeight / 2;

            for (int i = 0; i < numBars && i < _smoothedData.Length; i++)
            {
                double value = Math.Min(1.0, _smoothedData[i] * 2);
                double barHeight = Math.Max(2, value * maxHeight);

                // Top bar
                var barTop = new Rectangle
                {
                    Width = Math.Max(2, barWidth - 2),
                    Height = barHeight,
                    Fill = brush,
                    Opacity = 0.85,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(barTop, i * barWidth + 1);
                Canvas.SetTop(barTop, centerY - barHeight);
                canvas.Children.Add(barTop);

                // Bottom bar (mirrored)
                var barBottom = new Rectangle
                {
                    Width = Math.Max(2, barWidth - 2),
                    Height = barHeight,
                    Fill = brush,
                    Opacity = 0.85,
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(barBottom, i * barWidth + 1);
                Canvas.SetTop(barBottom, centerY);
                canvas.Children.Add(barBottom);
            }
        }

        private void RenderWaveform(Canvas canvas)
        {
            if (_smoothedData.Length < 2) return;

            var polyline = new Polyline
            {
                Stroke = GetColorSchemeBrush(false),
                StrokeThickness = 3,
                Opacity = 0.9
            };

            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double centerY = height / 2;
            double pointSpacing = width / _smoothedData.Length;

            for (int i = 0; i < _smoothedData.Length; i++)
            {
                double x = i * pointSpacing;
                double y = centerY - (_smoothedData[i] * centerY * 1.5);
                polyline.Points.Add(new Point(x, y));
            }

            canvas.Children.Add(polyline);
        }

        private void RenderCircular(Canvas canvas)
        {
            double centerX = canvas.ActualWidth / 2;
            double centerY = canvas.ActualHeight / 2;
            double radius = Math.Min(centerX, centerY) - 20;

            if (_smoothedData.Length == 0) return;

            for (int i = 0; i < _smoothedData.Length; i++)
            {
                double angle = (i / (double)_smoothedData.Length) * Math.PI * 2 - Math.PI / 2;
                double value = Math.Min(1.0, _smoothedData[i] * 2);
                double barRadius = radius * value * 0.6;

                double startX = centerX + Math.Cos(angle) * (radius * 0.3);
                double startY = centerY + Math.Sin(angle) * (radius * 0.3);
                double endX = centerX + Math.Cos(angle) * (radius * 0.3 + barRadius);
                double endY = centerY + Math.Sin(angle) * (radius * 0.3 + barRadius);

                var color = GetColorFromScheme(value);
                var line = new Line
                {
                    X1 = startX,
                    Y1 = startY,
                    X2 = endX,
                    Y2 = endY,
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = Math.Max(3, canvas.ActualWidth / _smoothedData.Length * 0.8),
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Opacity = 0.85
                };

                canvas.Children.Add(line);
            }
        }
        
        private void GenerateIdleAnimation()
        {
            // Ensure we have enough data for the bar count
            if (_spectrumData.Length != _barCount)
            {
                _spectrumData = new double[_barCount];
            }
            
            // Generate dynamic animated spectrum data
            for (int i = 0; i < _barCount; i++)
            {
                // Multiple sine waves at different frequencies for organic movement
                double wave1 = Math.Sin(_animationPhase * 1.2 + i * 0.15) * 0.25;
                double wave2 = Math.Sin(_animationPhase * 0.7 + i * 0.25) * 0.15;
                double wave3 = Math.Sin(_animationPhase * 2.0 + i * 0.1) * 0.1;
                
                // Bass frequencies (left side) are higher
                double bassBias = Math.Max(0, 1.0 - (i / (double)_barCount) * 1.5) * 0.2;
                
                // Random variation
                double noise = _random.NextDouble() * 0.08;
                
                // Combine all factors
                double value = 0.25 + wave1 + wave2 + wave3 + bassBias + noise;
                _spectrumData[i] = Math.Clamp(value, 0.05, 0.85);
            }
        }
        
        // ===== ADVANCED VISUALIZATION MODES =====
        
        private void RenderRadialSpectrum(Canvas canvas)
        {
            double centerX = canvas.ActualWidth / 2;
            double centerY = canvas.ActualHeight / 2;
            double maxRadius = Math.Min(centerX, centerY) - 10;

            if (_smoothedData.Length == 0) return;

            // Draw multiple rings with frequency data
            int rings = 4;
            for (int ring = 0; ring < rings; ring++)
            {
                double ringRadius = maxRadius * (0.3 + (ring * 0.2));
                double ringOpacity = 1.0 - (ring * 0.2);
                
                for (int i = 0; i < _smoothedData.Length; i++)
                {
                    double angle = (i / (double)_smoothedData.Length) * Math.PI * 2 - Math.PI / 2;
                    double nextAngle = ((i + 1) / (double)_smoothedData.Length) * Math.PI * 2 - Math.PI / 2;
                    
                    int dataIndex = (i + ring * 5) % _smoothedData.Length;
                    double value = Math.Min(1.0, _smoothedData[dataIndex] * 2.5);
                    
                    double innerRadius = ringRadius - 5;
                    double outerRadius = ringRadius + value * 30;
                    
                    // Draw arc segment
                    var path = new Path();
                    var geometry = new PathGeometry();
                    var figure = new PathFigure
                    {
                        StartPoint = new Point(
                            centerX + Math.Cos(angle) * innerRadius,
                            centerY + Math.Sin(angle) * innerRadius
                        )
                    };
                    
                    figure.Segments.Add(new LineSegment(new Point(
                        centerX + Math.Cos(angle) * outerRadius,
                        centerY + Math.Sin(angle) * outerRadius
                    ), true));
                    
                    figure.Segments.Add(new ArcSegment(
                        new Point(
                            centerX + Math.Cos(nextAngle) * outerRadius,
                            centerY + Math.Sin(nextAngle) * outerRadius
                        ),
                        new Size(outerRadius, outerRadius),
                        0, false, SweepDirection.Clockwise, true
                    ));
                    
                    figure.Segments.Add(new LineSegment(new Point(
                        centerX + Math.Cos(nextAngle) * innerRadius,
                        centerY + Math.Sin(nextAngle) * innerRadius
                    ), true));
                    
                    geometry.Figures.Add(figure);
                    path.Data = geometry;
                    
                    // Color based on color scheme and value
                    var color = GetColorFromScheme(value);
                    path.Fill = new SolidColorBrush(Color.FromArgb(
                        (byte)(ringOpacity * 200),
                        color.R,
                        color.G,
                        color.B
                    ));
                    
                    canvas.Children.Add(path);
                }
            }
        }
        
        private void RenderParticleField(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            
            // Calculate average intensity
            double avgIntensity = 0;
            for (int i = 0; i < Math.Min(20, _smoothedData.Length); i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Min(20, _smoothedData.Length);
            
            // Update and render particles
            int particleIndex = 0;
            foreach (var particle in _particles)
            {
                // Move particle
                particle.X += particle.VelocityX * (1 + avgIntensity * 3);
                particle.Y += particle.VelocityY * (1 + avgIntensity * 3);
                
                // Wrap around
                if (particle.X < 0) particle.X = 1;
                if (particle.X > 1) particle.X = 0;
                if (particle.Y < 0) particle.Y = 1;
                if (particle.Y > 1) particle.Y = 0;
                
                // Size pulsates with music
                double size = particle.Size * (1 + avgIntensity * 2);
                
                // Use color scheme instead of hue rotation
                double colorValue = (particleIndex / (double)_particles.Count + avgIntensity) % 1.0;
                var color = GetColorFromScheme(colorValue);
                
                var ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(200, color.R, color.G, color.B), 0),
                            new GradientStop(Color.FromArgb(0, color.R, color.G, color.B), 1)
                        }
                    }
                };
                
                Canvas.SetLeft(ellipse, particle.X * width - size / 2);
                Canvas.SetTop(ellipse, particle.Y * height - size / 2);
                canvas.Children.Add(ellipse);
                particleIndex++;
            }
            
            // Draw connecting lines between nearby particles using color scheme
            var lineColor = GetColorFromScheme(0.5 + avgIntensity * 0.5);
            for (int i = 0; i < _particles.Count; i++)
            {
                for (int j = i + 1; j < _particles.Count; j++)
                {
                    double dx = _particles[i].X - _particles[j].X;
                    double dy = _particles[i].Y - _particles[j].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    
                    if (dist < 0.15)
                    {
                        var line = new Line
                        {
                            X1 = _particles[i].X * width,
                            Y1 = _particles[i].Y * height,
                            X2 = _particles[j].X * width,
                            Y2 = _particles[j].Y * height,
                            Stroke = new SolidColorBrush(Color.FromArgb(
                                (byte)((1 - dist / 0.15) * 100 * (1 + avgIntensity)),
                                lineColor.R, lineColor.G, lineColor.B
                            )),
                            StrokeThickness = 1
                        };
                        canvas.Children.Add(line);
                    }
                }
            }
        }
        
        private void RenderAurora(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            
            _auroraPhase += 0.02;
            
            // Draw multiple aurora layers
            int layers = 5;
            for (int layer = 0; layer < layers; layer++)
            {
                var path = new Path();
                var geometry = new PathGeometry();
                var figure = new PathFigure { StartPoint = new Point(0, height) };
                
                double layerOffset = layer * 0.15;
                double layerY = height * (0.3 + layerOffset);
                
                // Create flowing curve using spectrum data
                for (int i = 0; i <= _smoothedData.Length; i++)
                {
                    double x = (i / (double)_smoothedData.Length) * width;
                    int dataIdx = Math.Min(i, _smoothedData.Length - 1);
                    double value = _smoothedData[dataIdx];
                    
                    // Add flowing wave effect
                    double wave = Math.Sin(_auroraPhase + i * 0.1 + layer * 0.5) * 20;
                    double y = layerY - (value * height * 0.4) + wave;
                    
                    figure.Segments.Add(new LineSegment(new Point(x, y), true));
                }
                
                figure.Segments.Add(new LineSegment(new Point(width, height), true));
                figure.Segments.Add(new LineSegment(new Point(0, height), true));
                geometry.Figures.Add(figure);
                path.Data = geometry;
                
                // Gradient fill using color scheme
                var baseColor = GetColorFromScheme(0.5 + layer * 0.1);
                var topColor = GetColorFromScheme(0.8 + layer * 0.04);
                path.Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb((byte)(150 - layer * 20), baseColor.R, baseColor.G, baseColor.B), 0),
                        new GradientStop(Color.FromArgb((byte)(100 - layer * 15), topColor.R, topColor.G, topColor.B), 0.5),
                        new GradientStop(Color.FromArgb(0, topColor.R, topColor.G, topColor.B), 1)
                    }
                };
                
                canvas.Children.Add(path);
            }
        }
        
        private void RenderWaveGrid(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            
            int gridX = 20;
            int gridY = 10;
            double cellWidth = width / gridX;
            double cellHeight = height / gridY;
            
            // Draw 3D-like wave grid
            for (int y = 0; y < gridY; y++)
            {
                for (int x = 0; x < gridX; x++)
                {
                    int dataIdx = (x * _smoothedData.Length / gridX) % _smoothedData.Length;
                    double value = _smoothedData[dataIdx];
                    
                    // Calculate perspective offset
                    double perspective = 1.0 - (y / (double)gridY) * 0.5;
                    double xOffset = (x - gridX / 2) * (1 - perspective) * 10;
                    
                    // Wave height based on data and position
                    double wavePhase = _animationPhase + x * 0.2 + y * 0.3;
                    double waveHeight = value * 30 * perspective + Math.Sin(wavePhase) * 5;
                    
                    double px = x * cellWidth + xOffset;
                    double py = y * cellHeight - waveHeight;
                    
                    // Draw cell
                    var color = GetColorFromScheme(value);
                    var rect = new Rectangle
                    {
                        Width = cellWidth * perspective,
                        Height = cellHeight * perspective * 0.8,
                        Fill = new SolidColorBrush(Color.FromArgb(
                            (byte)(150 + value * 100),
                            color.R,
                            color.G,
                            color.B
                        )),
                        RadiusX = 2,
                        RadiusY = 2
                    };
                    
                    Canvas.SetLeft(rect, px);
                    Canvas.SetTop(rect, py);
                    canvas.Children.Add(rect);
                }
            }
        }
        
        // Helper method to convert HSL to RGB
        private Color HslToRgb(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs((h / 60) % 2 - 1));
            double m = l - c / 2;
            
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            
            return Color.FromRgb(
                (byte)((r + m) * 255),
                (byte)((g + m) * 255),
                (byte)((b + m) * 255)
            );
        }
    }
    
    /// <summary>
    /// Represents a particle for the particle field visualization.
    /// </summary>
    internal class Particle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double VelocityX { get; set; }
        public double VelocityY { get; set; }
        public double Size { get; set; }
        public double Hue { get; set; }
    }
}
