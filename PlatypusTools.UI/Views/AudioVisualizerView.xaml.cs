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
        Monochrome,     // White/gray
        PipBoy,         // Fallout Pip-Boy green phosphor
        LCARS           // Star Trek LCARS orange/tan/purple
    }
    
    /// <summary>
    /// Native audio visualizer with spectrum bars and waveform rendering.
    /// Supports multiple visualization modes: Bars, Mirror, Waveform, Circular.
    /// Also supports advanced modes: Radial Spectrum, Particle Field, Aurora, Wave Grid.
    /// After Dark modes: Starfield, Flying Toasters.
    /// Features HD quality: logarithmic frequency mapping, smooth rise/fall, peak hold.
    /// </summary>
    public partial class AudioVisualizerView : UserControl
    {
        private double[] _spectrumData = new double[64]; // Match service band count
        private double[] _smoothedData = new double[64]; // Smoothed for display
        private double[] _peakHeights = new double[64]; // Peak hold heights
        private double[] _barHeights = new double[64]; // Current bar heights for smooth animation
        private DispatcherTimer? _renderTimer;
        private int _barCount = 64; // Reduced for performance (was 128)
        private string _visualizationMode = "Bars"; // Bars, Mirror, Waveform, Circular, Radial, Particles, Aurora, WaveGrid, Starfield, Toasters
        private bool _hasExternalData = false;
        private DateTime _lastExternalUpdate = DateTime.MinValue;
        private static readonly Random _random = new Random();
        private double _animationPhase = 0; // For smooth idle animation
        private bool _subscribedToService = false;
        private bool _isMusicPlaying = false; // Track if music is actually playing
        
        // HD quality settings - tuned for smoothness with sensitivity control
        private const double PEAK_FALL_SPEED = 0.02; // How fast peaks fall (per frame) - slightly faster
        private double _sensitivity = 1.0; // User-adjustable sensitivity (0.1-3.0)
        private double _riseSpeed = 0.5; // Attack speed for smooth animation - faster
        private double _fallSpeed = 0.1; // Decay speed for smooth animation - faster
        private const bool USE_LOGARITHMIC_FREQ = true; // Logarithmic frequency mapping
        
        // Color scheme
        private VisualizerColorScheme _colorScheme = VisualizerColorScheme.BlueGreen;
        
        // Performance: Cached UI elements to avoid recreating every frame
        private Rectangle? _cachedBackground;
        private readonly List<Rectangle> _cachedBars = new();
        private readonly List<Rectangle> _cachedPeaks = new();
        private string _lastMode = "";
        private int _lastBarCount = 0;
        private bool _needsFullRebuild = true;
        
        // Performance: Pre-cached brushes
        private Brush? _cachedBarBrush;
        private Brush? _cachedPeakBrush;
        private VisualizerColorScheme _lastColorScheme = VisualizerColorScheme.BlueGreen;
        
        // Performance: Frame skipping
        private int _frameCount = 0;
        private bool _isRendering = false;
        
        // Performance settings (adjustable)
        private int _maxStars = 400; // More stars for better effect
        private int _maxParticles = 100;
        private int _maxBarCount = 64;
        private int _targetFps = 22; // Target frame rate
        private double _cpuThrottlePercent = 0.5; // Max CPU usage (0.1-1.0)
        private long _maxMemoryBytes = 50 * 1024 * 1024; // 50MB max for visualizer
        
        // After Dark: Starfield
        private readonly List<Star> _stars = new();
        
        // After Dark: Flying Toasters
        private readonly List<Toaster> _toasters = new();
        
        // Matrix: Digital Rain
        private readonly List<MatrixColumn> _matrixColumns = new();
        private const string MatrixChars = "アイウエオカキクケコサシスセソタチツテトナニヌネノハヒフヘホマミムメモヤユヨラリルレロワヲン0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ@#$%^&*";
        
        // For advanced visualizations
        private readonly List<Particle> _particles = new();
        private double _auroraPhase = 0;
        private double[] _previousSmoothed = new double[64]; // Match bar count

        public AudioVisualizerView()
        {
            InitializeComponent();
            
            // Initialize with default data including peak heights
            for (int i = 0; i < _spectrumData.Length; i++)
            {
                _spectrumData[i] = 0.1;
                _smoothedData[i] = 0.1;
                _previousSmoothed[i] = 0.1;
                _peakHeights[i] = 0;
                _barHeights[i] = 0;
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
            
            // Initialize stars for starfield mode (After Dark style) - more, smaller stars
            for (int i = 0; i < 400; i++)
            {
                _stars.Add(new Star
                {
                    X = _random.NextDouble() - 0.5,
                    Y = _random.NextDouble() - 0.5,
                    Z = _random.NextDouble() * 4 + 0.1,
                    Speed = 0.005 + _random.NextDouble() * 0.02 // Slower base speed
                });
            }
            
            // Initialize flying toasters (After Dark style)
            for (int i = 0; i < 8; i++)
            {
                _toasters.Add(new Toaster
                {
                    X = _random.NextDouble(),
                    Y = _random.NextDouble(),
                    Speed = 0.002 + _random.NextDouble() * 0.003,
                    WingPhase = _random.NextDouble() * Math.PI * 2,
                    Size = 30 + _random.NextDouble() * 20
                });
            }
            
            // Initialize matrix columns (digital rain)
            for (int i = 0; i < 60; i++)
            {
                _matrixColumns.Add(new MatrixColumn
                {
                    X = i / 60.0,
                    Y = _random.NextDouble() * -1.0, // Start above screen
                    Speed = 0.01 + _random.NextDouble() * 0.02,
                    Length = 8 + _random.Next(15),
                    Characters = new char[25]
                });
                // Initialize with random characters
                for (int j = 0; j < _matrixColumns[i].Characters.Length; j++)
                {
                    _matrixColumns[i].Characters[j] = MatrixChars[_random.Next(MatrixChars.Length)];
                }
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
        /// Subscribes to EnhancedAudioPlayerService for spectrum data and playback state.
        /// </summary>
        private void SubscribeToService()
        {
            if (!_subscribedToService)
            {
                try
                {
                    // Subscribe to EnhancedAudioPlayerService for spectrum data and playback state
                    var service = PlatypusTools.UI.Services.EnhancedAudioPlayerService.Instance;
                    service.SpectrumDataUpdated += OnSpectrumDataFromEnhancedService;
                    service.PlaybackStateChanged += OnPlaybackStateChanged;
                    
                    // Get initial playback state
                    _isMusicPlaying = service.IsPlaying;
                    
                    _subscribedToService = true;
                    System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Subscribed to EnhancedAudioPlayerService");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"AudioVisualizerView: Failed to subscribe: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Handler for playback state changes - stop visualizer when not playing.
        /// </summary>
        private void OnPlaybackStateChanged(object? sender, bool isPlaying)
        {
            Dispatcher.BeginInvoke(() =>
            {
                _isMusicPlaying = isPlaying;
                if (!isPlaying)
                {
                    // Clear spectrum data when not playing
                    _hasExternalData = false;
                }
            });
        }
        
        /// <summary>
        /// Handler for EnhancedAudioPlayerService spectrum data (float[]).
        /// </summary>
        private void OnSpectrumDataFromEnhancedService(object? sender, float[] data)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (data != null && data.Length > 0)
                {
                    // Convert float[] to double[]
                    var doubleData = new double[data.Length];
                    for (int i = 0; i < data.Length; i++)
                        doubleData[i] = data[i];
                    
                    _spectrumData = ResampleSpectrum(doubleData, _barCount);
                    _hasExternalData = true;
                    _lastExternalUpdate = DateTime.Now;
                }
            });
        }
        
        /// <summary>
        /// Starts the render timer if not already started.
        /// </summary>
        private void StartRenderTimer()
        {
            // Check if timer exists and is running
            if (_renderTimer != null && _renderTimer.IsEnabled)
            {
                return; // Timer is already running
            }
            
            // Stop and clean up existing timer if any
            if (_renderTimer != null)
            {
                _renderTimer.Stop();
                _renderTimer.Tick -= OnRenderTick;
            }
            
            // Create and start new timer - ~22 FPS for balanced performance
            _renderTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(45) };
            _renderTimer.Tick += OnRenderTick;
            _renderTimer.Start();
            System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Render timer started (22 FPS)");
        }
        
        /// <summary>
        /// Resets the visualizer for a new track. Call this when track changes.
        /// </summary>
        public void Reset()
        {
            Dispatcher.BeginInvoke(() =>
            {
                // Clear spectrum data
                for (int i = 0; i < _spectrumData.Length; i++)
                {
                    _spectrumData[i] = 0.1;
                    _smoothedData[i] = 0.1;
                    _previousSmoothed[i] = 0.1;
                }
                
                // Reset animation phase
                _animationPhase = 0;
                _auroraPhase = 0;
                _hasExternalData = false;
                _lastExternalUpdate = DateTime.MinValue;
                
                // Ensure timer is running
                StartRenderTimer();
                
                // Force immediate redraw
                InvalidateVisual();
                RenderVisualization();
                
                System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Reset for new track");
            });
        }
        
        /// <summary>
        /// Stops the render timer.
        /// </summary>
        private void StopRenderTimer()
        {
            if (_renderTimer != null)
            {
                _renderTimer.Stop();
                _renderTimer.Tick -= OnRenderTick;
                _renderTimer = null;
                System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Render timer stopped");
            }
        }
        
        private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                if ((bool)e.NewValue)
                {
                    // When becoming visible, ensure we're subscribed and running
                    SubscribeToService();
                    
                    // Use Dispatcher to ensure timer starts on UI thread after layout is complete
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                    {
                        StartRenderTimer();
                    });
                    
                    System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Became visible, ensuring subscription and timer");
                }
                // Don't stop the timer when invisible - let it keep running
                // This prevents the visualizer from freezing when tabs are switched
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioVisualizerView.OnIsVisibleChanged error: {ex.Message}");
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
        
        // DEPRECATED: Old AudioPlayerService handler - kept for reference
        // private void OnSpectrumDataFromService(object? sender, double[] data)
        // {
        //     // Update spectrum data on UI thread
        //     Dispatcher.BeginInvoke(() =>
        //     {
        //         if (data != null && data.Length > 0)
        //         {
        //             _spectrumData = ResampleSpectrum(data, _barCount);
        //             _hasExternalData = true;
        //             _lastExternalUpdate = DateTime.Now;
        //         }
        //     });
        // }
        
        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Stop the render timer
            StopRenderTimer();
            
            // Unsubscribe from service when unloaded
            if (_subscribedToService)
            {
                try
                {
                    var service = PlatypusTools.UI.Services.EnhancedAudioPlayerService.Instance;
                    service.SpectrumDataUpdated -= OnSpectrumDataFromEnhancedService;
                    service.PlaybackStateChanged -= OnPlaybackStateChanged;
                    _subscribedToService = false;
                    System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Unsubscribed from EnhancedAudioPlayerService");
                }
                catch { }
            }
            System.Diagnostics.Debug.WriteLine("AudioVisualizerView.OnUnloaded");
        }
        
        private void OnRenderTick(object? sender, EventArgs e)
        {
            // Skip if already rendering (prevents UI thread blocking)
            if (_isRendering)
                return;
                
            try
            {
                _isRendering = true;
                _frameCount++;
                RenderVisualization();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AudioVisualizerView.OnRenderTick error: {ex.Message}");
            }
            finally
            {
                _isRendering = false;
            }
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Mark for full rebuild on size change
            _needsFullRebuild = true;
            // Redraw when canvas resizes
            RenderVisualization();
        }

        /// <summary>
        /// Sets the visualizer sensitivity (0.1 = very smooth, 3.0 = very responsive).
        /// Also adjusts rise/fall speeds for optimal animation.
        /// </summary>
        public void SetSensitivity(double sensitivity)
        {
            _sensitivity = Math.Clamp(sensitivity, 0.1, 3.0);
            
            // Adjust rise/fall speeds based on sensitivity
            // Higher sensitivity = faster response
            _riseSpeed = 0.2 + (_sensitivity * 0.3); // 0.23 to 1.1
            _fallSpeed = 0.04 + (_sensitivity * 0.04); // 0.044 to 0.16
        }
        
        /// <summary>
        /// Gets the current sensitivity value.
        /// </summary>
        public double GetSensitivity() => _sensitivity;
        
        #region Performance Settings
        
        /// <summary>
        /// Sets the target frame rate for the visualizer (10-60 FPS).
        /// Lower values use less CPU.
        /// </summary>
        public void SetTargetFps(int fps)
        {
            _targetFps = Math.Clamp(fps, 10, 60);
            int intervalMs = 1000 / _targetFps;
            
            if (_renderTimer != null)
            {
                _renderTimer.Interval = TimeSpan.FromMilliseconds(intervalMs);
            }
            
            System.Diagnostics.Debug.WriteLine($"AudioVisualizerView: Target FPS set to {_targetFps} ({intervalMs}ms)");
        }
        
        /// <summary>
        /// Gets the current target FPS.
        /// </summary>
        public int GetTargetFps() => _targetFps;
        
        /// <summary>
        /// Sets the maximum number of bars to render (16-128).
        /// Lower values use less CPU and memory.
        /// </summary>
        public void SetMaxBarCount(int barCount)
        {
            _maxBarCount = Math.Clamp(barCount, 16, 128);
            _barCount = Math.Min(_barCount, _maxBarCount);
            _needsFullRebuild = true;
        }
        
        /// <summary>
        /// Gets the current max bar count.
        /// </summary>
        public int GetMaxBarCount() => _maxBarCount;
        
        /// <summary>
        /// Sets the CPU throttle percentage (0.1 = 10%, 1.0 = 100%).
        /// This affects animation smoothness vs CPU usage.
        /// </summary>
        public void SetCpuThrottle(double percent)
        {
            _cpuThrottlePercent = Math.Clamp(percent, 0.1, 1.0);
            
            // Adjust bar count based on CPU throttle
            int adjustedBars = (int)(_maxBarCount * _cpuThrottlePercent);
            _barCount = Math.Max(16, adjustedBars);
            _needsFullRebuild = true;
            
            System.Diagnostics.Debug.WriteLine($"AudioVisualizerView: CPU throttle set to {_cpuThrottlePercent:P0}, bars: {_barCount}");
        }
        
        /// <summary>
        /// Gets the current CPU throttle percentage.
        /// </summary>
        public double GetCpuThrottle() => _cpuThrottlePercent;
        
        /// <summary>
        /// Sets the maximum memory usage in MB (10-100 MB).
        /// This limits particles and other effects.
        /// </summary>
        public void SetMaxMemoryMB(int megabytes)
        {
            _maxMemoryBytes = Math.Clamp(megabytes, 10, 100) * 1024L * 1024L;
            
            // Adjust particle and star counts based on memory limit
            _maxParticles = (int)(megabytes * 2); // ~2 particles per MB
            _maxStars = (int)(megabytes * 8); // ~8 stars per MB
            
            // Trim existing collections if needed
            while (_particles.Count > _maxParticles)
                _particles.RemoveAt(_particles.Count - 1);
            while (_stars.Count > _maxStars)
                _stars.RemoveAt(_stars.Count - 1);
                
            System.Diagnostics.Debug.WriteLine($"AudioVisualizerView: Max memory set to {megabytes}MB, particles: {_maxParticles}, stars: {_maxStars}");
        }
        
        /// <summary>
        /// Gets the current max memory in MB.
        /// </summary>
        public int GetMaxMemoryMB() => (int)(_maxMemoryBytes / (1024 * 1024));
        
        /// <summary>
        /// Applies a performance preset.
        /// </summary>
        public void ApplyPerformancePreset(string preset)
        {
            switch (preset.ToLowerInvariant())
            {
                case "low":
                    SetTargetFps(15);
                    SetMaxBarCount(32);
                    SetCpuThrottle(0.3);
                    SetMaxMemoryMB(20);
                    break;
                case "medium":
                    SetTargetFps(22);
                    SetMaxBarCount(64);
                    SetCpuThrottle(0.5);
                    SetMaxMemoryMB(50);
                    break;
                case "high":
                    SetTargetFps(30);
                    SetMaxBarCount(96);
                    SetCpuThrottle(0.75);
                    SetMaxMemoryMB(75);
                    break;
                case "ultra":
                    SetTargetFps(60);
                    SetMaxBarCount(128);
                    SetCpuThrottle(1.0);
                    SetMaxMemoryMB(100);
                    break;
                default:
                    // Default to medium
                    ApplyPerformancePreset("medium");
                    break;
            }
        }
        
        #endregion

        public void UpdateSpectrumData(double[] spectrumData, string mode = "Bars", int barCount = 128)
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
        }
        
        /// <summary>
        /// Updates spectrum data with color scheme and sensitivity.
        /// </summary>
        public void UpdateSpectrumData(double[] spectrumData, string mode, int barCount, int colorSchemeIndex, double sensitivity = 0.7, int fps = 22)
        {
            SetColorScheme(colorSchemeIndex);
            SetSensitivity(sensitivity);
            
            // Update FPS if it changed
            if (fps != _targetFps)
            {
                SetTargetFps(fps);
            }
            
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
                VisualizerColorScheme.PipBoy => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(10, 85, 48), 0),      // Dark green
                    new GradientStop(Color.FromRgb(27, 255, 128), 0.5),  // Pip-Boy green
                    new GradientStop(Color.FromRgb(77, 255, 163), 1)     // Light green
                },
                VisualizerColorScheme.LCARS => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(85, 136, 255), 0),     // Classic Blue #5588FF
                    new GradientStop(Color.FromRgb(153, 204, 255), 0.25), // Ice #99CCFF
                    new GradientStop(Color.FromRgb(255, 204, 0), 0.5),    // Gold #FFCC00
                    new GradientStop(Color.FromRgb(255, 153, 102), 0.75), // Butterscotch #FF9966
                    new GradientStop(Color.FromRgb(204, 153, 255), 1)     // African Violet #CC99FF
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
                VisualizerColorScheme.PipBoy => Color.FromRgb((byte)(10 + value * 67), (byte)(85 + value * 170), (byte)(48 + value * 115)),
                VisualizerColorScheme.LCARS => value < 0.5 
                    ? Color.FromRgb((byte)(85 + value * 170), (byte)(136 + value * 68), (byte)(255 - value * 255))  // Blue to Gold
                    : Color.FromRgb((byte)(255 - value * 51), (byte)(204 - value * 51), (byte)(value * 255)), // Gold to Violet
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

            // Check if we need full rebuild (mode changed, size changed, bar count changed, color scheme changed)
            bool modeChanged = _lastMode != _visualizationMode;
            bool barCountChanged = _lastBarCount != _barCount;
            bool colorChanged = _lastColorScheme != _colorScheme;
            
            if (_needsFullRebuild || modeChanged || barCountChanged || colorChanged)
            {
                canvas.Children.Clear();
                _cachedBars.Clear();
                _cachedPeaks.Clear();
                _cachedBackground = null;
                _cachedBarBrush = null;
                _cachedPeakBrush = null;
                _cachedWaveformLine = null;
                _needsFullRebuild = false;
                _lastMode = _visualizationMode;
                _lastBarCount = _barCount;
                _lastColorScheme = _colorScheme;
            }

            // Draw/update background using theme color
            var bgBrush = TryFindResource("WindowBackgroundBrush") as SolidColorBrush 
                ?? new SolidColorBrush(Color.FromRgb(10, 14, 39));
            
            if (_cachedBackground == null)
            {
                _cachedBackground = new Rectangle
                {
                    Width = canvas.ActualWidth,
                    Height = canvas.ActualHeight,
                    Fill = bgBrush
                };
                canvas.Children.Add(_cachedBackground);
            }
            else
            {
                _cachedBackground.Width = canvas.ActualWidth;
                _cachedBackground.Height = canvas.ActualHeight;
                _cachedBackground.Fill = bgBrush;
            }

            // When music is not playing and no recent data, show idle state (decay bars)
            bool hasRecentData = _hasExternalData && (DateTime.Now - _lastExternalUpdate).TotalMilliseconds < 100;
            
            if (!_isMusicPlaying && !hasRecentData)
            {
                // Decay all bars to zero when not playing
                for (int i = 0; i < _barHeights.Length; i++)
                {
                    _barHeights[i] *= 0.92; // Smooth decay
                    _smoothedData[i] = _barHeights[i];
                    _peakHeights[i] *= 0.95;
                    if (_barHeights[i] < 0.001) _barHeights[i] = 0;
                    if (_peakHeights[i] < 0.001) _peakHeights[i] = 0;
                }
            }
            else
            {
                // Apply smoothing for fluid animation when playing
                ApplySmoothing();
            }

            // Advance animation phase (only for idle animations like starfield/toasters)
            _animationPhase += 0.05;

            // For dynamic modes, clear non-cached elements before each frame
            // This prevents elements from piling up and causing trails/artifacts
            bool isDynamicMode = _visualizationMode is "Starfield" or "Toasters" or "Particles" or "Aurora" or "WaveGrid" or "Wave Grid" or "Circular" or "Radial" or "Mirror";
            if (isDynamicMode)
            {
                ClearDynamicElements(canvas);
            }

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
                case "Wave Grid":
                    RenderWaveGrid(canvas);
                    break;
                case "Starfield":
                    RenderStarfield(canvas);
                    break;
                case "Toasters":
                    RenderFlyingToasters(canvas);
                    break;
                case "Matrix":
                    RenderMatrix(canvas);
                    break;
                default: // Bars
                    RenderBars(canvas);
                    break;
            }
        }
        
        /// <summary>
        /// Clears all non-cached elements from the canvas.
        /// Used by dynamic modes (Starfield, Toasters, etc.) that create new elements each frame.
        /// Preserves the cached background rectangle.
        /// </summary>
        private void ClearDynamicElements(Canvas canvas)
        {
            // Remove all children except the cached background
            for (int i = canvas.Children.Count - 1; i >= 0; i--)
            {
                var child = canvas.Children[i];
                if (child != _cachedBackground)
                {
                    canvas.Children.RemoveAt(i);
                }
            }
        }
        
        private void ApplySmoothing()
        {
            // Ensure arrays match current bar count
            if (_smoothedData.Length != _barCount)
            {
                _smoothedData = new double[_barCount];
                _peakHeights = new double[_barCount];
                _barHeights = new double[_barCount];
            }
            
            // Apply logarithmic frequency mapping if enabled
            if (USE_LOGARITHMIC_FREQ && _spectrumData.Length > 0)
            {
                var logMapped = new double[_barCount];
                int spectrumLength = _spectrumData.Length;
                
                for (int i = 0; i < _barCount; i++)
                {
                    // Logarithmic frequency mapping: more resolution for bass frequencies
                    double logIndex = Math.Pow((double)i / _barCount, 2) * (spectrumLength - 1);
                    int srcIdx = Math.Min((int)logIndex, spectrumLength - 1);
                    
                    // Average nearby bins for smoother result
                    double sum = 0;
                    int count = 0;
                    int range = Math.Max(1, spectrumLength / (_barCount * 2));
                    for (int j = Math.Max(0, srcIdx - range); j <= Math.Min(spectrumLength - 1, srcIdx + range); j++)
                    {
                        sum += _spectrumData[j];
                        count++;
                    }
                    logMapped[i] = count > 0 ? sum / count : _spectrumData[srcIdx];
                }
                _spectrumData = logMapped;
            }
            else if (_spectrumData.Length != _barCount)
            {
                _spectrumData = ResampleSpectrum(_spectrumData, _barCount);
            }
            
            // Use pre-configured rise/fall speeds (set by SetSensitivity)
            // HD quality: smooth rise/fall with peak hold (like reference pro-grade visualizer)
            for (int i = 0; i < _barCount; i++)
            {
                double target = i < _spectrumData.Length ? _spectrumData[i] * _sensitivity : 0;
                
                // Apply attack/release envelope for smooth animation
                if (target > _barHeights[i])
                {
                    // Fast attack - rise quickly to match the music
                    _barHeights[i] += (target - _barHeights[i]) * _riseSpeed;
                }
                else
                {
                    // Smooth decay - fall gradually for fluid animation
                    _barHeights[i] += (target - _barHeights[i]) * _fallSpeed;
                    if (_barHeights[i] < 0.001) _barHeights[i] = 0;
                }
                
                // Peak hold: peaks rise instantly, fall slowly
                if (_barHeights[i] > _peakHeights[i])
                {
                    _peakHeights[i] = _barHeights[i];
                }
                else
                {
                    _peakHeights[i] -= PEAK_FALL_SPEED;
                    if (_peakHeights[i] < 0) _peakHeights[i] = 0;
                }
                
                // Set smoothed data for rendering
                _smoothedData[i] = _barHeights[i];
            }
        }

        private void RenderBars(Canvas canvas)
        {
            int numBars = _barCount;
            if (numBars == 0) numBars = 128;
            
            double barWidth = canvas.ActualWidth / numBars;
            double maxHeight = canvas.ActualHeight;

            // Cache brushes for performance
            if (_cachedBarBrush == null)
                _cachedBarBrush = GetColorSchemeBrush(true);
            if (_cachedPeakBrush == null)
                _cachedPeakBrush = new SolidColorBrush(Colors.White);

            // Ensure we have enough cached rectangles
            while (_cachedBars.Count < numBars)
            {
                var bar = new Rectangle
                {
                    Opacity = 0.85,
                    RadiusX = 1,
                    RadiusY = 1
                };
                _cachedBars.Add(bar);
                canvas.Children.Add(bar);
            }
            
            while (_cachedPeaks.Count < numBars)
            {
                var peak = new Rectangle
                {
                    Height = 3,
                    Fill = _cachedPeakBrush ?? new SolidColorBrush(Colors.White),
                    Opacity = 0.9,
                    RadiusX = 1,
                    RadiusY = 1,
                    Visibility = Visibility.Collapsed
                };
                _cachedPeaks.Add(peak);
                canvas.Children.Add(peak);
            }

            for (int i = 0; i < numBars && i < _smoothedData.Length; i++)
            {
                double value = Math.Min(1.0, _smoothedData[i] * 2.5); // Clamp and amplify for HD visibility
                double barHeight = Math.Max(2, value * maxHeight); // Minimum height of 2px

                // Update existing bar instead of creating new one
                var bar = _cachedBars[i];
                bar.Width = Math.Max(1, barWidth - 1); // Tighter bars for HD
                bar.Height = barHeight;
                bar.Fill = _cachedBarBrush;
                Canvas.SetLeft(bar, i * barWidth);
                Canvas.SetTop(bar, maxHeight - barHeight);
                
                // Peak indicators disabled - they were distracting
                // Keep the cached peaks but hide them
                var peakLine = _cachedPeaks[i];
                peakLine.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderMirrorBars(Canvas canvas)
        {
            int numBars = _barCount;
            if (numBars == 0) numBars = 128;
            
            double barWidth = canvas.ActualWidth / numBars;
            double maxHeight = canvas.ActualHeight / 2;

            // Cache brush for performance
            if (_cachedBarBrush == null)
                _cachedBarBrush = GetColorSchemeBrush(false);

            double centerY = canvas.ActualHeight / 2;

            for (int i = 0; i < numBars && i < _smoothedData.Length; i++)
            {
                double value = Math.Min(1.0, _smoothedData[i] * 2.5);
                double barHeight = Math.Max(2, value * maxHeight);

                // Top bar
                var barTop = new Rectangle
                {
                    Width = Math.Max(1, barWidth - 1),
                    Height = barHeight,
                    Fill = _cachedBarBrush,
                    Opacity = 0.85,
                    RadiusX = 1,
                    RadiusY = 1
                };
                Canvas.SetLeft(barTop, i * barWidth);
                Canvas.SetTop(barTop, centerY - barHeight);
                canvas.Children.Add(barTop);

                // Bottom bar (mirrored)
                var barBottom = new Rectangle
                {
                    Width = Math.Max(1, barWidth - 1),
                    Height = barHeight,
                    Fill = _cachedBarBrush,
                    Opacity = 0.85,
                    RadiusX = 1,
                    RadiusY = 1
                };
                Canvas.SetLeft(barBottom, i * barWidth);
                Canvas.SetTop(barBottom, centerY);
                canvas.Children.Add(barBottom);
                
                // Draw peak indicators for mirror mode
                if (i < _peakHeights.Length && _peakHeights[i] > 0.02)
                {
                    double peakHeight = Math.Min(1.0, _peakHeights[i] * 2.5) * maxHeight;
                    // Top peak
                    var peakTop = new Rectangle
                    {
                        Width = Math.Max(1, barWidth - 1),
                        Height = 2,
                        Fill = _cachedPeakBrush ?? new SolidColorBrush(Colors.White),
                        Opacity = 0.9
                    };
                    Canvas.SetLeft(peakTop, i * barWidth);
                    Canvas.SetTop(peakTop, centerY - peakHeight - 1);
                    canvas.Children.Add(peakTop);
                    
                    // Bottom peak
                    var peakBottom = new Rectangle
                    {
                        Width = Math.Max(1, barWidth - 1),
                        Height = 2,
                        Fill = _cachedPeakBrush ?? new SolidColorBrush(Colors.White),
                        Opacity = 0.9
                    };
                    Canvas.SetLeft(peakBottom, i * barWidth);
                    Canvas.SetTop(peakBottom, centerY + peakHeight - 1);
                    canvas.Children.Add(peakBottom);
                }
            }
        }

        // Cached polyline for waveform mode
        private Polyline? _cachedWaveformLine;
        
        private void RenderWaveform(Canvas canvas)
        {
            if (_smoothedData.Length < 2) return;

            // Cache brush for performance
            if (_cachedBarBrush == null)
                _cachedBarBrush = GetColorSchemeBrush(false);

            // Use cached polyline for performance
            if (_cachedWaveformLine == null)
            {
                _cachedWaveformLine = new Polyline
                {
                    Stroke = _cachedBarBrush,
                    StrokeThickness = 2,
                    Opacity = 0.9,
                    StrokeLineJoin = PenLineJoin.Round
                };
                canvas.Children.Add(_cachedWaveformLine);
            }
            else if (!canvas.Children.Contains(_cachedWaveformLine))
            {
                canvas.Children.Add(_cachedWaveformLine);
            }

            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double centerY = height / 2;
            double pointSpacing = width / _smoothedData.Length;

            // Clear and rebuild points (more efficient than recreating polyline)
            _cachedWaveformLine.Points.Clear();
            for (int i = 0; i < _smoothedData.Length; i++)
            {
                double x = i * pointSpacing;
                double y = centerY - (_smoothedData[i] * centerY * 2.0 * _sensitivity);
                _cachedWaveformLine.Points.Add(new Point(x, y));
            }
            _cachedWaveformLine.Stroke = _cachedBarBrush;
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
                double value = Math.Min(1.0, _smoothedData[i] * 2.5);
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
                    StrokeThickness = Math.Max(2, canvas.ActualWidth / _smoothedData.Length * 0.8),
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
        
        // ===== AFTER DARK STYLE VISUALIZATIONS =====
        
        /// <summary>
        /// Renders a classic starfield flying through space (After Dark style).
        /// Stars speed up and stretch based on audio intensity.
        /// </summary>
        private void RenderStarfield(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double centerX = width / 2;
            double centerY = height / 2;
            
            // Calculate average intensity from bass frequencies
            double avgIntensity = 0;
            int bassCount = Math.Min(10, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity = bassCount > 0 ? avgIntensity / bassCount : 0.3;
            
            // Speed multiplier based on music intensity
            double speedMultiplier = 1.0 + avgIntensity * 4;
            
            foreach (var star in _stars)
            {
                // Move star toward viewer (decrease Z)
                star.Z -= star.Speed * speedMultiplier;
                
                // Reset star if it passes the viewer
                if (star.Z <= 0.01)
                {
                    star.X = _random.NextDouble() - 0.5;
                    star.Y = _random.NextDouble() - 0.5;
                    star.Z = 4.0;
                }
                
                // Project 3D position to 2D screen
                double screenX = centerX + (star.X / star.Z) * width;
                double screenY = centerY + (star.Y / star.Z) * height;
                
                // Skip if off screen
                if (screenX < 0 || screenX > width || screenY < 0 || screenY > height)
                    continue;
                
                // Size based on distance (closer = larger) - smaller stars overall
                double size = Math.Max(0.5, (1 / star.Z) * 2 * (1 + avgIntensity * 0.5));
                
                // Calculate streak length for warp effect
                double streakLength = Math.Min(30, star.Speed * speedMultiplier * 60 / star.Z);
                
                // Previous position for streak
                double prevZ = star.Z + star.Speed * speedMultiplier;
                double prevScreenX = centerX + (star.X / prevZ) * width;
                double prevScreenY = centerY + (star.Y / prevZ) * height;
                
                // Brightness based on distance and intensity
                byte brightness = (byte)Math.Clamp(255 * (1 / star.Z) * 0.5 * (1 + avgIntensity), 50, 255);
                var starColor = GetColorFromScheme(1.0 / star.Z);
                starColor = Color.FromArgb(brightness, starColor.R, starColor.G, starColor.B);
                
                // Draw streak line
                if (streakLength > 1.5)
                {
                    var line = new Line
                    {
                        X1 = prevScreenX,
                        Y1 = prevScreenY,
                        X2 = screenX,
                        Y2 = screenY,
                        Stroke = new SolidColorBrush(starColor),
                        StrokeThickness = Math.Max(0.5, size * 0.3),
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round
                    };
                    canvas.Children.Add(line);
                }
                
                // Draw star point
                var ellipse = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = new SolidColorBrush(starColor)
                };
                Canvas.SetLeft(ellipse, screenX - size / 2);
                Canvas.SetTop(ellipse, screenY - size / 2);
                canvas.Children.Add(ellipse);
            }
        }
        
        /// <summary>
        /// Renders classic flying toasters (After Dark style).
        /// Toasters fly diagonally with flapping wings, reacting to audio.
        /// </summary>
        private void RenderFlyingToasters(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            
            // Calculate average intensity
            double avgIntensity = 0;
            for (int i = 0; i < Math.Min(20, _smoothedData.Length); i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Min(20, _smoothedData.Length);
            
            // Wing flap speed based on audio
            double wingSpeed = 0.3 + avgIntensity * 0.5;
            
            foreach (var toaster in _toasters)
            {
                // Move toaster diagonally (upper-right to lower-left)
                toaster.X -= toaster.Speed * (1 + avgIntensity * 2);
                toaster.Y += toaster.Speed * 0.6 * (1 + avgIntensity * 2);
                
                // Wrap around screen
                if (toaster.X < -0.2)
                {
                    toaster.X = 1.2;
                    toaster.Y = _random.NextDouble() * 0.5 - 0.2;
                }
                if (toaster.Y > 1.2)
                {
                    toaster.Y = -0.2;
                    toaster.X = _random.NextDouble() * 0.5 + 0.5;
                }
                
                // Update wing animation
                toaster.WingPhase += wingSpeed;
                double wingAngle = Math.Sin(toaster.WingPhase) * 0.5;
                
                double screenX = toaster.X * width;
                double screenY = toaster.Y * height;
                double size = toaster.Size * (1 + avgIntensity * 0.3);
                
                // Draw toaster body (rectangle with rounded corners)
                var toasterColor = GetColorFromScheme(0.5);
                var body = new Rectangle
                {
                    Width = size,
                    Height = size * 0.7,
                    Fill = new SolidColorBrush(Color.FromRgb(180, 180, 190)),
                    Stroke = new SolidColorBrush(Color.FromRgb(100, 100, 110)),
                    StrokeThickness = 2,
                    RadiusX = 4,
                    RadiusY = 4
                };
                Canvas.SetLeft(body, screenX);
                Canvas.SetTop(body, screenY);
                canvas.Children.Add(body);
                
                // Draw toaster slots
                var slot1 = new Rectangle
                {
                    Width = size * 0.3,
                    Height = size * 0.1,
                    Fill = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(slot1, screenX + size * 0.1);
                Canvas.SetTop(slot1, screenY + size * 0.15);
                canvas.Children.Add(slot1);
                
                var slot2 = new Rectangle
                {
                    Width = size * 0.3,
                    Height = size * 0.1,
                    Fill = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                    RadiusX = 2,
                    RadiusY = 2
                };
                Canvas.SetLeft(slot2, screenX + size * 0.55);
                Canvas.SetTop(slot2, screenY + size * 0.15);
                canvas.Children.Add(slot2);
                
                // Draw wings (triangular shapes that flap)
                double wingY = screenY + size * 0.2 + wingAngle * size * 0.3;
                
                // Left wing
                var leftWing = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(screenX, screenY + size * 0.35),
                        new Point(screenX - size * 0.4, wingY),
                        new Point(screenX, wingY + size * 0.15)
                    },
                    Fill = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                    Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(leftWing);
                
                // Right wing
                var rightWing = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(screenX + size, screenY + size * 0.35),
                        new Point(screenX + size + size * 0.4, wingY),
                        new Point(screenX + size, wingY + size * 0.15)
                    },
                    Fill = new SolidColorBrush(Color.FromRgb(220, 220, 230)),
                    Stroke = new SolidColorBrush(Color.FromRgb(150, 150, 160)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(rightWing);
                
                // Draw toast popping out (based on audio)
                if (avgIntensity > 0.3)
                {
                    double toastPop = (avgIntensity - 0.3) * size * 0.5;
                    var toast1 = new Rectangle
                    {
                        Width = size * 0.25,
                        Height = size * 0.3 + toastPop,
                        Fill = new LinearGradientBrush
                        {
                            StartPoint = new Point(0, 0),
                            EndPoint = new Point(0, 1),
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromRgb(139, 90, 43), 0),
                                new GradientStop(Color.FromRgb(210, 180, 140), 0.5),
                                new GradientStop(Color.FromRgb(139, 90, 43), 1)
                            }
                        },
                        RadiusX = 3,
                        RadiusY = 3
                    };
                    Canvas.SetLeft(toast1, screenX + size * 0.15);
                    Canvas.SetTop(toast1, screenY - toastPop * 0.5);
                    canvas.Children.Add(toast1);
                    
                    var toast2 = new Rectangle
                    {
                        Width = size * 0.25,
                        Height = size * 0.3 + toastPop,
                        Fill = new LinearGradientBrush
                        {
                            StartPoint = new Point(0, 0),
                            EndPoint = new Point(0, 1),
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromRgb(139, 90, 43), 0),
                                new GradientStop(Color.FromRgb(210, 180, 140), 0.5),
                                new GradientStop(Color.FromRgb(139, 90, 43), 1)
                            }
                        },
                        RadiusX = 3,
                        RadiusY = 3
                    };
                    Canvas.SetLeft(toast2, screenX + size * 0.58);
                    Canvas.SetTop(toast2, screenY - toastPop * 0.5);
                    canvas.Children.Add(toast2);
                }
            }
        }
        
        /// <summary>
        /// Renders Matrix-style digital rain visualization.
        /// Characters fall down in columns with varying speeds, creating the iconic "digital rain" effect.
        /// Audio affects fall speed and character brightness.
        /// </summary>
        private void RenderMatrix(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            ClearDynamicElements(canvas);
            
            // Black background for Matrix effect
            var background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(Color.FromRgb(0, 10, 0)) // Very dark green-black
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            canvas.Children.Add(background);
            
            // Calculate average intensity from audio
            double avgIntensity = 0;
            double bassIntensity = 0;
            for (int i = 0; i < Math.Min(10, _smoothedData.Length); i++)
            {
                bassIntensity += _smoothedData[i];
            }
            bassIntensity /= Math.Min(10, _smoothedData.Length);
            
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= _smoothedData.Length;
            
            // Speed multiplier based on audio
            double speedMultiplier = 1.0 + bassIntensity * 3.0;
            
            // Font size based on canvas width
            double fontSize = Math.Max(12, width / 50);
            double charHeight = fontSize * 1.2;
            
            // Update and render each column
            foreach (var column in _matrixColumns)
            {
                // Move column down
                column.Y += column.Speed * speedMultiplier;
                
                // Reset when fully off screen
                if (column.Y > 1.0 + (column.Length * charHeight / height))
                {
                    column.Y = _random.NextDouble() * -0.5 - 0.2;
                    column.Speed = 0.005 + _random.NextDouble() * 0.015;
                    column.Length = 8 + _random.Next(15);
                    // Randomize some characters
                    for (int i = 0; i < column.Characters.Length; i++)
                    {
                        if (_random.NextDouble() < 0.3)
                            column.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                    }
                }
                
                // Calculate screen position
                double screenX = column.X * width;
                double startY = column.Y * height;
                
                // Render characters in this column
                for (int i = 0; i < column.Length && i < column.Characters.Length; i++)
                {
                    double charY = startY - (i * charHeight);
                    
                    // Skip if off screen
                    if (charY < -charHeight || charY > height) continue;
                    
                    // Occasionally change character (creates the "glitching" effect)
                    if (_random.NextDouble() < 0.02)
                        column.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                    
                    // Calculate brightness - brightest at the head (bottom), fading up
                    double brightness;
                    if (i == 0)
                    {
                        // Head character is bright white-green
                        brightness = 1.0;
                    }
                    else
                    {
                        // Fade based on position in trail
                        brightness = 1.0 - (i / (double)column.Length);
                        brightness = Math.Pow(brightness, 0.7); // Adjust falloff curve
                    }
                    
                    // Audio modulates brightness
                    brightness *= (0.5 + avgIntensity);
                    brightness = Math.Min(1.0, brightness);
                    
                    // Color calculation - green with brightness variation
                    byte green, red, blue;
                    if (i == 0)
                    {
                        // Head is bright white-green
                        red = (byte)(180 + avgIntensity * 75);
                        green = (byte)(255);
                        blue = (byte)(180 + avgIntensity * 75);
                    }
                    else
                    {
                        // Trail is pure green with fading
                        red = 0;
                        green = (byte)(50 + 205 * brightness);
                        blue = 0;
                    }
                    
                    var charText = new TextBlock
                    {
                        Text = column.Characters[i].ToString(),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = fontSize,
                        FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal,
                        Foreground = new SolidColorBrush(Color.FromRgb(red, green, blue))
                    };
                    
                    Canvas.SetLeft(charText, screenX);
                    Canvas.SetTop(charText, charY);
                    canvas.Children.Add(charText);
                }
            }
            
            // Add subtle glow overlay based on audio intensity
            if (avgIntensity > 0.2)
            {
                var glow = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(avgIntensity * 30), 0, 255, 0)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(glow, 0);
                Canvas.SetTop(glow, 0);
                canvas.Children.Add(glow);
            }
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
    
    /// <summary>
    /// Represents a star for the starfield visualization (After Dark style).
    /// </summary>
    internal class Star
    {
        public double X { get; set; }  // -0.5 to 0.5 (centered)
        public double Y { get; set; }  // -0.5 to 0.5 (centered)
        public double Z { get; set; }  // Depth (0.1 to 4.0)
        public double Speed { get; set; }
    }
    
    /// <summary>
    /// Represents a flying toaster (After Dark style).
    /// </summary>
    internal class Toaster
    {
        public double X { get; set; }  // 0 to 1 (screen position)
        public double Y { get; set; }  // 0 to 1 (screen position)
        public double Speed { get; set; }
        public double WingPhase { get; set; }  // Wing flap animation phase
        public double Size { get; set; }
    }
    
    /// <summary>
    /// Represents a column of falling characters for the Matrix digital rain visualization.
    /// </summary>
    internal class MatrixColumn
    {
        public double X { get; set; }  // 0 to 1 (horizontal position)
        public double Y { get; set; }  // Head position (can be negative, starts above screen)
        public double Speed { get; set; }  // Fall speed
        public int Length { get; set; }  // Number of characters in the trail
        public char[] Characters { get; set; } = new char[25];  // Characters in this column
    }
}
