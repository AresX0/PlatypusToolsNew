using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.WPF;

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
        LCARS,          // Star Trek LCARS orange/tan/purple
        Klingon,        // Klingon Empire - blood red, black, metal
        Federation      // United Federation of Planets - blue, silver, gold
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
        private bool _isExternallyDriven = false; // When true, skip internal service subscription (for fullscreen)
        
        // HD quality settings - tuned for smoothness with sensitivity control
        private const double PEAK_FALL_SPEED = 0.02; // How fast peaks fall (per frame) - slightly faster
        private double _sensitivity = 1.0; // User-adjustable sensitivity (0.1-3.0)
        private double _riseSpeed = 0.5; // Attack speed for smooth animation - faster
        private double _fallSpeed = 0.1; // Decay speed for smooth animation - faster
        private const bool USE_LOGARITHMIC_FREQ = true; // Logarithmic frequency mapping
        
        // GPU Rendering toggle: when true, ALL modes use SkiaSharp GPU-accelerated rendering
        // When false, all modes use WPF Canvas (legacy mode for systems without decent GPU)
        private bool _useGpuRendering = true;
        private string _hdRenderMode = ""; // Which mode is being rendered in HD
        private SKBitmap? _hdWaterfallBitmap; // GPU-ready waterfall bitmap
        private SKBitmap? _hdBloomBuffer; // Bloom/glow post-processing buffer
        
        // Waterfall spectrogram history (HD: full resolution)
        private readonly List<double[]> _waterfallHistory = new();
        private const int WATERFALL_MAX_ROWS = 400; // Doubled for HD
        private WriteableBitmap? _waterfallBitmap;
        private Image? _waterfallImage;
        
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
        private bool _isLargeSurface; // Set per-frame; true when fullscreen or >1M pixels — disables expensive blurs
        private double _cpuThrottlePercent = 0.5; // Max CPU usage (0.1-1.0)
        private long _maxMemoryBytes = 50 * 1024 * 1024; // 50MB max for visualizer
        
        // Density setting - used for different purposes per visualizer mode
        private int _density = 32;
        private int _targetStarCount = 400; // Current target star count based on density
        
        // After Dark: Starfield
        private readonly List<Star> _stars = new();
        
        // After Dark: Flying Toasters
        private readonly List<Toaster> _toasters = new();
        
        // Matrix: Digital Rain
        private readonly List<MatrixColumn> _matrixColumns = new();
        private const string MatrixChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz@#$%^&*+=<>{}[]|/\\:;";
        
        // Star Wars Crawl
        private double _crawlPosition = 0;
        private double _crawlSpeed = 1.0;
        private static readonly string[] StarWarsCrawlText = new[]
        {
            "STAR WARS",
            "",
            "EPISODE IV",
            "A NEW HOPE",
            "",
            "It is a period of civil war.",
            "Rebel spaceships, striking",
            "from a hidden base, have won",
            "their first victory against",
            "the evil Galactic Empire.",
            "",
            "During the battle, Rebel",
            "spies managed to steal secret",
            "plans to the Empire's",
            "ultimate weapon, the DEATH",
            "STAR, an armored space",
            "station with enough power",
            "to destroy an entire planet.",
            "",
            "Pursued by the Empire's",
            "sinister agents, Princess",
            "Leia races home aboard her",
            "starship, custodian of the",
            "stolen plans that can save",
            "her people and restore",
            "freedom to the galaxy....",
            "",
            "",
            "EPISODE V",
            "THE EMPIRE STRIKES BACK",
            "",
            "It is a dark time for the",
            "Rebellion. Although the Death",
            "Star has been destroyed,",
            "Imperial troops have driven",
            "the Rebel forces from their",
            "hidden base and pursued them",
            "across the galaxy.",
            "",
            "Evading the dreaded Imperial",
            "Starfleet, a group of freedom",
            "fighters led by Luke Skywalker",
            "has established a new secret",
            "base on the remote ice world",
            "of Hoth.",
            "",
            "The evil lord Darth Vader,",
            "obsessed with finding young",
            "Skywalker, has dispatched",
            "thousands of remote probes",
            "into the far reaches of space....",
            "",
            "",
            "EPISODE VI",
            "RETURN OF THE JEDI",
            "",
            "Luke Skywalker has returned",
            "to his home planet of",
            "Tatooine in an attempt to",
            "rescue his friend Han Solo",
            "from the clutches of the",
            "vile gangster Jabba the Hutt.",
            "",
            "Little does Luke know that",
            "the GALACTIC EMPIRE has",
            "secretly begun construction",
            "on a new armored space",
            "station even more powerful",
            "than the first dreaded",
            "Death Star.",
            "",
            "When completed, this ultimate",
            "weapon will spell certain",
            "doom for the small band of",
            "rebels struggling to restore",
            "freedom to the galaxy....",
            "",
            "",
            "THE END",
            "",
            "May the Force be with you."
        };
        
        // Stargate: Dialing and Wormhole effect - authentic sequence
        private double _stargateDialPosition = 0;  // Current dial rotation (degrees)
        private double _stargateTargetPosition = 0; // Target rotation for current chevron
        private int _stargateChevronLit = 0;       // Number of chevrons locked
        private double _stargateWormholePhase = 0; // Wormhole animation phase
        private bool _stargateIsDialing = true;   // Whether in dial or wormhole mode
#pragma warning disable CS0414 // Field is assigned but never used - reserved for future use
        private double _stargateDialTimer = 0;    // Timer for current state
#pragma warning restore CS0414
        private int _stargateDialDirection = 1;   // 1 = clockwise, -1 = counter-clockwise (alternates)
        private double _stargateChevronEngageTimer = 0; // Timer for chevron lock animation
        private bool _stargateChevronEngaging = false;  // Currently in chevron lock animation
        private double _stargateKawooshPhase = 0; // Kawoosh animation phase (0-1)
        private bool _stargateKawooshActive = false; // Whether kawoosh is playing
        
        // Target glyph positions for each chevron (random but consistent per song)
        private readonly int[] _stargateTargetGlyphs = new int[9];
        
        private static readonly string[] StargateGlyphs = { "☣", "☄", "☉", "☽", "☾", "♀", "♂", "♃", "♄", "♅", "♆", "♇", "♈", "♉", "♊", "♋", "♌", "♍", "♎", "♏", "♐", "♑", "♒", "♓", "♔", "♕", "♖", "♗", "♘", "♙", "♚", "♛", "♜", "♝", "♞", "♟", "♠", "♣", "♥", "♦" };
        
        // For advanced visualizations
        private readonly List<Particle> _particles = new();
        private double _auroraPhase = 0;
        private double[] _previousSmoothed = new double[64]; // Match bar count
        
        // Klingon visualizer assets
        private ImageBrush? _klingonBackgroundBrush;
        private FontFamily? _klingonFont;

        // Federation visualizer assets
        private ImageBrush? _federationBackgroundBrush;
        private double _transporterPhase = 0;
        private readonly List<TransporterParticle> _transporterParticles = new();

        // Milkdrop engine (legacy CPU path kept for preset switching)
        private MilkdropEngine? _milkdropEngine;
        private WriteableBitmap? _milkdropBitmap;
        private Image? _milkdropImage;
        private int _milkdropPresetIndex = 0;
        private DateTime _lastMilkdropPresetChange = DateTime.MinValue;
        private double _milkdropAutoChangeSeconds = 30.0;
        private int[] _milkdropRowBuffer = Array.Empty<int>(); // Reuse across frames
        private const int MILKDROP_RENDER_WIDTH = 320;  // Render at low res for performance
        private const int MILKDROP_RENDER_HEIGHT = 240;
        
        // GPU Milkdrop state — persistent frame buffer for feedback loop (double-buffered)
        private SKBitmap? _milkdropGpuBuffer;
        private SKBitmap? _milkdropGpuBufferBack;
        private int _milkdropGpuW, _milkdropGpuH;
        private double _milkdropTime;
        private double _milkdropWaveHue;
        private int _milkdropWaveMode;
        private int _milkdropGpuFrame;

        // Jedi visualizer assets
        private double _jediTextScrollOffset = 0;
        private BitmapImage? _lightsaberHiltImage;
        // Aurebesh-style characters for R2-D2 beeps
        private static readonly string AurebeshChars = "ᗩᗷᑕᗪᕮᖴᘜᕼᓰᒍᖽᐸᒪᗰᘉᓍᕈᕴᖇSᖶᑌᐺᗯ᙭ᖻᘔ";
        // Helper to generate random Aurebesh-style beep sequence
        private static string GenerateR2Beep(int length)
        {
            var rnd = new Random(DateTime.Now.Millisecond);
            var chars = new char[length];
            for (int i = 0; i < length; i++)
                chars[i] = AurebeshChars[rnd.Next(AurebeshChars.Length)];
            return new string(chars);
        }
        private readonly List<string> _r2d2Messages = new()
        {
            "[R2-D2]: *beep* *whistle* *boop*",
            "LUKE: I've got a problem here.",
            "[R2-D2]: *worried beep*",
            "LUKE: Artoo, see if you can't increase the power.",
            "[R2-D2]: *beep beep*",
            "LUKE: Hurry, Artoo, we're coming up on the target.",
            "[R2-D2]: *affirmative whistle*",
            "LUKE: Artoo, that stabilizer's broken loose again.",
            "[R2-D2]: *concerned beep*",
            "LUKE: See if you can't lock it down.",
            "[R2-D2]: *beep boop whistle*",
            "LUKE: Red Five standing by.",
            "[R2-D2]: *excited whistle!*",
            "LUKE: I've lost Artoo!",
            "[R2-D2]: *alarmed scream!!*",
            "LUKE: Use the Force, Luke.",
            "[R2-D2]: *questioning beep?*",
            "LUKE: Trust your feelings.",
            "[R2-D2]: *hopeful whistle*",
            "LUKE: Great shot kid, that was one in a million!",
            "[R2-D2]: *celebration beeps!*"
        };

        // Time Lord visualizer assets
        private BitmapImage? _timeVortexImage;
        private BitmapImage? _tardisImage;
        private double _vortexRotation = 0;
        private double _tardisX = 0.5;
        private double _tardisY = 0.5;
        private double _tardisTumble = 0;
        private double _tardisScale = 1.0;
        
        // TimeLord GPU feedback vortex state (double-buffered to avoid per-frame Copy allocations)
        private SKBitmap? _timeLordVortexBuffer;
        private SKBitmap? _timeLordVortexBufferBack;
        private int _timeLordVortexW, _timeLordVortexH;
        private double _timeLordVortexHue;
        private int _timeLordVortexFrame;

        // SkiaSharp cached bitmaps for GPU rendering
        private SKBitmap? _skKlingonLogo;
        private SKBitmap? _skFederationLogo;
        private SKBitmap? _skTardisBitmap;
        private SKBitmap? _skLightsaberHilt;
        private SKTypeface? _skKlingonTypeface;
        
        // Cached typefaces — prevents native GDI handle leak (was creating new handles every frame)
        private static readonly SKTypeface _tfConsolas = SKTypeface.FromFamilyName("Consolas", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        private static readonly SKTypeface _tfConsolasNormal = SKTypeface.FromFamilyName("Consolas");
        private static readonly SKTypeface _tfArial = SKTypeface.FromFamilyName("Arial");
        private static readonly SKTypeface _tfArialBold = SKTypeface.FromFamilyName("Arial", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        private static readonly SKTypeface _tfImpactBold = SKTypeface.FromFamilyName("Impact", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        private static readonly SKTypeface _tfGeorgia = SKTypeface.FromFamilyName("Georgia");
        private static readonly SKTypeface _tfSegoeUI = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal);
        private static readonly SKTypeface _tfCourierNew = SKTypeface.FromFamilyName("Courier New", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        /// <summary>
        /// Loads an SKBitmap from a WPF pack:// resource URI.
        /// </summary>
        private static SKBitmap? LoadSkBitmapFromResource(string resourcePath)
        {
            try
            {
                var uri = new Uri(resourcePath, UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo?.Stream == null) return null;
                using var stream = streamInfo.Stream;
                return SKBitmap.Decode(stream);
            }
            catch { return null; }
        }

        /// <summary>
        /// Loads an SKTypeface from a TTF file — tries embedded resource first, then file on disk.
        /// </summary>
        private static SKTypeface? LoadSkTypefaceFromFile(string relativePath)
        {
            try
            {
                // Try loading from embedded WPF resource first (works in single-file publish)
                var resName = relativePath.Replace('\\', '/');
                var uri = new Uri($"pack://application:,,,/{resName}", UriKind.Absolute);
                var streamInfo = System.Windows.Application.GetResourceStream(uri);
                if (streamInfo?.Stream != null)
                {
                    using var stream = streamInfo.Stream;
                    using var skStream = new SKManagedStream(stream);
                    var tf = SKTypeface.FromStream(skStream);
                    if (tf != null) return tf;
                }
            }
            catch { /* fall through to file-based loading */ }
            
            try
            {
                var fullPath = System.IO.Path.Combine(AppContext.BaseDirectory, relativePath);
                if (!System.IO.File.Exists(fullPath)) return null;
                return SKTypeface.FromFile(fullPath);
            }
            catch { return null; }
        }

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
                    Hue = _random.NextDouble()
                });
            }
            
            // Initialize stars for starfield mode (After Dark style) - more, smaller stars
            // About 5% of stars get a special color (yellow, red, or blue)
            for (int i = 0; i < 400; i++)
            {
                int colorType = 0; // White by default
                if (_random.NextDouble() < 0.05) // 5% chance of colored star
                    colorType = _random.Next(1, 4); // 1=yellow, 2=red, 3=blue
                
                _stars.Add(new Star
                {
                    X = _random.NextDouble() - 0.5,
                    Y = _random.NextDouble() - 0.5,
                    Z = _random.NextDouble() * 4 + 0.1,
                    Speed = 0.005 + _random.NextDouble() * 0.02, // Slower base speed
                    ColorType = colorType
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
            
            // Initialize matrix columns (digital rain) - 60 columns for denser effect with smaller font
            for (int i = 0; i < 60; i++)
            {
                _matrixColumns.Add(new MatrixColumn
                {
                    X = i / 60.0,
                    Y = _random.NextDouble() * -1.0, // Start above screen
                    Speed = 0.01 + _random.NextDouble() * 0.02,
                    Length = 8 + _random.Next(12), // Longer trails for smaller font
                    Characters = new char[20] // More characters per trail
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
            
            // SkiaSharp canvas is wired up in XAML via PaintSurface="OnSkiaPaintSurface"
            
            // Subscribe immediately in constructor as backup
            SubscribeToService();
            StartRenderTimer();
        }
        
        /// <summary>
        /// Marks this visualizer as externally driven (fullscreen mode).
        /// Prevents internal service subscription and sets music-playing state.
        /// Must be called immediately after construction, before OnLoaded fires.
        /// </summary>
        public void SetExternallyDriven()
        {
            _isExternallyDriven = true;
            _isMusicPlaying = true;
            _hasExternalData = true;
            
            // Unsubscribe if already subscribed
            if (_subscribedToService)
            {
                try
                {
                    var service = PlatypusTools.UI.Services.EnhancedAudioPlayerService.Instance;
                    service.SpectrumDataUpdated -= OnSpectrumDataFromEnhancedService;
                    service.PlaybackStateChanged -= OnPlaybackStateChanged;
                    _subscribedToService = false;
                }
                catch { }
            }
        }
        
        /// <summary>
        /// Subscribes to EnhancedAudioPlayerService for spectrum data and playback state.
        /// </summary>
        private void SubscribeToService()
        {
            if (!_subscribedToService && !_isExternallyDriven)
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
                // Don't override state in externally driven (fullscreen) mode
                if (_isExternallyDriven) return;
                
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
                // Don't process internal data in externally driven (fullscreen) mode
                if (_isExternallyDriven) return;
                
                if (data != null && data.Length > 0)
                {
                    // Convert float[] to double[]
                    var doubleData = new double[data.Length];
                    for (int i = 0; i < data.Length; i++)
                        doubleData[i] = data[i];
                    
                    _spectrumData = ResampleSpectrum(doubleData, _barCount);
                    _hasExternalData = true;
                    _lastExternalUpdate = DateTime.Now;
                    
                    // Also sync the visualization mode from the ViewModel
                    // This ensures mode changes take effect even if the parent
                    // view's handler isn't active (e.g., after tab switch).
                    if (DataContext is PlatypusTools.UI.ViewModels.EnhancedAudioPlayerViewModel vm)
                    {
                        string mode = vm.VisualizerModeIndex switch
                        {
                            0 => "Bars", 1 => "Mirror", 2 => "Waveform", 3 => "VU Meter",
                            4 => "Oscilloscope", 5 => "Circular", 6 => "Radial", 7 => "Aurora",
                            8 => "Wave Grid", 9 => "3D Bars", 10 => "Waterfall", 11 => "Star Wars Crawl",
                            12 => "Particles", 13 => "Starfield", 14 => "Toasters", 15 => "Matrix",
                            16 => "Stargate", 17 => "Klingon", 18 => "Federation", 19 => "Jedi",
                            20 => "TimeLord", 21 => "Milkdrop", _ => "Bars"
                        };
                        if (_visualizationMode != mode)
                        {
                            CleanupModeResources(_visualizationMode, mode);
                            _visualizationMode = mode;
                            
                            // Reset smoothing buffers for fresh start with new mode
                            for (int i = 0; i < _barHeights.Length; i++)
                            {
                                _barHeights[i] = 0;
                                _smoothedData[i] = 0;
                                _peakHeights[i] = 0;
                            }
                            _isRendering = false;
                        }
                    }
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
        /// For Stargate mode, this starts a new dialing sequence.
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
                
                // Reset Stargate to dialing mode for new song
                _stargateIsDialing = true;
                _stargateChevronLit = 0;
                _stargateDialTimer = 0;
                _stargateWormholePhase = 0;
                _stargateDialPosition = 0;
                _stargateTargetPosition = 0;
                _stargateDialDirection = 1;
                _stargateChevronEngageTimer = 0;
                _stargateChevronEngaging = false;
                _stargateKawooshPhase = 0;
                _stargateKawooshActive = false;
                
                // Generate random target glyph positions for each chevron (new address each song)
                for (int i = 0; i < _stargateTargetGlyphs.Length; i++)
                {
                    _stargateTargetGlyphs[i] = _random.Next(0, 39);
                }
                
                // Reset Star Wars crawl position
                _crawlPosition = 0;
                
                // Ensure timer is running
                StartRenderTimer();
                
                // Force immediate redraw
                InvalidateVisual();
                RenderVisualization();
                
                System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Reset for new track - Stargate will dial");
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
        /// Pauses the render timer to reduce CPU/GPU usage while this visualizer is not visible.
        /// Called when the fullscreen visualizer opens to stop the hidden normal-view from
        /// competing for UI thread time and GPU rendering resources.
        /// </summary>
        public void PauseRendering()
        {
            if (_renderTimer != null && _renderTimer.IsEnabled)
            {
                _renderTimer.Stop();
                System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Render timer PAUSED (fullscreen open)");
            }
        }
        
        /// <summary>
        /// Resumes the render timer after a pause. Called when fullscreen closes.
        /// </summary>
        public void ResumeRendering()
        {
            StartRenderTimer();
            System.Diagnostics.Debug.WriteLine("AudioVisualizerView: Render timer RESUMED (fullscreen closed)");
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
            // Don't stop timer or dispose resources if externally driven (fullscreen) and still in visual tree
            // — prevents spurious Unloaded events from killing the fullscreen visualizer
            if (_isExternallyDriven && IsLoaded)
            {
                System.Diagnostics.Debug.WriteLine("AudioVisualizerView.OnUnloaded: Skipping cleanup for externally driven (still loaded)");
                return;
            }
            
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
            
            // Dispose all GPU bitmap resources to prevent native memory leaks
            DisposeGpuResources();
            
            System.Diagnostics.Debug.WriteLine("AudioVisualizerView.OnUnloaded");
        }
        
        /// <summary>
        /// Cleans up mode-specific resources when switching visualization modes.
        /// Prevents stale state from bleeding between modes and frees GPU buffers.
        /// <summary>
        /// Cleans up mode-specific resources when switching visualizer modes.
        /// Performs a full stop of the old mode and prepares the new mode for a fresh start.
        /// This prevents stale state from one mode interfering with another, which was
        /// causing Federation/Klingon to "not move with music" after playing other modes first.
        /// </summary>
        private void CleanupModeResources(string oldMode, string newMode)
        {
            // Reset common animation state for fresh start
            _animationPhase = 0;
            
            // Matrix: reset column positions for fresh rain on next visit
            if (oldMode == "Matrix" || newMode == "Matrix")
            {
                foreach (var col in _matrixColumns)
                {
                    col.Y = _random.NextDouble() * -1.0;
                    col.Speed = 0.008 + _random.NextDouble() * 0.012;
                    col.Length = 8 + _random.Next(12);
                    for (int i = 0; i < col.Characters.Length; i++)
                        col.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                }
            }
            
            // Waterfall: dispose bitmap and clear history
            if (oldMode == "Waterfall" || newMode == "Waterfall")
            {
                _waterfallHistory.Clear();
                if (_hdWaterfallBitmap != null)
                {
                    var bmp = _hdWaterfallBitmap;
                    _hdWaterfallBitmap = null;
                    bmp.Dispose();
                }
            }
            
            // TimeLord: reset animation state and dispose feedback buffers
            if (oldMode == "TimeLord" || newMode == "TimeLord")
            {
                _tardisX = 0.5; _tardisY = 0.5; _tardisTumble = 0;
                _tardisScale = 1.0; _vortexRotation = 0;
                _timeLordVortexBuffer?.Dispose();
                _timeLordVortexBuffer = null;
                _timeLordVortexBufferBack?.Dispose();
                _timeLordVortexBufferBack = null;
            }
            
            // Milkdrop: dispose feedback buffers for fresh start
            if (oldMode == "Milkdrop" || newMode == "Milkdrop")
            {
                _milkdropGpuBuffer?.Dispose();
                _milkdropGpuBuffer = null;
                _milkdropGpuBufferBack?.Dispose();
                _milkdropGpuBufferBack = null;
            }
            
            // Star Wars Crawl: reset scroll position
            if (oldMode == "Star Wars Crawl" || newMode == "Star Wars Crawl")
            {
                _crawlPosition = 0;
            }
            
            // Stargate: reset dialing state
            if (newMode == "Stargate")
            {
                _stargateDialPosition = 0;
                _stargateTargetPosition = 0;
                _stargateChevronLit = 0;
                _stargateWormholePhase = 0;
                _stargateIsDialing = true;
                _stargateDialTimer = 0;
                _stargateDialDirection = 1;
                _stargateChevronEngageTimer = 0;
                _stargateChevronEngaging = false;
                _stargateKawooshPhase = 0;
                _stargateKawooshActive = false;
                for (int i = 0; i < _stargateTargetGlyphs.Length; i++)
                    _stargateTargetGlyphs[i] = _random.Next(0, 39);
            }
            
            // Federation: reset transporter phase for fresh start
            if (oldMode == "Federation" || newMode == "Federation")
            {
                _transporterPhase = 0;
            }
            
            // Klingon: no persistent state besides cached logo (intentionally kept)
            
            // Jedi: reset text scroll offset
            if (oldMode == "Jedi" || newMode == "Jedi")
            {
                _jediTextScrollOffset = 0;
            }
            
            // Aurora: reset phase
            if (oldMode == "Aurora" || newMode == "Aurora")
            {
                _auroraPhase = 0;
            }
        }
        
        /// <summary>
        /// Disposes all GPU bitmap resources. Called on unload to prevent native memory leaks.
        /// </summary>
        private void DisposeGpuResources()
        {
            _hdWaterfallBitmap?.Dispose();
            _hdWaterfallBitmap = null;
            
            _milkdropGpuBuffer?.Dispose();
            _milkdropGpuBuffer = null;
            _milkdropGpuBufferBack?.Dispose();
            _milkdropGpuBufferBack = null;
            
            _timeLordVortexBuffer?.Dispose();
            _timeLordVortexBuffer = null;
            _timeLordVortexBufferBack?.Dispose();
            _timeLordVortexBufferBack = null;
            
            _skKlingonLogo?.Dispose();
            _skKlingonLogo = null;
            
            _skFederationLogo?.Dispose();
            _skFederationLogo = null;
            
            _skTardisBitmap?.Dispose();
            _skTardisBitmap = null;
            
            _skLightsaberHilt?.Dispose();
            _skLightsaberHilt = null;
            
            _skKlingonTypeface?.Dispose();
            _skKlingonTypeface = null;
            
            _hdBloomBuffer?.Dispose();
            _hdBloomBuffer = null;
            
            _matrixColumns.Clear();
            _waterfallHistory.Clear();
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

        /// <summary>
        /// Forces an immediate mode switch without waiting for the next spectrum data update.
        /// Called from fullscreen keyboard handler to ensure mode changes propagate even when
        /// heavy renderers (Matrix, TimeLord, Milkdrop, Jedi) are blocking the frame-skip guard.
        /// Performs a full stop/start: cleans up old mode resources, resets smoothing buffers,
        /// and initializes the new mode fresh so it renders with live audio data immediately.
        /// </summary>
        public void ForceVisualizationMode(string newMode)
        {
            if (_visualizationMode == newMode) return;
            
            var oldMode = _visualizationMode;
            
            try { CleanupModeResources(oldMode, newMode); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"ForceVisualizationMode cleanup error: {ex.Message}"); }
            
            _visualizationMode = newMode;
            
            // Reset smoothing buffers to force fresh data adoption — prevents stale
            // smoothed values from the previous mode carrying over and causing the
            // new mode to appear frozen (especially after switching from heavy modes
            // like TimeLord/Milkdrop to lighter modes like Federation/Klingon).
            for (int i = 0; i < _barHeights.Length; i++)
            {
                _barHeights[i] = _spectrumData.Length > i ? _spectrumData[i] * _sensitivity : 0;
                _smoothedData[i] = _barHeights[i];
                _peakHeights[i] = _barHeights[i];
            }
            
            // Reset rendering flag to prevent stuck state
            _isRendering = false;
            
            System.Diagnostics.Debug.WriteLine($"ForceVisualizationMode: {oldMode} → {newMode} (smoothing reset, ready for live data)");
            
            // Force immediate repaint
            try { SkiaCanvas?.InvalidateVisual(); } catch { }
        }

        public void UpdateSpectrumData(double[] spectrumData, string mode = "Bars", int density = 128)
        {
            if (_visualizationMode != mode)
            {
                try { CleanupModeResources(_visualizationMode, mode); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"CleanupModeResources error: {ex.Message}"); }
                
                // Always update mode even if cleanup throws
                _visualizationMode = mode;
                
                // Reset smoothing buffers on mode switch for fresh start
                for (int i = 0; i < _barHeights.Length; i++)
                {
                    _barHeights[i] = 0;
                    _smoothedData[i] = 0;
                    _peakHeights[i] = 0;
                }
                _isRendering = false;
                
                // Force immediate repaint so mode switch is visible without waiting for timer tick
                try { SkiaCanvas?.InvalidateVisual(); } catch { }
            }
            else
            {
                _visualizationMode = mode;
            }
            
            // Always update density
            _density = Math.Max(density, 8);
            _barCount = _density; // For bar-based modes
            
            // Adjust star count for starfield mode (density * 10 for meaningful star count)
            if (mode == "Starfield")
            {
                _targetStarCount = Math.Clamp(_density * 10, 100, 1280);
                AdjustStarCount(_targetStarCount);
            }
            // Adjust particle count for particle-based modes
            else if (mode == "Particles")
            {
                _maxParticles = Math.Clamp(_density * 2, 32, 256);
            }
            
            if (spectrumData != null && spectrumData.Length > 0)
            {
                // Resample input data to match our bar count
                _spectrumData = ResampleSpectrum(spectrumData, _barCount);
                _hasExternalData = true;
                _lastExternalUpdate = DateTime.Now;
            }
        }
        
        /// <summary>
        /// Dynamically adjusts star count to match target.
        /// </summary>
        private void AdjustStarCount(int targetCount)
        {
            // Add stars if needed
            while (_stars.Count < targetCount)
            {
                int colorType = 0;
                if (_random.NextDouble() < 0.05)
                    colorType = _random.Next(1, 4);
                
                _stars.Add(new Star
                {
                    X = _random.NextDouble() - 0.5,
                    Y = _random.NextDouble() - 0.5,
                    Z = _random.NextDouble() * 4 + 0.1,
                    Speed = 0.005 + _random.NextDouble() * 0.02,
                    ColorType = colorType
                });
            }
            
            // Remove stars if too many
            while (_stars.Count > targetCount)
            {
                _stars.RemoveAt(_stars.Count - 1);
            }
        }
        
        /// <summary>
        /// Updates spectrum data with color scheme and sensitivity.
        /// </summary>
        public void UpdateSpectrumData(double[] spectrumData, string mode, int density, int colorSchemeIndex, double sensitivity = 0.7, int fps = 22)
        {
            SetColorScheme(colorSchemeIndex);
            SetSensitivity(sensitivity);
            
            // Update FPS if it changed
            if (fps != _targetFps)
            {
                SetTargetFps(fps);
            }
            
            UpdateSpectrumData(spectrumData, mode, density);
        }
        
        /// <summary>
        /// Updates spectrum data with all parameters including crawl speed.
        /// </summary>
        public void UpdateSpectrumData(double[] spectrumData, string mode, int density, int colorSchemeIndex, double sensitivity, int fps, double crawlSpeed, bool useGpuRendering = true)
        {
            _crawlSpeed = crawlSpeed;
            _useGpuRendering = useGpuRendering;
            // Visibility switching is handled in RenderVisualization() every frame — no call needed here
            UpdateSpectrumData(spectrumData, mode, density, colorSchemeIndex, sensitivity, fps);
        }
        
        /// <summary>
        /// Sets whether GPU rendering is used (true = SkiaSharp for all modes, false = WPF Canvas for all modes).
        /// </summary>
        public void SetUseGpuRendering(bool useGpu)
        {
            _useGpuRendering = useGpu;
        }
        
        /// <summary>
        /// Gets whether GPU rendering mode is currently active.
        /// </summary>
        public bool IsGpuRenderingEnabled => _useGpuRendering;
        
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
                VisualizerColorScheme.Klingon => new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(40, 0, 0), 0),         // Deep black-red
                    new GradientStop(Color.FromRgb(139, 0, 0), 0.3),      // Blood red
                    new GradientStop(Color.FromRgb(255, 50, 50), 0.6),    // Fierce red
                    new GradientStop(Color.FromRgb(180, 140, 100), 0.8),  // Bat'leth metal
                    new GradientStop(Color.FromRgb(220, 180, 120), 1)     // Polished blade
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
        /// <summary>
        /// Returns a WPF Color from continuous gradient space — produces hundreds of thousands
        /// of unique colors using smooth interpolation. Mirrors the HD color engine for consistency.
        /// </summary>
        private Color GetColorFromScheme(double value)
        {
            value = Math.Clamp(value, 0, 1);
            
            return _colorScheme switch
            {
                VisualizerColorScheme.Rainbow => HslToRgb(value * 330, 1, 0.45 + value * 0.15),
                VisualizerColorScheme.Fire => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(20, 0, 0)), (0.2, Color.FromRgb(180, 20, 0)), (0.45, Color.FromRgb(255, 80, 0)),
                    (0.65, Color.FromRgb(255, 160, 10)), (0.85, Color.FromRgb(255, 220, 50)), (1.0, Color.FromRgb(255, 255, 200))),
                VisualizerColorScheme.Purple => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(30, 0, 50)), (0.3, Color.FromRgb(100, 20, 160)), (0.5, Color.FromRgb(160, 40, 200)),
                    (0.7, Color.FromRgb(200, 80, 220)), (0.85, Color.FromRgb(230, 140, 240)), (1.0, Color.FromRgb(255, 200, 255))),
                VisualizerColorScheme.Neon => HslToRgb(180 + value * 140, 1, 0.45 + value * 0.2),
                VisualizerColorScheme.Ocean => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(0, 5, 30)), (0.2, Color.FromRgb(0, 30, 80)), (0.4, Color.FromRgb(0, 80, 140)),
                    (0.6, Color.FromRgb(0, 140, 180)), (0.8, Color.FromRgb(40, 200, 220)), (1.0, Color.FromRgb(150, 240, 255))),
                VisualizerColorScheme.Sunset => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(40, 0, 60)), (0.2, Color.FromRgb(120, 0, 100)), (0.4, Color.FromRgb(200, 40, 80)),
                    (0.6, Color.FromRgb(255, 100, 30)), (0.8, Color.FromRgb(255, 180, 50)), (1.0, Color.FromRgb(255, 240, 150))),
                VisualizerColorScheme.Monochrome => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(15, 15, 15)), (0.3, Color.FromRgb(80, 80, 80)), (0.6, Color.FromRgb(160, 160, 160)),
                    (0.8, Color.FromRgb(210, 210, 210)), (1.0, Color.FromRgb(255, 255, 255))),
                VisualizerColorScheme.PipBoy => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(5, 30, 10)), (0.3, Color.FromRgb(20, 120, 50)), (0.5, Color.FromRgb(40, 180, 80)),
                    (0.7, Color.FromRgb(70, 220, 120)), (0.9, Color.FromRgb(120, 245, 160)), (1.0, Color.FromRgb(180, 255, 200))),
                VisualizerColorScheme.LCARS => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(60, 80, 200)), (0.25, Color.FromRgb(120, 140, 240)), (0.45, Color.FromRgb(200, 170, 100)),
                    (0.6, Color.FromRgb(255, 200, 80)), (0.8, Color.FromRgb(220, 130, 180)), (1.0, Color.FromRgb(180, 100, 220))),
                VisualizerColorScheme.Klingon => InterpolateWpfGradient(value,
                    (0.0, Color.FromRgb(30, 0, 0)), (0.3, Color.FromRgb(120, 10, 10)), (0.5, Color.FromRgb(180, 30, 20)),
                    (0.7, Color.FromRgb(220, 80, 40)), (0.85, Color.FromRgb(200, 140, 100)), (1.0, Color.FromRgb(180, 170, 160))),
                _ => InterpolateWpfGradient(value, // BlueGreen/Federation
                    (0.0, Color.FromRgb(0, 20, 80)), (0.25, Color.FromRgb(0, 80, 180)), (0.5, Color.FromRgb(30, 144, 255)),
                    (0.7, Color.FromRgb(80, 200, 220)), (0.85, Color.FromRgb(150, 230, 200)), (1.0, Color.FromRgb(200, 255, 240)))
            };
        }
        
        /// <summary>
        /// Smoothly interpolates through a multi-stop WPF Color gradient using Hermite smoothstep.
        /// Produces continuous color values with no visible banding.
        /// </summary>
        private static Color InterpolateWpfGradient(double t, params (double pos, Color color)[] stops)
        {
            t = Math.Clamp(t, 0, 1);
            
            int lower = 0;
            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (t >= stops[i].pos && t <= stops[i + 1].pos)
                {
                    lower = i;
                    break;
                }
                if (i == stops.Length - 2) lower = i;
            }
            
            double segStart = stops[lower].pos;
            double segEnd = stops[lower + 1].pos;
            double segLen = segEnd - segStart;
            double localT = segLen > 0 ? (t - segStart) / segLen : 0;
            
            // Hermite smoothstep for perceptually even transitions
            localT = localT * localT * (3.0 - 2.0 * localT);
            
            var c1 = stops[lower].color;
            var c2 = stops[lower + 1].color;
            
            return Color.FromRgb(
                (byte)(c1.R + (c2.R - c1.R) * localT),
                (byte)(c1.G + (c2.G - c1.G) * localT),
                (byte)(c1.B + (c2.B - c1.B) * localT));
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
            // GPU RENDERING TOGGLE: when GPU is on, ALL modes go through SKElement.
            // When GPU is off, ALL modes go through WPF Canvas. No per-mode switching.
            // This eliminates the waterfall/mode release bug entirely.
            if (_useGpuRendering)
            {
                VisualizerCanvas.Visibility = Visibility.Collapsed;
                SkiaCanvas.Visibility = Visibility.Visible;
                
                ApplySmoothing();
                _animationPhase += 0.05;
                _hdRenderMode = _visualizationMode;
                SkiaCanvas.InvalidateVisual();
                return;
            }
            
            // === NATIVE WPF CANVAS MODE (legacy fallback for systems without decent GPU) ===
            VisualizerCanvas.Visibility = Visibility.Visible;
            SkiaCanvas.Visibility = Visibility.Collapsed;
            _hdRenderMode = "";
            
            var canvas = VisualizerCanvas;
            if (canvas == null || canvas.ActualWidth < 1 || canvas.ActualHeight < 1) return;

            // Check if we need full rebuild (mode changed, size changed, bar count changed, color scheme changed)
            bool modeChanged = _lastMode != _visualizationMode;
            bool barCountChanged = _lastBarCount != _barCount;
            bool colorChanged = _lastColorScheme != _colorScheme;
            
            // Reset Star Wars crawl position and Stargate state when switching modes
            if (modeChanged)
            {
                _crawlPosition = 0;
                // Reset Stargate to fresh dialing state
                _stargateDialPosition = 0;
                _stargateTargetPosition = 0;
                _stargateChevronLit = 0;
                _stargateWormholePhase = 0;
                _stargateIsDialing = true;
                _stargateDialTimer = 0;
                _stargateDialDirection = 1;
                _stargateChevronEngageTimer = 0;
                _stargateChevronEngaging = false;
                _stargateKawooshPhase = 0;
                _stargateKawooshActive = false;
                
                // Generate new target glyphs for this viewing session
                for (int i = 0; i < _stargateTargetGlyphs.Length; i++)
                {
                    _stargateTargetGlyphs[i] = _random.Next(0, 39);
                }
            }
            
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
            bool isDynamicMode = _visualizationMode is "Starfield" or "Toasters" or "Particles" or "Aurora" or "WaveGrid" or "Wave Grid" or "Circular" or "Radial" or "Mirror" or "Matrix" or "Star Wars Crawl" or "Stargate" or "Klingon" or "Federation" or "Jedi" or "TimeLord" or "VU Meter" or "Oscilloscope" or "Milkdrop" or "3D Bars" or "Waterfall";
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
                case "Star Wars Crawl":
                    RenderStarWarsCrawl(canvas);
                    break;
                case "Stargate":
                    RenderStargate(canvas);
                    break;
                case "Klingon":
                    RenderKlingon(canvas);
                    break;
                case "Federation":
                    RenderFederation(canvas);
                    break;
                case "Jedi":
                    RenderJedi(canvas);
                    break;
                case "TimeLord":
                    RenderTimeLord(canvas);
                    break;
                case "VU Meter":
                    RenderVUMeter(canvas);
                    break;
                case "Oscilloscope":
                    RenderOscilloscope(canvas);
                    break;
                case "Milkdrop":
                    RenderMilkdrop(canvas);
                    break;
                case "3D Bars":
                    Render3DBars(canvas);
                    break;
                case "Waterfall":
                    RenderWaterfall(canvas);
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
            // For externally driven (fullscreen), use faster response for snappier feel
            double riseMultiplier = _isExternallyDriven ? 1.5 : 1.0;
            double fallMultiplier = _isExternallyDriven ? 1.3 : 1.0;
            for (int i = 0; i < _barCount; i++)
            {
                double target = i < _spectrumData.Length ? _spectrumData[i] * _sensitivity : 0;
                
                // Apply attack/release envelope for smooth animation
                if (target > _barHeights[i])
                {
                    // Fast attack - rise quickly to match the music
                    _barHeights[i] += (target - _barHeights[i]) * _riseSpeed * riseMultiplier;
                }
                else
                {
                    // Smooth decay - fall gradually for fluid animation
                    _barHeights[i] += (target - _barHeights[i]) * _fallSpeed * fallMultiplier;
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
            
            // Draw black background so starfield works in light mode
            var background = new System.Windows.Shapes.Rectangle
            {
                Width = width,
                Height = height,
                Fill = Brushes.Black
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            canvas.Children.Add(background);
            
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
                
                // Size based on distance (closer = larger) - much smaller stars
                double size = Math.Max(0.3, (1 / star.Z) * 1.2 * (1 + avgIntensity * 0.3));
                
                // Calculate streak length for warp effect
                double streakLength = Math.Min(30, star.Speed * speedMultiplier * 60 / star.Z);
                
                // Previous position for streak
                double prevZ = star.Z + star.Speed * speedMultiplier;
                double prevScreenX = centerX + (star.X / prevZ) * width;
                double prevScreenY = centerY + (star.Y / prevZ) * height;
                
                // Brightness based on distance and intensity
                byte brightness = (byte)Math.Clamp(255 * (1 / star.Z) * 0.5 * (1 + avgIntensity), 50, 255);
                
                // Determine star color based on ColorType
                Color starColor;
                switch (star.ColorType)
                {
                    case 1: // Yellow star
                        starColor = Color.FromArgb(brightness, 255, 255, 100);
                        break;
                    case 2: // Red star
                        starColor = Color.FromArgb(brightness, 255, 120, 120);
                        break;
                    case 3: // Blue star
                        starColor = Color.FromArgb(brightness, 150, 180, 255);
                        break;
                    default: // White star
                        starColor = Color.FromArgb(brightness, 255, 255, 255);
                        break;
                }
                
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
                        StrokeThickness = Math.Max(0.3, size * 0.25),
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
        /// OPTIMIZED: Uses DrawingVisual instead of individual TextBlocks for much better performance.
        /// </summary>
        private void RenderMatrix(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            ClearDynamicElements(canvas);
            
            // Enable clipping to prevent characters from rendering outside canvas
            canvas.ClipToBounds = true;
            
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
            
            // Speed multiplier based on audio - stops when no audio
            double speedMultiplier = bassIntensity > 0.01 ? (0.5 + bassIntensity * 3.0) : 0;
            
            // Font size - half the previous size for denser rain with more lines
            double fontSize = Math.Max(8, width / 60);
            double charHeight = fontSize * 1.2;
            double charWidth = fontSize * 0.7;
            
            // Create a DrawingVisual for the glow layer (rendered first, behind main text)
            var glowVisual = new DrawingVisual();
            using (var dc = glowVisual.RenderOpen())
            {
                var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                
                // Update columns and render glow for bright characters
                foreach (var column in _matrixColumns)
                {
                    // Move column down
                    column.Y += column.Speed * speedMultiplier;
                    
                    // Reset when fully off screen
                    if (column.Y > 1.0 + (column.Length * charHeight / height))
                    {
                        column.Y = _random.NextDouble() * -0.5 - 0.2;
                        column.Speed = 0.008 + _random.NextDouble() * 0.012;
                        column.Length = 8 + _random.Next(12);
                        // Randomize some characters
                        for (int i = 0; i < column.Characters.Length; i++)
                        {
                            if (_random.NextDouble() < 0.4)
                                column.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                        }
                    }
                    
                    // Calculate screen position
                    double screenX = column.X * width;
                    double startY = column.Y * height;
                    
                    // Draw glow for head and bright characters (first 3 chars get glow)
                    for (int i = 0; i < Math.Min(3, column.Length) && i < column.Characters.Length; i++)
                    {
                        double charY = startY - (i * charHeight);
                        if (charY < -charHeight || charY > height) continue;
                        
                        // Glow intensity - strongest at head, fading for next chars
                        double glowIntensity = i == 0 ? 1.0 : (i == 1 ? 0.6 : 0.3);
                        glowIntensity *= (0.7 + avgIntensity * 0.5);
                        
                        // Glow color - bright green with some white
                        byte glowG = (byte)(200 + 55 * glowIntensity);
                        byte glowRB = (byte)(80 * glowIntensity);
                        var glowColor = Color.FromArgb((byte)(180 * glowIntensity), glowRB, glowG, glowRB);
                        
                        // Draw larger, semi-transparent glow text
                        var glowText = new FormattedText(
                            column.Characters[i].ToString(),
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize * 1.5, // Larger for glow spread
                            new SolidColorBrush(glowColor),
                            dpi);
                        
                        // Center the larger glow text
                        double offsetX = (fontSize * 0.5) / 2 * -0.5;
                        double offsetY = (fontSize * 0.5) / 2 * -0.3;
                        dc.DrawText(glowText, new Point(screenX + offsetX, charY + offsetY));
                    }
                }
            }
            
            // Add glow layer with blur effect
            var glowHost = new DrawingVisualHost(glowVisual);
            glowHost.Effect = new BlurEffect { Radius = fontSize * 0.8, KernelType = KernelType.Gaussian };
            canvas.Children.Add(glowHost);
            
            // Create main DrawingVisual for sharp text (rendered on top of glow)
            var drawingVisual = new DrawingVisual();
            using (var dc = drawingVisual.RenderOpen())
            {
                var typeface = new Typeface(new FontFamily("Consolas"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                
                // Render each column (positions already updated above)
                foreach (var column in _matrixColumns)
                {
                    double screenX = column.X * width;
                    double startY = column.Y * height;
                    
                    // Render characters in this column
                    for (int i = 0; i < column.Length && i < column.Characters.Length; i++)
                    {
                        double charY = startY - (i * charHeight);
                        
                        // Skip if off screen
                        if (charY < -charHeight || charY > height) continue;
                        
                        // Occasionally change character (creates the "glitching" effect)
                        if (_random.NextDouble() < 0.03)
                            column.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                        
                        // Calculate brightness - brightest at the head (bottom), fading up
                        double brightness;
                        if (i == 0)
                        {
                            brightness = 1.0;
                        }
                        else
                        {
                            brightness = 1.0 - (i / (double)column.Length);
                            brightness = Math.Pow(brightness, 0.6);
                        }
                        
                        // Audio modulates brightness
                        brightness *= (0.5 + avgIntensity);
                        brightness = Math.Min(1.0, brightness);
                        
                        // Color calculation
                        Color color;
                        if (i == 0)
                        {
                            // Head is bright white-green (glowing)
                            byte r = (byte)(200 + avgIntensity * 55);
                            color = Color.FromRgb(r, 255, r);
                        }
                        else if (i == 1)
                        {
                            // Second char is still quite bright
                            color = Color.FromRgb((byte)(100 * brightness), (byte)(200 + 55 * brightness), (byte)(100 * brightness));
                        }
                        else
                        {
                            // Trail is pure green with fading
                            color = Color.FromRgb(0, (byte)(60 + 195 * brightness), 0);
                        }
                        
                        // Draw the character using FormattedText
                        var formattedText = new FormattedText(
                            column.Characters[i].ToString(),
                            System.Globalization.CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            typeface,
                            fontSize,
                            new SolidColorBrush(color),
                            dpi);
                        
                        dc.DrawText(formattedText, new Point(screenX, charY));
                    }
                }
            }
            
            // Add the main text DrawingVisual on top
            var host = new DrawingVisualHost(drawingVisual);
            canvas.Children.Add(host);
            
            // Add subtle glow overlay based on audio intensity
            if (avgIntensity > 0.25)
            {
                var glow = new Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(avgIntensity * 25), 0, 255, 0)),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(glow, 0);
                Canvas.SetTop(glow, 0);
                canvas.Children.Add(glow);
            }
        }
        
        /// <summary>
        /// Renders Star Wars opening crawl style text visualization.
        /// Text scrolls upward with perspective, audio affects shimmer and glow.
        /// </summary>
        private void RenderStarWarsCrawl(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            ClearDynamicElements(canvas);
            
            // Black space background with stars
            var background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(Color.FromRgb(0, 0, 0))
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            canvas.Children.Add(background);
            
            // Draw some background stars
            for (int i = 0; i < 50; i++)
            {
                double starX = (i * 17 + _crawlPosition * 3) % width;
                double starY = (i * 23 + i * i) % height;
                double starSize = 1 + (i % 3);
                byte brightness = (byte)(150 + (i * 7) % 100);
                
                var star = new Ellipse
                {
                    Width = starSize,
                    Height = starSize,
                    Fill = new SolidColorBrush(Color.FromRgb(brightness, brightness, brightness))
                };
                Canvas.SetLeft(star, starX);
                Canvas.SetTop(star, starY);
                canvas.Children.Add(star);
            }
            
            // Calculate audio intensity for effects
            double avgIntensity = 0;
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= _smoothedData.Length;
            
            // Update crawl position (scrolling upward) - only when audio is playing
            if (avgIntensity > 0.01)
            {
                _crawlPosition += 0.8 * _crawlSpeed;
            }
            
            // Calculate total height of text content
            double lineHeight = height / 18; // About 18 lines visible
            double totalTextHeight = StarWarsCrawlText.Length * lineHeight;
            
            // Reset when all text has scrolled past
            if (_crawlPosition > height + totalTextHeight)
            {
                _crawlPosition = 0;
            }
            
            // Perspective vanishing point
            double vanishY = height * 0.15; // Vanishing point near top
            double bottomY = height * 1.1;  // Start point below screen
            
            // Render each line of text
            for (int i = 0; i < StarWarsCrawlText.Length; i++)
            {
                string line = StarWarsCrawlText[i];
                if (string.IsNullOrEmpty(line)) continue;
                
                // Calculate Y position for this line
                double lineY = bottomY - _crawlPosition + (i * lineHeight);
                
                // Skip if off screen
                if (lineY > bottomY || lineY < vanishY - lineHeight) continue;
                
                // Calculate perspective scale (smaller as it gets higher/further away)
                double progress = (bottomY - lineY) / (bottomY - vanishY);
                progress = Math.Clamp(progress, 0, 1);
                double scale = 1.0 - (progress * 0.85); // Scale from 1.0 to 0.15
                
                // Calculate alpha (fade out as it approaches vanishing point)
                double alpha = 1.0 - Math.Pow(progress, 2);
                alpha = Math.Clamp(alpha, 0, 1);
                
                // Skip if too faded
                if (alpha < 0.05) continue;
                
                // Calculate font size with perspective
                double baseFontSize = line.StartsWith("EPISODE") || line.StartsWith("STAR WARS") || 
                                      line.Contains("NEW HOPE") || line.Contains("EMPIRE STRIKES") || 
                                      line.Contains("RETURN OF") || line == "THE END"
                    ? lineHeight * 1.2  // Titles larger
                    : lineHeight * 0.7; // Regular text
                double fontSize = baseFontSize * scale;
                
                if (fontSize < 6) continue; // Too small to read
                
                // Calculate X position (centered with perspective)
                double textWidth = line.Length * fontSize * 0.5; // Approximate
                double centerX = width / 2;
                double perspectiveX = centerX - (textWidth / 2);
                
                // Calculate screen Y position (with perspective compression)
                double screenY = vanishY + (lineY - vanishY) * scale;
                
                // Color with audio-reactive shimmer
                byte baseYellow = 229;
                byte shimmer = (byte)(avgIntensity * 25);
                Color textColor = Color.FromArgb(
                    (byte)(alpha * 255),
                    (byte)Math.Min(255, baseYellow + shimmer),
                    (byte)Math.Min(255, 177 + shimmer),
                    46
                );
                
                // Create text element
                var textBlock = new TextBlock
                {
                    Text = line,
                    FontFamily = new FontFamily("Franklin Gothic Medium, Arial"),
                    FontSize = fontSize,
                    FontWeight = line.StartsWith("EPISODE") || line.StartsWith("STAR WARS") || line == "THE END"
                        ? FontWeights.Bold 
                        : FontWeights.Normal,
                    Foreground = new SolidColorBrush(textColor),
                    TextAlignment = TextAlignment.Center
                };
                
                // Apply horizontal scale transform for perspective
                textBlock.RenderTransformOrigin = new Point(0.5, 0.5);
                textBlock.RenderTransform = new ScaleTransform(scale, 1.0);
                
                Canvas.SetLeft(textBlock, perspectiveX);
                Canvas.SetTop(textBlock, screenY);
                canvas.Children.Add(textBlock);
            }
            
            // Add glow overlay at bottom (where text appears from)
            var bottomGlow = new Rectangle
            {
                Width = width,
                Height = height * 0.15,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 0, 0, 0), 0),
                        new GradientStop(Color.FromArgb((byte)(30 + avgIntensity * 20), 255, 200, 50), 1)
                    }
                }
            };
            Canvas.SetLeft(bottomGlow, 0);
            Canvas.SetTop(bottomGlow, height * 0.85);
            canvas.Children.Add(bottomGlow);
        }
        
        /// <summary>
        /// Renders Stargate SG-1 style visualization with authentic dialing sequence and wormhole effect.
        /// Features rotating ring with glyphs that stops at each symbol, chevron lock animation,
        /// kawoosh effect, and persistent wormhole until song ends.
        /// </summary>
        private void RenderStargate(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            ClearDynamicElements(canvas);
            
            // Black/dark blue background like space
            var background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(5, 5, 20), 0),
                        new GradientStop(Color.FromRgb(2, 2, 10), 1)
                    }
                }
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            canvas.Children.Add(background);
            
            // Calculate audio intensity
            double avgIntensity = 0;
            double bassIntensity = 0;
            for (int i = 0; i < Math.Min(10, _smoothedData.Length); i++)
                bassIntensity += _smoothedData[i];
            bassIntensity = _smoothedData.Length > 0 ? bassIntensity / Math.Min(10, _smoothedData.Length) : 0;
            
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity = _smoothedData.Length > 0 ? avgIntensity / _smoothedData.Length : 0;
            
            // Gate dimensions
            double centerX = width / 2;
            double centerY = height / 2;
            double outerRadius = Math.Min(width, height) * 0.42;
            double innerRadius = outerRadius * 0.72;
            double glyphRadius = outerRadius * 0.86;
            double chevronBaseRadius = outerRadius * 1.02;
            
            // ==== AUTHENTIC DIALING STATE MACHINE ====
            const int numGlyphs = 39;
            const double degreesPerGlyph = 360.0 / numGlyphs;
            
            if (_stargateIsDialing && avgIntensity > 0.01)
            {
                if (!_stargateChevronEngaging)
                {
                    // Rotate ring toward target glyph for current chevron
                    int currentChevronIndex = _stargateChevronLit;
                    if (currentChevronIndex < 7 && currentChevronIndex < _stargateTargetGlyphs.Length)
                    {
                        // Calculate target position (glyph should align to top = 270 degrees)
                        double targetGlyphAngle = _stargateTargetGlyphs[currentChevronIndex] * degreesPerGlyph;
                        _stargateTargetPosition = 270 - targetGlyphAngle;
                        
                        // Normalize positions
                        while (_stargateTargetPosition < 0) _stargateTargetPosition += 360;
                        while (_stargateTargetPosition >= 360) _stargateTargetPosition -= 360;
                        
                        double currentNormalized = _stargateDialPosition % 360;
                        if (currentNormalized < 0) currentNormalized += 360;
                        
                        // Calculate distance to target in current direction
                        double distance = _stargateDialDirection > 0
                            ? (_stargateTargetPosition - currentNormalized + 360) % 360
                            : (currentNormalized - _stargateTargetPosition + 360) % 360;
                        
                        // Rotate ring - speed based on bass
                        double rotateSpeed = 1.5 + bassIntensity * 4;
                        
                        if (distance > 3) // Still rotating
                        {
                            _stargateDialPosition += _stargateDialDirection * rotateSpeed;
                        }
                        else // Reached target - start chevron engagement
                        {
                            _stargateChevronEngaging = true;
                            _stargateChevronEngageTimer = 0;
                        }
                    }
                }
                else
                {
                    // Chevron engagement animation
                    _stargateChevronEngageTimer += 0.08 + avgIntensity * 0.1;
                    
                    if (_stargateChevronEngageTimer >= 1.0)
                    {
                        // Chevron locked!
                        _stargateChevronLit++;
                        _stargateChevronEngaging = false;
                        _stargateChevronEngageTimer = 0;
                        _stargateDialDirection *= -1; // Alternate direction for next glyph
                        
                        // After 7 chevrons, trigger kawoosh
                        if (_stargateChevronLit >= 7)
                        {
                            _stargateIsDialing = false;
                            _stargateKawooshActive = true;
                            _stargateKawooshPhase = 0;
                        }
                    }
                }
            }
            
            // Update wormhole/kawoosh animation
            if (!_stargateIsDialing)
            {
                if (_stargateKawooshActive)
                {
                    _stargateKawooshPhase += 0.04;
                    if (_stargateKawooshPhase >= 1.0)
                    {
                        _stargateKawooshActive = false;
                    }
                }
                _stargateWormholePhase += 0.08 + avgIntensity * 0.15;
            }
            
            // ==== DRAW THE GATE (shape-based rendering) ====
            
            // Outer ring shadow/glow
            var outerGlow = new Ellipse
            {
                Width = outerRadius * 2.15,
                Height = outerRadius * 2.15,
                Fill = new RadialGradientBrush
                {
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0, 50, 50, 80), 0.85),
                        new GradientStop(Color.FromArgb(80, 40, 40, 60), 0.95),
                        new GradientStop(Color.FromArgb(0, 30, 30, 50), 1)
                    }
                }
            };
            Canvas.SetLeft(outerGlow, centerX - outerRadius * 1.075);
            Canvas.SetTop(outerGlow, centerY - outerRadius * 1.075);
            canvas.Children.Add(outerGlow);
            
            // Outer ring (naquadah ring with detail)
            var outerRing = new Ellipse
            {
                Width = outerRadius * 2,
                Height = outerRadius * 2,
                Stroke = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(100, 100, 120), 0),
                        new GradientStop(Color.FromRgb(60, 60, 80), 0.5),
                        new GradientStop(Color.FromRgb(90, 90, 110), 1)
                    }
                },
                StrokeThickness = outerRadius * 0.14,
                Fill = new SolidColorBrush(Color.FromRgb(35, 35, 50))
            };
            Canvas.SetLeft(outerRing, centerX - outerRadius);
            Canvas.SetTop(outerRing, centerY - outerRadius);
            canvas.Children.Add(outerRing);
            
            // Inner ring track for glyphs
            var glyphTrack = new Ellipse
            {
                Width = glyphRadius * 2,
                Height = glyphRadius * 2,
                Stroke = new SolidColorBrush(Color.FromRgb(55, 55, 75)),
                StrokeThickness = outerRadius * 0.08,
                Fill = new SolidColorBrush(Color.FromRgb(30, 30, 45))
            };
            Canvas.SetLeft(glyphTrack, centerX - glyphRadius);
            Canvas.SetTop(glyphTrack, centerY - glyphRadius);
            canvas.Children.Add(glyphTrack);
            
            // Draw glyphs around the ring (39 glyphs in a Stargate) - always shown for dialing
            double fontSize = Math.Max(10, outerRadius * 0.07);
            for (int i = 0; i < numGlyphs; i++)
            {
                double glyphAngle = i * degreesPerGlyph + _stargateDialPosition;
                double radians = glyphAngle * Math.PI / 180;
                double glyphX = centerX + Math.Cos(radians) * glyphRadius;
                double glyphY = centerY + Math.Sin(radians) * glyphRadius;
                
                string glyph = StargateGlyphs[i % StargateGlyphs.Length];
                
                // Check if this glyph is at the top (under the master chevron)
                double normalizedAngle = (glyphAngle % 360 + 360) % 360;
                bool isActive = Math.Abs(normalizedAngle - 270) < 6;
                
                // Check if this glyph is a target for a locked chevron
                bool isLocked = false;
                for (int c = 0; c < _stargateChevronLit && c < _stargateTargetGlyphs.Length; c++)
                {
                    if (_stargateTargetGlyphs[c] == i) isLocked = true;
                }
                
                Color glyphColor;
                if (isActive)
                    glyphColor = Color.FromRgb(255, 180, 80); // Bright orange at top
                else if (isLocked)
                    glyphColor = Color.FromRgb(255, 120, 50); // Orange for locked glyphs
                else
                    glyphColor = Color.FromRgb(130, 130, 160); // Gray for normal
                
                var glyphText = new TextBlock
                {
                    Text = glyph,
                    FontFamily = new FontFamily("Segoe UI Symbol"),
                    FontSize = fontSize,
                    Foreground = new SolidColorBrush(glyphColor)
                };
                Canvas.SetLeft(glyphText, glyphX - fontSize * 0.4);
                Canvas.SetTop(glyphText, glyphY - fontSize * 0.5);
                canvas.Children.Add(glyphText);
            }
            
            // ==== DRAW 9 CHEVRONS ====
            // Positions around gate - index 6 is top (master chevron)
            // Angles: 0=-50°(2:00), 1=-10°(3:00), 2=30°(4:00), 3=70°(5:00), 4=110°(7:00), 
            //         5=150°(8:00), 6=-90°(12:00 TOP), 7=-130°(10:00), 8=190°(9:00)
            // Bottom two (indices 3 and 4 at 70° and 110°) are NOT used for 7-symbol addresses
            double[] chevronAngles = { -50, -10, 30, 70, 110, 150, -90, -130, 190 }; // -90 is top (master)
            
            // Lighting order: 6 side chevrons first, then top (index 6) LAST
            // Skip indices 3 and 4 (the bottom two at ~5 and ~7 o'clock)
            int[] lightingOrder = { 0, 1, 2, 5, 8, 7, 6 }; // Top chevron (6) lights last
            
            for (int i = 0; i < 9; i++)
            {
                double chevronAngle = chevronAngles[i];
                double radians = chevronAngle * Math.PI / 180;
                double chevronX = centerX + Math.Cos(radians) * chevronBaseRadius;
                double chevronY = centerY + Math.Sin(radians) * chevronBaseRadius;
                
                // Determine if this chevron should be lit based on lighting order
                bool isLit = false;
                bool isEngaging = false;
                
                for (int lit = 0; lit < _stargateChevronLit && lit < lightingOrder.Length; lit++)
                {
                    if (lightingOrder[lit] == i) isLit = true;
                }
                
                // Check if this is the chevron currently engaging
                if (_stargateChevronEngaging && _stargateChevronLit < lightingOrder.Length)
                {
                    if (lightingOrder[_stargateChevronLit] == i) isEngaging = true;
                }
                
                // Chevron engage offset (moves inward when engaging)
                double engageOffset = isEngaging ? Math.Sin(_stargateChevronEngageTimer * Math.PI) * 8 : 0;
                double effectiveX = chevronX - Math.Cos(radians) * engageOffset;
                double effectiveY = chevronY - Math.Sin(radians) * engageOffset;
                
                // Chevron size scales with gate
                double chevronSize = outerRadius * 0.12;
                
                // Create chevron shape (V-shape pointing inward)
                var chevron = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(effectiveX, effectiveY),
                        new Point(effectiveX + Math.Cos(radians + 0.5) * chevronSize, effectiveY + Math.Sin(radians + 0.5) * chevronSize),
                        new Point(effectiveX + Math.Cos(radians) * chevronSize * 1.5, effectiveY + Math.Sin(radians) * chevronSize * 1.5),
                        new Point(effectiveX + Math.Cos(radians - 0.5) * chevronSize, effectiveY + Math.Sin(radians - 0.5) * chevronSize)
                    },
                    Stroke = new SolidColorBrush(Color.FromRgb(120, 120, 140)),
                    StrokeThickness = 2
                };
                
                // Color based on state
                if (isEngaging)
                {
                    // Pulsing during engagement
                    byte brightness = (byte)(150 + Math.Sin(_stargateChevronEngageTimer * Math.PI * 4) * 105);
                    chevron.Fill = new SolidColorBrush(Color.FromRgb(brightness, (byte)(brightness * 0.5), 20));
                }
                else if (isLit)
                {
                    chevron.Fill = new SolidColorBrush(Color.FromRgb(255, 140, 40));
                }
                else
                {
                    chevron.Fill = new SolidColorBrush(Color.FromRgb(60, 40, 30));
                }
                
                canvas.Children.Add(chevron);
                
                // Glow for lit chevrons
                if (isLit || isEngaging)
                {
                    byte glowAlpha = (byte)(isEngaging ? 200 * Math.Sin(_stargateChevronEngageTimer * Math.PI) : 120);
                    var glow = new Ellipse
                    {
                        Width = chevronSize * 2,
                        Height = chevronSize * 2,
                        Fill = new RadialGradientBrush
                        {
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromArgb(glowAlpha, 255, 180, 80), 0),
                                new GradientStop(Color.FromArgb((byte)(glowAlpha / 2), 255, 120, 40), 0.5),
                                new GradientStop(Color.FromArgb(0, 255, 80, 20), 1)
                            }
                        }
                    };
                    Canvas.SetLeft(glow, effectiveX - chevronSize + Math.Cos(radians) * chevronSize * 0.5);
                    Canvas.SetTop(glow, effectiveY - chevronSize + Math.Sin(radians) * chevronSize * 0.5);
                    canvas.Children.Add(glow);
                }
            }
            
            // ==== DRAW EVENT HORIZON (High-Resolution Swirling Wormhole) ====
            if (!_stargateIsDialing)
            {
                double wormholeRadius = innerRadius * 0.95;
                
                // Kawoosh effect (unstable vortex burst)
                if (_stargateKawooshActive)
                {
                    double kawooshPhase = _stargateKawooshPhase;
                    double kawooshExtent = Math.Sin(kawooshPhase * Math.PI) * wormholeRadius * 1.2;
                    double kawooshAlpha = (1 - kawooshPhase) * (1 - kawooshPhase);
                    
                    // Expanding bubble ring
                    double bubbleRadius = kawooshPhase * wormholeRadius * 1.5;
                    var bubbleRing = new Ellipse
                    {
                        Width = bubbleRadius * 2,
                        Height = bubbleRadius * 2,
                        Stroke = new SolidColorBrush(Color.FromArgb((byte)(kawooshAlpha * 200), 200, 240, 255)),
                        StrokeThickness = 8 * (1 - kawooshPhase),
                        Fill = null
                    };
                    Canvas.SetLeft(bubbleRing, centerX - bubbleRadius);
                    Canvas.SetTop(bubbleRing, centerY - bubbleRadius);
                    canvas.Children.Add(bubbleRing);
                    
                    // Main kawoosh splash (multiple layers)
                    for (int k = 0; k < 5; k++)
                    {
                        double kOffset = k * 0.1;
                        double kPhase = Math.Max(0, kawooshPhase - kOffset);
                        double kExtent = Math.Sin(kPhase * Math.PI) * wormholeRadius * (0.9 - k * 0.12);
                        byte kAlpha = (byte)(kawooshAlpha * 220 * (1 - k * 0.15));
                        
                        var kawoosh = new Ellipse
                        {
                            Width = wormholeRadius * (1.4 - k * 0.1),
                            Height = Math.Max(1, kExtent),
                            Fill = new RadialGradientBrush
                            {
                                GradientStops = new GradientStopCollection
                                {
                                    new GradientStop(Color.FromArgb(kAlpha, 220, 245, 255), 0),
                                    new GradientStop(Color.FromArgb((byte)(kAlpha * 0.7), 140, 200, 255), 0.4),
                                    new GradientStop(Color.FromArgb((byte)(kAlpha * 0.3), 80, 150, 230), 0.7),
                                    new GradientStop(Color.FromArgb(0, 40, 100, 200), 1)
                                }
                            }
                        };
                        Canvas.SetLeft(kawoosh, centerX - wormholeRadius * (0.7 - k * 0.05));
                        Canvas.SetTop(kawoosh, centerY - kExtent / 2);
                        canvas.Children.Add(kawoosh);
                    }
                }
                
                // === HIGH-RESOLUTION EVENT HORIZON ===
                // Base layer - deep blue-black center
                var horizonBase = new Ellipse
                {
                    Width = wormholeRadius * 2,
                    Height = wormholeRadius * 2,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(5, 15, 40), 0),
                            new GradientStop(Color.FromRgb(20, 50, 100), 0.5),
                            new GradientStop(Color.FromRgb(40, 90, 160), 0.8),
                            new GradientStop(Color.FromRgb(60, 120, 200), 1)
                        }
                    }
                };
                Canvas.SetLeft(horizonBase, centerX - wormholeRadius);
                Canvas.SetTop(horizonBase, centerY - wormholeRadius);
                canvas.Children.Add(horizonBase);
                
                // Swirling rings (smooth water-like rotation)
                int numSwirls = 24;
                for (int s = 0; s < numSwirls; s++)
                {
                    double sT = s / (double)numSwirls;
                    double sRadius = wormholeRadius * (0.15 + sT * 0.8);
                    
                    // Swirl angle increases toward center (creates vortex twist)
                    double swirlStrength = 2.0 + (1 - sT) * 4.0;
                    double sAngle = _stargateWormholePhase * 0.5 + sT * swirlStrength;
                    
                    // Ring offset based on swirl
                    double offsetX = Math.Cos(sAngle) * sRadius * 0.08 * (1 - sT);
                    double offsetY = Math.Sin(sAngle) * sRadius * 0.08 * (1 - sT);
                    
                    // Smooth color gradient: blue -> cyan -> white toward center
                    double hue = 0.55 + sT * 0.1; // 0.55 = cyan, 0.65 = blue
                    var ringColor = HsvToRgb(hue, 0.6 - sT * 0.4, 0.7 + sT * 0.3 + avgIntensity * 0.2);
                    byte ringAlpha = (byte)(80 + sT * 120 + avgIntensity * 50);
                    
                    // Ripple based on time
                    double ripple = Math.Sin(_stargateWormholePhase * 2 + s * 0.4) * 3;
                    
                    var swirlRing = new Ellipse
                    {
                        Width = (sRadius + ripple) * 2,
                        Height = (sRadius + ripple) * 2,
                        Stroke = new SolidColorBrush(Color.FromArgb(ringAlpha, ringColor.R, ringColor.G, ringColor.B)),
                        StrokeThickness = 3 + sT * 6,
                        Fill = null
                    };
                    Canvas.SetLeft(swirlRing, centerX - sRadius - ripple + offsetX);
                    Canvas.SetTop(swirlRing, centerY - sRadius - ripple + offsetY);
                    canvas.Children.Add(swirlRing);
                }
                
                // Flowing energy streams (spiral arms)
                int numStreams = 48;
                for (int st = 0; st < numStreams; st++)
                {
                    double stT = st / (double)numStreams;
                    
                    // Spiral from edge to center
                    double spiralAngle = stT * Math.PI * 6 + _stargateWormholePhase;
                    
                    // Multiple points along each spiral arm
                    for (int seg = 0; seg < 8; seg++)
                    {
                        double segT = seg / 8.0;
                        double r = wormholeRadius * (0.9 - segT * 0.75);
                        double twist = segT * Math.PI * 1.5;
                        double angle = spiralAngle + twist;
                        
                        double px = centerX + Math.Cos(angle) * r;
                        double py = centerY + Math.Sin(angle) * r;
                        
                        // Size decreases toward center
                        double pSize = 4 + (1 - segT) * 8 + avgIntensity * 4;
                        
                        // Color: cyan edge -> white center
                        byte pR = (byte)(100 + segT * 155);
                        byte pG = (byte)(180 + segT * 75);
                        byte pB = 255;
                        byte pAlpha = (byte)(40 + segT * 80 + avgIntensity * 40);
                        
                        var streamPoint = new Ellipse
                        {
                            Width = pSize,
                            Height = pSize,
                            Fill = new RadialGradientBrush
                            {
                                GradientStops = new GradientStopCollection
                                {
                                    new GradientStop(Color.FromArgb(pAlpha, pR, pG, pB), 0),
                                    new GradientStop(Color.FromArgb((byte)(pAlpha * 0.4), pR, pG, pB), 0.5),
                                    new GradientStop(Color.FromArgb(0, pR, pG, pB), 1)
                                }
                            }
                        };
                        Canvas.SetLeft(streamPoint, px - pSize / 2);
                        Canvas.SetTop(streamPoint, py - pSize / 2);
                        canvas.Children.Add(streamPoint);
                    }
                }
                
                // Audio-reactive ripples (bass hits create expanding waves)
                int numRipples = 6;
                for (int rp = 0; rp < numRipples; rp++)
                {
                    double rpT = rp / (double)numRipples;
                    double rpPhase = (_stargateWormholePhase * 0.5 + rpT * Math.PI * 2) % (Math.PI * 2);
                    double rpRadius = (rpPhase / (Math.PI * 2)) * wormholeRadius * 0.9;
                    double rpAlpha = (1 - rpPhase / (Math.PI * 2)) * (0.3 + bassIntensity * 0.5);
                    
                    if (rpRadius > 5)
                    {
                        var rippleRing = new Ellipse
                        {
                            Width = rpRadius * 2,
                            Height = rpRadius * 2,
                            Stroke = new SolidColorBrush(Color.FromArgb((byte)(rpAlpha * 255), 180, 220, 255)),
                            StrokeThickness = 2 + (1 - rpRadius / wormholeRadius) * 4,
                            Fill = null
                        };
                        Canvas.SetLeft(rippleRing, centerX - rpRadius);
                        Canvas.SetTop(rippleRing, centerY - rpRadius);
                        canvas.Children.Add(rippleRing);
                    }
                }
                
                // Frequency-reactive highlights (shimmer effect)
                int numHighlights = Math.Min(16, _smoothedData.Length / 4);
                for (int h = 0; h < numHighlights; h++)
                {
                    int freqIdx = h * (_smoothedData.Length / numHighlights);
                    double intensity = freqIdx < _smoothedData.Length ? _smoothedData[freqIdx] * _sensitivity : 0;
                    
                    if (intensity > 0.1)
                    {
                        double hAngle = (h * 360.0 / numHighlights + _stargateWormholePhase * 40) * Math.PI / 180;
                        double hDist = wormholeRadius * (0.2 + intensity * 0.5);
                        double hx = centerX + Math.Cos(hAngle) * hDist;
                        double hy = centerY + Math.Sin(hAngle) * hDist;
                        double hSize = 15 + intensity * 50;
                        
                        var highlight = new Ellipse
                        {
                            Width = hSize,
                            Height = hSize,
                            Fill = new RadialGradientBrush
                            {
                                GradientStops = new GradientStopCollection
                                {
                                    new GradientStop(Color.FromArgb((byte)(intensity * 220), 240, 250, 255), 0),
                                    new GradientStop(Color.FromArgb((byte)(intensity * 120), 150, 210, 255), 0.4),
                                    new GradientStop(Color.FromArgb((byte)(intensity * 40), 80, 160, 230), 0.7),
                                    new GradientStop(Color.FromArgb(0, 50, 120, 200), 1)
                                }
                            }
                        };
                        Canvas.SetLeft(highlight, hx - hSize / 2);
                        Canvas.SetTop(highlight, hy - hSize / 2);
                        canvas.Children.Add(highlight);
                    }
                }
                
                // Bright center core (pulsing with bass)
                double coreSize = 30 + bassIntensity * 80;
                var core3 = new Ellipse
                {
                    Width = coreSize * 2.5,
                    Height = coreSize * 2.5,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb((byte)(60 + bassIntensity * 60), 150, 200, 255), 0),
                            new GradientStop(Color.FromArgb((byte)(30 + bassIntensity * 30), 100, 160, 230), 0.5),
                            new GradientStop(Color.FromArgb(0, 60, 120, 200), 1)
                        }
                    }
                };
                Canvas.SetLeft(core3, centerX - coreSize * 1.25);
                Canvas.SetTop(core3, centerY - coreSize * 1.25);
                canvas.Children.Add(core3);
                
                var core2 = new Ellipse
                {
                    Width = coreSize * 1.5,
                    Height = coreSize * 1.5,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb((byte)(150 + bassIntensity * 80), 200, 230, 255), 0),
                            new GradientStop(Color.FromArgb((byte)(80 + bassIntensity * 50), 140, 190, 250), 0.5),
                            new GradientStop(Color.FromArgb(0, 80, 150, 220), 1)
                        }
                    }
                };
                Canvas.SetLeft(core2, centerX - coreSize * 0.75);
                Canvas.SetTop(core2, centerY - coreSize * 0.75);
                canvas.Children.Add(core2);
                
                var core1 = new Ellipse
                {
                    Width = coreSize,
                    Height = coreSize,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb((byte)(220 + bassIntensity * 35), 255, 255, 255), 0),
                            new GradientStop(Color.FromArgb((byte)(180 + bassIntensity * 50), 220, 240, 255), 0.3),
                            new GradientStop(Color.FromArgb((byte)(100 + bassIntensity * 40), 180, 220, 255), 0.6),
                            new GradientStop(Color.FromArgb(0, 120, 180, 240), 1)
                        }
                    }
                };
                Canvas.SetLeft(core1, centerX - coreSize / 2);
                Canvas.SetTop(core1, centerY - coreSize / 2);
                canvas.Children.Add(core1);
            }
            else
            {
                // Dark inner when gate is inactive/dialing
                var innerCircle = new Ellipse
                {
                    Width = innerRadius * 2,
                    Height = innerRadius * 2,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(10, 10, 20), 0),
                            new GradientStop(Color.FromRgb(5, 5, 15), 1)
                        }
                    }
                };
                Canvas.SetLeft(innerCircle, centerX - innerRadius);
                Canvas.SetTop(innerCircle, centerY - innerRadius);
                canvas.Children.Add(innerCircle);
            }
            
            // ==== STATUS TEXT ====
            string statusText;
            if (_stargateIsDialing)
            {
                if (_stargateChevronEngaging)
                    statusText = $"CHEVRON {_stargateChevronLit + 1} ENGAGING...";
                else if (_stargateChevronLit > 0)
                    statusText = $"CHEVRON {_stargateChevronLit} LOCKED";
                else
                    statusText = "DIALING...";
            }
            else if (_stargateKawooshActive)
            {
                statusText = "WORMHOLE ESTABLISHED";
            }
            else
            {
                statusText = "EVENT HORIZON ACTIVE";
            }
            
            var dialingText = new TextBlock
            {
                Text = statusText,
                FontFamily = new FontFamily("Consolas"),
                FontSize = Math.Max(14, height * 0.025),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(!_stargateIsDialing 
                    ? Color.FromRgb(100, 200, 255) 
                    : Color.FromRgb(255, 180, 80))
            };
            // Position halfway between the stargate's right edge and the right edge of the canvas
            double stargateRightEdge = centerX + outerRadius;
            double textX = stargateRightEdge + (width - stargateRightEdge) / 2 - statusText.Length * 4;
            Canvas.SetLeft(dialingText, textX);
            Canvas.SetTop(dialingText, centerY - 10);
            canvas.Children.Add(dialingText);
        }
        
        /// <summary>
        /// Renders a Klingon Empire-themed visualization.
        /// Features bat'leth-shaped bars, angular Klingon design elements,
        /// blood red colors with metallic accents, and the Klingon trefoil emblem.
        /// Audio intensity drives the aggression of the visual elements.
        /// </summary>
        private void RenderKlingon(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double centerX = width / 2;
            double centerY = height / 2;
            
            if (width <= 0 || height <= 0) return;
            
            // Calculate average intensity and bass for reactive elements
            double avgIntensity = 0;
            double bassIntensity = 0;
            int bassCount = Math.Min(10, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++)
                bassIntensity += _smoothedData[i];
            bassIntensity = bassCount > 0 ? bassIntensity / bassCount : 0.3;
            
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            // === LOAD KLINGON ASSETS (cached) ===
            if (_klingonBackgroundBrush == null)
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri("pack://application:,,,/Assets/KlingonGlowLogo.png", UriKind.Absolute));
                    _klingonBackgroundBrush = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.Uniform  // Keep aspect ratio, fit within bounds
                    };
                }
                catch
                {
                    // Fallback to gradient if image not found
                    _klingonBackgroundBrush = null;
                }
            }
            
            if (_klingonFont == null)
            {
                try
                {
                    // Load Klingon font from Assets folder
                    var fontPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "KlingonFont.ttf");
                    
                    if (System.IO.File.Exists(fontPath))
                    {
                        // The font family name inside the TTF file is "klingon font"
                        _klingonFont = new FontFamily(new Uri("file:///" + fontPath.Replace("\\", "/")), "#klingon font");
                    }
                    else
                    {
                        _klingonFont = new FontFamily("Impact");
                    }
                }
                catch
                {
                    _klingonFont = new FontFamily("Impact");
                }
            }
            
            // === BACKGROUND - Dark gradient base ===
            var background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    RadiusX = 1.0,
                    RadiusY = 1.0,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(40, 10, 10), 0),
                        new GradientStop(Color.FromRgb(25, 5, 5), 0.5),
                        new GradientStop(Color.FromRgb(10, 0, 0), 1)
                    }
                }
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            canvas.Children.Add(background);
            
            // === KLINGON LOGO - Centered and fully visible ===
            if (_klingonBackgroundBrush != null)
            {
                // Calculate size to fit within canvas while maintaining aspect ratio
                double logoSize = Math.Min(width, height) * 0.7; // 70% of smaller dimension
                var logoRect = new Rectangle
                {
                    Width = logoSize,
                    Height = logoSize,
                    Fill = _klingonBackgroundBrush,
                    Opacity = 0.3 + bassIntensity * 0.2 // Pulse with bass
                };
                // Center the logo
                Canvas.SetLeft(logoRect, (width - logoSize) / 2);
                Canvas.SetTop(logoRect, (height - logoSize) / 2);
                canvas.Children.Add(logoRect);
            }
            else
            {
                // Fallback: render the trefoil emblem
                double emblemSize = Math.Min(width, height) * 0.25;
                double emblemPulse = 1.0 + bassIntensity * 0.15;
                byte emblemAlpha = (byte)(40 + bassIntensity * 60);
                RenderKlingonTrefoil(canvas, centerX, centerY, emblemSize * emblemPulse, emblemAlpha);
            }
            
            // === ANGULAR GRID LINES (Klingon aesthetic) ===
            byte gridAlpha = (byte)(30 + avgIntensity * 40);
            var gridBrush = new SolidColorBrush(Color.FromArgb(gridAlpha, 139, 0, 0));
            
            // Diagonal crossed lines radiating from center
            for (int i = 0; i < 8; i++)
            {
                double angle = i * 45 * Math.PI / 180;
                double lineLength = Math.Max(width, height);
                var line = new Line
                {
                    X1 = centerX,
                    Y1 = centerY,
                    X2 = centerX + Math.Cos(angle) * lineLength,
                    Y2 = centerY + Math.Sin(angle) * lineLength,
                    Stroke = gridBrush,
                    StrokeThickness = 1
                };
                canvas.Children.Add(line);
            }
            
            // === BAT'LETH-STYLE SPECTRUM BARS ===
            int barCount = Math.Min(_barCount, _smoothedData.Length);
            double barWidth = (width * 0.8) / barCount;
            double barSpacing = barWidth * 0.15;
            double effectiveBarWidth = barWidth - barSpacing;
            double barAreaLeft = width * 0.1;
            
            for (int i = 0; i < barCount; i++)
            {
                double value = i < _smoothedData.Length ? _smoothedData[i] : 0;
                double barHeight = Math.Max(4, value * height * 0.45 * _sensitivity);
                double x = barAreaLeft + i * barWidth;
                double y = centerY - barHeight;
                
                // Create angled/pointed bar (bat'leth blade style)
                double pointOffset = effectiveBarWidth * 0.3;
                
                // Upper bar (pointing up)
                var upperBlade = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(x, centerY),
                        new Point(x + pointOffset, y),
                        new Point(x + effectiveBarWidth - pointOffset, y),
                        new Point(x + effectiveBarWidth, centerY)
                    },
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 1),
                        EndPoint = new Point(0, 0),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(80, 0, 0), 0),
                            new GradientStop(Color.FromRgb(180, 30, 30), 0.5),
                            new GradientStop(Color.FromRgb(220, 160, 100), 0.9),
                            new GradientStop(Color.FromRgb(255, 200, 140), 1)
                        }
                    },
                    Effect = value > 0.5 ? new DropShadowEffect
                    {
                        Color = Color.FromRgb(255, 50, 50),
                        BlurRadius = value * 15,
                        ShadowDepth = 0,
                        Opacity = value
                    } : null
                };
                canvas.Children.Add(upperBlade);
                
                // Lower bar (mirrored, pointing down)
                var lowerBlade = new Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(x, centerY),
                        new Point(x + pointOffset, centerY + barHeight),
                        new Point(x + effectiveBarWidth - pointOffset, centerY + barHeight),
                        new Point(x + effectiveBarWidth, centerY)
                    },
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(0, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(80, 0, 0), 0),
                            new GradientStop(Color.FromRgb(180, 30, 30), 0.5),
                            new GradientStop(Color.FromRgb(220, 160, 100), 0.9),
                            new GradientStop(Color.FromRgb(255, 200, 140), 1)
                        }
                    },
                    Effect = value > 0.5 ? new DropShadowEffect
                    {
                        Color = Color.FromRgb(255, 50, 50),
                        BlurRadius = value * 15,
                        ShadowDepth = 0,
                        Opacity = value
                    } : null
                };
                canvas.Children.Add(lowerBlade);
                
                // Peak indicator (blade tip style)
                if (i < _peakHeights.Length && _peakHeights[i] > 0.01)
                {
                    double peakY = _peakHeights[i] * height * 0.45;
                    var peakMark = new Polygon
                    {
                        Points = new PointCollection
                        {
                            new Point(x + effectiveBarWidth * 0.3, centerY - peakY - 3),
                            new Point(x + effectiveBarWidth * 0.5, centerY - peakY - 8),
                            new Point(x + effectiveBarWidth * 0.7, centerY - peakY - 3)
                        },
                        Fill = new SolidColorBrush(Color.FromRgb(255, 200, 140))
                    };
                    canvas.Children.Add(peakMark);
                }
            }
            
            // === KLINGON TEXT - Using Latin transliteration for visibility ===
            if (avgIntensity > 0.4)
            {
                byte textAlpha = (byte)((avgIntensity - 0.4) * 400);
                var battleCry = new TextBlock
                {
                    Text = "Qapla'!",  // Success/Victory
                    FontFamily = new FontFamily("Impact"),
                    FontSize = Math.Max(32, height * 0.12),
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(textAlpha, 255, 200, 140)),
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 10,
                        ShadowDepth = 3,
                        Opacity = 0.9
                    }
                };
                Canvas.SetLeft(battleCry, width - 180);
                Canvas.SetTop(battleCry, 20);
                canvas.Children.Add(battleCry);
            }
            
            // === HONOR TEXT (Bottom) ===
            var honorText = new TextBlock
            {
                Text = "Honor - batlh",
                FontFamily = new FontFamily("pIqaD, klingon font, Impact, Arial"),
                FontSize = Math.Max(24, height * 0.055),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 180, 100)),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 6,
                    ShadowDepth = 2,
                    Opacity = 0.8
                }
            };
            Canvas.SetLeft(honorText, 20);
            Canvas.SetTop(honorText, height - 55);
            canvas.Children.Add(honorText);
            
            // === We Are Klingon (Top) ===
            var gloryText = new TextBlock
            {
                Text = "We Are Klingon - tlhIngan maH",
                FontFamily = new FontFamily("pIqaD, klingon font, Impact, Arial"),
                FontSize = Math.Max(18, height * 0.04),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(120 + avgIntensity * 80), 220, 160, 100)),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 5,
                    ShadowDepth = 2,
                    Opacity = 0.8
                }
            };
            Canvas.SetLeft(gloryText, 20);
            Canvas.SetTop(gloryText, 20);
            canvas.Children.Add(gloryText);
        }
        
        /// <summary>
        /// Renders the Klingon trefoil emblem (the three-pointed symbol).
        /// </summary>
        private void RenderKlingonTrefoil(Canvas canvas, double centerX, double centerY, double size, byte alpha)
        {
            // The Klingon emblem has 3 curved points arranged in a triangular pattern
            double pointLength = size * 0.6;
            double pointWidth = size * 0.25;
            
            // Draw three pointed sections
            for (int i = 0; i < 3; i++)
            {
                double angle = (i * 120 - 90) * Math.PI / 180; // Start at top, 120° apart
                double nextAngle = ((i + 1) * 120 - 90) * Math.PI / 180;
                
                double tipX = centerX + Math.Cos(angle) * pointLength;
                double tipY = centerY + Math.Sin(angle) * pointLength;
                
                // Create curved blade shape
                var blade = new PathGeometry();
                var figure = new PathFigure { StartPoint = new Point(centerX, centerY) };
                
                // Control points for bezier curve
                double ctrlDist = pointLength * 0.6;
                double innerAngle1 = angle - 0.3;
                double innerAngle2 = angle + 0.3;
                
                figure.Segments.Add(new BezierSegment(
                    new Point(centerX + Math.Cos(innerAngle1) * ctrlDist, centerY + Math.Sin(innerAngle1) * ctrlDist),
                    new Point(tipX + Math.Cos(angle - 0.5) * pointWidth, tipY + Math.Sin(angle - 0.5) * pointWidth),
                    new Point(tipX, tipY),
                    true
                ));
                
                figure.Segments.Add(new BezierSegment(
                    new Point(tipX + Math.Cos(angle + 0.5) * pointWidth, tipY + Math.Sin(angle + 0.5) * pointWidth),
                    new Point(centerX + Math.Cos(innerAngle2) * ctrlDist, centerY + Math.Sin(innerAngle2) * ctrlDist),
                    new Point(centerX, centerY),
                    true
                ));
                
                blade.Figures.Add(figure);
                
                var bladePath = new Path
                {
                    Data = blade,
                    Fill = new SolidColorBrush(Color.FromArgb(alpha, 139, 0, 0)),
                    Stroke = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.7), 100, 50, 50)),
                    StrokeThickness = 1
                };
                canvas.Children.Add(bladePath);
            }
            
            // Center circle
            var centerCircle = new Ellipse
            {
                Width = size * 0.2,
                Height = size * 0.2,
                Fill = new SolidColorBrush(Color.FromArgb(alpha, 60, 0, 0)),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(alpha * 0.8), 100, 50, 50)),
                StrokeThickness = 1
            };
            Canvas.SetLeft(centerCircle, centerX - size * 0.1);
            Canvas.SetTop(centerCircle, centerY - size * 0.1);
            canvas.Children.Add(centerCircle);
        }

        /// <summary>
        /// Renders a United Federation of Planets themed visualization.
        /// Features the Federation logo with a Voyager-style transporter beam effect.
        /// Glowing particles shimmer and cascade like the transporter dematerialization effect.
        /// </summary>
        private void RenderFederation(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            double centerX = width / 2;
            double centerY = height / 2;
            
            if (width <= 0 || height <= 0) return;
            
            // Advance transporter phase for animation
            _transporterPhase += 0.03;
            if (_transporterPhase > Math.PI * 2) _transporterPhase -= Math.PI * 2;
            
            // Calculate average intensity and treble for reactive elements
            double avgIntensity = 0;
            double trebleIntensity = 0;
            double bassIntensity = 0;
            
            int bassCount = Math.Min(10, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++)
                bassIntensity += _smoothedData[i];
            bassIntensity = bassCount > 0 ? bassIntensity / bassCount : 0.3;
            
            int trebleStart = _smoothedData.Length * 2 / 3;
            int trebleCount = _smoothedData.Length - trebleStart;
            for (int i = trebleStart; i < _smoothedData.Length; i++)
                trebleIntensity += _smoothedData[i];
            trebleIntensity = trebleCount > 0 ? trebleIntensity / trebleCount : 0.3;
            
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            // === LOAD FEDERATION ASSETS (cached) ===
            if (_federationBackgroundBrush == null)
            {
                try
                {
                    var bitmap = new BitmapImage(new Uri("pack://application:,,,/Assets/FederationLogoTransparent.png", UriKind.Absolute));
                    _federationBackgroundBrush = new ImageBrush(bitmap)
                    {
                        Stretch = Stretch.Uniform
                    };
                }
                catch
                {
                    _federationBackgroundBrush = null;
                }
            }
            
            // === BACKGROUND - Deep space gradient with subtle blue ===
            var background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.5, 0.5),
                    RadiusX = 1.2,
                    RadiusY = 1.2,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(20, 35, 55), 0),
                        new GradientStop(Color.FromRgb(10, 18, 30), 0.5),
                        new GradientStop(Color.FromRgb(5, 8, 15), 1)
                    }
                }
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            canvas.Children.Add(background);
            
            // === TRANSPORTER PARTICLE EFFECT ===
            // Maintain transporter particles
            int targetParticleCount = 200 + (int)(avgIntensity * 300);
            
            // Add new particles
            while (_transporterParticles.Count < targetParticleCount)
            {
                _transporterParticles.Add(new TransporterParticle
                {
                    X = centerX + (_random.NextDouble() - 0.5) * width * 0.7,
                    Y = _random.NextDouble() * height,
                    VelocityY = -0.5 - _random.NextDouble() * 2,
                    Size = 1 + _random.NextDouble() * 3,
                    Phase = _random.NextDouble() * Math.PI * 2,
                    Brightness = 0.3 + _random.NextDouble() * 0.7,
                    ColorType = _random.Next(3) // 0=blue, 1=gold, 2=white
                });
            }
            
            // Remove excess particles
            while (_transporterParticles.Count > targetParticleCount)
            {
                _transporterParticles.RemoveAt(_transporterParticles.Count - 1);
            }
            
            // Update and render transporter particles
            for (int i = _transporterParticles.Count - 1; i >= 0; i--)
            {
                var p = _transporterParticles[i];
                
                // Update position - cascade upward like transporter dematerialization
                p.Y += p.VelocityY * (1 + avgIntensity);
                p.Phase += 0.1;
                
                // Horizontal shimmer
                double shimmer = Math.Sin(p.Phase) * 3 * (1 + trebleIntensity);
                
                // Reset particles that go off screen
                if (p.Y < -10)
                {
                    p.Y = height + 10;
                    p.X = centerX + (_random.NextDouble() - 0.5) * width * 0.7;
                }
                
                // Pulsating brightness based on phase and audio
                double pulseBrightness = p.Brightness * (0.5 + 0.5 * Math.Sin(p.Phase + _transporterPhase));
                pulseBrightness *= (0.7 + bassIntensity * 0.6);
                
                // Determine color based on type
                Color particleColor;
                switch (p.ColorType)
                {
                    case 0: // Blue
                        particleColor = Color.FromArgb(
                            (byte)(pulseBrightness * 255),
                            100, 180, 255);
                        break;
                    case 1: // Gold
                        particleColor = Color.FromArgb(
                            (byte)(pulseBrightness * 255),
                            255, 215, 100);
                        break;
                    default: // White
                        particleColor = Color.FromArgb(
                            (byte)(pulseBrightness * 255),
                            230, 240, 255);
                        break;
                }
                
                // Render particle with glow effect
                var particle = new Ellipse
                {
                    Width = p.Size * (1 + trebleIntensity * 0.5),
                    Height = p.Size * (1 + trebleIntensity * 0.5),
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(particleColor, 0),
                            new GradientStop(Color.FromArgb(0, particleColor.R, particleColor.G, particleColor.B), 1)
                        }
                    }
                };
                Canvas.SetLeft(particle, p.X + shimmer - p.Size / 2);
                Canvas.SetTop(particle, p.Y - p.Size / 2);
                canvas.Children.Add(particle);
            }
            
            // === TRANSPORTER BEAM COLUMNS ===
            // Voyager-style vertical shimmering beams
            int beamCount = 5;
            double beamSpacing = width * 0.12;
            double beamStartX = centerX - (beamCount / 2.0) * beamSpacing;
            
            for (int i = 0; i < beamCount; i++)
            {
                double beamX = beamStartX + i * beamSpacing;
                double beamIntensity = _smoothedData[i * (_smoothedData.Length / beamCount)] * _sensitivity;
                
                // Main beam
                byte beamAlpha = (byte)(40 + beamIntensity * 120);
                var beam = new Rectangle
                {
                    Width = 8 + beamIntensity * 15,
                    Height = height,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 0),
                        EndPoint = new Point(0.5, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb(0, 100, 180, 255), 0),
                            new GradientStop(Color.FromArgb(beamAlpha, 150, 200, 255), 0.2),
                            new GradientStop(Color.FromArgb((byte)(beamAlpha * 1.2), 200, 230, 255), 0.5),
                            new GradientStop(Color.FromArgb(beamAlpha, 150, 200, 255), 0.8),
                            new GradientStop(Color.FromArgb(0, 100, 180, 255), 1)
                        }
                    },
                    Effect = new BlurEffect { Radius = 10 + beamIntensity * 8 }
                };
                Canvas.SetLeft(beam, beamX - beam.Width / 2);
                Canvas.SetTop(beam, 0);
                canvas.Children.Add(beam);
                
                // Shimmer effect - moving bright spots
                double shimmerY = (height * ((Math.Sin(_transporterPhase * 2 + i) + 1) / 2));
                var shimmerSpot = new Ellipse
                {
                    Width = 15 + beamIntensity * 20,
                    Height = 30 + beamIntensity * 40,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb((byte)(180 + beamIntensity * 75), 255, 255, 255), 0),
                            new GradientStop(Color.FromArgb((byte)(100 + beamIntensity * 50), 180, 220, 255), 0.4),
                            new GradientStop(Color.FromArgb(0, 100, 180, 255), 1)
                        }
                    }
                };
                Canvas.SetLeft(shimmerSpot, beamX - shimmerSpot.Width / 2);
                Canvas.SetTop(shimmerSpot, shimmerY - shimmerSpot.Height / 2);
                canvas.Children.Add(shimmerSpot);
            }
            
            // === FEDERATION LOGO - Centered with pulsating glow ===
            if (_federationBackgroundBrush != null)
            {
                double logoSize = Math.Min(width, height) * 0.5;
                double pulseScale = 1.0 + Math.Sin(_transporterPhase * 1.5) * 0.03 * (1 + bassIntensity);
                double actualSize = logoSize * pulseScale;
                
                // Outer glow
                var logoGlow = new Ellipse
                {
                    Width = actualSize * 1.5,
                    Height = actualSize * 1.5,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb((byte)(50 + bassIntensity * 70), 100, 180, 255), 0),
                            new GradientStop(Color.FromArgb((byte)(25 + bassIntensity * 35), 80, 150, 220), 0.5),
                            new GradientStop(Color.FromArgb(0, 50, 100, 180), 1)
                        }
                    }
                };
                Canvas.SetLeft(logoGlow, centerX - actualSize * 0.75);
                Canvas.SetTop(logoGlow, centerY - actualSize * 0.75);
                canvas.Children.Add(logoGlow);
                
                // Logo itself (transparent PNG)
                var logoRect = new Rectangle
                {
                    Width = actualSize,
                    Height = actualSize,
                    Fill = _federationBackgroundBrush,
                    Opacity = 0.9 + bassIntensity * 0.1
                };
                Canvas.SetLeft(logoRect, centerX - actualSize / 2);
                Canvas.SetTop(logoRect, centerY - actualSize / 2);
                canvas.Children.Add(logoRect);
            }
            
            // === SPECTRUM RING ===
            // Circular spectrum visualization around the logo
            double ringRadius = Math.Min(width, height) * 0.38;
            int segmentCount = Math.Min(32, _smoothedData.Length);
            
            for (int i = 0; i < segmentCount; i++)
            {
                double angle = (i * 360.0 / segmentCount - 90) * Math.PI / 180;
                double nextAngle = ((i + 1) * 360.0 / segmentCount - 90) * Math.PI / 180;
                double value = i < _smoothedData.Length ? _smoothedData[i] : 0;
                
                double innerR = ringRadius;
                double outerR = ringRadius + 10 + value * height * 0.15 * _sensitivity;
                
                // Create arc segment
                var segment = new PathGeometry();
                var figure = new PathFigure
                {
                    StartPoint = new Point(
                        centerX + Math.Cos(angle) * innerR,
                        centerY + Math.Sin(angle) * innerR)
                };
                
                figure.Segments.Add(new LineSegment(
                    new Point(centerX + Math.Cos(angle) * outerR, centerY + Math.Sin(angle) * outerR), true));
                figure.Segments.Add(new ArcSegment(
                    new Point(centerX + Math.Cos(nextAngle) * outerR, centerY + Math.Sin(nextAngle) * outerR),
                    new Size(outerR, outerR), 0, false, SweepDirection.Clockwise, true));
                figure.Segments.Add(new LineSegment(
                    new Point(centerX + Math.Cos(nextAngle) * innerR, centerY + Math.Sin(nextAngle) * innerR), true));
                figure.Segments.Add(new ArcSegment(
                    new Point(centerX + Math.Cos(angle) * innerR, centerY + Math.Sin(angle) * innerR),
                    new Size(innerR, innerR), 0, false, SweepDirection.Counterclockwise, true));
                
                segment.Figures.Add(figure);
                
                // Color gradient from blue to gold to white based on intensity
                byte r, g, b;
                if (value < 0.4)
                {
                    r = (byte)(80 + value * 150);
                    g = (byte)(150 + value * 100);
                    b = 255;
                }
                else if (value < 0.7)
                {
                    double t = (value - 0.4) / 0.3;
                    r = (byte)(140 + t * 115);
                    g = (byte)(190 + t * 25);
                    b = (byte)(255 - t * 100);
                }
                else
                {
                    r = 255;
                    g = (byte)(215 + (value - 0.7) * 40);
                    b = (byte)(155 + (value - 0.7) * 100);
                }
                
                var segmentPath = new Path
                {
                    Data = segment,
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(150 + value * 105), r, g, b)),
                    Effect = value > 0.5 ? new BlurEffect { Radius = 3 } : null
                };
                canvas.Children.Add(segmentPath);
            }
            
            // === STARFLEET TEXT ===
            var starfleetText = new TextBlock
            {
                Text = "UNITED FEDERATION OF PLANETS",
                FontFamily = new FontFamily("Segoe UI, Arial"),
                FontSize = Math.Max(14, height * 0.025),
                FontWeight = FontWeights.Light,
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(150 + avgIntensity * 100), 180, 210, 255)),
                TextAlignment = TextAlignment.Center
            };
            starfleetText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(starfleetText, centerX - starfleetText.DesiredSize.Width / 2);
            Canvas.SetTop(starfleetText, height - 40);
            canvas.Children.Add(starfleetText);
            
            // === STARDATE ===
            var stardate = new TextBlock
            {
                Text = $"STARDATE {DateTime.Now:yyMMdd}.{DateTime.Now:HHmm}",
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = Math.Max(12, height * 0.02),
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(120 + avgIntensity * 80), 150, 190, 230))
            };
            Canvas.SetLeft(stardate, 15);
            Canvas.SetTop(stardate, 15);
            canvas.Children.Add(stardate);
        }

        /// <summary>
        /// Renders the Jedi visualizer with lightsabers and R2-D2 translator text.
        /// </summary>
        private void RenderJedi(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            
            if (width <= 0 || height <= 0) return;
            
            // Calculate audio metrics
            double avgIntensity = 0;
            double bassIntensity = 0;
            double trebleIntensity = 0;
            
            int bassCount = Math.Min(10, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++)
                bassIntensity += _smoothedData[i];
            bassIntensity = bassCount > 0 ? bassIntensity / bassCount : 0.3;
            
            int trebleStart = _smoothedData.Length * 2 / 3;
            int trebleCount = _smoothedData.Length - trebleStart;
            for (int i = trebleStart; i < _smoothedData.Length; i++)
                trebleIntensity += _smoothedData[i];
            trebleIntensity = trebleCount > 0 ? trebleIntensity / trebleCount : 0.3;
            
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            // Advance animation
            _jediTextScrollOffset += 0.5 + avgIntensity * 0.5;
            
            // === LOAD LIGHTSABER HILT IMAGE (cached) ===
            if (_lightsaberHiltImage == null)
            {
                try
                {
                    _lightsaberHiltImage = new BitmapImage(new Uri("pack://application:,,,/Assets/lightsaber.png", UriKind.Absolute));
                }
                catch
                {
                    _lightsaberHiltImage = null;
                }
            }
            
            // === SPACE BACKGROUND ===
            var background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromRgb(0, 0, 15), 0),
                        new GradientStop(Color.FromRgb(5, 5, 25), 0.5),
                        new GradientStop(Color.FromRgb(0, 0, 10), 1)
                    }
                }
            };
            Canvas.SetLeft(background, 0);
            Canvas.SetTop(background, 0);
            canvas.Children.Add(background);
            
            // === STARS ===
            for (int i = 0; i < 80; i++)
            {
                double starX = (i * 47) % width;
                double starY = (i * 31) % height;
                double twinkle = 0.3 + 0.7 * Math.Sin(_animationPhase * 2 + i * 0.5);
                
                var star = new Ellipse
                {
                    Width = 1 + (i % 3),
                    Height = 1 + (i % 3),
                    Fill = new SolidColorBrush(Color.FromArgb((byte)(100 + twinkle * 155), 255, 255, 255))
                };
                Canvas.SetLeft(star, starX);
                Canvas.SetTop(star, starY);
                canvas.Children.Add(star);
            }
            
            // === LIGHTSABERS ===
            // Lightsaber colors: 0=blue (Luke), 1=green (Luke ROTJ), 2=purple (Mace), 3=red (enemy)
            Color[] saberColors = {
                Color.FromRgb(100, 180, 255),  // Blue
                Color.FromRgb(100, 255, 100),  // Green
                Color.FromRgb(180, 100, 255),  // Purple
                Color.FromRgb(255, 50, 50)     // Red (occasional)
            };
            
            // Responsive saber count and sizing based on window size
            int saberCount = width < 600 ? 8 : (width < 1000 ? 12 : 16);
            saberCount = Math.Min(saberCount, _smoothedData.Length);
            
            // Fixed blade width (8-12 pixels) regardless of window size
            double bladeWidth = width < 600 ? 8 : (width < 1000 ? 10 : 12);
            double saberSpacing = (width * 0.5) / saberCount;
            double saberAreaStart = width * 0.05;
            
            // Hilt size scales with blade width
            double hiltWidth = bladeWidth * 4;
            double hiltHeight = Math.Min(80, height * 0.12);
            
            for (int i = 0; i < saberCount; i++)
            {
                int freqIndex = i * _smoothedData.Length / saberCount;
                double value = freqIndex < _smoothedData.Length ? _smoothedData[freqIndex] * _sensitivity : 0;
                double x = saberAreaStart + i * saberSpacing;
                double saberHeight = 30 + value * height * 0.5;
                double saberBottom = height - hiltHeight - 10;
                
                // Determine color based on position (mostly blue/green with occasional purple)
                int colorIndex = i % 5 == 0 ? 2 : (i % 2);
                Color saberColor = saberColors[colorIndex];
                
                // Hilt - use image if available, otherwise fallback to rectangle
                if (_lightsaberHiltImage != null)
                {
                    var hiltImage = new Image
                    {
                        Source = _lightsaberHiltImage,
                        Width = hiltWidth,
                        Height = hiltHeight,
                        Stretch = Stretch.Uniform
                    };
                    Canvas.SetLeft(hiltImage, x + (saberSpacing - hiltWidth) / 2);
                    Canvas.SetTop(hiltImage, saberBottom);
                    canvas.Children.Add(hiltImage);
                }
                else
                {
                    // Fallback hilt (metallic gray)
                    var hilt = new Rectangle
                    {
                        Width = hiltWidth,
                        Height = hiltHeight * 0.5,
                        Fill = new LinearGradientBrush
                        {
                            StartPoint = new Point(0, 0.5),
                            EndPoint = new Point(1, 0.5),
                            GradientStops = new GradientStopCollection
                            {
                                new GradientStop(Color.FromRgb(80, 80, 90), 0),
                                new GradientStop(Color.FromRgb(150, 150, 160), 0.3),
                                new GradientStop(Color.FromRgb(100, 100, 110), 0.7),
                                new GradientStop(Color.FromRgb(60, 60, 70), 1)
                            }
                        },
                        RadiusX = 2,
                        RadiusY = 2
                    };
                    Canvas.SetLeft(hilt, x + (saberSpacing - hiltWidth) / 2);
                    Canvas.SetTop(hilt, saberBottom);
                    canvas.Children.Add(hilt);
                }
                
                // Blade glow (outer) - width is 3x blade for glow effect
                double glowWidth = bladeWidth * 3;
                var bladeGlow = new Rectangle
                {
                    Width = glowWidth,
                    Height = saberHeight + 20,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 1),
                        EndPoint = new Point(0.5, 0),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromArgb((byte)(100 + value * 100), saberColor.R, saberColor.G, saberColor.B), 0),
                            new GradientStop(Color.FromArgb((byte)(60 + value * 60), saberColor.R, saberColor.G, saberColor.B), 0.5),
                            new GradientStop(Color.FromArgb(0, saberColor.R, saberColor.G, saberColor.B), 1)
                        }
                    },
                    Effect = new BlurEffect { Radius = 15 + value * 10 },
                    RadiusX = glowWidth / 2,
                    RadiusY = 5
                };
                Canvas.SetLeft(bladeGlow, x + (saberSpacing - glowWidth) / 2);
                Canvas.SetTop(bladeGlow, saberBottom - saberHeight - 10);
                canvas.Children.Add(bladeGlow);
                
                // Blade core (bright white/color center)
                var bladeCore = new Rectangle
                {
                    Width = bladeWidth,
                    Height = saberHeight,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0.5, 1),
                        EndPoint = new Point(0.5, 0),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Colors.White, 0),
                            new GradientStop(Color.FromArgb(255, 
                                (byte)Math.Min(255, saberColor.R + 100), 
                                (byte)Math.Min(255, saberColor.G + 100), 
                                (byte)Math.Min(255, saberColor.B + 100)), 0.3),
                            new GradientStop(saberColor, 0.8),
                            new GradientStop(Color.FromArgb(200, saberColor.R, saberColor.G, saberColor.B), 1)
                        }
                    },
                    RadiusX = bladeWidth / 2,
                    RadiusY = 3
                };
                Canvas.SetLeft(bladeCore, x + (saberSpacing - bladeWidth) / 2);
                Canvas.SetTop(bladeCore, saberBottom - saberHeight);
                canvas.Children.Add(bladeCore);
            }
            
            // === R2-D2 TRANSLATOR TEXT PANEL ===
            double textPanelWidth = width * 0.38;
            double textPanelX = width * 0.58;
            double textPanelY = 30;
            double textPanelHeight = height - 100;
            
            // Panel background
            var textPanel = new Rectangle
            {
                Width = textPanelWidth,
                Height = textPanelHeight,
                Fill = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
                Stroke = new SolidColorBrush(Color.FromArgb(150, 100, 180, 255)),
                StrokeThickness = 1,
                RadiusX = 5,
                RadiusY = 5
            };
            Canvas.SetLeft(textPanel, textPanelX);
            Canvas.SetTop(textPanel, textPanelY);
            canvas.Children.Add(textPanel);
            
            // Panel header
            var headerText = new TextBlock
            {
                Text = "◈ X-WING COMM CHANNEL ◈",
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = Math.Max(11, height * 0.02),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 200, 255)),
                TextAlignment = TextAlignment.Center,
                Width = textPanelWidth - 20
            };
            Canvas.SetLeft(headerText, textPanelX + 10);
            Canvas.SetTop(headerText, textPanelY + 8);
            canvas.Children.Add(headerText);
            
            // Divider line
            var divider = new Rectangle
            {
                Width = textPanelWidth - 20,
                Height = 1,
                Fill = new SolidColorBrush(Color.FromArgb(100, 100, 180, 255))
            };
            Canvas.SetLeft(divider, textPanelX + 10);
            Canvas.SetTop(divider, textPanelY + 32);
            canvas.Children.Add(divider);
            
            // Scrolling messages
            double lineHeight = Math.Max(18, height * 0.028);
            double textStartY = textPanelY + 45;
            double visibleLines = (textPanelHeight - 60) / lineHeight;
            int scrollOffset = (int)(_jediTextScrollOffset / 30) % _r2d2Messages.Count;
            
            for (int i = 0; i < (int)visibleLines + 2; i++)
            {
                int msgIndex = (scrollOffset + i) % _r2d2Messages.Count;
                double yOffset = ((_jediTextScrollOffset % 30) / 30) * lineHeight;
                double y = textStartY + i * lineHeight - yOffset;
                
                if (y < textStartY - lineHeight || y > textStartY + textPanelHeight - 50) continue;
                
                string msg = _r2d2Messages[msgIndex];
                bool isR2 = msg.StartsWith("[R2");
                
                // Glow effect for R2's beeps based on audio
                byte glowAlpha = isR2 ? (byte)(180 + avgIntensity * 75) : (byte)220;
                Color textColor = isR2 
                    ? Color.FromArgb(glowAlpha, 100, 220, 255)
                    : Color.FromArgb(220, 255, 200, 100);
                
                var msgText = new TextBlock
                {
                    Text = msg,
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = Math.Max(10, height * 0.018),
                    Foreground = new SolidColorBrush(textColor),
                    Width = textPanelWidth - 25,
                    TextWrapping = TextWrapping.Wrap
                };
                
                if (isR2 && avgIntensity > 0.4)
                {
                    msgText.Effect = new BlurEffect { Radius = 1 + avgIntensity * 2 };
                }
                
                Canvas.SetLeft(msgText, textPanelX + 12);
                Canvas.SetTop(msgText, y);
                canvas.Children.Add(msgText);
            }
            
            // === FORCE PARTICLES ===
            // Small glowing particles that react to music
            int particleCount = 15 + (int)(avgIntensity * 20);
            for (int i = 0; i < particleCount; i++)
            {
                double px = ((i * 73 + _animationPhase * 20) % (width * 0.5)) + width * 0.02;
                double py = ((i * 47 + _animationPhase * 15) % (height - 100)) + 20;
                double pSize = 2 + (i % 4) + bassIntensity * 3;
                double pAlpha = 0.3 + 0.5 * Math.Sin(_animationPhase + i);
                
                // Blue or green particles
                Color pColor = i % 2 == 0 
                    ? Color.FromArgb((byte)(pAlpha * 200), 100, 180, 255)
                    : Color.FromArgb((byte)(pAlpha * 200), 100, 255, 150);
                
                var particle = new Ellipse
                {
                    Width = pSize,
                    Height = pSize,
                    Fill = new RadialGradientBrush
                    {
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(pColor, 0),
                            new GradientStop(Color.FromArgb(0, pColor.R, pColor.G, pColor.B), 1)
                        }
                    }
                };
                Canvas.SetLeft(particle, px);
                Canvas.SetTop(particle, py);
                canvas.Children.Add(particle);
            }
            
            // === JEDI ORDER EMBLEM (subtle) ===
            double emblemSize = Math.Min(width, height) * 0.15;
            double emblemX = width * 0.28;
            double emblemY = 30;
            
            // Outer wings shape (simplified Jedi Order symbol)
            var emblemPath = new Path
            {
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(60 + bassIntensity * 60), 200, 180, 100)),
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb((byte)(20 + bassIntensity * 20), 200, 180, 100)),
                Effect = new BlurEffect { Radius = 2 }
            };
            
            var emblemGeometry = new PathGeometry();
            var emblemFigure = new PathFigure
            {
                StartPoint = new Point(emblemX, emblemY + emblemSize * 0.5),
                IsClosed = true
            };
            // Create stylized wing shape
            emblemFigure.Segments.Add(new BezierSegment(
                new Point(emblemX + emblemSize * 0.3, emblemY),
                new Point(emblemX + emblemSize * 0.7, emblemY),
                new Point(emblemX + emblemSize, emblemY + emblemSize * 0.5),
                true));
            emblemFigure.Segments.Add(new BezierSegment(
                new Point(emblemX + emblemSize * 0.7, emblemY + emblemSize),
                new Point(emblemX + emblemSize * 0.3, emblemY + emblemSize),
                new Point(emblemX, emblemY + emblemSize * 0.5),
                true));
            emblemGeometry.Figures.Add(emblemFigure);
            emblemPath.Data = emblemGeometry;
            canvas.Children.Add(emblemPath);
            
            // === QUOTE ===
            var quote = new TextBlock
            {
                Text = "\"The Force will be with you. Always.\"",
                FontFamily = new FontFamily("Georgia, Times New Roman"),
                FontSize = Math.Max(12, height * 0.022),
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(120 + avgIntensity * 80), 200, 180, 100)),
                TextAlignment = TextAlignment.Center
            };
            quote.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(quote, (width * 0.5 - quote.DesiredSize.Width) / 2);
            Canvas.SetTop(quote, height - 35);
            canvas.Children.Add(quote);
        }

        /// <summary>
        /// Renders the Time Lord visualizer with swirling time vortex tunnel and flying TARDIS.
        /// Uses the TimeVortex.png image with multiple rotating/scaling layers for depth.
        /// </summary>
        private void RenderTimeLord(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            
            if (width <= 0 || height <= 0) return;
            
            // Calculate audio metrics with smoothing
            double avgIntensity = 0;
            double bassIntensity = 0;
            double midIntensity = 0;
            double trebleIntensity = 0;
            
            int bassCount = Math.Min(8, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++)
                bassIntensity += _smoothedData[i];
            bassIntensity = bassCount > 0 ? bassIntensity / bassCount : 0.3;
            
            int midStart = _smoothedData.Length / 3;
            int midEnd = _smoothedData.Length * 2 / 3;
            for (int i = midStart; i < midEnd; i++)
                midIntensity += _smoothedData[i];
            midIntensity = (midEnd - midStart) > 0 ? midIntensity / (midEnd - midStart) : 0.3;
            
            int trebleStart = _smoothedData.Length * 2 / 3;
            for (int i = trebleStart; i < _smoothedData.Length; i++)
                trebleIntensity += _smoothedData[i];
            trebleIntensity = (_smoothedData.Length - trebleStart) > 0 ? trebleIntensity / (_smoothedData.Length - trebleStart) : 0.3;
            
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            // Load images (cached)
            if (_timeVortexImage == null)
            {
                try
                {
                    _timeVortexImage = new BitmapImage(new Uri("pack://application:,,,/Assets/TimeVortex.png", UriKind.Absolute));
                }
                catch { _timeVortexImage = null; }
            }
            
            if (_tardisImage == null)
            {
                try
                {
                    _tardisImage = new BitmapImage(new Uri("pack://application:,,,/Assets/Tardis.png", UriKind.Absolute));
                }
                catch { _tardisImage = null; }
            }
            
            // === SMOOTH ANIMATION UPDATE ===
            double rotationSpeed = 0.8 + bassIntensity * 2.0 + avgIntensity * 1.0;
            _vortexRotation += rotationSpeed;
            if (_vortexRotation >= 360) _vortexRotation -= 360;
            
            // TARDIS smooth orbital movement
            double orbitSpeed = 0.015 + avgIntensity * 0.025;
            _animationPhase += orbitSpeed;
            
            // Smooth spiral path for TARDIS
            double spiralRadius = 0.12 + bassIntensity * 0.08;
            double targetX = 0.5 + Math.Sin(_animationPhase * 1.1) * spiralRadius 
                                + Math.Sin(_animationPhase * 2.3) * 0.04;
            double targetY = 0.5 + Math.Cos(_animationPhase * 0.8) * spiralRadius * 0.8
                                + Math.Cos(_animationPhase * 1.9) * 0.03;
            
            // Smooth interpolation
            _tardisX += (targetX - _tardisX) * 0.18;
            _tardisY += (targetY - _tardisY) * 0.18;
            
            // Gentle tumble
            double moveDeltaX = targetX - _tardisX;
            _tardisTumble = _tardisTumble * 0.92 + moveDeltaX * 40;
            
            // Scale pulses with bass
            double targetScale = 1.0 + bassIntensity * 0.25;
            _tardisScale += (targetScale - _tardisScale) * 0.12;
            
            double centerX = width / 2;
            double centerY = height / 2;
            
            // === BLACK BACKGROUND ===
            var background = new Rectangle
            {
                Width = width,
                Height = height,
                Fill = new SolidColorBrush(Colors.Black)
            };
            canvas.Children.Add(background);
            
            // === LAYERED VORTEX IMAGES WITH BLUR ===
            if (_timeVortexImage != null)
            {
                double baseSize = Math.Max(width, height) * 1.8;
                
                // Layer 1: Outermost - large, slow, heavily blurred
                double layer1Size = baseSize * (1.4 + avgIntensity * 0.2);
                var vortexLayer1 = new Image
                {
                    Source = _timeVortexImage,
                    Width = layer1Size,
                    Height = layer1Size,
                    Stretch = Stretch.Uniform,
                    Opacity = 0.5 + avgIntensity * 0.2,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(_vortexRotation * 0.25),
                    Effect = new BlurEffect { Radius = 25 }
                };
                Canvas.SetLeft(vortexLayer1, centerX - layer1Size / 2);
                Canvas.SetTop(vortexLayer1, centerY - layer1Size / 2);
                canvas.Children.Add(vortexLayer1);
                
                // Layer 2: Medium blur, counter rotation
                double layer2Size = baseSize * (1.1 + avgIntensity * 0.15);
                var vortexLayer2 = new Image
                {
                    Source = _timeVortexImage,
                    Width = layer2Size,
                    Height = layer2Size,
                    Stretch = Stretch.Uniform,
                    Opacity = 0.6 + avgIntensity * 0.25,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(-_vortexRotation * 0.4),
                    Effect = new BlurEffect { Radius = 15 }
                };
                Canvas.SetLeft(vortexLayer2, centerX - layer2Size / 2);
                Canvas.SetTop(vortexLayer2, centerY - layer2Size / 2);
                canvas.Children.Add(vortexLayer2);
                
                // Layer 3: Light blur
                double layer3Size = baseSize * (0.85 + avgIntensity * 0.12);
                var vortexLayer3 = new Image
                {
                    Source = _timeVortexImage,
                    Width = layer3Size,
                    Height = layer3Size,
                    Stretch = Stretch.Uniform,
                    Opacity = 0.75 + avgIntensity * 0.2,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(_vortexRotation * 0.6),
                    Effect = new BlurEffect { Radius = 8 }
                };
                Canvas.SetLeft(vortexLayer3, centerX - layer3Size / 2);
                Canvas.SetTop(vortexLayer3, centerY - layer3Size / 2);
                canvas.Children.Add(vortexLayer3);
                
                // Layer 4: Sharp core
                double layer4Size = baseSize * (0.6 + bassIntensity * 0.15);
                var vortexLayer4 = new Image
                {
                    Source = _timeVortexImage,
                    Width = layer4Size,
                    Height = layer4Size,
                    Stretch = Stretch.Uniform,
                    Opacity = 0.9 + bassIntensity * 0.1,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(-_vortexRotation * 0.9),
                    Effect = new BlurEffect { Radius = 3 }
                };
                Canvas.SetLeft(vortexLayer4, centerX - layer4Size / 2);
                Canvas.SetTop(vortexLayer4, centerY - layer4Size / 2);
                canvas.Children.Add(vortexLayer4);
                
                // Layer 5: Innermost - pulsing, sharp
                double layer5Size = baseSize * (0.35 + bassIntensity * 0.2);
                var vortexLayer5 = new Image
                {
                    Source = _timeVortexImage,
                    Width = layer5Size,
                    Height = layer5Size,
                    Stretch = Stretch.Uniform,
                    Opacity = 0.95,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(_vortexRotation * 1.2)
                };
                Canvas.SetLeft(vortexLayer5, centerX - layer5Size / 2);
                Canvas.SetTop(vortexLayer5, centerY - layer5Size / 2);
                canvas.Children.Add(vortexLayer5);
            }
            
            // === TARDIS ===
            double tardisBaseSize = Math.Min(width, height) * 0.18;
            double tardisDisplaySize = tardisBaseSize * _tardisScale;
            double tardisDisplayX = _tardisX * width - tardisDisplaySize / 2;
            double tardisDisplayY = _tardisY * height - tardisDisplaySize * 0.7;
            
            // Glow behind TARDIS
            double glowRadius = tardisDisplaySize * 1.2;
            var tardisGlow = new Ellipse
            {
                Width = glowRadius * 2,
                Height = glowRadius * 2,
                Fill = new RadialGradientBrush
                {
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb((byte)(100 + avgIntensity * 100), 100, 180, 255), 0),
                        new GradientStop(Color.FromArgb((byte)(50 + avgIntensity * 50), 80, 120, 200), 0.5),
                        new GradientStop(Color.FromArgb(0, 50, 80, 150), 1)
                    }
                }
            };
            Canvas.SetLeft(tardisGlow, tardisDisplayX + tardisDisplaySize / 2 - glowRadius);
            Canvas.SetTop(tardisGlow, tardisDisplayY + tardisDisplaySize * 0.7 - glowRadius);
            canvas.Children.Add(tardisGlow);
            
            if (_tardisImage != null)
            {
                var tardis = new Image
                {
                    Source = _tardisImage,
                    Width = tardisDisplaySize,
                    Height = tardisDisplaySize * 1.4,
                    Stretch = Stretch.Uniform,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new TransformGroup
                    {
                        Children = new TransformCollection
                        {
                            new RotateTransform(_tardisTumble * 0.2),
                            new ScaleTransform(_tardisScale, _tardisScale)
                        }
                    }
                };
                Canvas.SetLeft(tardis, tardisDisplayX);
                Canvas.SetTop(tardis, tardisDisplayY);
                canvas.Children.Add(tardis);
            }
            else
            {
                // Fallback TARDIS box
                var tardisBox = new Rectangle
                {
                    Width = tardisDisplaySize * 0.45,
                    Height = tardisDisplaySize * 1.0,
                    Fill = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(Color.FromRgb(0, 60, 140), 0),
                            new GradientStop(Color.FromRgb(0, 40, 100), 1)
                        }
                    },
                    Stroke = new SolidColorBrush(Color.FromRgb(0, 100, 200)),
                    StrokeThickness = 2,
                    RadiusX = 3,
                    RadiusY = 3,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = new RotateTransform(_tardisTumble * 0.2)
                };
                Canvas.SetLeft(tardisBox, tardisDisplayX + tardisDisplaySize * 0.275);
                Canvas.SetTop(tardisBox, tardisDisplayY + tardisDisplaySize * 0.2);
                canvas.Children.Add(tardisBox);
            }
            
            // === STATUS TEXT ===
            var statusText = new TextBlock
            {
                Text = "◈ TIME VORTEX ACTIVE ◈",
                FontFamily = new FontFamily("Consolas, Courier New"),
                FontSize = Math.Max(14, height * 0.025),
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(180 + avgIntensity * 75), 150, 100, 255))
            };
            statusText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(statusText, (width - statusText.DesiredSize.Width) / 2);
            Canvas.SetTop(statusText, 15);
            canvas.Children.Add(statusText);
            
            // === QUOTE ===
            var quote = new TextBlock
            {
                Text = "\"Allons-y!\"",
                FontFamily = new FontFamily("Georgia, Times New Roman"),
                FontSize = Math.Max(14, height * 0.028),
                FontStyle = FontStyles.Italic,
                Foreground = new SolidColorBrush(Color.FromArgb((byte)(150 + bassIntensity * 100), 200, 150, 255))
            };
            quote.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(quote, (width - quote.DesiredSize.Width) / 2);
            Canvas.SetTop(quote, height - 45);
            canvas.Children.Add(quote);
        }
        
        /// <summary>
        /// Renders VU Meter style visualization with left/right channel meters.
        /// </summary>
        private void RenderVUMeter(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            // Get VU levels from ViewModel
            double peakLeft = 0, peakRight = 0;
            if (DataContext is EnhancedAudioPlayerViewModel vm)
            {
                peakLeft = vm.VUPeakLeft;
                peakRight = vm.VUPeakRight;
            }
            else
            {
                // Fallback: derive from spectrum data
                if (_smoothedData.Length > 1)
                {
                    peakLeft = _smoothedData.Take(_smoothedData.Length / 2).Average();
                    peakRight = _smoothedData.Skip(_smoothedData.Length / 2).Average();
                }
            }
            
            double meterWidth = width * 0.35;
            double meterHeight = height * 0.6;
            double meterSpacing = width * 0.1;
            double meterY = (height - meterHeight) / 2;
            double leftMeterX = (width / 2) - meterWidth - (meterSpacing / 2);
            double rightMeterX = (width / 2) + (meterSpacing / 2);
            
            // Draw meter backgrounds
            var bgBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            
            var leftBg = new Rectangle
            {
                Width = meterWidth,
                Height = meterHeight,
                Fill = bgBrush,
                Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                StrokeThickness = 2,
                RadiusX = 4,
                RadiusY = 4
            };
            Canvas.SetLeft(leftBg, leftMeterX);
            Canvas.SetTop(leftBg, meterY);
            canvas.Children.Add(leftBg);
            
            var rightBg = new Rectangle
            {
                Width = meterWidth,
                Height = meterHeight,
                Fill = bgBrush,
                Stroke = new SolidColorBrush(Color.FromRgb(80, 80, 80)),
                StrokeThickness = 2,
                RadiusX = 4,
                RadiusY = 4
            };
            Canvas.SetLeft(rightBg, rightMeterX);
            Canvas.SetTop(rightBg, meterY);
            canvas.Children.Add(rightBg);
            
            // Draw meter fills using color scheme
            double leftFillHeight = meterHeight * Math.Clamp(peakLeft, 0, 1);
            double rightFillHeight = meterHeight * Math.Clamp(peakRight, 0, 1);
            
            // Get colors from the current color scheme
            var colorLow = GetColorFromScheme(0.0);
            var colorMid = GetColorFromScheme(0.5);
            var colorHigh = GetColorFromScheme(1.0);
            
            var meterGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 1),
                EndPoint = new Point(0, 0),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(colorLow, 0),
                    new GradientStop(colorMid, 0.5),
                    new GradientStop(colorHigh, 0.85),
                    new GradientStop(Colors.Red, 1.0)
                }
            };
            
            var leftFill = new Rectangle
            {
                Width = meterWidth - 8,
                Height = leftFillHeight,
                Fill = meterGradient
            };
            Canvas.SetLeft(leftFill, leftMeterX + 4);
            Canvas.SetTop(leftFill, meterY + meterHeight - leftFillHeight - 4);
            canvas.Children.Add(leftFill);
            
            var rightFill = new Rectangle
            {
                Width = meterWidth - 8,
                Height = rightFillHeight,
                Fill = meterGradient.Clone()
            };
            Canvas.SetLeft(rightFill, rightMeterX + 4);
            Canvas.SetTop(rightFill, meterY + meterHeight - rightFillHeight - 4);
            canvas.Children.Add(rightFill);
            
            // Channel labels
            var leftLabel = new TextBlock
            {
                Text = "L",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(leftLabel, leftMeterX + meterWidth / 2 - 8);
            Canvas.SetTop(leftLabel, meterY + meterHeight + 10);
            canvas.Children.Add(leftLabel);
            
            var rightLabel = new TextBlock
            {
                Text = "R",
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Canvas.SetLeft(rightLabel, rightMeterX + meterWidth / 2 - 8);
            Canvas.SetTop(rightLabel, meterY + meterHeight + 10);
            canvas.Children.Add(rightLabel);
            
            // dB scale markers
            string[] dbMarkers = { "0", "-6", "-12", "-24", "-48" };
            double[] dbPositions = { 0, 0.25, 0.5, 0.75, 1.0 };
            for (int i = 0; i < dbMarkers.Length; i++)
            {
                var marker = new TextBlock
                {
                    Text = dbMarkers[i],
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150))
                };
                Canvas.SetLeft(marker, leftMeterX - 30);
                Canvas.SetTop(marker, meterY + (meterHeight * dbPositions[i]) - 6);
                canvas.Children.Add(marker);
            }
        }
        
        /// <summary>
        /// Renders oscilloscope-style waveform visualization.
        /// </summary>
        private void RenderOscilloscope(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            // Get oscilloscope data from ViewModel if available
            float[]? oscData = null;
            if (DataContext is EnhancedAudioPlayerViewModel vm)
            {
                oscData = vm.OscilloscopeData;
            }
            
            // Get grid color from scheme (dimmed)
            var schemeColor = GetColorFromScheme(0.3);
            var gridColor = Color.FromArgb(80, schemeColor.R, schemeColor.G, schemeColor.B);
            var gridBrush = new SolidColorBrush(gridColor);
            var centerLineColor = Color.FromArgb(150, schemeColor.R, schemeColor.G, schemeColor.B);
            
            // Horizontal center line
            var centerLine = new Line
            {
                X1 = 0,
                Y1 = height / 2,
                X2 = width,
                Y2 = height / 2,
                Stroke = new SolidColorBrush(centerLineColor),
                StrokeThickness = 1
            };
            canvas.Children.Add(centerLine);
            
            // Vertical grid lines
            for (int i = 1; i < 10; i++)
            {
                var vLine = new Line
                {
                    X1 = width * i / 10,
                    Y1 = 0,
                    X2 = width * i / 10,
                    Y2 = height,
                    Stroke = gridBrush,
                    StrokeThickness = 0.5
                };
                canvas.Children.Add(vLine);
            }
            
            // Horizontal grid lines
            for (int i = 1; i < 8; i++)
            {
                var hLine = new Line
                {
                    X1 = 0,
                    Y1 = height * i / 8,
                    X2 = width,
                    Y2 = height * i / 8,
                    Stroke = gridBrush,
                    StrokeThickness = 0.5
                };
                canvas.Children.Add(hLine);
            }
            
            // Draw waveform
            var waveColor = GetColorFromScheme(0.5);
            var waveBrush = new SolidColorBrush(waveColor);
            
            if (oscData != null && oscData.Length > 1)
            {
                var polyline = new Polyline
                {
                    Stroke = waveBrush,
                    StrokeThickness = 2,
                    Points = new PointCollection()
                };
                
                double centerY = height / 2;
                double amplitude = height * 0.4;
                double step = width / (oscData.Length - 1);
                
                for (int i = 0; i < oscData.Length; i++)
                {
                    double x = i * step;
                    double y = centerY - (oscData[i] * amplitude);
                    polyline.Points.Add(new Point(x, y));
                }
                
                canvas.Children.Add(polyline);
            }
            else if (_smoothedData.Length > 1)
            {
                // Fallback to spectrum data as pseudo-waveform
                var polyline = new Polyline
                {
                    Stroke = waveBrush,
                    StrokeThickness = 2,
                    Points = new PointCollection()
                };
                
                double centerY = height / 2;
                double amplitude = height * 0.35;
                double step = width / (_smoothedData.Length - 1);
                
                for (int i = 0; i < _smoothedData.Length; i++)
                {
                    double x = i * step;
                    // Create pseudo-waveform from spectrum
                    double phase = i * Math.PI * 4 / _smoothedData.Length;
                    double y = centerY - (Math.Sin(phase) * _smoothedData[i] * amplitude);
                    polyline.Points.Add(new Point(x, y));
                }
                
                canvas.Children.Add(polyline);
            }
            
            // Add phosphor glow effect
            var glowLabel = new TextBlock
            {
                Text = "OSCILLOSCOPE",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 180, 100)),
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(glowLabel, 10);
            Canvas.SetTop(glowLabel, 10);
            canvas.Children.Add(glowLabel);
        }
        
        #region Milkdrop Visualizer
        
        /// <summary>
        /// Renders Milkdrop2-style visualization using the native C# MilkdropEngine.
        /// Uses WriteableBitmap for efficient pixel buffer rendering.
        /// </summary>
        private void RenderMilkdrop(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width < 10 || height < 10) return;

            // Render at fixed lower resolution for performance, upscale via WPF
            int pixelWidth = MILKDROP_RENDER_WIDTH;
            int pixelHeight = MILKDROP_RENDER_HEIGHT;

            // Initialize engine on first use or resize
            if (_milkdropEngine == null)
            {
                _milkdropEngine = new MilkdropEngine();
                _milkdropEngine.Initialize(pixelWidth, pixelHeight);
                _milkdropEngine.LoadPreset(MilkdropPreset.CreateDefault(), false);
                _lastMilkdropPresetChange = DateTime.Now;
            }
            else if (_milkdropEngine.Width != pixelWidth || _milkdropEngine.Height != pixelHeight)
            {
                _milkdropEngine.Resize(pixelWidth, pixelHeight);
            }

            // Auto-cycle presets
            if ((DateTime.Now - _lastMilkdropPresetChange).TotalSeconds >= _milkdropAutoChangeSeconds)
            {
                _milkdropEngine.LoadRandomPreset(true);
                _lastMilkdropPresetChange = DateTime.Now;
            }

            // Pass sensitivity to engine so it scales audio analysis accordingly
            _milkdropEngine.SetSensitivity(_sensitivity);

            // Feed spectrum data to engine
            var spectrumForEngine = new double[512];
            for (int i = 0; i < 512; i++)
            {
                int srcIdx = (int)((double)i / 512 * _smoothedData.Length);
                srcIdx = Math.Clamp(srcIdx, 0, _smoothedData.Length - 1);
                spectrumForEngine[i] = _smoothedData[srcIdx];
            }
            _milkdropEngine.AddSpectrumData(spectrumForEngine);

            // Feed REAL PCM waveform data from the oscilloscope buffer
            float[]? realPcm = null;
            if (DataContext is EnhancedAudioPlayerViewModel vm)
            {
                realPcm = vm.OscilloscopeData;
            }

            if (realPcm != null && realPcm.Length > 0)
            {
                // Use real PCM data directly - much more responsive than synthesized
                _milkdropEngine.AddPCMData(realPcm, realPcm.Length, false);
            }
            else
            {
                // Fallback: synthesize from spectrum (legacy behavior, boosted)
                var waveformForEngine = new float[512];
                for (int i = 0; i < 512; i++)
                {
                    double t = (double)i / 512;
                    float val = 0;
                    for (int band = 0; band < Math.Min(16, _smoothedData.Length); band++)
                    {
                        val += (float)(_smoothedData[band] * Math.Sin(t * Math.PI * 2 * (band + 1)) * 0.3);
                    }
                    waveformForEngine[i] = Math.Clamp(val, -1f, 1f);
                }
                _milkdropEngine.AddPCMData(waveformForEngine, 512, false);
            }

            // Render frame
            double deltaTime = 1.0 / Math.Max(_targetFps, 10);
            uint[] pixels = _milkdropEngine.RenderFrame(deltaTime);

            // Create or update WriteableBitmap at render resolution
            if (_milkdropBitmap == null || _milkdropBitmap.PixelWidth != pixelWidth || _milkdropBitmap.PixelHeight != pixelHeight)
            {
                _milkdropBitmap = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null);
                _milkdropImage = new Image
                {
                    Source = _milkdropBitmap,
                    Width = width,
                    Height = height,
                    Stretch = Stretch.Fill,
                    // Use NearestNeighbor for a crispy retro look, or Fant for smooth upscale
                    SnapsToDevicePixels = false
                };
                RenderOptions.SetBitmapScalingMode(_milkdropImage, BitmapScalingMode.Fant);
            }

            // Ensure row buffer is large enough (reuse across frames)
            if (_milkdropRowBuffer.Length < pixelWidth)
            {
                _milkdropRowBuffer = new int[pixelWidth];
            }

            // Write pixels to bitmap - single bulk copy
            _milkdropBitmap.Lock();
            try
            {
                IntPtr backBuffer = _milkdropBitmap.BackBuffer;
                int stride = _milkdropBitmap.BackBufferStride;

                // Copy pixel data row by row using reused buffer
                for (int y = 0; y < pixelHeight; y++)
                {
                    int srcOffset = y * pixelWidth;
                    int copyLength = Math.Min(pixelWidth, pixels.Length - srcOffset);
                    if (copyLength <= 0) break;

                    Buffer.BlockCopy(pixels, srcOffset * 4, _milkdropRowBuffer, 0, copyLength * 4);
                    System.Runtime.InteropServices.Marshal.Copy(_milkdropRowBuffer, 0, backBuffer + y * stride, copyLength);
                }

                _milkdropBitmap.AddDirtyRect(new Int32Rect(0, 0, pixelWidth, pixelHeight));
            }
            finally
            {
                _milkdropBitmap.Unlock();
            }

            // Update image dimensions to fill canvas
            _milkdropImage!.Width = width;
            _milkdropImage.Height = height;

            // Add image to canvas
            Canvas.SetLeft(_milkdropImage, 0);
            Canvas.SetTop(_milkdropImage, 0);
            canvas.Children.Add(_milkdropImage);

            // Add preset name overlay
            var builtInPresets = MilkdropPreset.GetBuiltInPresets();
            string presetName = _milkdropPresetIndex < builtInPresets.Count ? builtInPresets[_milkdropPresetIndex].Name : "Milkdrop";
            
            var label = new TextBlock
            {
                Text = $"MILKDROP: {presetName}",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromArgb(180, 200, 200, 255)),
                FontWeight = FontWeights.Bold
            };
            Canvas.SetLeft(label, 10);
            Canvas.SetTop(label, 10);
            canvas.Children.Add(label);

            // Keyboard hint
            var hintLabel = new TextBlock
            {
                Text = "Click to change wave mode",
                FontFamily = new FontFamily("Consolas"),
                FontSize = 10,
                Foreground = new SolidColorBrush(Color.FromArgb(100, 180, 180, 200))
            };
            Canvas.SetLeft(hintLabel, 10);
            Canvas.SetTop(hintLabel, height - 20);
            canvas.Children.Add(hintLabel);
        }

        /// <summary>
        /// Advance to the next Milkdrop wave mode (GPU path) or preset (legacy CPU path).
        /// </summary>
        public void NextMilkdropPreset()
        {
            // GPU path: cycle through wave modes and reset frame buffer for fresh visuals
            if (_useGpuRendering)
            {
                _milkdropWaveMode = (_milkdropWaveMode + 1) % 6;
                // Reset the feedback buffer for a clean transition
                if (_milkdropGpuBuffer != null)
                {
                    using var clearCanvas = new SKCanvas(_milkdropGpuBuffer);
                    clearCanvas.Clear(SKColors.Black);
                }
                _milkdropGpuFrame = 0;
                _lastMilkdropPresetChange = DateTime.Now;
                return;
            }
            
            // Legacy CPU path
            if (_milkdropEngine == null) return;
            var presets = MilkdropPreset.GetBuiltInPresets();
            _milkdropPresetIndex = (_milkdropPresetIndex + 1) % presets.Count;
            _milkdropEngine.LoadPreset(presets[_milkdropPresetIndex], true);
            _lastMilkdropPresetChange = DateTime.Now;
        }

        /// <summary>
        /// Load a specific Milkdrop preset by index.
        /// </summary>
        public void LoadMilkdropPreset(int index)
        {
            if (_milkdropEngine == null) return;
            var presets = MilkdropPreset.GetBuiltInPresets();
            if (index >= 0 && index < presets.Count)
            {
                _milkdropPresetIndex = index;
                _milkdropEngine.LoadPreset(presets[index], true);
                _lastMilkdropPresetChange = DateTime.Now;
            }
        }

        /// <summary>
        /// Load a Milkdrop preset from a .milk file.
        /// </summary>
        public void LoadMilkdropPresetFile(string filePath)
        {
            if (_milkdropEngine == null) return;
            var preset = MilkdropPreset.ParseFromFile(filePath);
            _milkdropEngine.LoadPreset(preset, true);
            _lastMilkdropPresetChange = DateTime.Now;
        }

        #endregion
        
        /// <summary>
        /// Handle click on visualizer canvas - cycles Milkdrop presets when in Milkdrop mode.
        /// </summary>
        private void VisualizerCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_visualizationMode == "Milkdrop")
            {
                NextMilkdropPreset();
            }
        }
        
        /// <summary>
        /// Converts HSV color values to RGB Color.
        /// </summary>
        /// <param name="h">Hue (0.0 to 1.0)</param>
        /// <param name="s">Saturation (0.0 to 1.0)</param>
        /// <param name="v">Value/Brightness (0.0 to 1.0)</param>
        /// <returns>RGB Color</returns>
        private static Color HsvToRgb(double h, double s, double v)
        {
            h = Math.Clamp(h, 0, 1);
            s = Math.Clamp(s, 0, 1);
            v = Math.Clamp(v, 0, 1);
            
            int hi = (int)(h * 6) % 6;
            double f = h * 6 - Math.Floor(h * 6);
            double p = v * (1 - s);
            double q = v * (1 - f * s);
            double t = v * (1 - (1 - f) * s);
            
            double r, g, b;
            switch (hi)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            
            return Color.FromRgb(
                (byte)(r * 255),
                (byte)(g * 255),
                (byte)(b * 255)
            );
        }
        
#if false // HD Visualization Methods - disabled until WriteableBitmap HD rendering is fully implemented
        #region HD Visualization Methods
        
        /// <summary>
        /// HD Bar Chart - Classic vertical bars with peak indicators.
        /// </summary>
        private void RenderBarChartHD()
        {
            if (_hdBitmap == null || _smoothedData == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int barCount = Math.Min(_barCount, _smoothedData.Length);
            if (barCount <= 0) return;
            
            double barWidth = (double)width / barCount;
            double gap = Math.Max(1, barWidth * 0.1);
            double effectiveBarWidth = barWidth - gap;
            
            for (int i = 0; i < barCount && i < _smoothedData.Length; i++)
            {
                double value = Math.Clamp(_smoothedData[i], 0, 1);
                int barHeight = (int)(value * height * 0.9);
                if (barHeight < 1) continue;
                
                int x = (int)(i * barWidth + gap / 2);
                int y = height - barHeight;
                int w = (int)effectiveBarWidth;
                
                // Get gradient colors for this bar
                Color bottomColor = GetSchemeColorForValue(0);
                Color topColor = GetSchemeColorForValue(value);
                
                FillRectGradient(x, y, w, barHeight, topColor, bottomColor);
                
                // Draw peak indicator
                if (i < _peakHeights.Length && _peakHeights[i] > 0.01)
                {
                    int peakY = height - (int)(_peakHeights[i] * height * 0.9);
                    Color peakColor = Colors.White;
                    FillRect(x, peakY, w, 2, peakColor.R, peakColor.G, peakColor.B);
                }
            }
        }
        
        /// <summary>
        /// HD Mirror Bars - Spectrum mirrored vertically.
        /// </summary>
        private void RenderMirrorBarsHD()
        {
            if (_hdBitmap == null || _smoothedData == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int barCount = Math.Min(_barCount, _smoothedData.Length);
            if (barCount <= 0) return;
            
            double barWidth = (double)width / barCount;
            double gap = Math.Max(1, barWidth * 0.1);
            double effectiveBarWidth = barWidth - gap;
            int centerY = height / 2;
            
            for (int i = 0; i < barCount && i < _smoothedData.Length; i++)
            {
                double value = Math.Clamp(_smoothedData[i], 0, 1);
                int barHeight = (int)(value * centerY * 0.9);
                if (barHeight < 1) continue;
                
                int x = (int)(i * barWidth + gap / 2);
                int w = (int)effectiveBarWidth;
                
                Color bottomColor = GetSchemeColorForValue(0);
                Color topColor = GetSchemeColorForValue(value);
                
                // Top half (upward from center)
                FillRectGradient(x, centerY - barHeight, w, barHeight, topColor, bottomColor);
                // Bottom half (downward from center, mirrored)
                FillRectGradient(x, centerY, w, barHeight, bottomColor, topColor);
            }
        }
        
        /// <summary>
        /// HD Waveform - Smooth anti-aliased waveform line.
        /// </summary>
        private void RenderWaveformHD()
        {
            if (_hdBitmap == null || _smoothedData == null || _smoothedData.Length < 2) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int centerY = height / 2;
            
            Color waveColor = GetSchemeColorForValue(0.7);
            
            for (int i = 0; i < _smoothedData.Length - 1; i++)
            {
                double x0Ratio = (double)i / _smoothedData.Length;
                double x1Ratio = (double)(i + 1) / _smoothedData.Length;
                
                int x0 = (int)(x0Ratio * width);
                int x1 = (int)(x1Ratio * width);
                
                double val0 = _smoothedData[i] - 0.5;
                double val1 = _smoothedData[i + 1] - 0.5;
                
                int y0 = centerY - (int)(val0 * height * 0.8);
                int y1 = centerY - (int)(val1 * height * 0.8);
                
                DrawLine(x0, y0, x1, y1, waveColor.R, waveColor.G, waveColor.B);
                // Draw thicker line
                DrawLine(x0, y0 + 1, x1, y1 + 1, waveColor.R, waveColor.G, waveColor.B);
            }
        }
        
        /// <summary>
        /// HD Circular - Circular spectrum visualization.
        /// </summary>
        private void RenderCircularHD()
        {
            if (_hdBitmap == null || _smoothedData == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int cx = width / 2;
            int cy = height / 2;
            int baseRadius = Math.Min(cx, cy) / 3;
            int maxRadius = Math.Min(cx, cy) - 10;
            
            int barCount = Math.Min(_barCount, _smoothedData.Length);
            if (barCount <= 0) return;
            
            double angleStep = 2 * Math.PI / barCount;
            
            for (int i = 0; i < barCount && i < _smoothedData.Length; i++)
            {
                double angle = i * angleStep - Math.PI / 2;
                double value = Math.Clamp(_smoothedData[i], 0, 1);
                int barLength = (int)(value * (maxRadius - baseRadius));
                
                Color color = GetSchemeColorForValue(value);
                
                int x0 = cx + (int)(Math.Cos(angle) * baseRadius);
                int y0 = cy + (int)(Math.Sin(angle) * baseRadius);
                int x1 = cx + (int)(Math.Cos(angle) * (baseRadius + barLength));
                int y1 = cy + (int)(Math.Sin(angle) * (baseRadius + barLength));
                
                DrawLine(x0, y0, x1, y1, color.R, color.G, color.B);
                // Draw adjacent angles for thickness
                double angleOffset = angleStep * 0.3;
                int x0b = cx + (int)(Math.Cos(angle + angleOffset) * baseRadius);
                int y0b = cy + (int)(Math.Sin(angle + angleOffset) * baseRadius);
                int x1b = cx + (int)(Math.Cos(angle + angleOffset) * (baseRadius + barLength));
                int y1b = cy + (int)(Math.Sin(angle + angleOffset) * (baseRadius + barLength));
                DrawLine(x0b, y0b, x1b, y1b, color.R, color.G, color.B);
            }
            
            // Draw center circle
            Color centerColor = GetSchemeColorForValue(0.5);
            DrawCircle(cx, cy, baseRadius, centerColor.R, centerColor.G, centerColor.B, 255, 2);
        }
        
        /// <summary>
        /// HD Radial - Radial spectrum with concentric rings.
        /// </summary>
        private void RenderRadialHD()
        {
            if (_hdBitmap == null || _smoothedData == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int cx = width / 2;
            int cy = height / 2;
            int maxRadius = Math.Min(cx, cy) - 5;
            
            // Calculate average intensity
            double avgIntensity = 0;
            for (int i = 0; i < _smoothedData.Length; i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= _smoothedData.Length;
            
            // Draw concentric rings based on frequency bands
            int rings = Math.Min(8, _smoothedData.Length);
            for (int r = 0; r < rings; r++)
            {
                int dataIndex = r * _smoothedData.Length / rings;
                double value = _smoothedData[dataIndex];
                int radius = maxRadius * (r + 1) / rings;
                int thickness = (int)(2 + value * 6);
                
                Color color = GetSchemeColorForValue(value);
                byte alpha = (byte)(100 + value * 155);
                
                DrawCircle(cx, cy, radius, color.R, color.G, color.B, alpha, thickness);
            }
            
            // Pulsing center
            int centerRadius = (int)(10 + avgIntensity * 30);
            Color centerColor = GetSchemeColorForValue(avgIntensity);
            FillCircle(cx, cy, centerRadius, centerColor.R, centerColor.G, centerColor.B);
        }
        
        /// <summary>
        /// HD Particles - Particle field visualization.
        /// </summary>
        private void RenderParticlesHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            
            // Get average intensity for particle behavior
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                for (int i = 0; i < _smoothedData.Length; i++)
                    avgIntensity += _smoothedData[i];
                avgIntensity /= _smoothedData.Length;
            }
            
            // Update and render particles
            while (_particles.Count < _maxParticles)
            {
                _particles.Add(new Particle
                {
                    X = _random.NextDouble(),
                    Y = _random.NextDouble(),
                    VelocityX = (_random.NextDouble() - 0.5) * 0.02,
                    VelocityY = (_random.NextDouble() - 0.5) * 0.02,
                    Size = 2 + _random.NextDouble() * 4,
                    Hue = _random.NextDouble()
                });
            }
            
            foreach (var p in _particles)
            {
                // Update position with intensity-based speed
                double speed = 0.5 + avgIntensity * 2;
                p.X += p.VelocityX * speed;
                p.Y += p.VelocityY * speed;
                
                // Wrap around
                if (p.X < 0) p.X = 1;
                if (p.X > 1) p.X = 0;
                if (p.Y < 0) p.Y = 1;
                if (p.Y > 1) p.Y = 0;
                
                // Render particle
                int px = (int)(p.X * width);
                int py = (int)(p.Y * height);
                int size = (int)(p.Size * (0.5 + avgIntensity));
                
                Color color = GetSchemeColorForValue(p.Hue);
                byte alpha = (byte)(150 + avgIntensity * 105);
                
                FillCircle(px, py, size, color.R, color.G, color.B, alpha);
            }
        }
        
        /// <summary>
        /// HD Aurora - Aurora borealis effect.
        /// </summary>
        private void RenderAuroraHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            
            double phase = _animationPhase;
            
            // Get bass and treble intensity
            double bassIntensity = 0;
            double trebleIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                int mid = _smoothedData.Length / 2;
                for (int i = 0; i < mid; i++)
                    bassIntensity += _smoothedData[i];
                for (int i = mid; i < _smoothedData.Length; i++)
                    trebleIntensity += _smoothedData[i];
                bassIntensity /= mid;
                trebleIntensity /= (_smoothedData.Length - mid);
            }
            
            // Draw aurora waves
            for (int wave = 0; wave < 5; wave++)
            {
                double waveOffset = wave * 0.7;
                double waveIntensity = wave % 2 == 0 ? bassIntensity : trebleIntensity;
                
                for (int x = 0; x < width; x += 2)
                {
                    double t = (double)x / width;
                    double waveY = 0.3 + wave * 0.1;
                    waveY += Math.Sin(t * 6 + phase + waveOffset) * 0.1 * (1 + waveIntensity);
                    waveY += Math.Sin(t * 3 + phase * 0.5 + waveOffset) * 0.05;
                    
                    int y = (int)(waveY * height);
                    int waveHeight = (int)(20 + waveIntensity * 60);
                    
                    Color color = GetSchemeColorForValue(t + wave * 0.15);
                    byte alpha = (byte)(40 + waveIntensity * 80);
                    
                    for (int dy = 0; dy < waveHeight; dy++)
                    {
                        int py = y + dy;
                        if (py >= 0 && py < height)
                        {
                            byte fadeAlpha = (byte)(alpha * (1 - (double)dy / waveHeight));
                            BlendPixel(x, py, color.R, color.G, color.B, fadeAlpha);
                            if (x + 1 < width)
                                BlendPixel(x + 1, py, color.R, color.G, color.B, fadeAlpha);
                        }
                    }
                }
            }
        }
        
        /// <summary>
        /// HD Wave Grid - 3D perspective wave grid.
        /// </summary>
        private void RenderWaveGridHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            
            double phase = _animationPhase;
            int gridSize = 20;
            
            // Get intensity
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            
            // Draw grid lines
            for (int gz = 0; gz < gridSize; gz++)
            {
                double zRatio = (double)gz / gridSize;
                double perspective = 1 - zRatio * 0.7;
                int rowY = (int)(height * 0.3 + zRatio * height * 0.6);
                
                for (int gx = 0; gx < gridSize - 1; gx++)
                {
                    double xRatio = (double)gx / gridSize;
                    double xRatioNext = (double)(gx + 1) / gridSize;
                    
                    // Wave height based on position and audio
                    int dataIdx = (int)(xRatio * (_smoothedData?.Length ?? 1));
                    double waveVal = _smoothedData != null && dataIdx < _smoothedData.Length ? _smoothedData[dataIdx] : 0;
                    double wave = Math.Sin(xRatio * 8 + phase + zRatio * 4) * (20 + waveVal * 40);
                    double waveNext = Math.Sin(xRatioNext * 8 + phase + zRatio * 4) * (20 + waveVal * 40);
                    
                    int x0 = (int)(width * 0.1 + xRatio * width * 0.8 * perspective);
                    int x1 = (int)(width * 0.1 + xRatioNext * width * 0.8 * perspective);
                    int y0 = rowY - (int)(wave * perspective);
                    int y1 = rowY - (int)(waveNext * perspective);
                    
                    Color color = GetSchemeColorForValue(xRatio);
                    byte alpha = (byte)(100 + perspective * 155);
                    
                    DrawLine(x0, y0, x1, y1, color.R, color.G, color.B, alpha);
                }
            }
        }
        
        /// <summary>
        /// HD Starfield - Classic After Dark starfield with motion blur.
        /// </summary>
        private void RenderStarfieldHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int cx = width / 2;
            int cy = height / 2;
            
            // Get intensity for speed boost
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            double speedMultiplier = 1 + avgIntensity * 3;
            
            // Update and render stars
            foreach (var star in _stars)
            {
                // Move star toward viewer
                star.Z -= star.Speed * speedMultiplier;
                if (star.Z <= 0.1)
                {
                    star.X = _random.NextDouble() - 0.5;
                    star.Y = _random.NextDouble() - 0.5;
                    star.Z = 4;
                }
                
                // Project to screen
                double scale = 200 / star.Z;
                int sx = cx + (int)(star.X * scale);
                int sy = cy + (int)(star.Y * scale);
                
                if (sx < 0 || sx >= width || sy < 0 || sy >= height) continue;
                
                // Star brightness based on depth
                byte brightness = (byte)(255 * (1 - star.Z / 4));
                int size = (int)(3 * (1 - star.Z / 4)) + 1;
                
                // Star color
                Color color = star.ColorType switch
                {
                    1 => Color.FromRgb(255, 255, 200), // Yellow
                    2 => Color.FromRgb(255, 180, 180), // Red
                    3 => Color.FromRgb(180, 200, 255), // Blue
                    _ => Color.FromRgb(255, 255, 255)  // White
                };
                
                byte r = (byte)(color.R * brightness / 255);
                byte g = (byte)(color.G * brightness / 255);
                byte b = (byte)(color.B * brightness / 255);
                
                // Draw star with motion blur streak
                double prevScale = 200 / (star.Z + star.Speed * speedMultiplier);
                int prevX = cx + (int)(star.X * prevScale);
                int prevY = cy + (int)(star.Y * prevScale);
                
                if (size > 1)
                    FillCircle(sx, sy, size, r, g, b);
                else
                    SetPixel(sx, sy, r, g, b);
                    
                // Motion trail
                DrawLine(prevX, prevY, sx, sy, r, g, b, (byte)(brightness / 2));
            }
        }
        
        /// <summary>
        /// HD Toasters - Flying toasters (simplified geometric version).
        /// </summary>
        private void RenderToastersHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            
            // Get intensity
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            
            // Ensure we have toasters
            while (_toasters.Count < 8)
            {
                _toasters.Add(new Toaster
                {
                    X = 1 + _random.NextDouble() * 0.3,
                    Y = _random.NextDouble() * 0.8,
                    Speed = 0.005 + _random.NextDouble() * 0.01,
                    WingPhase = _random.NextDouble() * Math.PI * 2,
                    Size = 30 + _random.NextDouble() * 20
                });
            }
            
            foreach (var toaster in _toasters)
            {
                // Update position
                double speed = toaster.Speed * (1 + avgIntensity);
                toaster.X -= speed;
                toaster.Y += speed * 0.5;
                toaster.WingPhase += 0.3 * (1 + avgIntensity * 2);
                
                // Reset if off screen
                if (toaster.X < -0.15 || toaster.Y > 1.15)
                {
                    toaster.X = 1 + _random.NextDouble() * 0.2;
                    toaster.Y = _random.NextDouble() * 0.6;
                }
                
                int tx = (int)(toaster.X * width);
                int ty = (int)(toaster.Y * height);
                int size = (int)toaster.Size;
                
                // Draw simplified toaster body (silver rectangle)
                FillRect(tx, ty, size, (int)(size * 0.7), 180, 180, 190);
                
                // Toast slots (dark)
                int slotWidth = size / 4;
                int slotHeight = size / 6;
                FillRect(tx + slotWidth / 2, ty + 4, slotWidth, slotHeight, 40, 30, 20);
                FillRect(tx + size / 2, ty + 4, slotWidth, slotHeight, 40, 30, 20);
                
                // Wings (animated)
                double wingAngle = Math.Sin(toaster.WingPhase) * 0.5;
                int wingLength = size / 2;
                int wingY = ty + size / 3;
                int wingEndY = wingY - (int)(wingLength * wingAngle);
                
                // Left wing
                DrawLine(tx, wingY, tx - wingLength / 2, wingEndY, 220, 220, 230);
                DrawLine(tx, wingY + 2, tx - wingLength / 2, wingEndY + 2, 220, 220, 230);
                
                // Toast (brown, popping out)
                int toastY = ty - (int)(Math.Abs(Math.Sin(toaster.WingPhase)) * 10);
                FillRect(tx + 2, toastY, size - 4, size / 4, 180, 140, 80);
            }
        }
        
        /// <summary>
        /// HD Matrix - Digital rain effect.
        /// </summary>
        private void RenderMatrixHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            
            // Get intensity
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            
            int columnCount = width / 14;
            
            // Ensure columns — add if needed, trim if too many
            while (_matrixColumns.Count < columnCount)
            {
                var col = new MatrixColumn
                {
                    X = (double)_matrixColumns.Count / columnCount,
                    Y = -_random.NextDouble(),
                    Speed = 0.02 + _random.NextDouble() * 0.03,
                    Length = 8 + _random.Next(12)
                };
                for (int i = 0; i < col.Characters.Length; i++)
                    col.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                _matrixColumns.Add(col);
            }
            while (_matrixColumns.Count > columnCount)
            {
                _matrixColumns.RemoveAt(_matrixColumns.Count - 1);
            }
            
            foreach (var col in _matrixColumns)
            {
                // Update position
                col.Y += col.Speed * (1 + avgIntensity);
                if (col.Y > 1.5)
                {
                    col.Y = -_random.NextDouble() * 0.5;
                    col.Speed = 0.02 + _random.NextDouble() * 0.03;
                    // Randomize some characters
                    for (int i = 0; i < col.Characters.Length; i++)
                    {
                        if (_random.NextDouble() < 0.3)
                            col.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                    }
                }
                
                int px = (int)(col.X * width);
                int charHeight = 14;
                
                // Draw trail
                for (int i = 0; i < col.Length; i++)
                {
                    int cy = (int)((col.Y - i * 0.05) * height);
                    if (cy < 0 || cy >= height - charHeight) continue;
                    
                    // Brightness fades for trail
                    double fade = 1.0 - (double)i / col.Length;
                    byte brightness = (byte)(255 * fade);
                    
                    // Head character is white, rest are green
                    byte r = i == 0 ? (byte)255 : (byte)0;
                    byte g = brightness;
                    byte b = i == 0 ? (byte)255 : (byte)0;
                    
                    // Simple character representation (filled rect for performance)
                    FillRect(px, cy, 10, 12, r, g, b, brightness);
                }
            }
        }
        
        /// <summary>
        /// HD Star Wars Crawl - Perspective text crawl.
        /// </summary>
        private void RenderStarWarsCrawlHD()
        {
            if (_hdBitmap == null) return;
            
            // Update crawl position
            _crawlPosition += _crawlSpeed * 0.5;
            if (_crawlPosition > StarWarsCrawlText.Length * 30 + _hdHeight)
                _crawlPosition = 0;
            
            int width = _hdWidth;
            int height = _hdHeight;
            
            // Draw starfield background
            for (int i = 0; i < 100; i++)
            {
                int sx = (int)((_random.NextDouble() * 12345 + i * 7) % width);
                int sy = (int)((_random.NextDouble() * 54321 + i * 13) % height);
                byte brightness = (byte)(100 + (i % 156));
                SetPixel(sx, sy, brightness, brightness, brightness);
            }
            
            // Draw text lines with perspective
            int centerX = width / 2;
            int lineHeight = 25;
            
            for (int i = 0; i < StarWarsCrawlText.Length; i++)
            {
                double y = height - (i * lineHeight - _crawlPosition);
                if (y < 0 || y > height * 1.2) continue;
                
                // Perspective scaling
                double perspective = y / height;
                double scale = 0.3 + perspective * 0.7;
                int textWidth = StarWarsCrawlText[i].Length * 8;
                int scaledWidth = (int)(textWidth * scale);
                
                int tx = centerX - scaledWidth / 2;
                int ty = (int)(y * 0.5 + height * 0.1);
                
                if (ty < 0 || ty >= height) continue;
                
                // Fade based on position
                byte alpha = (byte)(255 * Math.Min(1, perspective * 2));
                
                // Draw simplified text representation
                int charWidth = (int)(8 * scale);
                for (int c = 0; c < StarWarsCrawlText[i].Length && tx + c * charWidth < width; c++)
                {
                    if (StarWarsCrawlText[i][c] == ' ') continue;
                    int cx = tx + c * charWidth;
                    if (cx < 0) continue;
                    FillRect(cx, ty, Math.Max(1, charWidth - 1), (int)(12 * scale), 255, 232, 31, alpha);
                }
            }
        }
        
        /// <summary>
        /// HD Stargate - Animated event horizon with kawoosh effect.
        /// </summary>
        private void RenderStargateHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int cx = width / 2;
            int cy = height / 2;
            int radius = Math.Min(cx, cy) - 20;
            
            double phase = _animationPhase;
            
            // Get intensity
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            
            // Draw outer ring (dark metal)
            DrawCircle(cx, cy, radius + 8, 60, 60, 70, 255, 10);
            DrawCircle(cx, cy, radius, 80, 80, 90, 255, 5);
            
            // Draw event horizon (rippling water-like effect)
            for (int r = radius - 5; r > 0; r -= 2)
            {
                double rRatio = (double)r / radius;
                double ripple = Math.Sin(rRatio * 10 + phase * 3) * 0.3 + 0.7;
                
                // Blue-white gradient
                byte blue = (byte)(150 + rRatio * 100);
                byte green = (byte)(100 + rRatio * 50 + ripple * 50);
                byte red = (byte)(50 + ripple * 50);
                byte alpha = (byte)(200 + avgIntensity * 55);
                
                DrawCircle(cx, cy, r, red, green, blue, alpha, 2);
            }
            
            // Bright center
            FillCircle(cx, cy, radius / 6, 200, 220, 255);
            
            // Chevrons (simplified as dots around the ring)
            for (int i = 0; i < 9; i++)
            {
                double angle = i * Math.PI * 2 / 9 - Math.PI / 2;
                int chevronX = cx + (int)(Math.Cos(angle) * (radius + 3));
                int chevronY = cy + (int)(Math.Sin(angle) * (radius + 3));
                
                // Lit chevrons are orange, unlit are gray
                bool lit = i < 7;
                if (lit)
                    FillCircle(chevronX, chevronY, 6, 255, 150, 50);
                else
                    FillCircle(chevronX, chevronY, 6, 80, 80, 80);
            }
        }
        
        /// <summary>
        /// HD Klingon - Trefoil emblem with blood red theme.
        /// </summary>
        private void RenderKlingonHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int cx = width / 2;
            int cy = height / 2;
            int size = Math.Min(cx, cy) - 20;
            
            double phase = _animationPhase;
            
            // Get intensity
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            
            // Draw pulsing background glow
            int glowSize = (int)(size * (1.2 + avgIntensity * 0.3));
            for (int r = glowSize; r > size; r -= 3)
            {
                byte alpha = (byte)(50 * (1 - (double)(r - size) / (glowSize - size)));
                DrawCircle(cx, cy, r, 180, 0, 0, alpha, 3);
            }
            
            // Draw trefoil shape (simplified with three overlapping circles)
            double[] angles = { -Math.PI / 2, Math.PI / 6, 5 * Math.PI / 6 };
            int leafRadius = size / 2;
            int leafOffset = size / 3;
            
            foreach (double angle in angles)
            {
                int lx = cx + (int)(Math.Cos(angle) * leafOffset);
                int ly = cy + (int)(Math.Sin(angle) * leafOffset);
                
                // Gradient from dark to blood red
                for (int r = leafRadius; r > 0; r -= 2)
                {
                    double rRatio = (double)r / leafRadius;
                    byte red = (byte)(80 + rRatio * 120 + avgIntensity * 55);
                    byte gb = (byte)(rRatio * 20);
                    DrawCircle(lx, ly, r, red, gb, gb, 255, 2);
                }
            }
            
            // Center connector
            FillCircle(cx, cy, leafOffset / 2, 60, 10, 10);
            
            // Metal trim
            DrawCircle(cx, cy, size, 120, 100, 80, 255, 3);
        }
        
        /// <summary>
        /// HD Federation - Starfleet delta with transporter effect.
        /// </summary>
        private void RenderFederationHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int cx = width / 2;
            int cy = height / 2;
            int size = Math.Min(cx, cy) - 20;
            
            double phase = _animationPhase;
            
            // Get intensity
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            
            // Draw starfield background dots
            for (int i = 0; i < 50; i++)
            {
                int sx = (int)((_random.NextDouble() * 9999 + i * 17) % width);
                int sy = (int)((_random.NextDouble() * 7777 + i * 23) % height);
                byte brightness = (byte)(80 + (i % 176));
                SetPixel(sx, sy, brightness, brightness, brightness);
            }
            
            // Draw transporter sparkles
            int sparkleCount = (int)(30 + avgIntensity * 50);
            for (int i = 0; i < sparkleCount; i++)
            {
                double sparklePhase = phase + i * 0.3;
                int sx = cx + (int)(Math.Sin(sparklePhase * 3 + i) * size * 0.8);
                int sy = cy + (int)(Math.Cos(sparklePhase * 2 + i * 1.5) * size * 0.8);
                
                sx = Math.Clamp(sx, 0, width - 1);
                sy = Math.Clamp(sy, 0, height - 1);
                
                byte brightness = (byte)(150 + Math.Sin(sparklePhase * 5) * 100);
                FillCircle(sx, sy, 2, brightness, brightness, 255, brightness);
            }
            
            // Draw delta shield (triangle outline)
            int[] deltaX = { cx, cx - size / 2, cx + size / 2 };
            int[] deltaY = { cy - size / 2, cy + size / 2, cy + size / 2 };
            
            DrawLine(deltaX[0], deltaY[0], deltaX[1], deltaY[1], 80, 100, 180);
            DrawLine(deltaX[1], deltaY[1], deltaX[2], deltaY[2], 80, 100, 180);
            DrawLine(deltaX[2], deltaY[2], deltaX[0], deltaY[0], 80, 100, 180);
            
            // Center star
            FillCircle(cx, cy, 8, 255, 215, 0);
        }
        
        /// <summary>
        /// HD Jedi - Lightsaber spectrum bars.
        /// </summary>
        private void RenderJediHD()
        {
            if (_hdBitmap == null || _smoothedData == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int barCount = Math.Min(_barCount, _smoothedData.Length);
            if (barCount <= 0) return;
            
            double barWidth = (double)width / barCount;
            
            // Lightsaber colors
            Color[] saberColors = { 
                Color.FromRgb(100, 150, 255),  // Blue
                Color.FromRgb(100, 255, 100),  // Green  
                Color.FromRgb(200, 100, 255),  // Purple
                Color.FromRgb(255, 100, 100)   // Red
            };
            
            for (int i = 0; i < barCount && i < _smoothedData.Length; i++)
            {
                double value = Math.Clamp(_smoothedData[i], 0, 1);
                int barHeight = (int)(value * height * 0.8);
                if (barHeight < 5) barHeight = 5;
                
                int x = (int)(i * barWidth);
                int y = height - barHeight;
                int w = (int)(barWidth * 0.8);
                
                Color saberColor = saberColors[i % saberColors.Length];
                
                // Draw glow
                for (int g = 3; g >= 0; g--)
                {
                    byte alpha = (byte)(60 - g * 15);
                    FillRect(x - g, y - g, w + g * 2, barHeight + g, 
                        saberColor.R, saberColor.G, saberColor.B, alpha);
                }
                
                // Draw core (white center)
                int coreWidth = Math.Max(1, w / 3);
                int coreX = x + (w - coreWidth) / 2;
                FillRect(coreX, y, coreWidth, barHeight, 255, 255, 255);
                
                // Handle
                int handleHeight = 15;
                FillRect(x, height - handleHeight, w, handleHeight, 80, 80, 90);
            }
        }
        
        /// <summary>
        /// HD TimeLord - Gallifreyan circular patterns.
        /// </summary>
        private void RenderTimeLordHD()
        {
            if (_hdBitmap == null) return;
            
            int width = _hdWidth;
            int height = _hdHeight;
            int cx = width / 2;
            int cy = height / 2;
            int maxRadius = Math.Min(cx, cy) - 10;
            
            double phase = _animationPhase;
            
            // Get intensity
            double avgIntensity = 0;
            if (_smoothedData != null && _smoothedData.Length > 0)
            {
                foreach (double d in _smoothedData)
                    avgIntensity += d;
                avgIntensity /= _smoothedData.Length;
            }
            
            // Draw concentric rings (Gallifreyan style)
            int rings = 7;
            for (int r = 0; r < rings; r++)
            {
                int radius = maxRadius * (r + 1) / rings;
                double rotation = phase * (r % 2 == 0 ? 1 : -1) * 0.5;
                
                // Main ring
                byte brightness = (byte)(120 + avgIntensity * 80);
                DrawCircle(cx, cy, radius, brightness, brightness, 100, 200, 2);
                
                // Decorative dots along ring
                int dotCount = 6 + r * 2;
                for (int d = 0; d < dotCount; d++)
                {
                    double angle = d * Math.PI * 2 / dotCount + rotation;
                    int dx = cx + (int)(Math.Cos(angle) * radius);
                    int dy = cy + (int)(Math.Sin(angle) * radius);
                    
                    int dotSize = 3 + (int)(avgIntensity * 3);
                    FillCircle(dx, dy, dotSize, 255, 200, 100);
                }
            }
            
            // Center symbol (simplified)
            FillCircle(cx, cy, 15, 255, 215, 0);
            DrawCircle(cx, cy, 20, 200, 180, 100, 255, 2);
        }
        
        #endregion
#endif // Old WriteableBitmap HD methods (deprecated)
        
        #region SkiaSharp HD Rendering
        
        /// <summary>
        /// SkiaSharp paint event handler — unified GPU renderer for ALL visualization modes.
        /// Uses hardware-accelerated anti-aliased rendering with millions of colors.
        /// </summary>
        private void OnSkiaPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            var info = e.Info;
            
            // Flag large surfaces for performance scaling (skip expensive blurs, reduce effects)
            _isLargeSurface = (long)info.Width * info.Height > 1_000_000; // ~1000x1000+
            
            // Clear with appropriate background color per mode
            bool needsBlackBg = _visualizationMode is "Matrix" or "Star Wars Crawl" or "Starfield" 
                or "Stargate" or "Jedi" or "Waterfall" or "Toasters";
            bool needsCustomBg = _visualizationMode is "Klingon" or "Federation" or "TimeLord";
            
            if (needsBlackBg)
            {
                canvas.Clear(SKColors.Black);
            }
            else if (!needsCustomBg)
            {
                var bgColor = TryFindResource("WindowBackgroundBrush") as SolidColorBrush;
                byte bgR = bgColor?.Color.R ?? 10;
                byte bgG = bgColor?.Color.G ?? 14;
                byte bgB = bgColor?.Color.B ?? 39;
                canvas.Clear(new SKColor(bgR, bgG, bgB));
            }
            
            if (_smoothedData == null || _smoothedData.Length == 0) return;
            
            // Decay bars when music is not playing (but NOT in externally driven fullscreen mode)
            if (!_isMusicPlaying && !_isExternallyDriven)
            {
                for (int i = 0; i < _barHeights.Length; i++)
                {
                    _barHeights[i] *= 0.92;
                    _smoothedData[i] = _barHeights[i];
                    if (_barHeights[i] < 0.001) _barHeights[i] = 0;
                }
            }
            
            // Dispatch to appropriate GPU renderer based on mode
            // Wrapped in try/catch to prevent render exceptions from freezing the visual
            // (WPF may stop calling OnRender for elements that throw during render pass)
            try
            {
                switch (_visualizationMode)
                {
                    case "Bars":
                        RenderBarsHdSkia(canvas, info);
                        break;
                    case "Mirror":
                        RenderMirrorHdSkia(canvas, info);
                        break;
                    case "Waveform":
                        RenderWaveformHdSkia(canvas, info);
                        break;
                    case "Circular":
                        RenderCircularHdSkia(canvas, info);
                        break;
                    case "Radial":
                        RenderRadialHdSkia(canvas, info);
                        break;
                    case "Particles":
                        RenderParticlesHdSkia(canvas, info);
                        break;
                    case "Aurora":
                        RenderAuroraHdSkia(canvas, info);
                        break;
                    case "WaveGrid":
                    case "Wave Grid":
                        RenderWaveGridHdSkia(canvas, info);
                        break;
                    case "Starfield":
                        RenderStarfieldHdSkia(canvas, info);
                        break;
                    case "Toasters":
                        RenderToastersHdSkia(canvas, info);
                        break;
                    case "Matrix":
                        RenderMatrixHdSkia(canvas, info);
                        break;
                    case "Star Wars Crawl":
                        RenderStarWarsCrawlHdSkia(canvas, info);
                        break;
                    case "Stargate":
                        RenderStargateHdSkia(canvas, info);
                        break;
                    case "Klingon":
                        RenderKlingonHdSkia(canvas, info);
                        break;
                    case "Federation":
                        RenderFederationHdSkia(canvas, info);
                        break;
                    case "Jedi":
                        RenderJediHdSkia(canvas, info);
                        break;
                    case "TimeLord":
                        RenderTimeLordHdSkia(canvas, info);
                        break;
                    case "VU Meter":
                        RenderVUMeterHdSkia(canvas, info);
                        break;
                    case "Oscilloscope":
                        RenderOscilloscopeHdSkia(canvas, info);
                        break;
                    case "Waterfall":
                        RenderWaterfallHD(canvas, info);
                        break;
                    case "3D Bars":
                        Render3DBarsHD(canvas, info);
                        break;
                    case "Milkdrop":
                        RenderMilkdropHD(canvas, info);
                        break;
                    default:
                        RenderBarsHdSkia(canvas, info);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnSkiaPaintSurface render error ({_visualizationMode}): {ex.Message}");
                // Draw error indicator so user knows something went wrong
                using var errPaint = new SKPaint { Color = SKColors.Red, IsAntialias = true };
                using var errFont = new SKFont { Size = 14 };
                canvas.DrawText($"Render error: {_visualizationMode}", 10, 20, errFont, errPaint);
            }
        }
        
        /// <summary>
        /// Converts WPF color scheme color to SkiaSharp color.
        /// </summary>
        private SKColor GetSkiaColorFromScheme(double value)
        {
            var wpfColor = GetColorFromScheme(value);
            return new SKColor(wpfColor.R, wpfColor.G, wpfColor.B, 255);
        }
        
        /// <summary>
        /// Creates a gradient shader for the current color scheme.
        /// </summary>
        private SKShader CreateGradientShader(float x0, float y0, float x1, float y1)
        {
            var colors = _colorScheme switch
            {
                VisualizerColorScheme.Rainbow => new SKColor[] { 
                    SKColors.Red, SKColors.Orange, SKColors.Yellow, 
                    SKColors.Lime, SKColors.Cyan, SKColors.Blue, SKColors.Magenta 
                },
                VisualizerColorScheme.Fire => new SKColor[] { 
                    new SKColor(255, 50, 0), new SKColor(255, 150, 0), new SKColor(255, 255, 50) 
                },
                VisualizerColorScheme.Purple => new SKColor[] { 
                    new SKColor(75, 0, 130), new SKColor(180, 50, 180), new SKColor(255, 105, 210) 
                },
                VisualizerColorScheme.Neon => new SKColor[] { 
                    new SKColor(0, 255, 255), new SKColor(0, 255, 128), new SKColor(128, 255, 0) 
                },
                VisualizerColorScheme.Ocean => new SKColor[] { 
                    new SKColor(0, 30, 60), new SKColor(0, 100, 150), new SKColor(0, 200, 200) 
                },
                VisualizerColorScheme.PipBoy => new SKColor[] {
                    new SKColor(10, 85, 48), new SKColor(50, 180, 90), new SKColor(77, 255, 163)
                },
                VisualizerColorScheme.LCARS => new SKColor[] {
                    new SKColor(255, 153, 0), new SKColor(204, 153, 255), new SKColor(153, 153, 255)
                },
                _ => new SKColor[] { 
                    new SKColor(30, 144, 255), new SKColor(0, 255, 127), new SKColor(127, 255, 0) 
                }
            };
            
            return SKShader.CreateLinearGradient(
                new SKPoint(x0, y0), new SKPoint(x1, y1),
                colors, null, SKShaderTileMode.Clamp);
        }
        
        /// <summary>
        /// HD Circular visualizer using SkiaSharp with anti-aliased lines and glow effects.
        /// </summary>
        private void RenderCircularHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float radius = Math.Min(centerX, centerY) - 30;
            
            if (_smoothedData.Length == 0) return;
            
            // Create paints for rendering
            using var barPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = Math.Max(3, info.Width / _smoothedData.Length * 0.8f)
            };
            
            using var circularGlowBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
            using var glowPaint = _isLargeSurface ? null : new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeWidth = barPaint.StrokeWidth + 4,
                MaskFilter = circularGlowBlur
            };
            
            // Draw outer ring guide
            using var ringPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                Color = new SKColor(60, 60, 80, 100)
            };
            canvas.DrawCircle(centerX, centerY, radius * 0.3f, ringPaint);
            canvas.DrawCircle(centerX, centerY, radius * 0.9f, ringPaint);
            
            // Draw bars with glow
            for (int i = 0; i < _smoothedData.Length; i++)
            {
                double angle = (i / (double)_smoothedData.Length) * Math.PI * 2 - Math.PI / 2;
                double value = Math.Min(1.0, _smoothedData[i] * 2.5);
                double barRadius = radius * value * 0.6;
                
                float startX = centerX + (float)(Math.Cos(angle) * (radius * 0.3));
                float startY = centerY + (float)(Math.Sin(angle) * (radius * 0.3));
                float endX = centerX + (float)(Math.Cos(angle) * (radius * 0.3 + barRadius));
                float endY = centerY + (float)(Math.Sin(angle) * (radius * 0.3 + barRadius));
                
                var color = GetSkiaColorFromScheme(value);
                var glowColor = new SKColor(color.Red, color.Green, color.Blue, 80);
                
                // Draw glow first
                if (glowPaint != null)
                {
                    glowPaint.Color = glowColor;
                    canvas.DrawLine(startX, startY, endX, endY, glowPaint);
                }
                
                // Draw main bar
                barPaint.Color = color;
                canvas.DrawLine(startX, startY, endX, endY, barPaint);
            }
            
            // Center decoration
            using var centerPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = new SKColor(40, 50, 80, 200)
            };
            canvas.DrawCircle(centerX, centerY, radius * 0.25f, centerPaint);
        }
        
        /// <summary>
        /// HD Bars visualizer with gradient fills and glow effects.
        /// </summary>
        private void RenderBarsHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            if (_smoothedData.Length == 0) return;
            
            float barWidth = (float)info.Width / _smoothedData.Length;
            float gap = barWidth * 0.15f;
            float actualBarWidth = barWidth - gap;
            
            using var barPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            using var barsGlowBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8);
            using var glowPaint = _isLargeSurface ? null : new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                MaskFilter = barsGlowBlur
            };
            
            using var peakPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = SKColors.White
            };
            
            for (int i = 0; i < _smoothedData.Length; i++)
            {
                double value = Math.Min(1.0, _smoothedData[i] * 2.5);
                float barHeight = (float)(info.Height * value * 0.9);
                float x = i * barWidth + gap / 2;
                float y = info.Height - barHeight;
                
                var color = GetSkiaColorFromScheme(value);
                
                // Create vertical gradient for bar
                using var shader = SKShader.CreateLinearGradient(
                    new SKPoint(x, info.Height),
                    new SKPoint(x, y),
                    new SKColor[] { color.WithAlpha(200), color },
                    null, SKShaderTileMode.Clamp);
                barPaint.Shader = shader;
                
                // Draw glow
                if (glowPaint != null)
                {
                    glowPaint.Color = color.WithAlpha(60);
                    canvas.DrawRoundRect(x - 2, y - 2, actualBarWidth + 4, barHeight + 4, 3, 3, glowPaint);
                }
                
                // Draw bar with rounded corners
                canvas.DrawRoundRect(x, y, actualBarWidth, barHeight, 2, 2, barPaint);
                
                // Draw peak indicator
                if (_peakHeights.Length > i && _peakHeights[i] > 0.01)
                {
                    float peakY = info.Height - (float)(_peakHeights[i] * info.Height * 0.9);
                    canvas.DrawRect(x, peakY, actualBarWidth, 3, peakPaint);
                }
            }
        }
        
        /// <summary>
        /// HD Mirror bars visualizer (bars from center going up and down).
        /// </summary>
        private void RenderMirrorHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            if (_smoothedData.Length == 0) return;
            
            float barWidth = (float)info.Width / _smoothedData.Length;
            float gap = barWidth * 0.15f;
            float actualBarWidth = barWidth - gap;
            float centerY = info.Height / 2f;
            
            using var barPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var mirrorGlowBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6);
            using var glowPaint = _isLargeSurface ? null : new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                MaskFilter = mirrorGlowBlur
            };
            
            for (int i = 0; i < _smoothedData.Length; i++)
            {
                double value = Math.Min(1.0, _smoothedData[i] * 2.5);
                float barHeight = (float)(info.Height * 0.45 * value);
                float x = i * barWidth + gap / 2;
                
                var color = GetSkiaColorFromScheme(value);
                barPaint.Color = color;
                if (glowPaint != null)
                    glowPaint.Color = color.WithAlpha(60);
                
                // Top half
                if (glowPaint != null)
                    canvas.DrawRoundRect(x, centerY - barHeight, actualBarWidth, barHeight, 2, 2, glowPaint);
                canvas.DrawRoundRect(x, centerY - barHeight, actualBarWidth, barHeight, 2, 2, barPaint);
                
                // Bottom half (mirrored)
                if (glowPaint != null)
                    canvas.DrawRoundRect(x, centerY, actualBarWidth, barHeight, 2, 2, glowPaint);
                canvas.DrawRoundRect(x, centerY, actualBarWidth, barHeight, 2, 2, barPaint);
            }
            
            // Center line
            using var linePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                Color = new SKColor(100, 100, 120, 150)
            };
            canvas.DrawLine(0, centerY, info.Width, centerY, linePaint);
        }
        
        /// <summary>
        /// HD Waveform visualizer with smooth bezier curves.
        /// </summary>
        private void RenderWaveformHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            if (_smoothedData.Length == 0) return;
            
            float centerY = info.Height / 2f;
            float stepX = (float)info.Width / (_smoothedData.Length - 1);
            
            using var path = new SKPath();
            using var wavePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round
            };
            
            using var waveformGlowBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6);
            using var glowPaint = _isLargeSurface ? null : new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 8,
                StrokeCap = SKStrokeCap.Round,
                MaskFilter = waveformGlowBlur
            };
            
            // Build smooth path using quadratic beziers
            path.MoveTo(0, centerY);
            for (int i = 0; i < _smoothedData.Length; i++)
            {
                float x = i * stepX;
                double value = (_smoothedData[i] - 0.5) * 2;
                float y = centerY - (float)(value * info.Height * 0.4);
                
                if (i == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    float prevX = (i - 1) * stepX;
                    float midX = (prevX + x) / 2;
                    path.QuadTo(prevX, path.LastPoint.Y, midX, (path.LastPoint.Y + y) / 2);
                }
            }
            
            // Draw glow
            if (glowPaint != null)
            {
                using var glowShader = CreateGradientShader(0, 0, info.Width, 0);
                glowPaint.Shader = glowShader;
                glowPaint.Color = glowPaint.Color.WithAlpha(80);
                canvas.DrawPath(path, glowPaint);
            }
            
            // Draw main waveform
            using var waveShader = CreateGradientShader(0, 0, info.Width, 0);
            wavePaint.Shader = waveShader;
            canvas.DrawPath(path, wavePaint);
        }
        
        /// <summary>
        /// HD Radial spectrum with multiple concentric rings.
        /// </summary>
        private void RenderRadialHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float centerX = info.Width / 2f;
            float centerY = info.Height / 2f;
            float maxRadius = Math.Min(centerX, centerY) - 20;
            
            if (_smoothedData.Length == 0) return;
            
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            int rings = 4;
            for (int ring = 0; ring < rings; ring++)
            {
                float ringRadius = maxRadius * (0.25f + ring * 0.2f);
                float opacity = 1f - ring * 0.15f;
                
                for (int i = 0; i < _smoothedData.Length; i++)
                {
                    double angle = (i / (double)_smoothedData.Length) * Math.PI * 2 - Math.PI / 2;
                    double nextAngle = ((i + 1) / (double)_smoothedData.Length) * Math.PI * 2 - Math.PI / 2;
                    
                    int dataIndex = (i + ring * 5) % _smoothedData.Length;
                    double value = Math.Min(1.0, _smoothedData[dataIndex] * 2.5);
                    
                    float innerR = ringRadius - 5;
                    float outerR = ringRadius + (float)(value * 25);
                    
                    var color = GetSkiaColorFromScheme(value);
                    paint.Color = color.WithAlpha((byte)(opacity * 200));
                    
                    using var path = new SKPath();
                    path.MoveTo(centerX + (float)(Math.Cos(angle) * innerR), centerY + (float)(Math.Sin(angle) * innerR));
                    path.LineTo(centerX + (float)(Math.Cos(angle) * outerR), centerY + (float)(Math.Sin(angle) * outerR));
                    path.LineTo(centerX + (float)(Math.Cos(nextAngle) * outerR), centerY + (float)(Math.Sin(nextAngle) * outerR));
                    path.LineTo(centerX + (float)(Math.Cos(nextAngle) * innerR), centerY + (float)(Math.Sin(nextAngle) * innerR));
                    path.Close();
                    
                    canvas.DrawPath(path, paint);
                }
            }
        }
        
        /// <summary>
        /// HD Particles visualization with smooth gradients.
        /// </summary>
        private void RenderParticlesHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            // Calculate average intensity
            double avgIntensity = 0;
            if (_smoothedData.Length > 0)
            {
                foreach (var v in _smoothedData) avgIntensity += v;
                avgIntensity /= _smoothedData.Length;
            }
            
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            using var linePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1
            };
            
            // Update and draw particles
            foreach (var particle in _particles)
            {
                // Move particle (was missing — particles were static in GPU path)
                particle.X += particle.VelocityX * (1 + avgIntensity * 3);
                particle.Y += particle.VelocityY * (1 + avgIntensity * 3);
                
                // Wrap around
                if (particle.X < 0) particle.X = 1;
                if (particle.X > 1) particle.X = 0;
                if (particle.Y < 0) particle.Y = 1;
                if (particle.Y > 1) particle.Y = 0;
                
                float x = (float)(particle.X * info.Width);
                float y = (float)(particle.Y * info.Height);
                float size = (float)(particle.Size * (1 + avgIntensity * 2));
                
                var color = GetSkiaColorFromScheme(particle.Hue);
                
                // Draw glow
                if (!_isLargeSurface)
                {
                    using var glowShader = SKShader.CreateRadialGradient(
                        new SKPoint(x, y), size * 2,
                        new SKColor[] { color.WithAlpha(100), SKColors.Transparent },
                        null, SKShaderTileMode.Clamp);
                    paint.Shader = glowShader;
                    canvas.DrawCircle(x, y, size * 2, paint);
                    paint.Shader = null;
                }
                
                // Draw core
                paint.Color = color;
                canvas.DrawCircle(x, y, size, paint);
            }
            
            // Draw connecting lines between nearby particles
            var lineColor = GetSkiaColorFromScheme(0.5 + avgIntensity * 0.5);
            for (int i = 0; i < _particles.Count; i++)
            {
                for (int j = i + 1; j < _particles.Count; j++)
                {
                    double dx = _particles[i].X - _particles[j].X;
                    double dy = _particles[i].Y - _particles[j].Y;
                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < 0.15)
                    {
                        byte alpha = (byte)(200 * (1 - dist / 0.15));
                        linePaint.Color = lineColor.WithAlpha(alpha);
                        canvas.DrawLine(
                            (float)(_particles[i].X * info.Width), (float)(_particles[i].Y * info.Height),
                            (float)(_particles[j].X * info.Width), (float)(_particles[j].Y * info.Height),
                            linePaint);
                    }
                }
            }
        }
        
        /// <summary>
        /// HD Aurora visualization with smooth flowing waves.
        /// </summary>
        private void RenderAuroraHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            double avgIntensity = 0;
            if (_smoothedData.Length > 0)
            {
                foreach (var v in _smoothedData) avgIntensity += v;
                avgIntensity /= _smoothedData.Length;
            }
            
            using var paint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };
            
            int layers = 5;
            for (int layer = 0; layer < layers; layer++)
            {
                using var path = new SKPath();
                float phase = (float)(_animationPhase + layer * 0.5);
                
                path.MoveTo(0, info.Height);
                
                for (int x = 0; x <= info.Width; x += 10)
                {
                    float normalizedX = x / (float)info.Width;
                    int dataIdx = (int)(normalizedX * _smoothedData.Length) % Math.Max(1, _smoothedData.Length);
                    double dataValue = _smoothedData.Length > 0 ? _smoothedData[dataIdx] : 0.5;
                    
                    float wave = (float)(Math.Sin(normalizedX * 4 + phase) * 30 +
                                        Math.Sin(normalizedX * 7 + phase * 1.3) * 20);
                    float baseY = info.Height * (0.3f + layer * 0.12f);
                    float y = baseY + wave + (float)(dataValue * 60);
                    
                    path.LineTo(x, y);
                }
                
                path.LineTo(info.Width, info.Height);
                path.Close();
                
                var color = GetSkiaColorFromScheme((layer + 1) / (float)layers);
                byte alpha = (byte)(120 - layer * 20);
                paint.Color = color.WithAlpha(alpha);
                
                canvas.DrawPath(path, paint);
            }
        }
        
        /// <summary>
        /// HD Starfield warp visualization — classic After Dark style with warp streaks.
        /// Stars move toward viewer with Z-depth projection, streaks show motion.
        /// </summary>
        private void RenderStarfieldHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float centerX = w / 2f;
            float centerY = h / 2f;
            
            // Calculate audio intensity for speed
            double avgIntensity = 0;
            int bassCount = Math.Min(10, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++) avgIntensity += _smoothedData[i];
            avgIntensity = bassCount > 0 ? avgIntensity / bassCount : 0.3;
            double speedMultiplier = 1.0 + avgIntensity * 4;
            
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var linePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
            
            foreach (var star in _stars)
            {
                // Move star toward viewer
                star.Z -= star.Speed * speedMultiplier;
                if (star.Z <= 0.01)
                {
                    star.X = _random.NextDouble() - 0.5;
                    star.Y = _random.NextDouble() - 0.5;
                    star.Z = 4.0;
                }
                
                // 3D→2D projection
                float depthFactor = (float)(1.0 / star.Z);
                float screenX = centerX + (float)(star.X * w * depthFactor);
                float screenY = centerY + (float)(star.Y * h * depthFactor);
                
                if (screenX < -10 || screenX > w + 10 || screenY < -10 || screenY > h + 10) continue;
                
                float size = Math.Max(0.2f, 0.5f * depthFactor * (1 + (float)avgIntensity * 0.2f));
                byte brightness = (byte)Math.Clamp(255 * depthFactor * 0.5 * (1 + avgIntensity), 50, 255);
                
                SKColor color = star.ColorType switch
                {
                    1 => new SKColor(brightness, brightness, (byte)(brightness * 0.4f)),
                    2 => new SKColor(brightness, (byte)(brightness * 0.47f), (byte)(brightness * 0.47f)),
                    3 => new SKColor((byte)(brightness * 0.59f), (byte)(brightness * 0.71f), brightness),
                    _ => new SKColor(brightness, brightness, brightness)
                };
                
                // Warp streak lines
                double streakLength = Math.Min(30, star.Speed * speedMultiplier * 60 / star.Z);
                if (streakLength > 1.5)
                {
                    double prevZ = star.Z + star.Speed * speedMultiplier;
                    float prevDepth = (float)(1.0 / prevZ);
                    float prevX = centerX + (float)(star.X * w * prevDepth);
                    float prevY = centerY + (float)(star.Y * h * prevDepth);
                    
                    linePaint.Color = color;
                    linePaint.StrokeWidth = Math.Max(0.2f, size * 0.2f);
                    canvas.DrawLine(prevX, prevY, screenX, screenY, linePaint);
                }
                
                // Star point with glow for close stars (skip blur on large surfaces)
                if (size > 1.0f && !_isLargeSurface)
                {
                    using var starGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * 0.8f);
                    paint.MaskFilter = starGlow;
                    paint.Color = color.WithAlpha(40);
                    canvas.DrawCircle(screenX, screenY, size * 1.5f, paint);
                    paint.MaskFilter = null;
                }
                
                paint.Color = color;
                canvas.DrawCircle(screenX, screenY, size, paint);
            }
        }
        
        /// <summary>
        /// HD Wave Grid with perspective-distorted cells using SkiaSharp.
        /// </summary>
        private void RenderWaveGridHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            int gridX = 20;
            int gridY = 10;
            float cellWidth = w / gridX;
            float cellHeight = h / gridY;
            
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var waveGridGlowBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
            using var glowPaint = _isLargeSurface ? null : new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill, MaskFilter = waveGridGlowBlur };
            
            for (int y = 0; y < gridY; y++)
            {
                for (int x = 0; x < gridX; x++)
                {
                    int dataIdx = (x * _smoothedData.Length / gridX) % Math.Max(1, _smoothedData.Length);
                    double value = _smoothedData.Length > 0 ? _smoothedData[dataIdx] : 0;
                    
                    float perspective = 1.0f - (y / (float)gridY) * 0.5f;
                    float xOffset = (x - gridX / 2f) * (1 - perspective) * 10;
                    float wavePhase = (float)(_animationPhase + x * 0.2 + y * 0.3);
                    float waveHeight = (float)(value * 30 * perspective + Math.Sin(wavePhase) * 5);
                    
                    float px = x * cellWidth + xOffset;
                    float py = y * cellHeight - waveHeight;
                    float cw = cellWidth * perspective;
                    float ch = cellHeight * perspective * 0.8f;
                    
                    var color = GetSkiaColorFromScheme(value);
                    byte alpha = (byte)(150 + value * 100);
                    
                    if (glowPaint != null)
                    {
                        glowPaint.Color = color.WithAlpha((byte)(alpha / 3));
                        canvas.DrawRoundRect(px - 1, py - 1, cw + 2, ch + 2, 3, 3, glowPaint);
                    }
                    
                    paint.Color = color.WithAlpha(alpha);
                    canvas.DrawRoundRect(px, py, cw, ch, 2, 2, paint);
                }
            }
        }
        
        /// <summary>
        /// HD Flying Toasters (After Dark) using SkiaSharp with anti-aliased shapes.
        /// </summary>
        private void RenderToastersHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            
            double avgIntensity = 0;
            for (int i = 0; i < Math.Min(20, _smoothedData.Length); i++)
                avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, Math.Min(20, _smoothedData.Length));
            
            double wingSpeed = 0.3 + avgIntensity * 0.5;
            
            using var bodyPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            using var wingPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var toastPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            
            foreach (var toaster in _toasters)
            {
                toaster.X -= toaster.Speed * (1 + avgIntensity * 2);
                toaster.Y += toaster.Speed * 0.6 * (1 + avgIntensity * 2);
                if (toaster.X < -0.2) { toaster.X = 1.2; toaster.Y = _random.NextDouble() * 0.5 - 0.2; }
                if (toaster.Y > 1.2) { toaster.Y = -0.2; toaster.X = _random.NextDouble() * 0.5 + 0.5; }
                toaster.WingPhase += wingSpeed;
                
                float sx = (float)(toaster.X * w);
                float sy = (float)(toaster.Y * h);
                float size = (float)(toaster.Size * (1 + avgIntensity * 0.3));
                float wingAngle = (float)(Math.Sin(toaster.WingPhase) * 0.5);
                
                // Body
                bodyPaint.Color = new SKColor(180, 180, 190);
                strokePaint.Color = new SKColor(100, 100, 110);
                canvas.DrawRoundRect(sx, sy, size, size * 0.7f, 4, 4, bodyPaint);
                canvas.DrawRoundRect(sx, sy, size, size * 0.7f, 4, 4, strokePaint);
                
                // Slots
                bodyPaint.Color = new SKColor(50, 50, 50);
                canvas.DrawRoundRect(sx + size * 0.1f, sy + size * 0.15f, size * 0.3f, size * 0.1f, 2, 2, bodyPaint);
                canvas.DrawRoundRect(sx + size * 0.55f, sy + size * 0.15f, size * 0.3f, size * 0.1f, 2, 2, bodyPaint);
                
                // Wings
                float wingY = sy + size * 0.2f + wingAngle * size * 0.3f;
                wingPaint.Color = new SKColor(220, 220, 230);
                using var leftWing = new SKPath();
                leftWing.MoveTo(sx, sy + size * 0.35f);
                leftWing.LineTo(sx - size * 0.4f, wingY);
                leftWing.LineTo(sx, wingY + size * 0.15f);
                leftWing.Close();
                canvas.DrawPath(leftWing, wingPaint);
                
                using var rightWing = new SKPath();
                rightWing.MoveTo(sx + size, sy + size * 0.35f);
                rightWing.LineTo(sx + size + size * 0.4f, wingY);
                rightWing.LineTo(sx + size, wingY + size * 0.15f);
                rightWing.Close();
                canvas.DrawPath(rightWing, wingPaint);
                
                // Toast popping out
                if (avgIntensity > 0.3)
                {
                    float pop = (float)((avgIntensity - 0.3) * size * 0.5);
                    using var toastShader = SKShader.CreateLinearGradient(
                        new SKPoint(0, sy - pop * 0.5f), new SKPoint(0, sy + size * 0.3f),
                        new SKColor[] { new SKColor(139, 90, 43), new SKColor(210, 180, 140), new SKColor(139, 90, 43) },
                        null, SKShaderTileMode.Clamp);
                    toastPaint.Shader = toastShader;
                    canvas.DrawRoundRect(sx + size * 0.15f, sy - pop * 0.5f, size * 0.25f, size * 0.3f + pop, 3, 3, toastPaint);
                    canvas.DrawRoundRect(sx + size * 0.58f, sy - pop * 0.5f, size * 0.25f, size * 0.3f + pop, 3, 3, toastPaint);
                    toastPaint.Shader = null;
                }
            }
        }
        
        /// <summary>
        /// HD Matrix digital rain using SkiaSharp with proper glow effects.
        /// </summary>
        private void RenderMatrixHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            
            double avgIntensity = 0, bassIntensity = 0;
            for (int i = 0; i < Math.Min(10, _smoothedData.Length); i++) bassIntensity += _smoothedData[i];
            bassIntensity /= Math.Max(1, Math.Min(10, _smoothedData.Length));
            for (int i = 0; i < _smoothedData.Length; i++) avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            double speedMultiplier = 0.5 + bassIntensity * 3.0;
            float fontSize = Math.Max(8, w / 60);
            float charHeight = fontSize * 1.2f;
            
            using var textPaint = new SKPaint { IsAntialias = true, IsLinearText = true };
            // Use lighter glow on large surfaces (fullscreen) for performance -- but DON'T skip entirely
            using var matrixGlowBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _isLargeSurface ? fontSize * 0.6f : fontSize * 1.5f);
            using var glowPaint = new SKPaint
            {
                IsAntialias = true,
                MaskFilter = matrixGlowBlur
            };
            using var matrixWideGlowBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, fontSize * 3.5f);
            using var wideGlowPaint = _isLargeSurface ? null : new SKPaint { IsAntialias = true, MaskFilter = matrixWideGlowBlur };
            // Use cached Consolas typeface (prevents GDI handle leak)
            var matrixTypeface = _tfConsolas ?? _tfCourierNew;
            using var font = new SKFont { Size = fontSize, Typeface = matrixTypeface };
            using var glowFont = new SKFont { Size = fontSize * 1.5f, Typeface = matrixTypeface };
            
            foreach (var column in _matrixColumns)
            {
                column.Y += column.Speed * speedMultiplier;
                if (column.Y > 1.0 + (column.Length * charHeight / h))
                {
                    column.Y = _random.NextDouble() * -0.5 - 0.2;
                    column.Speed = 0.008 + _random.NextDouble() * 0.012;
                    column.Length = 8 + _random.Next(12);
                    for (int i = 0; i < column.Characters.Length; i++)
                        if (_random.NextDouble() < 0.4) column.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                }
                
                float screenX = (float)(column.X * w);
                float startY = (float)(column.Y * h);
                
                for (int i = 0; i < column.Length && i < column.Characters.Length; i++)
                {
                    float charY = startY - (i * charHeight);
                    if (charY < -charHeight || charY > h) continue;
                    if (_random.NextDouble() < 0.03) column.Characters[i] = MatrixChars[_random.Next(MatrixChars.Length)];
                    
                    double brightness = i == 0 ? 1.0 : Math.Pow(1.0 - (i / (double)column.Length), 0.6);
                    brightness *= (0.5 + avgIntensity);
                    brightness = Math.Min(1.0, brightness);
                    
                    string ch = column.Characters[i].ToString();
                    
                    // Wide ambient glow for the lead character (skip on large surfaces for perf)
                    if (i == 0 && wideGlowPaint != null)
                    {
                        wideGlowPaint.Color = new SKColor(0, 255, 0, 120);
                        canvas.DrawText(ch, screenX - fontSize * 0.3f, charY + fontSize, glowFont, wideGlowPaint);
                        // Extra wide bloom
                        wideGlowPaint.Color = new SKColor(0, 200, 0, 50);
                        canvas.DrawCircle(screenX + fontSize * 0.3f, charY + fontSize * 0.5f, fontSize * 2.5f, wideGlowPaint);
                    }
                    
                    // Glow for head characters — on fullscreen only glow top 3 (cheaper), on small screens glow top 8
                    int glowLimit = _isLargeSurface ? 3 : 8;
                    if (i < glowLimit)
                    {
                        double glowI = i == 0 ? 1.0 : i == 1 ? 0.85 : i == 2 ? 0.7 : i == 3 ? 0.55 : i == 4 ? 0.4 : i == 5 ? 0.3 : i == 6 ? 0.2 : 0.1;
                        glowPaint.Color = new SKColor((byte)(120 * glowI), (byte)(255 * glowI), (byte)(120 * glowI), (byte)(220 * glowI));
                        canvas.DrawText(ch, screenX - fontSize * 0.2f, charY + fontSize, glowFont, glowPaint);
                    }
                    
                    // Main character — vivid Matrix green
                    if (i == 0)
                        textPaint.Color = new SKColor(220, 255, 220); // Near-white green head
                    else if (i == 1)
                        textPaint.Color = new SKColor((byte)(60 + 80 * brightness), (byte)(220 + 35 * brightness), (byte)(60 + 80 * brightness));
                    else
                        textPaint.Color = new SKColor(0, (byte)(80 + 175 * brightness), 0);
                    
                    canvas.DrawText(ch, screenX, charY + fontSize, font, textPaint);
                }
            }
            
            // Subtle green overlay on bass hits
            if (avgIntensity > 0.25)
            {
                using var overlayPaint = new SKPaint { Color = new SKColor(0, 255, 0, (byte)(avgIntensity * 25)) };
                canvas.DrawRect(0, 0, w, h, overlayPaint);
            }
        }
        
        /// <summary>
        /// HD Star Wars Crawl using SkiaSharp with smooth perspective text.
        /// </summary>
        private void RenderStarWarsCrawlHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            
            // Stars
            using var starPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            for (int i = 0; i < 50; i++)
            {
                float starX = (float)((i * 17 + _crawlPosition * 3) % w);
                float starY = (float)((i * 23 + i * i) % h);
                float starSize = 1 + (i % 3);
                byte bright = (byte)(150 + (i * 7) % 100);
                starPaint.Color = new SKColor(bright, bright, bright);
                canvas.DrawCircle(starX, starY, starSize / 2f, starPaint);
            }
            
            double avgIntensity = 0;
            for (int i = 0; i < _smoothedData.Length; i++) avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            if (avgIntensity > 0.01) _crawlPosition += 0.8 * _crawlSpeed;
            
            float lineHeight = h / 18f;
            float totalTextHeight = StarWarsCrawlText.Length * lineHeight;
            if (_crawlPosition > h + totalTextHeight) _crawlPosition = 0;
            
            float vanishY = h * 0.15f;
            float bottomY = h * 1.1f;
            
            using var textPaint = new SKPaint { IsAntialias = true, IsLinearText = true };
            using var font = new SKFont { Typeface = _tfArial };
            
            for (int i = 0; i < StarWarsCrawlText.Length; i++)
            {
                string line = StarWarsCrawlText[i];
                if (string.IsNullOrEmpty(line)) continue;
                
                float lineY = (float)(bottomY - _crawlPosition + (i * lineHeight));
                if (lineY > bottomY || lineY < vanishY - lineHeight) continue;
                
                float progress = (bottomY - lineY) / (bottomY - vanishY);
                progress = Math.Clamp(progress, 0, 1);
                float scale = 1.0f - (progress * 0.85f);
                float alpha = 1.0f - progress * progress;
                if (alpha < 0.05f) continue;
                
                bool isTitle = line.StartsWith("EPISODE") || line.StartsWith("STAR WARS") || line.Contains("NEW HOPE") || line.Contains("EMPIRE STRIKES") || line.Contains("RETURN OF") || line == "THE END";
                float baseFontSize = isTitle ? lineHeight * 1.2f : lineHeight * 0.7f;
                float fs = baseFontSize * scale;
                if (fs < 6) continue;
                
                font.Size = fs;
                float textWidth = font.MeasureText(line, out _);
                float textX = (w - textWidth * scale) / 2f;
                float screenY = vanishY + (lineY - vanishY) * scale;
                
                byte shimmer = (byte)(avgIntensity * 25);
                textPaint.Color = new SKColor(
                    (byte)Math.Min(255, 229 + shimmer),
                    (byte)Math.Min(255, 177 + shimmer),
                    46,
                    (byte)(alpha * 255));
                
                canvas.Save();
                canvas.Scale(scale, 1f, w / 2f, screenY);
                canvas.DrawText(line, textX, screenY + fs, font, textPaint);
                canvas.Restore();
            }
            
            // Bottom glow
            using var glowRect = new SKPaint();
            using var glowShader = SKShader.CreateLinearGradient(
                new SKPoint(0, h * 0.85f), new SKPoint(0, h),
                new SKColor[] { SKColors.Transparent, new SKColor(255, 200, 50, (byte)(30 + avgIntensity * 20)) },
                null, SKShaderTileMode.Clamp);
            glowRect.Shader = glowShader;
            canvas.DrawRect(0, h * 0.85f, w, h * 0.15f, glowRect);
        }
        
        /// <summary>
        /// HD Stargate SG-1 visualization with procedurally rendered naquadah gate ring,
        /// rotating inner glyph ring, 9 chevrons, event horizon, kawoosh, and wormhole animation.
        /// </summary>
        private void RenderStargateHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float cx = w / 2f;
            float cy = h / 2f;
            // Gate fits within view — account for the ramp/base at bottom
            float gateSize = Math.Min(w, h) * 0.88f;
            float gateR = gateSize * 0.42f; // Outer radius of the ring for chevron/wormhole alignment
            float innerR = gateR * 0.72f;   // Inner opening
            
            double avgIntensity = 0, bassIntensity = 0;
            for (int i = 0; i < Math.Min(10, _smoothedData.Length); i++) bassIntensity += _smoothedData[i];
            bassIntensity = _smoothedData.Length > 0 ? bassIntensity / Math.Min(10, _smoothedData.Length) : 0;
            for (int i = 0; i < _smoothedData.Length; i++) avgIntensity += _smoothedData[i];
            avgIntensity = _smoothedData.Length > 0 ? avgIntensity / _smoothedData.Length : 0;
            
            // Update dialing state machine
            if (_stargateIsDialing && avgIntensity > 0.01)
            {
                if (!_stargateChevronEngaging)
                {
                    int ci = _stargateChevronLit;
                    if (ci < 7 && ci < _stargateTargetGlyphs.Length)
                    {
                        const double degreesPerGlyph = 360.0 / 39;
                        double targetAngle = _stargateTargetGlyphs[ci] * degreesPerGlyph;
                        _stargateTargetPosition = 270 - targetAngle;
                        while (_stargateTargetPosition < 0) _stargateTargetPosition += 360;
                        while (_stargateTargetPosition >= 360) _stargateTargetPosition -= 360;
                        double cur = _stargateDialPosition % 360; if (cur < 0) cur += 360;
                        double dist = _stargateDialDirection > 0 ? (_stargateTargetPosition - cur + 360) % 360 : (cur - _stargateTargetPosition + 360) % 360;
                        if (dist > 3) _stargateDialPosition += _stargateDialDirection * (1.5 + bassIntensity * 4);
                        else { _stargateChevronEngaging = true; _stargateChevronEngageTimer = 0; }
                    }
                }
                else
                {
                    _stargateChevronEngageTimer += 0.08 + avgIntensity * 0.1;
                    if (_stargateChevronEngageTimer >= 1.0)
                    {
                        _stargateChevronLit++; _stargateChevronEngaging = false; _stargateChevronEngageTimer = 0;
                        _stargateDialDirection *= -1;
                        if (_stargateChevronLit >= 7) { _stargateIsDialing = false; _stargateKawooshActive = true; _stargateKawooshPhase = 0; }
                    }
                }
            }
            if (!_stargateIsDialing)
            {
                if (_stargateKawooshActive) { _stargateKawooshPhase += 0.04; if (_stargateKawooshPhase >= 1.0) _stargateKawooshActive = false; }
                _stargateWormholePhase += 0.08 + avgIntensity * 0.15;
            }
            
            using var paint = new SKPaint { IsAntialias = true };
            
            // Draw event horizon / wormhole BEHIND the gate (fills the inner ring)
            if (!_stargateIsDialing)
            {
                float eventR = innerR * 0.92f;
                float phase = (float)_stargateWormholePhase;
                
                // Kawoosh burst
                if (_stargateKawooshActive)
                {
                    float kawooshR = eventR * (1f + (float)_stargateKawooshPhase * 1.2f);
                    paint.Style = SKPaintStyle.Fill;
                    paint.Color = new SKColor(120, 200, 255, (byte)(220 * (1 - _stargateKawooshPhase)));
                    {
                        using var kawooshBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20);
                        paint.MaskFilter = kawooshBlur;
                        canvas.DrawCircle(cx, cy, kawooshR, paint);
                        paint.MaskFilter = null;
                    }
                }
                
                // Clip to inner circle for clean event horizon
                canvas.Save();
                using var clipPath = new SKPath();
                clipPath.AddCircle(cx, cy, eventR);
                canvas.ClipPath(clipPath);
                
                // Rippling water-like event horizon with multiple concentric layers
                for (int layer = 0; layer < 8; layer++)
                {
                    float r = eventR * (1f - layer * 0.1f);
                    float wave = (float)(Math.Sin(phase * 1.5 + layer * 0.7) * 4 + avgIntensity * 8);
                    float wave2 = (float)(Math.Cos(phase * 1.1 + layer * 1.3) * 3);
                    
                    // SG-1 wormhole: shifting blues with bright shimmering center
                    byte blue = (byte)Math.Clamp(160 + layer * 12, 0, 255);
                    byte green = (byte)Math.Clamp(100 + layer * 18 + (int)(avgIntensity * 40), 0, 255);
                    byte alpha2 = (byte)Math.Clamp(200 - layer * 20, 30, 255);
                    
                    paint.Color = new SKColor(40, green, blue, alpha2);
                    {
                        using var layerBlur = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4 + layer * 3);
                        paint.MaskFilter = layerBlur;
                        paint.Style = SKPaintStyle.Fill;
                        canvas.DrawCircle(cx + wave2, cy + wave * 0.5f, r + wave, paint);
                        paint.MaskFilter = null;
                    }
                }
                
                // Shimmer ripples across the event horizon
                for (int ripple = 0; ripple < 6; ripple++)
                {
                    float rippleR = eventR * (0.3f + ripple * 0.12f);
                    float rippleWave = (float)(Math.Sin(phase * 2 + ripple * 1.1) * eventR * 0.03f);
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1.5f + (float)(avgIntensity * 2);
                    byte shimmerAlpha = (byte)(60 + avgIntensity * 60 + Math.Sin(phase * 3 + ripple) * 30);
                    paint.Color = new SKColor(180, 220, 255, shimmerAlpha);
                    canvas.DrawCircle(cx, cy, rippleR + rippleWave, paint);
                }
                
                // Central bright glow
                using var holeShader = SKShader.CreateRadialGradient(
                    new SKPoint(cx, cy), eventR * 0.7f,
                    new SKColor[] { 
                        new SKColor(220, 240, 255, (byte)(180 + avgIntensity * 60)), 
                        new SKColor(100, 180, 240, (byte)(120 + avgIntensity * 40)),
                        new SKColor(40, 100, 200, 60),
                        SKColors.Transparent 
                    },
                    new float[] { 0, 0.3f, 0.6f, 1f },
                    SKShaderTileMode.Clamp);
                paint.Shader = holeShader;
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawCircle(cx, cy, eventR * 0.7f, paint);
                paint.Shader = null;
                
                canvas.Restore();
            }
            else
            {
                // Dark interior when dialing (no wormhole yet)
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(5, 5, 15);
                canvas.DrawCircle(cx, cy, innerR * 0.92f, paint);
            }
            
            // ==== PROCEDURAL GATE RING (SG-1 Stargate) ====
            float ringWidth = gateR - innerR;
            float midR = innerR + ringWidth * 0.55f; // Division between inner glyph ring and outer ring
            
            // --- Outer fixed ring (naquadah frame) ---
            using var outerRingPath = new SKPath();
            outerRingPath.AddCircle(cx, cy, gateR);
            outerRingPath.AddCircle(cx, cy, midR);
            outerRingPath.FillType = SKPathFillType.EvenOdd;
            
            // Metallic sweep gradient for naquadah appearance
            using var metalShader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                new SKColor[] {
                    new SKColor(95, 100, 110), new SKColor(125, 130, 140),
                    new SKColor(80, 85, 95), new SKColor(115, 120, 130),
                    new SKColor(90, 95, 105), new SKColor(130, 135, 145),
                    new SKColor(85, 90, 100), new SKColor(95, 100, 110)
                },
                null);
            paint.Shader = metalShader;
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawPath(outerRingPath, paint);
            paint.Shader = null;
            
            // Segment lines on outer ring (decorative radial divisions)
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1.5f;
            paint.Color = new SKColor(60, 65, 75, 180);
            for (int s = 0; s < 36; s++)
            {
                double segAngle = s * 10 * Math.PI / 180.0;
                float sx1 = cx + (float)(Math.Cos(segAngle) * midR);
                float sy1 = cy + (float)(Math.Sin(segAngle) * midR);
                float sx2 = cx + (float)(Math.Cos(segAngle) * gateR);
                float sy2 = cy + (float)(Math.Sin(segAngle) * gateR);
                canvas.DrawLine(sx1, sy1, sx2, sy2, paint);
            }
            
            // --- Inner rotating glyph ring (spins during dialing like the SG-1 gate) ---
            canvas.Save();
            canvas.RotateDegrees((float)_stargateDialPosition, cx, cy);
            
            using var innerRingPath = new SKPath();
            innerRingPath.AddCircle(cx, cy, midR);
            innerRingPath.AddCircle(cx, cy, innerR);
            innerRingPath.FillType = SKPathFillType.EvenOdd;
            
            // Slightly different metallic tone for the rotating ring
            using var innerMetalShader = SKShader.CreateSweepGradient(
                new SKPoint(cx, cy),
                new SKColor[] {
                    new SKColor(105, 110, 120), new SKColor(80, 85, 95),
                    new SKColor(115, 120, 130), new SKColor(90, 95, 105),
                    new SKColor(105, 110, 120)
                },
                null);
            paint.Shader = innerMetalShader;
            paint.Style = SKPaintStyle.Fill;
            canvas.DrawPath(innerRingPath, paint);
            paint.Shader = null;
            
            // 39 constellation glyph markers on the inner ring
            float glyphMidR = (innerR + midR) / 2f;
            float glyphMarkSize = ringWidth * 0.08f;
            for (int g = 0; g < 39; g++)
            {
                double glyphAngle = g * (360.0 / 39) * Math.PI / 180.0;
                float gx = cx + (float)(Math.Cos(glyphAngle) * glyphMidR);
                float gy = cy + (float)(Math.Sin(glyphAngle) * glyphMidR);
                
                // Draw simplified constellation markers with variety
                paint.Color = new SKColor(170, 180, 195, 200);
                
                canvas.Save();
                canvas.Translate(gx, gy);
                canvas.RotateDegrees((float)(g * (360.0 / 39)));
                
                if (g % 3 == 0)
                {
                    // V-shape glyph
                    using var glyphPath = new SKPath();
                    glyphPath.MoveTo(-glyphMarkSize, -glyphMarkSize * 0.6f);
                    glyphPath.LineTo(0, glyphMarkSize * 0.6f);
                    glyphPath.LineTo(glyphMarkSize, -glyphMarkSize * 0.6f);
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1.5f;
                    canvas.DrawPath(glyphPath, paint);
                }
                else if (g % 3 == 1)
                {
                    // Diamond glyph
                    using var glyphPath = new SKPath();
                    glyphPath.MoveTo(0, -glyphMarkSize * 0.7f);
                    glyphPath.LineTo(glyphMarkSize * 0.5f, 0);
                    glyphPath.LineTo(0, glyphMarkSize * 0.7f);
                    glyphPath.LineTo(-glyphMarkSize * 0.5f, 0);
                    glyphPath.Close();
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1.2f;
                    canvas.DrawPath(glyphPath, paint);
                }
                else
                {
                    // Circle with center dot
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1.2f;
                    canvas.DrawCircle(0, 0, glyphMarkSize * 0.5f, paint);
                    paint.Style = SKPaintStyle.Fill;
                    canvas.DrawCircle(0, 0, glyphMarkSize * 0.15f, paint);
                }
                
                canvas.Restore();
                
                // Radial tick line between glyph positions
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1f;
                paint.Color = new SKColor(70, 75, 85, 150);
                double tickAngle = (g + 0.5) * (360.0 / 39) * Math.PI / 180.0;
                float tx1 = cx + (float)(Math.Cos(tickAngle) * (innerR + ringWidth * 0.05f));
                float ty1 = cy + (float)(Math.Sin(tickAngle) * (innerR + ringWidth * 0.05f));
                float tx2 = cx + (float)(Math.Cos(tickAngle) * (midR - ringWidth * 0.05f));
                float ty2 = cy + (float)(Math.Sin(tickAngle) * (midR - ringWidth * 0.05f));
                canvas.DrawLine(tx1, ty1, tx2, ty2, paint);
            }
            
            canvas.Restore(); // End inner ring rotation
            
            // Ring edge highlights
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 2f;
            paint.Color = new SKColor(55, 60, 70);
            canvas.DrawCircle(cx, cy, gateR, paint);
            paint.Color = new SKColor(65, 70, 80);
            canvas.DrawCircle(cx, cy, midR, paint);
            paint.Color = new SKColor(145, 150, 160);
            paint.StrokeWidth = 1.5f;
            canvas.DrawCircle(cx, cy, innerR, paint);
            
            // --- 9 Chevrons (SG-1 style arrow markers around the outer ring) ---
            // Lighting order: top chevron first, down the right side, then left side
            // Skip indices 4 and 5 (bottom two at ~5 and ~7 o'clock)
            int[] chevLightingOrder = { 0, 1, 2, 3, 6, 7, 8 }; // top(0), down right(1,2,3), left side(6,7,8)
            float chevSize = ringWidth * 0.55f;
            for (int c = 0; c < 9; c++)
            {
                double chevAngleRad = (c * 40 - 90) * Math.PI / 180.0;
                float chevCenterX = cx + (float)(Math.Cos(chevAngleRad) * (gateR - ringWidth * 0.1f));
                float chevCenterY = cy + (float)(Math.Sin(chevAngleRad) * (gateR - ringWidth * 0.1f));
                
                // Determine if this chevron should be lit based on lighting order
                bool lit = false;
                bool engaging = false;
                for (int lockIdx = 0; lockIdx < _stargateChevronLit && lockIdx < chevLightingOrder.Length; lockIdx++)
                {
                    if (chevLightingOrder[lockIdx] == c) lit = true;
                }
                if (_stargateChevronEngaging && _stargateChevronLit < chevLightingOrder.Length)
                {
                    if (chevLightingOrder[_stargateChevronLit] == c) engaging = true;
                }
                
                float chevW = chevSize * 0.7f;
                float chevH = chevSize;
                float rotAngle = c * 40f; // rotate to point inward toward center
                
                canvas.Save();
                canvas.Translate(chevCenterX, chevCenterY);
                canvas.RotateDegrees(rotAngle);
                
                // Chevron body - inverted V / arrow pointing inward with hollow center
                using var chevPath = new SKPath();
                chevPath.MoveTo(-chevW / 2, -chevH / 3);
                chevPath.LineTo(0, chevH / 2);
                chevPath.LineTo(chevW / 2, -chevH / 3);
                chevPath.LineTo(chevW / 4, -chevH / 3);
                chevPath.LineTo(0, chevH / 6);
                chevPath.LineTo(-chevW / 4, -chevH / 3);
                chevPath.Close();
                
                paint.Style = SKPaintStyle.Fill;
                
                if (lit || engaging)
                {
                    float glow = engaging ? (float)_stargateChevronEngageTimer : 1f;
                    
                    // Orange glow halo behind chevron (skip on large surfaces)
                    {
                        using var chevGlow = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8 + glow * 6);
                        paint.MaskFilter = chevGlow;
                        paint.Color = new SKColor(255, (byte)(100 + 100 * glow), 0, (byte)(160 * glow));
                        canvas.DrawPath(chevPath, paint);
                        paint.MaskFilter = null;
                    }
                    
                    // Solid lit chevron body
                    paint.Color = new SKColor(255, (byte)(150 + 70 * glow), 20, 255);
                    canvas.DrawPath(chevPath, paint);
                    
                    // Bright center highlight line
                    paint.Color = new SKColor(255, 240, 180, (byte)(200 * glow));
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1.5f;
                    canvas.DrawLine(0, chevH / 2, 0, -chevH / 6, paint);
                }
                else
                {
                    // Unlit chevron - dark metallic
                    paint.Color = new SKColor(65, 65, 75);
                    canvas.DrawPath(chevPath, paint);
                    
                    // Edge outline
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 1f;
                    paint.Color = new SKColor(95, 95, 110);
                    canvas.DrawPath(chevPath, paint);
                }
                
                canvas.Restore();
            }
            
            // Chevron status text
            string statusText;
            if (_stargateIsDialing)
            {
                if (_stargateChevronEngaging)
                    statusText = $"CHEVRON {_stargateChevronLit + 1} ENGAGING...";
                else if (_stargateChevronLit > 0)
                    statusText = $"CHEVRON {_stargateChevronLit} LOCKED";
                else
                    statusText = "DIALING...";
            }
            else if (_stargateKawooshActive)
            {
                statusText = "WORMHOLE ESTABLISHED";
            }
            else
            {
                statusText = "EVENT HORIZON ACTIVE";
            }
            
            using var statusFont = new SKFont { Size = Math.Max(14, h * 0.028f), Typeface = _tfConsolas };
            paint.Style = SKPaintStyle.Fill;
            paint.Color = _stargateIsDialing ? new SKColor(255, 180, 80, 220) : new SKColor(100, 200, 255, 220);
            float statusW = statusFont.MeasureText(statusText);
            // Position text in middle-right area
            float textX = w * 0.55f + (w * 0.4f - statusW) / 2f;
            float textY = cy;
            canvas.DrawText(statusText, textX, textY, statusFont, paint);
            
            // Spectrum bars along bottom as subtle visualizer
            int barCount = Math.Min(48, _smoothedData.Length);
            float barAreaW = w * 0.6f;
            float barW = barAreaW / barCount;
            float barLeft = (w - barAreaW) / 2f;
            for (int i = 0; i < barCount; i++)
            {
                double val = i < _smoothedData.Length ? _smoothedData[i] * _sensitivity : 0;
                float barH = (float)(val * h * 0.08);
                if (barH < 1) continue;
                float bx = barLeft + i * barW;
                paint.Color = new SKColor(80, (byte)(140 + val * 100), 255, (byte)(120 + val * 80));
                canvas.DrawRect(bx, h - 10 - barH, barW - 1, barH, paint);
            }
        }
        
        /// <summary>
        /// HD Klingon visualization using SkiaSharp with Klingon logo PNG, Klingon font, 
        /// bat'leth-style spectrum bars, and angular grid lines.
        /// </summary>
        private void RenderKlingonHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float cx = w / 2f;
            float cy = h / 2f;
            
            double avgIntensity = 0, bassIntensity = 0;
            int bassCount = Math.Min(10, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++) bassIntensity += _smoothedData[i];
            bassIntensity = bassCount > 0 ? bassIntensity / bassCount : 0;
            for (int i = 0; i < _smoothedData.Length; i++) avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            using var paint = new SKPaint { IsAntialias = true };
            
            // Dark red radial background
            canvas.Clear(SKColors.Black);
            using var bgShader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), Math.Max(w, h) * 0.7f,
                new SKColor[] { new SKColor(40, 10, 10), new SKColor(25, 5, 5), new SKColor(10, 0, 0) },
                null, SKShaderTileMode.Clamp);
            paint.Shader = bgShader;
            canvas.DrawRect(0, 0, w, h, paint);
            paint.Shader = null;
            
            // Load and draw Klingon logo PNG (cached)
            _skKlingonLogo ??= LoadSkBitmapFromResource("pack://application:,,,/Assets/KlingonGlowLogo.png");
            if (_skKlingonLogo != null)
            {
                float logoSize = Math.Min(w, h) * 0.7f;
                float logoX = (w - logoSize) / 2f;
                float logoY = (h - logoSize) / 2f;
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(255, 255, 255, (byte)(80 + bassIntensity * 50));
                canvas.DrawBitmap(_skKlingonLogo, new SKRect(logoX, logoY, logoX + logoSize, logoY + logoSize), paint);
            }
            
            // Angular grid lines radiating from center
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            byte gridAlpha = (byte)(30 + avgIntensity * 40);
            paint.Color = new SKColor(139, 0, 0, gridAlpha);
            float maxLen = (float)Math.Max(w, h);
            for (int i = 0; i < 8; i++)
            {
                float angle = i * 45f * (float)Math.PI / 180f;
                canvas.DrawLine(cx, cy, cx + (float)Math.Cos(angle) * maxLen, cy + (float)Math.Sin(angle) * maxLen, paint);
            }
            
            // Bat'leth-style spectrum bars (mirrored from center)
            int barCount = Math.Min(_barCount, _smoothedData.Length);
            float barAreaW = w * 0.8f;
            float barW = barAreaW / barCount;
            float barSpacing = barW * 0.15f;
            float effectiveBarW = barW - barSpacing;
            float barLeft = w * 0.1f;
            float pointOffset = effectiveBarW * 0.3f;
            
            paint.Style = SKPaintStyle.Fill;
            for (int i = 0; i < barCount; i++)
            {
                double val = i < _smoothedData.Length ? _smoothedData[i] * _sensitivity : 0;
                float barH = (float)Math.Max(4, val * h * 0.45);
                float bx = barLeft + i * barW;
                
                // Gradient color: dark red → red → metallic gold
                byte r = (byte)(80 + Math.Min(175, val * 175));
                byte g = (byte)(Math.Min(200, val * 200 * 0.8));
                byte b = (byte)(Math.Min(140, val * 100));
                
                // Upper blade (pointing up)
                using var upperBlade = new SKPath();
                upperBlade.MoveTo(bx, cy);
                upperBlade.LineTo(bx + pointOffset, cy - barH);
                upperBlade.LineTo(bx + effectiveBarW - pointOffset, cy - barH);
                upperBlade.LineTo(bx + effectiveBarW, cy);
                upperBlade.Close();
                paint.Color = new SKColor(r, g, b, 220);
                if (val > 0.5 && !_isLargeSurface)
                {
                    using var barGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(val * 8));
                    paint.MaskFilter = barGlow;
                    paint.Color = new SKColor(255, 50, 50, (byte)(val * 120));
                    canvas.DrawPath(upperBlade, paint);
                    paint.MaskFilter = null;
                    paint.Color = new SKColor(r, g, b, 220);
                }
                canvas.DrawPath(upperBlade, paint);
                
                // Lower blade (mirrored, pointing down)
                using var lowerBlade = new SKPath();
                lowerBlade.MoveTo(bx, cy);
                lowerBlade.LineTo(bx + pointOffset, cy + barH);
                lowerBlade.LineTo(bx + effectiveBarW - pointOffset, cy + barH);
                lowerBlade.LineTo(bx + effectiveBarW, cy);
                lowerBlade.Close();
                canvas.DrawPath(lowerBlade, paint);
            }
            
            // Load Klingon font (cached) — try multiple paths
            _skKlingonTypeface ??= LoadSkTypefaceFromFile("Assets/KlingonFont.ttf");
            _skKlingonTypeface ??= LoadSkTypefaceFromFile("Assets/klingon font.ttf");
            
            // Klingon text labels — use Klingon font exclusively
            var klingonTextFace = _skKlingonTypeface ?? _tfImpactBold;
            using var textFont = new SKFont { Size = Math.Max(18, h * 0.04f), Typeface = klingonTextFace };
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(220, 160, 100, (byte)(120 + avgIntensity * 80));
            canvas.DrawText("We Are Klingon - tlhIngan maH", 20, 30, textFont, paint);
            
            paint.Color = new SKColor(255, 180, 100, 180);
            using var bottomFont = new SKFont { Size = Math.Max(24, h * 0.055f), Typeface = klingonTextFace };
            canvas.DrawText("Honor - batlh", 20, h - 25, bottomFont, paint);
            
            // Battle cry on high intensity
            if (avgIntensity > 0.4)
            {
                byte textAlpha = (byte)((avgIntensity - 0.4) * 400);
                paint.Color = new SKColor(255, 200, 140, textAlpha);
                using var cryFont = new SKFont { Size = Math.Max(32, h * 0.12f), Typeface = _tfImpactBold };
                float cryWidth = cryFont.MeasureText("Qapla'!", out _);
                canvas.DrawText("Qapla'!", w - cryWidth - 20, 50, cryFont, paint);
            }
        }
        
        /// <summary>
        /// HD Federation visualization using SkiaSharp with FederationLogo PNG,
        /// transporter particles, LCARS panels, and spectrum ring.
        /// </summary>
        private void RenderFederationHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float cx = w / 2f;
            float cy = h / 2f;
            
            double avgIntensity = 0;
            for (int i = 0; i < _smoothedData.Length; i++) avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            using var paint = new SKPaint { IsAntialias = true };
            
            // Deep space blue radial background
            canvas.Clear(SKColors.Black);
            using var bgShader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), Math.Max(w, h) * 0.7f,
                new SKColor[] { new SKColor(10, 20, 60), new SKColor(5, 10, 30), new SKColor(2, 5, 15) },
                null, SKShaderTileMode.Clamp);
            paint.Shader = bgShader;
            canvas.DrawRect(0, 0, w, h, paint);
            paint.Shader = null;
            
            // Transporter particles (draw behind logo)
            _transporterPhase += 0.05 + avgIntensity * 0.1;
            paint.Style = SKPaintStyle.Fill;
            foreach (var tp in _transporterParticles)
            {
                tp.Y += tp.VelocityY;
                tp.Phase += 0.1;
                if (tp.Y > 1.1 || tp.Y < -0.1) { tp.Y = _random.NextDouble(); tp.X = 0.3 + _random.NextDouble() * 0.4; }
                
                float px = (float)(tp.X * w);
                float py = (float)(tp.Y * h);
                float ps = (float)(tp.Size * (1 + avgIntensity) * (0.5 + 0.5 * Math.Sin(tp.Phase)));
                
                SKColor tpColor = tp.ColorType switch
                {
                    1 => new SKColor(255, 215, 0, (byte)(tp.Brightness * 200)),
                    2 => new SKColor(255, 255, 255, (byte)(tp.Brightness * 180)),
                    _ => new SKColor(100, 150, 255, (byte)(tp.Brightness * 200))
                };
                
                paint.Color = tpColor;
                if (!_isLargeSurface)
                {
                    using var tpGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ps);
                    paint.MaskFilter = tpGlow;
                    canvas.DrawCircle(px, py, ps * 2, paint);
                    paint.MaskFilter = null;
                }
                else
                {
                    canvas.DrawCircle(px, py, ps * 2, paint);
                }
                paint.Color = tpColor.WithAlpha(255);
                canvas.DrawCircle(px, py, ps * 0.5f, paint);
            }
            
            // Transporter beam columns
            for (int col = 0; col < 5; col++)
            {
                float beamX = w * (0.3f + col * 0.1f);
                paint.Color = new SKColor(100, 150, 255, (byte)(20 + avgIntensity * 30));
                if (!_isLargeSurface)
                {
                    using var beamGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8);
                    paint.MaskFilter = beamGlow;
                    canvas.DrawRect(beamX - 3, 0, 6, h, paint);
                    paint.MaskFilter = null;
                }
                else
                {
                    canvas.DrawRect(beamX - 3, 0, 6, h, paint);
                }
            }
            
            // Federation logo PNG (cached)
            _skFederationLogo ??= LoadSkBitmapFromResource("pack://application:,,,/Assets/FederationLogoTransparent.png");
            if (_skFederationLogo != null)
            {
                float logoSize = Math.Min(w, h) * 0.4f;
                float logoX = (w - logoSize) / 2f;
                float logoY = (h - logoSize) / 2f - h * 0.02f;
                float pulse = (float)(1.0 + avgIntensity * 0.08);
                
                // Glow behind logo (skip blur on large surfaces)
                {
                    SKMaskFilter? logoGlow = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 15);
                    paint.MaskFilter = logoGlow;
                    paint.Color = new SKColor(100, 150, 255, (byte)(40 + avgIntensity * 40));
                    canvas.DrawCircle(cx, cy - h * 0.02f, logoSize * 0.5f * pulse, paint);
                    paint.MaskFilter = null;
                    logoGlow?.Dispose();
                }
                
                paint.Color = new SKColor(255, 255, 255, (byte)(200 + avgIntensity * 55));
                float scaledSize = logoSize * pulse;
                float scaledX = cx - scaledSize / 2f;
                float scaledY = cy - h * 0.02f - scaledSize / 2f;
                canvas.DrawBitmap(_skFederationLogo, new SKRect(scaledX, scaledY, scaledX + scaledSize, scaledY + scaledSize), paint);
            }
            
            // Circular spectrum ring around emblem
            float ringR = Math.Min(w, h) * 0.28f;
            int segments = Math.Min(48, _smoothedData.Length);
            float segAngle = 360f / segments;
            paint.Style = SKPaintStyle.Fill;
            for (int i = 0; i < segments; i++)
            {
                int idx = Math.Clamp((int)((double)i / segments * _smoothedData.Length), 0, _smoothedData.Length - 1);
                double val = Math.Clamp(_smoothedData[idx] * _sensitivity, 0, 1);
                float startAngle = i * segAngle - 90;
                float arcLen = segAngle - 1;
                float thickness = (float)(4 + val * 15);
                
                // Color: blue → gold → white based on intensity
                byte r2 = (byte)(100 + val * 155);
                byte g2 = (byte)(150 + val * 65);
                byte b2 = 255;
                paint.Color = new SKColor(r2, g2, b2, 200);
                
                using var arcPath = new SKPath();
                arcPath.AddArc(new SKRect(cx - ringR, cy - h * 0.02f - ringR, cx + ringR, cy - h * 0.02f + ringR), startAngle, arcLen);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = thickness;
                canvas.DrawPath(arcPath, paint);
                paint.Style = SKPaintStyle.Fill;
            }
            
            // LCARS panels at edges
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(255, 153, 0, 180);
            canvas.DrawRoundRect(10, 10, 120, 25, 12, 12, paint);
            paint.Color = new SKColor(204, 153, 255, 180);
            canvas.DrawRoundRect(10, 40, 80, 20, 10, 10, paint);
            paint.Color = new SKColor(153, 153, 255, 180);
            canvas.DrawRoundRect(10, 65, 100, 18, 9, 9, paint);
            
            // "UNITED FEDERATION OF PLANETS" text
            using var fedFont = new SKFont { Size = 14, Typeface = _tfArialBold };
            paint.Color = new SKColor(180, 200, 240, 160);
            string fedText = "UNITED FEDERATION OF PLANETS";
            float textW = fedFont.MeasureText(fedText, out _);
            canvas.DrawText(fedText, (w - textW) / 2, h - 20, fedFont, paint);
            
            // Stardate
            paint.Color = new SKColor(255, 200, 100, 150);
            string stardate = $"STARDATE {DateTime.Now:yyyyMMdd.HHmm}";
            canvas.DrawText(stardate, w - 200, h - 20, fedFont, paint);
        }
        
        /// <summary>
        /// HD Jedi visualization — lightsaber spectrum bars with hilt images, R2-D2 translator,
        /// force particles, Jedi emblem, star background. Matches WPF version.
        /// </summary>
        private void RenderJediHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            
            double avgIntensity = 0;
            for (int i = 0; i < _smoothedData.Length; i++) avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            using var paint = new SKPaint { IsAntialias = true };
            
            // Star field background
            for (int i = 0; i < 80; i++)
            {
                float sx = (float)((i * 31 + 17) % w);
                float sy = (float)((i * 47 + i * i * 3) % (h * 0.7f));
                float twinkle = (float)(0.5 + 0.5 * Math.Sin(_animationPhase * 2 + i * 0.7));
                byte bright = (byte)(120 + 135 * twinkle);
                paint.Color = new SKColor(bright, bright, (byte)(bright * 0.95), 255);
                canvas.DrawCircle(sx, sy, 0.8f + (i % 3) * 0.5f, paint);
            }
            
            // Responsive saber count based on width
            int saberCount = w > 800 ? 16 : (w > 500 ? 12 : 8);
            saberCount = Math.Min(saberCount, _smoothedData.Length);
            float totalW = w * 0.85f;
            float saberSpacing = totalW / saberCount;
            float saberW = saberSpacing * 0.22f; // Thinner blades to fit hilt image
            float hiltH = 40;
            float hiltW = saberW * 2.5f; // Hilt wider than blade
            float maxBladeH = h * 0.6f;
            float baseY = h - 55;
            float startX = (w - totalW) / 2f;
            
            // Lightsaber colors: blue, green, purple
            SKColor[] bladeColors = { new SKColor(50, 100, 255), new SKColor(50, 255, 50), new SKColor(160, 50, 255) };
            
            // Load lightsaber hilt image (cached)
            _skLightsaberHilt ??= LoadSkBitmapFromResource("pack://application:,,,/Assets/lightsaber.png");
            
            for (int i = 0; i < saberCount; i++)
            {
                int idx = Math.Clamp((int)((double)i / saberCount * _smoothedData.Length), 0, _smoothedData.Length - 1);
                double val = Math.Clamp(_smoothedData[idx] * _sensitivity * 1.5, 0, 1);
                float bladeH = (float)(val * maxBladeH);
                float sx = startX + i * saberSpacing;
                
                var saberColor = bladeColors[i % bladeColors.Length];
                
                // Blade glow (outer) — skip blur on large surfaces
                float bladeCenterX = sx + saberSpacing * 0.5f - saberW * 0.5f; // Center blade in spacing
                if (bladeH > 2)
                {
                    if (!_isLargeSurface)
                    {
                        using var saberGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, saberW * 1.2f);
                        paint.MaskFilter = saberGlow;
                        paint.Color = saberColor.WithAlpha(60);
                        canvas.DrawRoundRect(bladeCenterX - saberW * 0.6f, baseY - bladeH, saberW * 2.2f, bladeH, 5, 5, paint);
                        paint.MaskFilter = null;
                    }
                    
                    // Blade body with white core — thin beam
                    using var bladeShader = SKShader.CreateLinearGradient(
                        new SKPoint(bladeCenterX, 0), new SKPoint(bladeCenterX + saberW, 0),
                        new SKColor[] { SKColors.Transparent, saberColor.WithAlpha(180), new SKColor(255, 255, 255, 220), saberColor.WithAlpha(180), SKColors.Transparent },
                        new float[] { 0, 0.15f, 0.5f, 0.85f, 1f },
                        SKShaderTileMode.Clamp);
                    paint.Shader = bladeShader;
                    canvas.DrawRoundRect(bladeCenterX, baseY - bladeH, saberW, bladeH, 3, 3, paint);
                    paint.Shader = null;
                }
                
                // Hilt (image or fallback) — centered under blade, wider than blade
                float hiltX = bladeCenterX + saberW * 0.5f - hiltW * 0.5f;
                if (_skLightsaberHilt != null)
                {
                    canvas.DrawBitmap(_skLightsaberHilt, new SKRect(hiltX, baseY, hiltX + hiltW, baseY + hiltH));
                }
                else
                {
                    paint.Color = new SKColor(140, 140, 150);
                    canvas.DrawRoundRect(hiltX, baseY, hiltW, hiltH, 3, 3, paint);
                    paint.Color = new SKColor(80, 80, 90);
                    canvas.DrawRoundRect(hiltX + hiltW * 0.2f, baseY + 5, hiltW * 0.6f, 6, 2, 2, paint);
                }
            }
            
            // Force particles
            paint.Style = SKPaintStyle.Fill;
            for (int i = 0; i < 20; i++)
            {
                double px = ((_animationPhase * 30 + i * 53) % w);
                double py = ((_animationPhase * 20 + i * i * 7) % (h * 0.7));
                float ps = 2 + (float)(avgIntensity * 3);
                byte pAlpha = (byte)(80 + 100 * Math.Sin(_animationPhase * 3 + i));
                SKColor pColor = i % 2 == 0 ? new SKColor(100, 150, 255, pAlpha) : new SKColor(100, 255, 100, pAlpha);
                paint.Color = pColor;
                {
                    using var forceGlow = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, ps);
                    paint.MaskFilter = forceGlow;
                    canvas.DrawCircle((float)px, (float)py, ps, paint);
                    paint.MaskFilter = null;
                }
            }
            
            // R2-D2 translator panel (top-right) — use Consolas for LUKE lines, Segoe UI Symbol for R2-D2
            float panelW = Math.Min(280, w * 0.28f);
            float panelH = 90;
            float panelX = w - panelW - 10;
            float panelY = 10;
            paint.Color = new SKColor(0, 20, 50, 180);
            canvas.DrawRoundRect(panelX, panelY, panelW, panelH, 8, 8, paint);
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1;
            paint.Color = new SKColor(100, 150, 255, 120);
            canvas.DrawRoundRect(panelX, panelY, panelW, panelH, 8, 8, paint);
            paint.Style = SKPaintStyle.Fill;
            
            _jediTextScrollOffset += 0.3 + avgIntensity * 0.5;
            int msgIdx = (int)(_jediTextScrollOffset / 3) % _r2d2Messages.Count;
            // All R2-D2 text is now ASCII-safe, use Consolas for everything
            using var r2Font = new SKFont { Size = 11, Typeface = _tfConsolasNormal };
            for (int line = 0; line < 4 && (msgIdx + line) < _r2d2Messages.Count; line++)
            {
                string msg = _r2d2Messages[(msgIdx + line) % _r2d2Messages.Count];
                bool isR2 = msg.StartsWith("[R2");
                paint.Color = isR2 ? new SKColor(100, 200, 255, 200) : new SKColor(200, 200, 200, 180);
                canvas.DrawText(msg, panelX + 8, panelY + 18 + line * 18, r2Font, paint);
            }
            
            // "JEDI" label
            using var labelFont = new SKFont { Size = 22, Typeface = _tfArialBold };
            paint.Color = new SKColor(200, 200, 100, 200);
            canvas.DrawText("JEDI", 10, 30, labelFont, paint);
            
            // Quote
            using var quoteFont = new SKFont { Size = 12, Typeface = _tfArial };
            paint.Color = new SKColor(180, 200, 255, 120);
            canvas.DrawText("The Force will be with you. Always.", 10, h - 15, quoteFont, paint);
        }
        
        /// <summary>
        /// HD TimeLord visualization — 5-layer time vortex images, TARDIS on spiral orbit,
        /// TARDIS glow, Gallifreyan circles, vivid colors. Matches WPF version.
        /// </summary>
        private void RenderTimeLordHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            float cx = w / 2f;
            float cy = h / 2f;
            
            // Audio metrics
            double avgIntensity = 0;
            double bassIntensity = 0;
            int bassCount = Math.Min(8, _smoothedData.Length);
            for (int i = 0; i < bassCount; i++) bassIntensity += _smoothedData[i];
            bassIntensity = bassCount > 0 ? bassIntensity / bassCount : 0.3;
            for (int i = 0; i < _smoothedData.Length; i++) avgIntensity += _smoothedData[i];
            avgIntensity /= Math.Max(1, _smoothedData.Length);
            
            // Animation update
            double rotationSpeed = 0.8 + bassIntensity * 2.0 + avgIntensity * 1.0;
            _vortexRotation += rotationSpeed;
            if (_vortexRotation >= 360) _vortexRotation -= 360;
            
            // TARDIS orbital movement
            double orbitSpeed = 0.015 + avgIntensity * 0.025;
            _animationPhase += orbitSpeed;
            double spiralRadius = 0.12 + bassIntensity * 0.08;
            double targetX = 0.5 + Math.Sin(_animationPhase * 1.1) * spiralRadius + Math.Sin(_animationPhase * 2.3) * 0.04;
            double targetY = 0.5 + Math.Cos(_animationPhase * 0.8) * spiralRadius * 0.8 + Math.Cos(_animationPhase * 1.9) * 0.03;
            _tardisX += (targetX - _tardisX) * 0.18;
            _tardisY += (targetY - _tardisY) * 0.18;
            double moveDeltaX = targetX - _tardisX;
            _tardisTumble = _tardisTumble * 0.92 + moveDeltaX * 40;  // Decay tumble so it doesn't accumulate
            double targetScale = 1.0 + bassIntensity * 0.25;
            _tardisScale += (targetScale - _tardisScale) * 0.12;
            
            using var paint = new SKPaint { IsAntialias = true };
            
            // Load TARDIS image (cached)
            _skTardisBitmap ??= LoadSkBitmapFromResource("pack://application:,,,/Assets/Tardis.png");
            
            // === MILKDROP-STYLE FEEDBACK VORTEX ===
            // Double-buffered: draw into front buffer reading from back buffer, then swap.
            // Eliminates per-frame Copy() allocation that caused GC pressure / memory leaks.
            {
                // Cap buffer resolution for performance (smaller in fullscreen)
                int bufW = _isLargeSurface ? 480 : Math.Min((int)w, 800);
                int bufH = (int)(bufW * (h / w));
                if (bufH <= 0) bufH = 1;
                float bcx = bufW / 2f;
                float bcy = bufH / 2f;
                
                // Allocate or resize both persistent buffers
                if (_timeLordVortexBuffer == null || _timeLordVortexW != bufW || _timeLordVortexH != bufH)
                {
                    _timeLordVortexBuffer?.Dispose();
                    _timeLordVortexBufferBack?.Dispose();
                    _timeLordVortexBuffer = new SKBitmap(bufW, bufH, SKColorType.Rgba8888, SKAlphaType.Premul);
                    _timeLordVortexBufferBack = new SKBitmap(bufW, bufH, SKColorType.Rgba8888, SKAlphaType.Premul);
                    using var initCanvas1 = new SKCanvas(_timeLordVortexBuffer);
                    initCanvas1.Clear(SKColors.Black);
                    using var initCanvas2 = new SKCanvas(_timeLordVortexBufferBack);
                    initCanvas2.Clear(SKColors.Black);
                    _timeLordVortexW = bufW;
                    _timeLordVortexH = bufH;
                }
                
                _timeLordVortexFrame++;
                // Slow phase drift for subtle color shimmer (NOT hue cycling)
                _timeLordVortexHue += 0.08 + bassIntensity * 0.15;
                if (_timeLordVortexHue >= 360) _timeLordVortexHue -= 360;
                
                // Double-buffer swap: read from back, draw into front
                var vortexBuf = _timeLordVortexBuffer;
                var prevFrame = _timeLordVortexBufferBack;
                if (vortexBuf == null || prevFrame == null) return;
                using var vCanvas = new SKCanvas(vortexBuf);
                vCanvas.Clear(SKColors.Black);
                
                // --- Step 1: Draw previous frame with zoom + spiral rotation (tunnel effect) ---
                float zoom = 1.015f + (float)(bassIntensity * 0.02 + Math.Sin(_animationPhase * 0.5) * 0.003);
                float spiralRot = (float)(0.8 + bassIntensity * 1.5 + Math.Sin(_animationPhase * 0.3) * 0.3);
                float warpX = (float)(Math.Sin(_animationPhase * 0.6) * avgIntensity * 3);
                float warpY = (float)(Math.Cos(_animationPhase * 0.5) * avgIntensity * 3);
                
                vCanvas.Save();
                vCanvas.Translate(bcx + warpX, bcy + warpY);
                vCanvas.RotateDegrees(spiralRot);
                vCanvas.Scale(zoom, zoom);
                vCanvas.Translate(-bcx, -bcy);
                using (var framePaint = new SKPaint { IsAntialias = true })
                {
                    vCanvas.DrawBitmap(prevFrame, 0, 0, framePaint);
                }
                vCanvas.Restore();
                
                // --- Step 2: Decay (deep blue-indigo overlay — keeps edges dark and colors rich) ---
                float decayAlpha = 18 + (float)(avgIntensity * 8);
                using (var decayPaint = new SKPaint())
                {
                    decayPaint.Color = new SKColor(10, 8, 38, (byte)Math.Clamp(decayAlpha, 14, 40));
                    decayPaint.Style = SKPaintStyle.Fill;
                    vCanvas.DrawRect(0, 0, bufW, bufH, decayPaint);
                }
                // Dark vignette — force outer edges to deep saturated dark blue
                // Must be very opaque at edges to overpower accumulated feedback buffer colors
                using (var vignetteShader = SKShader.CreateRadialGradient(
                    new SKPoint(bcx, bcy), Math.Max(bufW, bufH) * 0.52f,
                    new SKColor[] {
                        SKColors.Transparent,
                        new SKColor(8, 6, 35, 10),
                        new SKColor(10, 8, 45, 100),
                        new SKColor(8, 6, 38, 200),
                        new SKColor(6, 4, 30, 245)
                    },
                    new float[] { 0f, 0.25f, 0.5f, 0.72f, 1f },
                    SKShaderTileMode.Clamp))
                using (var vignettePaint = new SKPaint { Shader = vignetteShader })
                {
                    vCanvas.DrawRect(0, 0, bufW, bufH, vignettePaint);
                }
                
                // --- Step 3: Radial gradient background — deep indigo edges, subtle warm center ---
                // Gentle warmth in center that fades quickly to dark purple
                using (var bgGrad = SKShader.CreateRadialGradient(
                    new SKPoint(bcx, bcy), Math.Max(bufW, bufH) * 0.5f,
                    new SKColor[] {
                        new SKColor(200, 160, 80, (byte)(6 + avgIntensity * 8)),      // Muted gold center
                        new SKColor(120, 50, 100, (byte)(4 + avgIntensity * 5)),      // Muted purple-pink
                        new SKColor(30, 40, 100, (byte)(3 + avgIntensity * 4)),       // Blue-purple mid
                        new SKColor(40, 12, 80, (byte)(2 + avgIntensity * 3)),        // Dark purple
                        new SKColor(10, 3, 25, 1)                                      // Near-black indigo
                    },
                    new float[] { 0f, 0.12f, 0.3f, 0.55f, 1f },
                    SKShaderTileMode.Clamp))
                using (var bgPaint = new SKPaint { Shader = bgGrad, BlendMode = SKBlendMode.Plus })
                {
                    vCanvas.DrawRect(0, 0, bufW, bufH, bgPaint);
                }
                
                // --- Step 4: Draw thick nebulous vortex cloud bands (spiral arms) ---
                using var vortexPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
                
                // FIXED color palette — NO hue cycling. Only purple/indigo/magenta/pink/violet.
                // Small phase drift for subtle shimmer, but colors stay in the purple family.
                float phase = (float)(_timeLordVortexHue * Math.PI / 180);
                float shimmer = (float)(Math.Sin(phase) * 0.12); // ±12% brightness variation
                
                SKColor c1 = new SKColor((byte)(30 + shimmer * 20), (byte)(10 + shimmer * 5), (byte)(90 + shimmer * 30));   // Deep indigo
                SKColor c2 = new SKColor((byte)(100 + shimmer * 25), (byte)(30 + shimmer * 10), (byte)(160 + shimmer * 20)); // Purple
                SKColor c3 = new SKColor((byte)(180 + shimmer * 20), (byte)(60 + shimmer * 15), (byte)(160 + shimmer * 15)); // Magenta/pink
                SKColor c4 = new SKColor((byte)(80 + shimmer * 15), (byte)(20 + shimmer * 8), (byte)(140 + shimmer * 25));   // Deep violet
                SKColor c5 = new SKColor((byte)(255), (byte)(200 + shimmer * 20), (byte)(100 + shimmer * 30));               // Warm gold
                SKColor c6 = new SKColor((byte)(30 + shimmer * 10), (byte)(120 + shimmer * 20), (byte)(230 + shimmer * 15)); // Electric blue
                SKColor c7 = new SKColor((byte)(60 + shimmer * 15), (byte)(80 + shimmer * 12), (byte)(200 + shimmer * 20));  // Deep electric blue
                // Arm colors: purple/magenta tones with electric blue accents
                SKColor[] vortexColors = { c1, c2, c6, c3, c4, c7, c3 };
                // Gold-tinted versions for inner portions of arms
                SKColor cGoldTint = new SKColor((byte)(200 + shimmer * 15), (byte)(160 + shimmer * 15), (byte)(80 + shimmer * 20));
                
                float vortexMaxR = Math.Min(bufW, bufH) * 0.48f;
                int armCount = _isLargeSurface ? 5 : 7; // Fewer arms in fullscreen for performance
                
                // --- Main spiral arms (3-layer cloud rendering, reduced in fullscreen) ---
                for (int arm = 0; arm < armCount; arm++)
                {
                    using var spiralPath = new SKPath();
                    float armOffset = arm * 360f / armCount + (float)_vortexRotation;
                    int sampleCount = _isLargeSurface 
                        ? Math.Min(64, _smoothedData.Length > 0 ? _smoothedData.Length : 64)
                        : Math.Min(128, _smoothedData.Length > 0 ? _smoothedData.Length : 64);
                    
                    // Store points for nebula puffs
                    var armPoints = new List<(float x, float y, float t, float audio)>();
                    
                    for (int s = 0; s < sampleCount; s++)
                    {
                        float t = (float)s / sampleCount;
                        float angle = (armOffset + t * 720) * (float)Math.PI / 180f; // 2 full spiral turns
                        float r = t * vortexMaxR;
                        float audioVal = s < _smoothedData.Length ? (float)(_smoothedData[s] * _sensitivity) : 0;
                        r += audioVal * vortexMaxR * 0.35f;
                        // Turbulence — multiple sine waves for organic wobble
                        r += (float)(Math.Sin(t * 12 + _animationPhase * 3 + arm) * vortexMaxR * 0.04f);
                        r += (float)(Math.Sin(t * 7.3 + _animationPhase * 1.7 + arm * 2.1) * vortexMaxR * 0.025f);
                        
                        float px = bcx + (float)(Math.Cos(angle) * r);
                        float py = bcy + (float)(Math.Sin(angle) * r);
                        
                        if (s == 0) spiralPath.MoveTo(px, py);
                        else spiralPath.LineTo(px, py);
                        
                        armPoints.Add((px, py, t, audioVal));
                    }
                    
                    SKColor armColor = vortexColors[arm % vortexColors.Length];
                    
                    // Wide outer nebula glow — skip in fullscreen for performance
                    if (!_isLargeSurface)
                    {
                        vortexPaint.StrokeWidth = 35 + (float)(bassIntensity * 18);
                        vortexPaint.Color = armColor.WithAlpha(14);
                        using var wideGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20);
                        vortexPaint.MaskFilter = wideGlow;
                        vCanvas.DrawPath(spiralPath, vortexPaint);
                        vortexPaint.MaskFilter = null;
                    }
                    
                    // Mid glow layer — medium blur
                    vortexPaint.StrokeWidth = 16 + (float)(bassIntensity * 10);
                    vortexPaint.Color = armColor.WithAlpha(30);
                    using var midGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10);
                    vortexPaint.MaskFilter = midGlow;
                    vCanvas.DrawPath(spiralPath, vortexPaint);
                    vortexPaint.MaskFilter = null;
                    
                    // Bright core stroke
                    vortexPaint.StrokeWidth = 4 + (float)(bassIntensity * 3);
                    vortexPaint.Color = armColor.WithAlpha((byte)(100 + avgIntensity * 60));
                    vCanvas.DrawPath(spiralPath, vortexPaint);
                    
                    // --- Nebula puffs: skip in fullscreen for performance ---
                    if (!_isLargeSurface)
                    {
                        using var puffPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                        for (int p = 0; p < armPoints.Count; p += 4) // Every 4th point
                        {
                            var (px, py, t, audioVal) = armPoints[p];
                            // Puff size varies with audio + position
                            float puffR = (6 + audioVal * 20 + (float)Math.Sin(p * 0.7 + _animationPhase * 2 + arm) * 4) * (1 - t * 0.3f);
                            // Near center: blend toward gold; outer: use arm color
                            float goldBlend = Math.Clamp(1f - t * 2.5f, 0f, 1f); // 1.0 at center, 0.0 past 40%
                            byte pr = (byte)(armColor.Red + (cGoldTint.Red - armColor.Red) * goldBlend);
                            byte pg = (byte)(armColor.Green + (cGoldTint.Green - armColor.Green) * goldBlend);
                            byte pb = (byte)(armColor.Blue + (cGoldTint.Blue - armColor.Blue) * goldBlend);
                            // Vary alpha per puff for organic look
                            byte pa = (byte)Math.Clamp(12 + audioVal * 25 + Math.Sin(p * 1.3 + _animationPhase) * 6, 4, 40);
                            puffPaint.Color = new SKColor(pr, pg, pb, pa);
                            using var puffBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, puffR * 0.6f);
                            puffPaint.MaskFilter = puffBlur;
                            vCanvas.DrawCircle(px, py, puffR, puffPaint);
                            puffPaint.MaskFilter = null;
                        }
                    }
                }
                
                // --- Secondary wisps: skip in fullscreen for performance ---
                if (!_isLargeSurface)
                {
                    for (int wisp = 0; wisp < armCount; wisp++)
                    {
                        using var wispPath = new SKPath();
                        float wispOffset = (wisp + 0.5f) * 360f / armCount + (float)_vortexRotation * 0.85f;
                        int wSamples = 80;
                        for (int s = 0; s < wSamples; s++)
                        {
                            float t = (float)s / wSamples;
                            float angle = (wispOffset + t * 680) * (float)Math.PI / 180f;
                            float r = t * vortexMaxR * 0.9f;
                            float audioVal = s < _smoothedData.Length ? (float)(_smoothedData[s] * _sensitivity * 0.5f) : 0;
                            r += audioVal * vortexMaxR * 0.25f;
                            r += (float)(Math.Sin(t * 9 + _animationPhase * 2.3 + wisp * 1.7) * vortexMaxR * 0.05f);
                            float px = bcx + (float)(Math.Cos(angle) * r);
                            float py = bcy + (float)(Math.Sin(angle) * r);
                            if (s == 0) wispPath.MoveTo(px, py);
                            else wispPath.LineTo(px, py);
                        }
                        // Soft wide wisp
                        vortexPaint.StrokeWidth = 18 + (float)(bassIntensity * 8);
                        vortexPaint.Color = vortexColors[wisp % vortexColors.Length].WithAlpha(8);
                        using var wispBlur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14);
                        vortexPaint.MaskFilter = wispBlur;
                        vCanvas.DrawPath(wispPath, vortexPaint);
                        vortexPaint.MaskFilter = null;
                    }
                }
                
                // --- Step 5: Golden center glow (event horizon) — compact and warm ---
                using (var innerGold = SKShader.CreateRadialGradient(
                    new SKPoint(bcx, bcy), vortexMaxR * 0.2f,
                    new SKColor[] {
                        new SKColor(255, 220, 140, (byte)(40 + bassIntensity * 45)),   // Warm gold
                        new SKColor(230, 170, 90, (byte)(20 + bassIntensity * 25)),    // Amber
                        new SKColor(150, 80, 100, (byte)(8 + avgIntensity * 10)),      // Dim mauve fade
                        SKColors.Transparent
                    },
                    new float[] { 0f, 0.3f, 0.65f, 1f },
                    SKShaderTileMode.Clamp))
                using (var innerPaint = new SKPaint { Shader = innerGold, BlendMode = SKBlendMode.Plus })
                {
                    vCanvas.DrawRect(0, 0, bufW, bufH, innerPaint);
                }
                
                // Circular energy ring at center
                using var ringPath = new SKPath();
                float ringR = vortexMaxR * 0.15f + (float)(bassIntensity * vortexMaxR * 0.04f);
                int ringPts = 64;
                for (int i = 0; i <= ringPts; i++)
                {
                    float angle = (float)(i * Math.PI * 2 / ringPts);
                    int idx = i % Math.Max(1, _smoothedData.Length);
                    float audioVal = idx < _smoothedData.Length ? (float)(_smoothedData[idx] * _sensitivity) : 0;
                    float r = ringR + audioVal * ringR * 0.8f;
                    float px = bcx + (float)(Math.Cos(angle) * r);
                    float py = bcy + (float)(Math.Sin(angle) * r);
                    if (i == 0) ringPath.MoveTo(px, py);
                    else ringPath.LineTo(px, py);
                }
                ringPath.Close();
                
                // Ring glow — purple
                vortexPaint.StrokeWidth = 6;
                vortexPaint.Color = c2.WithAlpha(40);
                using var ringGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8);
                vortexPaint.MaskFilter = ringGlow;
                vCanvas.DrawPath(ringPath, vortexPaint);
                vortexPaint.MaskFilter = null;
                
                // Ring core — warm gold
                vortexPaint.StrokeWidth = 2;
                vortexPaint.Color = c5.WithAlpha(160);
                vCanvas.DrawPath(ringPath, vortexPaint);
                
                // --- Step 6: Beat flash — subtle purple/golden pulse ---
                if (bassIntensity > 0.5)
                {
                    using var flashPaint = new SKPaint();
                    byte flashA = (byte)Math.Clamp(bassIntensity * 25, 0, 50);
                    flashPaint.Color = new SKColor(120, 60, 180, flashA); // Purple flash
                    flashPaint.BlendMode = SKBlendMode.Plus;
                    vCanvas.DrawRect(0, 0, bufW, bufH, flashPaint);
                }
                
                // --- Step 7: Blit the vortex buffer to the main canvas (upscaled) ---
                if (vortexBuf != null)
                {
                    var destRect = new SKRect(0, 0, w, h);
                    using var blitPaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
                    canvas.DrawBitmap(vortexBuf, destRect, blitPaint);
                }
                
                // Swap buffers: current front becomes next frame's back (source)
                _timeLordVortexBuffer = prevFrame;
                _timeLordVortexBufferBack = vortexBuf;
            }
            
            // === GALLIFREYAN CIRCLES (overlaid on vortex) ===
            // Outer Gallifreyan writing system: concentric circles with arc segments
            float glyphR = Math.Min(w, h) * 0.14f;
            float gallifreyRotation = (float)(_vortexRotation * Math.PI / 180.0);
            
            // Main circle rings - thin elegant strokes
            for (int r = 0; r < 4; r++)
            {
                float radius = glyphR * (0.6f + r * 0.35f);
                float strokeW = r == 0 ? 1.5f : (r == 3 ? 1.0f : 1.2f);
                byte alpha = (byte)(160 - r * 25 + bassIntensity * 50);
                
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = strokeW;
                paint.Color = new SKColor(220, 190, 120, alpha);
                canvas.DrawCircle(cx, cy, radius, paint);
                
                // Decorative arc segments along each ring (Gallifreyan writing look)
                int arcCount = 3 + r * 2;
                for (int a = 0; a < arcCount; a++)
                {
                    float arcStart = (float)(a * 360.0 / arcCount + _vortexRotation * (0.5 + r * 0.3));
                    float arcSweep = 360f / arcCount * 0.4f;
                    float arcRadius = radius + 3 + (float)(avgIntensity * 4);
                    
                    using var arcPath = new SKPath();
                    var arcRect = new SKRect(cx - arcRadius, cy - arcRadius, cx + arcRadius, cy + arcRadius);
                    arcPath.AddArc(arcRect, arcStart, arcSweep);
                    
                    paint.StrokeWidth = strokeW + 0.5f;
                    paint.Color = new SKColor(230, 200, 130, (byte)(120 + avgIntensity * 60));
                    canvas.DrawPath(arcPath, paint);
                }
                
                // Small connecting dots at arc intersections
                int dotCount = arcCount;
                paint.Style = SKPaintStyle.Fill;
                for (int d = 0; d < dotCount; d++)
                {
                    float angle = (float)(d * Math.PI * 2 / dotCount + gallifreyRotation * (1 + r * 0.4));
                    float dx = cx + (float)(Math.Cos(angle) * radius);
                    float dy = cy + (float)(Math.Sin(angle) * radius);
                    float dotSize = 1.5f + (float)(avgIntensity * 1.5);
                    paint.Color = new SKColor(255, 225, 140, (byte)(180 - r * 25));
                    canvas.DrawCircle(dx, dy, dotSize, paint);
                }
            }
            
            // Inner symbol: small circles inside the Gallifreyan writing
            float innerR = glyphR * 0.35f;
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 1.0f;
            paint.Color = new SKColor(200, 180, 100, 120);
            canvas.DrawCircle(cx, cy, innerR, paint);
            
            // Tiny inner dots — represent Gallifreyan consonants
            paint.Style = SKPaintStyle.Fill;
            for (int d = 0; d < 4; d++)
            {
                float angle = (float)(d * Math.PI / 2 + gallifreyRotation * 1.5);
                float dx = cx + (float)(Math.Cos(angle) * innerR * 0.6f);
                float dy = cy + (float)(Math.Sin(angle) * innerR * 0.6f);
                paint.Color = new SKColor(220, 200, 120, 150);
                canvas.DrawCircle(dx, dy, 2f, paint);
            }
            
            // === TARDIS ===
            float tardisBaseSize = Math.Min(w, h) * 0.18f;
            float tardisDisplaySize = (float)(tardisBaseSize * _tardisScale);
            float tardisPosX = (float)(_tardisX * w);
            float tardisPosY = (float)(_tardisY * h);
            
            // TARDIS glow ellipse — warm golden center glow matching vortex image
            float glowRadius2 = tardisDisplaySize * 1.2f;
            paint.Style = SKPaintStyle.Fill;
            using var glowShader = SKShader.CreateRadialGradient(
                new SKPoint(tardisPosX, tardisPosY),
                glowRadius2,
                new SKColor[] {
                    new SKColor(255, 220, 120, (byte)(90 + avgIntensity * 80)),
                    new SKColor(200, 140, 180, (byte)(50 + avgIntensity * 40)),
                    new SKColor(100, 50, 150, 0)
                },
                new float[] { 0, 0.5f, 1f },
                SKShaderTileMode.Clamp);
            paint.Shader = glowShader;
            canvas.DrawOval(tardisPosX, tardisPosY, glowRadius2, glowRadius2, paint);
            paint.Shader = null;
            
            if (_skTardisBitmap != null)
            {
                // Draw TARDIS image with gentle tumble rotation — NO extra canvas.Scale
                // (tardisDisplaySize already incorporates _tardisScale)
                canvas.Save();
                float tardisCx = (float)(_tardisX * w);
                float tardisCy = (float)(_tardisY * h);
                canvas.Translate(tardisCx, tardisCy);
                canvas.RotateDegrees((float)(_tardisTumble * 0.15));
                canvas.Translate(-tardisCx, -tardisCy);
                
                // TARDIS proportions: use actual image aspect ratio for natural look
                float imgAspect = (float)_skTardisBitmap.Width / _skTardisBitmap.Height;
                float tardisH = tardisDisplaySize * 1.6f;
                float tardisW = tardisH * imgAspect;  // Preserve image aspect ratio
                var tardisRect = new SKRect(
                    tardisCx - tardisW / 2f, tardisCy - tardisH / 2f, 
                    tardisCx + tardisW / 2f, tardisCy + tardisH / 2f);
                canvas.DrawBitmap(_skTardisBitmap, tardisRect);
                canvas.Restore();
            }
            else
            {
                // Fallback blue TARDIS box — proper police box proportions
                canvas.Save();
                float tardisCx = (float)(_tardisX * w);
                float tardisCy = (float)(_tardisY * h);
                canvas.Translate(tardisCx, tardisCy);
                canvas.RotateDegrees((float)(_tardisTumble * 0.15));
                canvas.Translate(-tardisCx, -tardisCy);
                
                float boxH = tardisDisplaySize * 1.6f;
                float boxW = boxH * 0.5f;
                float boxX = tardisCx - boxW / 2f;
                float boxY = tardisCy - boxH / 2f;
                
                using var boxGrad = SKShader.CreateLinearGradient(
                    new SKPoint(boxX, boxY), new SKPoint(boxX + boxW, boxY + boxH),
                    new SKColor[] { new SKColor(0, 60, 140), new SKColor(0, 40, 100) },
                    null, SKShaderTileMode.Clamp);
                paint.Shader = boxGrad;
                canvas.DrawRoundRect(boxX, boxY, boxW, boxH, 3, 3, paint);
                paint.Shader = null;
                
                paint.Style = SKPaintStyle.Stroke;
                paint.Color = new SKColor(0, 100, 200);
                paint.StrokeWidth = 2;
                canvas.DrawRoundRect(boxX, boxY, boxW, boxH, 3, 3, paint);
                
                // POLICE BOX sign at top
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(255, 255, 255, 200);
                float signH = boxH * 0.08f;
                canvas.DrawRect(boxX + 2, boxY + 2, boxW - 4, signH, paint);
                using var policeFont = new SKFont { Size = signH * 0.7f, Typeface = _tfArialBold };
                paint.Color = new SKColor(0, 30, 80);
                float policeW = policeFont.MeasureText("POLICE BOX");
                canvas.DrawText("POLICE BOX", boxX + (boxW - policeW) / 2f, boxY + 2 + signH * 0.75f, policeFont, paint);
                
                // Window panels (2x2 grid)
                paint.Color = new SKColor(100, 160, 220, 180);
                float panelMargin = boxW * 0.1f;
                float panelGap = boxW * 0.06f;
                float panelSize = (boxW - panelMargin * 2 - panelGap) / 2f;
                float panelTop = boxY + signH + boxH * 0.05f;
                for (int pr = 0; pr < 2; pr++)
                    for (int pc = 0; pc < 2; pc++)
                        canvas.DrawRect(boxX + panelMargin + pc * (panelSize + panelGap), panelTop + pr * (panelSize + panelGap), panelSize, panelSize, paint);
                
                // Lamp on top
                paint.Color = new SKColor(200, 220, 255, 220);
                float lampW = boxW * 0.15f;
                float lampH = boxH * 0.06f;
                canvas.DrawRect(boxX + (boxW - lampW) / 2f, boxY - lampH, lampW, lampH, paint);
                
                paint.Style = SKPaintStyle.Fill;
                canvas.Restore();
            }
            
            // === STATUS TEXT ===
            using var statusFont = new SKFont { Size = Math.Max(14, h * 0.025f), Typeface = _tfConsolas };
            paint.Color = new SKColor(150, 100, 255, (byte)(180 + avgIntensity * 75));
            string statusTxt = "\u25C8 TIME VORTEX ACTIVE \u25C8";
            float statusW = statusFont.MeasureText(statusTxt);
            canvas.DrawText(statusTxt, (w - statusW) / 2f, 30, statusFont, paint);
            
            // === QUOTE ===
            using var quoteFont = new SKFont { Size = Math.Max(14, h * 0.028f), Typeface = _tfGeorgia };
            paint.Color = new SKColor(200, 150, 255, (byte)(150 + bassIntensity * 100));
            string quoteTxt = "\"Allons-y!\"";
            float quoteW = quoteFont.MeasureText(quoteTxt);
            canvas.DrawText(quoteTxt, (w - quoteW) / 2f, h - 20, quoteFont, paint);
        }
        
        /// <summary>
        /// HD VU Meter using SkiaSharp with smooth gradient fills.
        /// </summary>
        private void RenderVUMeterHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            
            double peakLeft = 0, peakRight = 0;
            if (DataContext is EnhancedAudioPlayerViewModel vm2)
            {
                peakLeft = vm2.VUPeakLeft;
                peakRight = vm2.VUPeakRight;
            }
            else if (_smoothedData.Length > 1)
            {
                peakLeft = _smoothedData.Take(_smoothedData.Length / 2).Average();
                peakRight = _smoothedData.Skip(_smoothedData.Length / 2).Average();
            }
            
            using var paint = new SKPaint { IsAntialias = true };
            
            float meterW = w * 0.35f;
            float meterH = h * 0.6f;
            float spacing = w * 0.1f;
            float meterY = (h - meterH) / 2;
            float leftX = w / 2 - meterW - spacing / 2;
            float rightX = w / 2 + spacing / 2;
            
            // Draw meters
            for (int ch = 0; ch < 2; ch++)
            {
                float mx = ch == 0 ? leftX : rightX;
                double peak = ch == 0 ? peakLeft : peakRight;
                
                // Background
                paint.Color = new SKColor(30, 30, 30);
                paint.Style = SKPaintStyle.Fill;
                canvas.DrawRoundRect(mx, meterY, meterW, meterH, 4, 4, paint);
                paint.Color = new SKColor(80, 80, 80);
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 2;
                canvas.DrawRoundRect(mx, meterY, meterW, meterH, 4, 4, paint);
                
                // Meter fill with gradient (green → yellow → red)
                float fillH = (float)(peak * meterH * 0.9);
                if (fillH > 1)
                {
                    paint.Style = SKPaintStyle.Fill;
                    float fillY = meterY + meterH * 0.95f - fillH;
                    using var meterShader = SKShader.CreateLinearGradient(
                        new SKPoint(mx, meterY + meterH), new SKPoint(mx, meterY),
                        new SKColor[] { new SKColor(0, 200, 0), new SKColor(200, 200, 0), new SKColor(255, 0, 0) },
                        new float[] { 0f, 0.6f, 1f },
                        SKShaderTileMode.Clamp);
                    paint.Shader = meterShader;
                    canvas.DrawRoundRect(mx + 5, fillY, meterW - 10, fillH, 2, 2, paint);
                    paint.Shader = null;
                    
                    // Peak indicator line
                    paint.Color = SKColors.White;
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 3;
                    canvas.DrawLine(mx + 5, fillY, mx + meterW - 5, fillY, paint);
                }
                
                // dB scale markings
                paint.Style = SKPaintStyle.Fill;
                paint.Color = new SKColor(180, 180, 180);
                using var scaleFont = new SKFont { Size = 10 };
                string[] dbLabels = { "0", "-3", "-6", "-12", "-20", "-40" };
                float[] dbPositions = { 0.05f, 0.15f, 0.25f, 0.4f, 0.6f, 0.9f };
                for (int d = 0; d < dbLabels.Length; d++)
                {
                    float labelY = meterY + meterH * dbPositions[d];
                    canvas.DrawText(dbLabels[d], mx + meterW + 5, labelY + 4, scaleFont, paint);
                    paint.Style = SKPaintStyle.Stroke;
                    paint.StrokeWidth = 0.5f;
                    canvas.DrawLine(mx + meterW - 3, labelY, mx + meterW + 3, labelY, paint);
                    paint.Style = SKPaintStyle.Fill;
                }
                
                // Channel label
                using var chFont = new SKFont { Size = 16, Typeface = _tfArialBold };
                paint.Color = new SKColor(200, 200, 200);
                canvas.DrawText(ch == 0 ? "L" : "R", mx + meterW / 2 - 5, meterY + meterH + 25, chFont, paint);
            }
        }
        
        /// <summary>
        /// HD Oscilloscope using SkiaSharp with CRT phosphor look and smooth waveform.
        /// </summary>
        private void RenderOscilloscopeHdSkia(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            
            float[]? oscData = null;
            if (DataContext is EnhancedAudioPlayerViewModel vm3)
                oscData = vm3.OscilloscopeData;
            
            var schemeColor = GetSkiaColorFromScheme(0.3);
            using var paint = new SKPaint { IsAntialias = true };
            
            // Grid
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 0.5f;
            paint.Color = new SKColor(schemeColor.Red, schemeColor.Green, schemeColor.Blue, 80);
            for (int i = 1; i < 10; i++) canvas.DrawLine(w * i / 10, 0, w * i / 10, h, paint);
            for (int i = 1; i < 8; i++) canvas.DrawLine(0, h * i / 8, w, h * i / 8, paint);
            
            // Center line
            paint.StrokeWidth = 1;
            paint.Color = new SKColor(schemeColor.Red, schemeColor.Green, schemeColor.Blue, 150);
            canvas.DrawLine(0, h / 2, w, h / 2, paint);
            
            // Waveform with glow
            var waveColor = GetSkiaColorFromScheme(0.5);
            using var wavePath = new SKPath();
            float centerY = h / 2;
            
            if (oscData != null && oscData.Length > 1)
            {
                float step = w / (oscData.Length - 1);
                wavePath.MoveTo(0, centerY - oscData[0] * h * 0.4f);
                for (int i = 1; i < oscData.Length; i++)
                {
                    float x = i * step;
                    float y = centerY - oscData[i] * h * 0.4f;
                    wavePath.LineTo(x, y);
                }
            }
            else if (_smoothedData.Length > 1)
            {
                float step = w / (_smoothedData.Length - 1);
                for (int i = 0; i < _smoothedData.Length; i++)
                {
                    float x = i * step;
                    float phase = (float)(i * Math.PI * 4 / _smoothedData.Length);
                    float y = centerY - (float)(Math.Sin(phase) * _smoothedData[i] * h * 0.35);
                    if (i == 0) wavePath.MoveTo(x, y);
                    else wavePath.LineTo(x, y);
                }
            }
            
            // Draw glow (skip on large surfaces)
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 6;
            paint.Color = waveColor.WithAlpha(60);
            {
                using var oscGlow = _isLargeSurface ? null : SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
                paint.MaskFilter = oscGlow;
                canvas.DrawPath(wavePath, paint);
                paint.MaskFilter = null;
            }
            
            // Draw main trace
            paint.StrokeWidth = 2;
            paint.Color = waveColor;
            canvas.DrawPath(wavePath, paint);
            
            // Label
            using var labelFont = new SKFont { Size = 14, Typeface = _tfConsolas };
            paint.Style = SKPaintStyle.Fill;
            paint.Color = new SKColor(100, 180, 100);
            canvas.DrawText("OSCILLOSCOPE", 10, 20, labelFont, paint);
        }
        
        #endregion
        
        #region GPU-Accelerated SkiaSharp Rendering (Waterfall / 3D Bars / Milkdrop)
        
        // Note: duplicate OnSkiaPaintSurface removed — unified method is in SkiaSharp HD Rendering region above
        
        #region HD Color Engine
        
        /// <summary>
        /// Returns an SKColor from continuous HSL space — produces millions of unique colors
        /// instead of the banded 6-tier approach. Uses the current color scheme as the
        /// base palette but interpolates smoothly through the full gradient.
        /// </summary>
        private SKColor GetHDColor(double value, double saturationBoost = 1.0, double brightnessBoost = 1.0)
        {
            value = Math.Clamp(value, 0, 1);
            
            return _colorScheme switch
            {
                VisualizerColorScheme.Rainbow => SKColorFromHSL(value * 360.0, 1.0 * saturationBoost, 0.5 * brightnessBoost),
                VisualizerColorScheme.Fire => InterpolateHDGradient(value, 
                    new[] { (0.0, new SKColor(20, 0, 0)), (0.2, new SKColor(180, 20, 0)), (0.45, new SKColor(255, 80, 0)),
                            (0.65, new SKColor(255, 160, 10)), (0.85, new SKColor(255, 220, 50)), (1.0, new SKColor(255, 255, 200)) }),
                VisualizerColorScheme.Purple => InterpolateHDGradient(value,
                    new[] { (0.0, new SKColor(30, 0, 50)), (0.3, new SKColor(100, 20, 160)), (0.5, new SKColor(160, 40, 200)),
                            (0.7, new SKColor(200, 80, 220)), (0.85, new SKColor(230, 140, 240)), (1.0, new SKColor(255, 200, 255)) }),
                VisualizerColorScheme.Neon => SKColorFromHSL(180 + value * 140, 1.0 * saturationBoost, (0.45 + value * 0.2) * brightnessBoost),
                VisualizerColorScheme.Ocean => InterpolateHDGradient(value,
                    new[] { (0.0, new SKColor(0, 5, 30)), (0.2, new SKColor(0, 30, 80)), (0.4, new SKColor(0, 80, 140)),
                            (0.6, new SKColor(0, 140, 180)), (0.8, new SKColor(40, 200, 220)), (1.0, new SKColor(150, 240, 255)) }),
                VisualizerColorScheme.Sunset => InterpolateHDGradient(value,
                    new[] { (0.0, new SKColor(40, 0, 60)), (0.2, new SKColor(120, 0, 100)), (0.4, new SKColor(200, 40, 80)),
                            (0.6, new SKColor(255, 100, 30)), (0.8, new SKColor(255, 180, 50)), (1.0, new SKColor(255, 240, 150)) }),
                VisualizerColorScheme.Monochrome => InterpolateHDGradient(value,
                    new[] { (0.0, new SKColor(15, 15, 15)), (0.3, new SKColor(80, 80, 80)), (0.6, new SKColor(160, 160, 160)),
                            (0.8, new SKColor(210, 210, 210)), (1.0, new SKColor(255, 255, 255)) }),
                VisualizerColorScheme.PipBoy => InterpolateHDGradient(value,
                    new[] { (0.0, new SKColor(5, 30, 10)), (0.3, new SKColor(20, 120, 50)), (0.5, new SKColor(40, 180, 80)),
                            (0.7, new SKColor(70, 220, 120)), (0.9, new SKColor(120, 245, 160)), (1.0, new SKColor(180, 255, 200)) }),
                VisualizerColorScheme.LCARS => InterpolateHDGradient(value,
                    new[] { (0.0, new SKColor(60, 80, 200)), (0.25, new SKColor(120, 140, 240)), (0.45, new SKColor(200, 170, 100)),
                            (0.6, new SKColor(255, 200, 80)), (0.8, new SKColor(220, 130, 180)), (1.0, new SKColor(180, 100, 220)) }),
                VisualizerColorScheme.Klingon => InterpolateHDGradient(value,
                    new[] { (0.0, new SKColor(30, 0, 0)), (0.3, new SKColor(120, 10, 10)), (0.5, new SKColor(180, 30, 20)),
                            (0.7, new SKColor(220, 80, 40)), (0.85, new SKColor(200, 140, 100)), (1.0, new SKColor(180, 170, 160)) }),
                _ => InterpolateHDGradient(value, // BlueGreen/Federation
                    new[] { (0.0, new SKColor(0, 20, 80)), (0.25, new SKColor(0, 80, 180)), (0.5, new SKColor(30, 144, 255)),
                            (0.7, new SKColor(80, 200, 220)), (0.85, new SKColor(150, 230, 200)), (1.0, new SKColor(200, 255, 240)) })
            };
        }
        
        /// <summary>
        /// Interpolates smoothly through an HD gradient with arbitrary color stops.
        /// Produces continuous color values — millions of unique colors across the 0-1 range.
        /// </summary>
        private static SKColor InterpolateHDGradient(double t, (double pos, SKColor color)[] stops)
        {
            t = Math.Clamp(t, 0, 1);
            
            // Find the two surrounding stops
            int lower = 0;
            for (int i = 0; i < stops.Length - 1; i++)
            {
                if (t >= stops[i].pos && t <= stops[i + 1].pos)
                {
                    lower = i;
                    break;
                }
                if (i == stops.Length - 2) lower = i; // Clamp to last segment
            }
            
            double segStart = stops[lower].pos;
            double segEnd = stops[lower + 1].pos;
            double segLen = segEnd - segStart;
            double localT = segLen > 0 ? (t - segStart) / segLen : 0;
            
            // Smooth cubic interpolation for perceptually even gradients
            localT = localT * localT * (3.0 - 2.0 * localT); // Hermite smoothstep
            
            var c1 = stops[lower].color;
            var c2 = stops[lower + 1].color;
            
            return new SKColor(
                (byte)(c1.Red + (c2.Red - c1.Red) * localT),
                (byte)(c1.Green + (c2.Green - c1.Green) * localT),
                (byte)(c1.Blue + (c2.Blue - c1.Blue) * localT),
                255);
        }
        
        /// <summary>
        /// Creates an SKColor from HSL values with full floating-point precision.
        /// Hue: 0-360, Saturation: 0-1, Lightness: 0-1.
        /// </summary>
        private static SKColor SKColorFromHSL(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360; // Normalize
            s = Math.Clamp(s, 0, 1);
            l = Math.Clamp(l, 0, 1);
            
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
            
            return new SKColor(
                (byte)Math.Clamp((r + m) * 255, 0, 255),
                (byte)Math.Clamp((g + m) * 255, 0, 255),
                (byte)Math.Clamp((b + m) * 255, 0, 255));
        }
        
        /// <summary>
        /// Builds a SkiaSharp linear gradient shader from the HD color engine.
        /// Used for bar fills, producing smooth color transitions with no banding.
        /// </summary>
        private SKShader CreateHDGradientShader(float x0, float y0, float x1, float y1, int colorStops = 16)
        {
            var colors = new SKColor[colorStops];
            var positions = new float[colorStops];
            for (int i = 0; i < colorStops; i++)
            {
                float t = (float)i / (colorStops - 1);
                colors[i] = GetHDColor(t);
                positions[i] = t;
            }
            return SKShader.CreateLinearGradient(
                new SKPoint(x0, y0), new SKPoint(x1, y1),
                colors, positions, SKShaderTileMode.Clamp);
        }
        
        #endregion
        
        #region HD Waterfall (GPU-Accelerated Spectrogram)
        
        /// <summary>
        /// Renders a scrolling spectrogram using SkiaSharp GPU acceleration.
        /// Uses efficient scroll-and-render-one-row technique instead of full-bitmap redraw.
        /// Each pixel row = one spectrum snapshot. Scrolls down, newest data at top.
        /// </summary>
        private void RenderWaterfallHD(SKCanvas canvas, SKImageInfo info)
        {
            int w = info.Width;
            int h = info.Height;
            if (w <= 0 || h <= 0) return;
            
            // Create or resize the HD bitmap
            bool needsResize = _hdWaterfallBitmap == null || _hdWaterfallBitmap.Width != w || _hdWaterfallBitmap.Height != h;
            if (needsResize)
            {
                _hdWaterfallBitmap?.Dispose();
                _hdWaterfallBitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                // Clear to black on resize
                var clearPixels = _hdWaterfallBitmap.GetPixels();
                unsafe
                {
                    uint* cPtr = (uint*)clearPixels.ToPointer();
                    for (int i = 0; i < w * h; i++)
                        cPtr[i] = 0xFF000000; // opaque black
                }
            }
            
            // Guard against concurrent disposal
            var waterBmp = _hdWaterfallBitmap;
            if (waterBmp == null) return;
            
            // SCROLL: shift all rows down by 1, then render only the NEW top row
            var pixels = waterBmp.GetPixels();
            unsafe
            {
                uint* ptr = (uint*)pixels.ToPointer();
                
                // Scroll down: copy rows from top to bottom (start from bottom to avoid overwrite)
                for (int y = h - 1; y > 0; y--)
                {
                    int srcOffset = (y - 1) * w;
                    int dstOffset = y * w;
                    Buffer.MemoryCopy(ptr + srcOffset, ptr + dstOffset, (long)w * 4, (long)w * 4);
                }
                
                // Render ONLY the top row (row 0) from current spectrum data
                int rowLen = _smoothedData.Length;
                for (int x = 0; x < w; x++)
                {
                    // Logarithmic frequency mapping: low freqs get more screen space
                    double normalizedX = (double)x / w;
                    double logFreq = Math.Pow(normalizedX, 0.7);
                    
                    // Sub-pixel interpolation between frequency bins
                    double exactIdx = logFreq * rowLen;
                    int idx0 = Math.Clamp((int)exactIdx, 0, rowLen - 1);
                    int idx1 = Math.Clamp(idx0 + 1, 0, rowLen - 1);
                    double frac = exactIdx - idx0;
                    double intensity = _smoothedData[idx0] * (1.0 - frac) + _smoothedData[idx1] * frac;
                    intensity *= _sensitivity;
                    intensity = Math.Clamp(intensity, 0, 1);
                    
                    // Apply gamma curve for better dynamic range
                    intensity = Math.Pow(intensity, 0.75);
                    
                    // HD color
                    var color = GetHDColor(intensity);
                    
                    ptr[x] = (uint)(
                        0xFF000000 |
                        ((uint)Math.Clamp((int)color.Blue, 0, 255)) |
                        ((uint)Math.Clamp((int)color.Green, 0, 255) << 8) |
                        ((uint)Math.Clamp((int)color.Red, 0, 255) << 16));
                }
            }
            
            canvas.DrawBitmap(waterBmp, 0, 0);
            
            // Draw frequency labels with anti-aliased text
            using var labelPaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 180),
                TextSize = 11,
                IsAntialias = true,
                Typeface = _tfSegoeUI
            };
            
            string[] freqLabels = { "20Hz", "100Hz", "500Hz", "1kHz", "5kHz", "10kHz", "20kHz" };
            double[] freqPositions = { 0.02, 0.08, 0.22, 0.38, 0.62, 0.78, 0.95 };
            for (int i = 0; i < freqLabels.Length; i++)
            {
                float lx = (float)(freqPositions[i] * w);
                canvas.DrawText(freqLabels[i], lx, h - 4, labelPaint);
            }
            
            // Time axis labels
            using var timePaint = new SKPaint
            {
                Color = new SKColor(255, 255, 255, 120),
                TextSize = 9,
                IsAntialias = true
            };
            canvas.DrawText("now", 4, 14, timePaint);
            // Estimate time range based on FPS and pixel height
            float timeRange = h / Math.Max(_targetFps, 1f);
            canvas.DrawText($"~{timeRange:F0}s ago", 4, h - 20, timePaint);
        }
        
        #endregion
        
        #region HD 3D Bars (GPU-Accelerated)
        
        /// <summary>
        /// Renders 3D perspective spectrum bars using SkiaSharp GPU acceleration.
        /// Features smooth gradients, real-time shadows, ambient glow, specular highlights,
        /// and anti-aliased edges. Each bar uses the HD color engine for continuous
        /// color gradients — no color banding.
        /// </summary>
        private void Render3DBarsHD(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            if (w <= 0 || h <= 0) return;
            
            int barCount = Math.Min(_density, _smoothedData.Length);
            if (barCount <= 0) return;
            
            float barWidth = w / barCount * 0.72f;
            float gap = w / barCount * 0.28f;
            float vanishingY = h * 0.1f;
            float baseY = h * 0.92f;
            float depthOffset = Math.Max(6, w / barCount * 0.25f);
            float maxBarHeight = baseY - vanishingY;
            
            // Draw reflective floor
            using var floorPaint = new SKPaint { IsAntialias = true };
            using var floorShader = SKShader.CreateLinearGradient(
                new SKPoint(0, baseY), new SKPoint(0, h),
                new SKColor[] { new SKColor(30, 30, 40, 100), SKColors.Transparent },
                null, SKShaderTileMode.Clamp);
            floorPaint.Shader = floorShader;
            canvas.DrawRect(0, baseY, w, h - baseY, floorPaint);
            
            using var barPaint = new SKPaint { IsAntialias = true, IsStroke = false };
            using var glowPaint = new SKPaint { IsAntialias = true, IsStroke = false };
            using var edgePaint = new SKPaint { IsAntialias = true, IsStroke = true, StrokeWidth = 0.5f };
            
            for (int i = 0; i < barCount; i++)
            {
                int dataIdx = (int)((double)i / barCount * _smoothedData.Length);
                dataIdx = Math.Clamp(dataIdx, 0, _smoothedData.Length - 1);
                double value = _smoothedData[dataIdx] * _sensitivity;
                value = Math.Clamp(value, 0, 1);
                
                float barHeight = Math.Max(2, (float)(value * maxBarHeight * 0.85));
                float x = i * (barWidth + gap) + gap / 2;
                
                // Perspective scaling
                float centerDist = Math.Abs(i - barCount / 2.0f) / (barCount / 2.0f);
                float perspScale = 1.0f - centerDist * 0.12f;
                float scaledWidth = barWidth * perspScale;
                float scaledHeight = barHeight * perspScale;
                float scaledY = baseY - scaledHeight;
                
                // === AMBIENT GLOW (drawn first, behind the bar) — skip blur on large surfaces ===
                if (value > 0.15 && !_isLargeSurface)
                {
                    var glowColor = GetHDColor(value);
                    glowPaint.Color = glowColor.WithAlpha((byte)(value * 60));
                    using var barGlowMf = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)(value * 8 + 2));
                    glowPaint.MaskFilter = barGlowMf;
                    canvas.DrawRect(x - 4, scaledY - 4, scaledWidth + 8, scaledHeight + 8, glowPaint);
                    glowPaint.MaskFilter = null;
                }
                
                // === FRONT FACE (main bar with HD gradient) ===
                var baseColor = GetHDColor(value);
                var topColor = GetHDColor(Math.Min(value * 1.3, 1.0), 0.9, 1.2);
                var darkColor = new SKColor(
                    (byte)(baseColor.Red * 0.35), 
                    (byte)(baseColor.Green * 0.35), 
                    (byte)(baseColor.Blue * 0.35));
                
                using var frontShader = SKShader.CreateLinearGradient(
                    new SKPoint(x, scaledY), new SKPoint(x, baseY),
                    new SKColor[] { topColor, baseColor, darkColor },
                    new float[] { 0f, 0.4f, 1f },
                    SKShaderTileMode.Clamp);
                barPaint.Shader = frontShader;
                canvas.DrawRect(x, scaledY, scaledWidth, scaledHeight, barPaint);
                barPaint.Shader = null;
                
                // Specular highlight stripe on front face
                if (value > 0.1)
                {
                    var highlightColor = new SKColor(255, 255, 255, (byte)(value * 50 + 15));
                    using var highlightShader = SKShader.CreateLinearGradient(
                        new SKPoint(x, 0), new SKPoint(x + scaledWidth, 0),
                        new SKColor[] { SKColors.Transparent, highlightColor, SKColors.Transparent },
                        new float[] { 0.1f, 0.35f, 0.6f },
                        SKShaderTileMode.Clamp);
                    barPaint.Shader = highlightShader;
                    canvas.DrawRect(x, scaledY, scaledWidth, Math.Min(scaledHeight, 20), barPaint);
                    barPaint.Shader = null;
                }
                
                // === TOP FACE (lighter, 3D perspective) ===
                var topFaceColor = GetHDColor(Math.Min(value * 1.4, 1.0), 0.8, 1.3);
                var topFaceDarker = new SKColor(
                    (byte)(topFaceColor.Red * 0.8),
                    (byte)(topFaceColor.Green * 0.8),
                    (byte)(topFaceColor.Blue * 0.8));
                
                using var topPath = new SKPath();
                topPath.MoveTo(x, scaledY);
                topPath.LineTo(x + scaledWidth, scaledY);
                topPath.LineTo(x + scaledWidth + depthOffset, scaledY - depthOffset);
                topPath.LineTo(x + depthOffset, scaledY - depthOffset);
                topPath.Close();
                
                using var topShader = SKShader.CreateLinearGradient(
                    new SKPoint(x, scaledY), new SKPoint(x + depthOffset, scaledY - depthOffset),
                    new SKColor[] { topFaceColor.WithAlpha(220), topFaceDarker.WithAlpha(200) },
                    null, SKShaderTileMode.Clamp);
                barPaint.Shader = topShader;
                canvas.DrawPath(topPath, barPaint);
                barPaint.Shader = null;
                
                // === RIGHT SIDE FACE (darker for depth) ===
                var sideColor = GetHDColor(value * 0.6);
                var sideDark = new SKColor(
                    (byte)(sideColor.Red * 0.4),
                    (byte)(sideColor.Green * 0.4),
                    (byte)(sideColor.Blue * 0.4));
                
                using var sidePath = new SKPath();
                sidePath.MoveTo(x + scaledWidth, scaledY);
                sidePath.LineTo(x + scaledWidth, baseY);
                sidePath.LineTo(x + scaledWidth + depthOffset, baseY - depthOffset);
                sidePath.LineTo(x + scaledWidth + depthOffset, scaledY - depthOffset);
                sidePath.Close();
                
                using var sideShader = SKShader.CreateLinearGradient(
                    new SKPoint(x + scaledWidth, scaledY), new SKPoint(x + scaledWidth + depthOffset, baseY),
                    new SKColor[] { sideColor.WithAlpha(180), sideDark.WithAlpha(160) },
                    null, SKShaderTileMode.Clamp);
                barPaint.Shader = sideShader;
                canvas.DrawPath(sidePath, barPaint);
                barPaint.Shader = null;
                
                // === EDGE HIGHLIGHTS ===
                edgePaint.Color = new SKColor(255, 255, 255, 25);
                canvas.DrawLine(x, scaledY, x + scaledWidth, scaledY, edgePaint);
                
                // === FLOOR REFLECTION ===
                if (scaledHeight > 10)
                {
                    float reflHeight = Math.Min(scaledHeight * 0.25f, 30f);
                    var reflColor = GetHDColor(value * 0.4);
                    using var reflShader = SKShader.CreateLinearGradient(
                        new SKPoint(x, baseY), new SKPoint(x, baseY + reflHeight),
                        new SKColor[] { reflColor.WithAlpha((byte)(value * 45 + 10)), SKColors.Transparent },
                        null, SKShaderTileMode.Clamp);
                    barPaint.Shader = reflShader;
                    canvas.DrawRect(x, baseY, scaledWidth, reflHeight, barPaint);
                    barPaint.Shader = null;
                }
            }
            
            // Peak indicators with glow
            using var peakPaint = new SKPaint { IsAntialias = true };
            for (int i = 0; i < barCount && i < _peakHeights.Length; i++)
            {
                float x = i * (barWidth + gap) + gap / 2;
                float centerDist2 = Math.Abs(i - barCount / 2.0f) / (barCount / 2.0f);
                float perspScale2 = 1.0f - centerDist2 * 0.12f;
                float scaledWidth2 = barWidth * perspScale2;
                
                float peakY = baseY - (float)(_peakHeights[i] * _sensitivity * maxBarHeight * 0.85 * perspScale2);
                if (peakY < baseY - 3)
                {
                    var peakColor = GetHDColor(Math.Min(_peakHeights[i] * _sensitivity * 1.2, 1.0));
                    peakPaint.Color = peakColor;
                    canvas.DrawRect(x, peakY, scaledWidth2, 2, peakPaint);
                    
                    // Peak glow (skip on large surfaces)
                    if (!_isLargeSurface)
                    {
                        peakPaint.Color = peakColor.WithAlpha(40);
                        using var peakGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
                        peakPaint.MaskFilter = peakGlow;
                        canvas.DrawRect(x - 2, peakY - 2, scaledWidth2 + 4, 6, peakPaint);
                        peakPaint.MaskFilter = null;
                    }
                }
            }
        }
        
        #endregion
        
        #region HD Milkdrop (GPU path)
        
        /// <summary>
        /// GPU-accelerated Milkdrop visualization using SkiaSharp canvas drawing.
        /// Uses a persistent frame buffer with feedback loop (zoom/rotate/decay) and
        /// draws waveforms directly with GPU-accelerated paths, gradients, and glow.
        /// </summary>
        private void RenderMilkdropHD(SKCanvas canvas, SKImageInfo info)
        {
            int w = info.Width;
            int h = info.Height;
            if (w <= 0 || h <= 0) return;
            
            // Cap buffer resolution for fullscreen performance
            int bufW = w > 1200 ? 640 : w;
            int bufH = w > 1200 ? (int)(640.0 * h / w) : h;
            if (bufH <= 0) bufH = 1;
            
            float cx = bufW / 2f;
            float cy = bufH / 2f;
            
            // Audio analysis
            double bass = 0, mid = 0, treb = 0, avg = 0;
            int len = _smoothedData.Length;
            if (len > 0)
            {
                int bassEnd = Math.Min(len / 6, len);
                int midEnd = Math.Min(len / 2, len);
                for (int i = 0; i < bassEnd; i++) bass += _smoothedData[i];
                bass /= Math.Max(1, bassEnd);
                for (int i = bassEnd; i < midEnd; i++) mid += _smoothedData[i];
                mid /= Math.Max(1, midEnd - bassEnd);
                for (int i = midEnd; i < len; i++) treb += _smoothedData[i];
                treb /= Math.Max(1, len - midEnd);
                for (int i = 0; i < len; i++) avg += _smoothedData[i];
                avg /= len;
            }
            bass = Math.Min(bass * _sensitivity * 3, 2.0);
            mid = Math.Min(mid * _sensitivity * 3, 2.0);
            treb = Math.Min(treb * _sensitivity * 3, 2.0);
            avg = Math.Min(avg * _sensitivity * 3, 1.5);
            
            _milkdropTime += 1.0 / Math.Max(_targetFps, 15);
            _milkdropGpuFrame++;
            
            // Cycle hue continuously
            _milkdropWaveHue += 0.3 + treb * 0.5;
            if (_milkdropWaveHue >= 360) _milkdropWaveHue -= 360;
            
            // Auto-change wave mode every ~20 seconds
            if (_milkdropGpuFrame % (20 * Math.Max(_targetFps, 15)) == 0)
                _milkdropWaveMode = (_milkdropWaveMode + 1) % 6;
            
            // === PERSISTENT FRAME BUFFER (feedback loop) ===
            // Double-buffered: draw into front reading from back, then swap.
            // Eliminates per-frame Copy() allocation that caused GC pressure / memory leaks.
            if (_milkdropGpuBuffer == null || _milkdropGpuW != bufW || _milkdropGpuH != bufH)
            {
                _milkdropGpuBuffer?.Dispose();
                _milkdropGpuBufferBack?.Dispose();
                _milkdropGpuBuffer = new SKBitmap(bufW, bufH, SKColorType.Rgba8888, SKAlphaType.Premul);
                _milkdropGpuBufferBack = new SKBitmap(bufW, bufH, SKColorType.Rgba8888, SKAlphaType.Premul);
                using var initCanvas1 = new SKCanvas(_milkdropGpuBuffer);
                initCanvas1.Clear(SKColors.Black);
                using var initCanvas2 = new SKCanvas(_milkdropGpuBufferBack);
                initCanvas2.Clear(SKColors.Black);
                _milkdropGpuW = bufW;
                _milkdropGpuH = bufH;
            }
            
            // Double-buffer swap: read from back, draw into front
            var currentBuffer = _milkdropGpuBuffer;
            var workBitmap = _milkdropGpuBufferBack;
            if (currentBuffer == null || workBitmap == null) return;
            using var workCanvas = new SKCanvas(currentBuffer);
            workCanvas.Clear(SKColors.Black);
            
            // --- Step 1: Draw previous frame with zoom/rotation (motion warp) ---
            float zoom = 1.015f + (float)(bass * 0.04 + Math.Sin(_milkdropTime * 0.3) * 0.01);
            float rotation = (float)(0.5 + treb * 1.5 + Math.Sin(_milkdropTime * 0.2) * 0.3);
            float warpX = (float)(Math.Sin(_milkdropTime * 0.4) * mid * 3);
            float warpY = (float)(Math.Cos(_milkdropTime * 0.3) * mid * 3);
            
            workCanvas.Save();
            workCanvas.Translate(cx + warpX, cy + warpY);
            workCanvas.RotateDegrees(rotation);
            workCanvas.Scale(zoom, zoom);
            workCanvas.Translate(-cx, -cy);
            
            using (var framePaint = new SKPaint { IsAntialias = true })
            {
                workCanvas.DrawBitmap(workBitmap, 0, 0, framePaint);
            }
            workCanvas.Restore();
            
            // --- Step 2: Decay (semi-transparent dark overlay) ---
            float decayAlpha = 12 + (float)(avg * 8); // More active = slower decay
            using (var decayPaint = new SKPaint())
            {
                decayPaint.Color = new SKColor(0, 0, 0, (byte)Math.Clamp(decayAlpha, 5, 40));
                decayPaint.Style = SKPaintStyle.Fill;
                workCanvas.DrawRect(0, 0, bufW, bufH, decayPaint);
            }
            
            // --- Step 3: Color tint wash based on current hue ---
            float hueRad = (float)(_milkdropWaveHue * Math.PI / 180);
            byte tintR = (byte)Math.Clamp((Math.Sin(hueRad) * 0.5 + 0.5) * avg * 25, 0, 20);
            byte tintG = (byte)Math.Clamp((Math.Sin(hueRad + 2.094) * 0.5 + 0.5) * avg * 25, 0, 20);
            byte tintB = (byte)Math.Clamp((Math.Sin(hueRad + 4.189) * 0.5 + 0.5) * avg * 25, 0, 20);
            if (tintR > 0 || tintG > 0 || tintB > 0)
            {
                using var tintPaint = new SKPaint();
                tintPaint.Color = new SKColor(tintR, tintG, tintB, 60);
                tintPaint.BlendMode = SKBlendMode.Plus;
                workCanvas.DrawRect(0, 0, bufW, bufH, tintPaint);
            }
            
            // --- Step 4: Draw waveform with GPU-accelerated paths ---
            using var wavePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
            
            // Primary wave color from cycling hue (vivid, fully saturated)
            SKColor waveColor = SKColor.FromHsl((float)_milkdropWaveHue, 100, 60);
            // Secondary complementary color
            SKColor waveColor2 = SKColor.FromHsl((float)((_milkdropWaveHue + 120) % 360), 100, 55);
            // Tertiary triadic color
            SKColor waveColor3 = SKColor.FromHsl((float)((_milkdropWaveHue + 240) % 360), 90, 50);
            
            int samples = Math.Min(256, len > 0 ? len : 256);
            
            switch (_milkdropWaveMode)
            {
                case 0: // Circular waveform
                    DrawMilkdropCircularWave(workCanvas, wavePaint, waveColor, waveColor2, cx, cy, bufW, bufH, samples, avg, bass);
                    break;
                case 1: // Horizontal wave
                    DrawMilkdropHorizontalWave(workCanvas, wavePaint, waveColor, waveColor2, bufW, bufH, samples);
                    break;
                case 2: // Center spike (radial burst)
                    DrawMilkdropRadialBurst(workCanvas, wavePaint, waveColor, waveColor2, waveColor3, cx, cy, bufW, bufH, samples, bass);
                    break;
                case 3: // Dual channel (L/R)
                    DrawMilkdropDualWave(workCanvas, wavePaint, waveColor, waveColor2, bufW, bufH, samples);
                    break;
                case 4: // Spectrum bars
                    DrawMilkdropSpectrumBars(workCanvas, waveColor, waveColor2, bufW, bufH, avg);
                    break;
                case 5: // Dot scatter
                    DrawMilkdropDotScatter(workCanvas, waveColor, waveColor2, waveColor3, cx, cy, bufW, bufH, samples, bass);
                    break;
            }
            
            // --- Step 5: Beat flash ---
            if (bass > 0.6)
            {
                using var flashPaint = new SKPaint();
                byte flashA = (byte)Math.Clamp(bass * 40, 0, 80);
                flashPaint.Color = waveColor.WithAlpha(flashA);
                flashPaint.BlendMode = SKBlendMode.Plus;
                workCanvas.DrawRect(0, 0, bufW, bufH, flashPaint);
            }
            
            // --- Step 6: Darken center (like original Milkdrop) ---
            using (var centerPaint = new SKPaint())
            {
                using var centerShader = SKShader.CreateRadialGradient(
                    new SKPoint(cx, cy), Math.Min(bufW, bufH) * 0.15f,
                    new SKColor[] { new SKColor(0, 0, 0, 30), SKColors.Transparent },
                    null, SKShaderTileMode.Clamp);
                centerPaint.Shader = centerShader;
                workCanvas.DrawRect(0, 0, bufW, bufH, centerPaint);
            }
            
            // --- Step 7: Blit persistent buffer to the main canvas (upscale if needed) ---
            if (currentBuffer == null) return;
            if (bufW == w && bufH == h)
            {
                canvas.DrawBitmap(currentBuffer, 0, 0);
            }
            else
            {
                var dest = new SKRect(0, 0, w, h);
                using var blitPaint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Medium };
                canvas.DrawBitmap(currentBuffer, dest, blitPaint);
            }
            
            // Swap buffers: current front becomes next frame's back (source)
            _milkdropGpuBuffer = workBitmap;
            _milkdropGpuBufferBack = currentBuffer;
        }
        
        /// <summary>Circular waveform with glow — Milkdrop-style ring.</summary>
        private void DrawMilkdropCircularWave(SKCanvas c, SKPaint paint, SKColor color, SKColor color2,
            float cx, float cy, float w, float h, int samples, double avg, double bass)
        {
            float baseR = Math.Min(w, h) * (0.18f + (float)avg * 0.06f);
            
            using var path = new SKPath();
            using var path2 = new SKPath();
            for (int i = 0; i <= samples; i++)
            {
                int idx = i % samples;
                double angle = (double)i / samples * Math.PI * 2;
                double val = idx < _smoothedData.Length ? _smoothedData[idx] * _sensitivity : 0;
                float r1 = baseR + (float)(val * baseR * 1.5);
                float r2 = baseR * 0.7f + (float)(val * baseR * 0.8);
                float px1 = cx + (float)(Math.Cos(angle) * r1);
                float py1 = cy + (float)(Math.Sin(angle) * r1);
                float px2 = cx + (float)(Math.Cos(angle + 0.5) * r2);
                float py2 = cy + (float)(Math.Sin(angle + 0.5) * r2);
                
                if (i == 0) { path.MoveTo(px1, py1); path2.MoveTo(px2, py2); }
                else { path.LineTo(px1, py1); path2.LineTo(px2, py2); }
            }
            path.Close();
            path2.Close();
            
            // Outer glow
            paint.StrokeWidth = 6;
            paint.Color = color.WithAlpha(40);
            using var ringOuterGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8);
            paint.MaskFilter = ringOuterGlow;
            c.DrawPath(path, paint);
            paint.MaskFilter = null;
            
            // Primary ring
            paint.StrokeWidth = 3;
            paint.Color = color;
            c.DrawPath(path, paint);
            
            // Inner ring (complementary color)
            paint.StrokeWidth = 2;
            paint.Color = color2.WithAlpha(180);
            c.DrawPath(path2, paint);
        }
        
        /// <summary>Horizontal waveform with gradient stroke.</summary>
        private void DrawMilkdropHorizontalWave(SKCanvas c, SKPaint paint, SKColor color, SKColor color2,
            float w, float h, int samples)
        {
            using var path = new SKPath();
            using var path2 = new SKPath();
            for (int i = 0; i < samples; i++)
            {
                float x = (float)i / samples * w;
                double val = i < _smoothedData.Length ? _smoothedData[i] * _sensitivity : 0;
                float y1 = h * 0.5f + (float)(val * h * 0.35);
                float y2 = h * 0.5f - (float)(val * h * 0.25);
                if (i == 0) { path.MoveTo(x, y1); path2.MoveTo(x, y2); }
                else { path.LineTo(x, y1); path2.LineTo(x, y2); }
            }
            
            // Glow
            paint.StrokeWidth = 5;
            paint.Color = color.WithAlpha(50);
            using var hWaveGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6);
            paint.MaskFilter = hWaveGlow;
            c.DrawPath(path, paint);
            paint.MaskFilter = null;
            
            // Main wave
            paint.StrokeWidth = 2.5f;
            using var waveShader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint(w, 0),
                new SKColor[] { color, color2, color },
                null, SKShaderTileMode.Clamp);
            paint.Shader = waveShader;
            c.DrawPath(path, paint);
            paint.Shader = null;
            
            // Mirror wave
            paint.StrokeWidth = 1.5f;
            paint.Color = color2.WithAlpha(150);
            c.DrawPath(path2, paint);
        }
        
        /// <summary>Radial burst — spikes from center.</summary>
        private void DrawMilkdropRadialBurst(SKCanvas c, SKPaint paint, SKColor color, SKColor color2, SKColor color3,
            float cx, float cy, float w, float h, int samples, double bass)
        {
            float maxLen = Math.Min(w, h) * 0.4f;
            paint.StrokeWidth = 2;
            
            for (int i = 0; i < samples; i++)
            {
                double angle = (double)i / samples * Math.PI * 2;
                double val = i < _smoothedData.Length ? Math.Abs(_smoothedData[i] * _sensitivity) : 0;
                float length = (float)(val * maxLen);
                if (length < 2) continue;
                
                float ex = cx + (float)(Math.Cos(angle) * length);
                float ey = cy + (float)(Math.Sin(angle) * length);
                
                // Color based on position in spectrum
                float t = (float)i / samples;
                SKColor lineColor;
                if (t < 0.33f) lineColor = color;
                else if (t < 0.67f) lineColor = color2;
                else lineColor = color3;
                
                paint.Color = lineColor.WithAlpha((byte)(180 + val * 75));
                c.DrawLine(cx, cy, ex, ey, paint);
            }
            
            // Center dot glow
            paint.Style = SKPaintStyle.Fill;
            paint.Color = color.WithAlpha(80);
            using var burstGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 10);
            paint.MaskFilter = burstGlow;
            c.DrawCircle(cx, cy, 8 + (float)(bass * 15), paint);
            paint.MaskFilter = null;
            paint.Style = SKPaintStyle.Stroke;
        }
        
        /// <summary>Dual channel L/R waves.</summary>
        private void DrawMilkdropDualWave(SKCanvas c, SKPaint paint, SKColor color, SKColor color2,
            float w, float h, int samples)
        {
            using var pathL = new SKPath();
            using var pathR = new SKPath();
            
            float topY = h * 0.3f;
            float botY = h * 0.7f;
            
            for (int i = 0; i < samples; i++)
            {
                float x = (float)i / samples * w;
                // Use left/right split from spectrum data
                int leftIdx = Math.Min(i, _smoothedData.Length - 1);
                int rightIdx = Math.Min(samples - 1 - i, _smoothedData.Length - 1);
                if (leftIdx < 0) leftIdx = 0;
                if (rightIdx < 0) rightIdx = 0;
                double valL = leftIdx < _smoothedData.Length ? _smoothedData[leftIdx] * _sensitivity : 0;
                double valR = rightIdx < _smoothedData.Length ? _smoothedData[rightIdx] * _sensitivity : 0;
                
                float yL = topY + (float)(valL * h * 0.2);
                float yR = botY + (float)(valR * h * 0.2);
                
                if (i == 0) { pathL.MoveTo(x, yL); pathR.MoveTo(x, yR); }
                else { pathL.LineTo(x, yL); pathR.LineTo(x, yR); }
            }
            
            paint.StrokeWidth = 3;
            paint.Color = color;
            using var dualGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 3);
            paint.MaskFilter = dualGlow;
            c.DrawPath(pathL, paint);
            paint.Color = color2;
            c.DrawPath(pathR, paint);
            paint.MaskFilter = null;
        }
        
        /// <summary>Spectrum bars — colorful vertical bars.</summary>
        private void DrawMilkdropSpectrumBars(SKCanvas c, SKColor color, SKColor color2,
            float w, float h, double avg)
        {
            int barCount = Math.Min(64, _smoothedData.Length);
            if (barCount <= 0) return;
            float barW = w / barCount;
            
            using var barPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            
            for (int i = 0; i < barCount; i++)
            {
                int idx = (int)((double)i / barCount * _smoothedData.Length);
                idx = Math.Clamp(idx, 0, _smoothedData.Length - 1);
                double val = _smoothedData[idx] * _sensitivity;
                float barH = (float)(val * h * 0.7);
                if (barH < 1) continue;
                
                float x = i * barW;
                float y = h - barH;
                
                // Color from hue cycling based on bar index
                float barHue = (float)((_milkdropWaveHue + i * 360.0 / barCount) % 360);
                SKColor barColor = SKColor.FromHsl(barHue, 90, 50 + (float)(val * 20));
                
                using var barShader = SKShader.CreateLinearGradient(
                    new SKPoint(x, y), new SKPoint(x, h),
                    new SKColor[] { barColor, barColor.WithAlpha(80) },
                    null, SKShaderTileMode.Clamp);
                barPaint.Shader = barShader;
                c.DrawRect(x, y, barW - 1, barH, barPaint);
                barPaint.Shader = null;
                
                // Top glow (skip on large surfaces)
                if (!_isLargeSurface)
                {
                    barPaint.Color = barColor.WithAlpha(60);
                    using var barTopGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 4);
                    barPaint.MaskFilter = barTopGlow;
                    c.DrawRect(x - 1, y - 3, barW + 2, 6, barPaint);
                    barPaint.MaskFilter = null;
                }
            }
        }
        
        /// <summary>Dot scatter — particles following waveform.</summary>
        private void DrawMilkdropDotScatter(SKCanvas c, SKColor color, SKColor color2, SKColor color3,
            float cx, float cy, float w, float h, int samples, double bass)
        {
            using var dotPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            
            for (int i = 0; i < samples; i++)
            {
                double val = i < _smoothedData.Length ? _smoothedData[i] * _sensitivity : 0;
                if (val < 0.02) continue;
                
                // XY plot using neighboring indices
                int idx2 = (i + samples / 4) % _smoothedData.Length;
                double val2 = _smoothedData[idx2] * _sensitivity;
                
                float px = cx + (float)((val - 0.5) * w * 0.6);
                float py = cy + (float)((val2 - 0.5) * h * 0.6);
                
                float dotSize = 2 + (float)(val * 4);
                float t = (float)i / samples;
                SKColor dotColor;
                if (t < 0.33f) dotColor = color;
                else if (t < 0.67f) dotColor = color2;
                else dotColor = color3;
                
                dotPaint.Color = dotColor.WithAlpha((byte)(120 + val * 100));
                c.DrawCircle(px, py, dotSize, dotPaint);
            }
        }
        
        #endregion
        
        #region HD Bars Fallback
        
        /// <summary>
        /// HD fallback renderer for standard bars mode using SkiaSharp.
        /// Smooth gradients, anti-aliased edges, ambient glow.
        /// </summary>
        private void RenderBarsHD(SKCanvas canvas, SKImageInfo info)
        {
            float w = info.Width;
            float h = info.Height;
            if (w <= 0 || h <= 0) return;
            
            int barCount = Math.Min(_density, _smoothedData.Length);
            if (barCount <= 0) return;
            
            float barWidth = w / barCount * 0.75f;
            float gap = w / barCount * 0.25f;
            
            using var paint = new SKPaint { IsAntialias = true };
            
            for (int i = 0; i < barCount; i++)
            {
                int dataIdx = (int)((double)i / barCount * _smoothedData.Length);
                dataIdx = Math.Clamp(dataIdx, 0, _smoothedData.Length - 1);
                double value = _smoothedData[dataIdx] * _sensitivity;
                value = Math.Clamp(value, 0, 1);
                
                float barHeight = (float)(value * h * 0.9);
                float x = i * (barWidth + gap) + gap / 2;
                float y = h - barHeight;
                
                var topColor = GetHDColor(value);
                var bottomColor = GetHDColor(value * 0.3);
                
                using var shader = SKShader.CreateLinearGradient(
                    new SKPoint(x, y), new SKPoint(x, h),
                    new SKColor[] { topColor, bottomColor },
                    null, SKShaderTileMode.Clamp);
                paint.Shader = shader;
                canvas.DrawRect(x, y, barWidth, barHeight, paint);
                paint.Shader = null;
            }
        }
        
        #endregion
        
        #endregion // GPU-Accelerated SkiaSharp Rendering
        
        #region 3D Bars Visualizer (Legacy WPF Canvas)
        
        /// <summary>
        /// Renders a 3D perspective spectrum bar visualization with depth shading.
        /// </summary>
        private void Render3DBars(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            int barCount = Math.Min(_density, _smoothedData.Length);
            if (barCount <= 0) return;
            
            double barWidth = width / barCount * 0.7;
            double gap = width / barCount * 0.3;
            double vanishingY = height * 0.15;
            double baseY = height * 0.95;
            double depthOffset = 8;
            
            for (int i = 0; i < barCount; i++)
            {
                int dataIdx = (int)((double)i / barCount * _smoothedData.Length);
                dataIdx = Math.Clamp(dataIdx, 0, _smoothedData.Length - 1);
                double value = _smoothedData[dataIdx] * _sensitivity;
                
                double barHeight = Math.Max(2, value * (baseY - vanishingY) * 0.85);
                double x = i * (barWidth + gap) + gap / 2;
                
                double centerDist = Math.Abs(i - barCount / 2.0) / (barCount / 2.0);
                double perspScale = 1.0 - centerDist * 0.15;
                double scaledWidth = barWidth * perspScale;
                double scaledHeight = barHeight * perspScale;
                double scaledY = baseY - scaledHeight;
                
                // 3D top face (lighter)
                var topColor = GetColorFromScheme(value * 1.3);
                var topFace = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(x, scaledY),
                        new Point(x + scaledWidth, scaledY),
                        new Point(x + scaledWidth + depthOffset, scaledY - depthOffset),
                        new Point(x + depthOffset, scaledY - depthOffset)
                    },
                    Fill = new SolidColorBrush(Color.FromArgb(200, 
                        (byte)Math.Min(255, topColor.R + 40), 
                        (byte)Math.Min(255, topColor.G + 40), 
                        (byte)Math.Min(255, topColor.B + 40)))
                };
                canvas.Children.Add(topFace);
                
                // 3D right side face (darker)
                var sideColor = GetColorFromScheme(value * 0.7);
                var sideFace = new System.Windows.Shapes.Polygon
                {
                    Points = new PointCollection
                    {
                        new Point(x + scaledWidth, scaledY),
                        new Point(x + scaledWidth, baseY),
                        new Point(x + scaledWidth + depthOffset, baseY - depthOffset),
                        new Point(x + scaledWidth + depthOffset, scaledY - depthOffset)
                    },
                    Fill = new SolidColorBrush(Color.FromArgb(180,
                        (byte)(sideColor.R * 0.6),
                        (byte)(sideColor.G * 0.6),
                        (byte)(sideColor.B * 0.6)))
                };
                canvas.Children.Add(sideFace);
                
                // Front face (main bar)
                var frontColor = GetColorFromScheme(value);
                var frontRect = new Rectangle
                {
                    Width = scaledWidth,
                    Height = scaledHeight,
                    Fill = new LinearGradientBrush(
                        Color.FromArgb(230, frontColor.R, frontColor.G, frontColor.B),
                        Color.FromArgb(180, (byte)(frontColor.R * 0.5), (byte)(frontColor.G * 0.5), (byte)(frontColor.B * 0.5)),
                        new Point(0.5, 0),
                        new Point(0.5, 1))
                };
                Canvas.SetLeft(frontRect, x);
                Canvas.SetTop(frontRect, scaledY);
                canvas.Children.Add(frontRect);
                
                // Reflection
                if (scaledHeight > 10)
                {
                    var reflColor = GetColorFromScheme(value * 0.3);
                    var reflection = new Rectangle
                    {
                        Width = scaledWidth,
                        Height = Math.Min(scaledHeight * 0.3, 20),
                        Fill = new LinearGradientBrush(
                            Color.FromArgb(60, reflColor.R, reflColor.G, reflColor.B),
                            Colors.Transparent,
                            new Point(0.5, 0),
                            new Point(0.5, 1)),
                        Opacity = 0.5
                    };
                    Canvas.SetLeft(reflection, x);
                    Canvas.SetTop(reflection, baseY);
                    canvas.Children.Add(reflection);
                }
            }
        }
        
        #endregion
        
        #region Waterfall/Spectrogram Visualizer
        
        /// <summary>
        /// Renders a high-definition scrolling spectrogram (time vs frequency heatmap).
        /// Uses the HD color engine with smooth Hermite interpolation for hundreds of thousands
        /// of unique colors. Logarithmic frequency mapping gives bass more screen space.
        /// Gamma correction provides better dynamic range in quiet passages.
        /// </summary>
        private void RenderWaterfall(Canvas canvas)
        {
            double width = canvas.ActualWidth;
            double height = canvas.ActualHeight;
            if (width <= 0 || height <= 0) return;
            
            int pixelWidth = (int)width;
            int pixelHeight = (int)height;
            
            var currentRow = new double[_smoothedData.Length];
            Array.Copy(_smoothedData, currentRow, _smoothedData.Length);
            _waterfallHistory.Add(currentRow);
            
            while (_waterfallHistory.Count > WATERFALL_MAX_ROWS)
                _waterfallHistory.RemoveAt(0);
            
            if (_waterfallBitmap == null || _waterfallBitmap.PixelWidth != pixelWidth || _waterfallBitmap.PixelHeight != pixelHeight)
            {
                _waterfallBitmap = new WriteableBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Bgra32, null);
                _waterfallImage = new Image
                {
                    Source = _waterfallBitmap,
                    Width = width,
                    Height = height,
                    Stretch = Stretch.Fill
                };
            }
            
            _waterfallBitmap.Lock();
            try
            {
                int stride = _waterfallBitmap.BackBufferStride;
                int historyCount = _waterfallHistory.Count;
                byte[] pixels = new byte[pixelHeight * stride];
                
                for (int y = 0; y < pixelHeight; y++)
                {
                    // Map screen row to history (newest at top)
                    int histIdx = historyCount - 1 - (int)((double)y / pixelHeight * historyCount);
                    histIdx = Math.Clamp(histIdx, 0, historyCount - 1);
                    
                    double[] rowData = _waterfallHistory[histIdx];
                    int rowLen = rowData.Length;
                    int rowOffset = y * stride;
                    
                    for (int x = 0; x < pixelWidth; x++)
                    {
                        // Logarithmic frequency mapping: bass gets more screen space
                        double normalizedX = (double)x / pixelWidth;
                        double logFreq = Math.Pow(normalizedX, 0.7);
                        
                        // Sub-pixel interpolation between frequency bins
                        double exactIdx = logFreq * rowLen;
                        int idx0 = Math.Clamp((int)exactIdx, 0, rowLen - 1);
                        int idx1 = Math.Clamp(idx0 + 1, 0, rowLen - 1);
                        double frac = exactIdx - idx0;
                        double intensity = rowData[idx0] * (1.0 - frac) + rowData[idx1] * frac;
                        intensity *= _sensitivity;
                        intensity = Math.Clamp(intensity, 0, 1);
                        
                        // Gamma correction for better dynamic range
                        intensity = Math.Pow(intensity, 0.75);
                        
                        // Use the HD color engine for smooth continuous gradients
                        var color = GetColorFromScheme(intensity);
                        
                        int offset = rowOffset + x * 4;
                        pixels[offset] = color.B;
                        pixels[offset + 1] = color.G;
                        pixels[offset + 2] = color.R;
                        pixels[offset + 3] = 255;
                    }
                }
                
                System.Runtime.InteropServices.Marshal.Copy(pixels, 0, _waterfallBitmap.BackBuffer, pixels.Length);
                _waterfallBitmap.AddDirtyRect(new Int32Rect(0, 0, pixelWidth, pixelHeight));
            }
            finally
            {
                _waterfallBitmap.Unlock();
            }
            
            _waterfallImage!.Width = width;
            _waterfallImage.Height = height;
            Canvas.SetLeft(_waterfallImage, 0);
            Canvas.SetTop(_waterfallImage, 0);
            canvas.Children.Add(_waterfallImage);
            
            // Frequency labels with logarithmic positions
            var labelBrush = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
            string[] freqLabels = { "20Hz", "100Hz", "500Hz", "1kHz", "5kHz", "10kHz", "20kHz" };
            double[] freqPositions = { 0.02, 0.08, 0.22, 0.38, 0.62, 0.78, 0.95 };
            for (int i = 0; i < freqLabels.Length; i++)
            {
                var label = new TextBlock
                {
                    Text = freqLabels[i],
                    FontSize = 10,
                    Foreground = labelBrush
                };
                Canvas.SetLeft(label, freqPositions[i] * width);
                Canvas.SetBottom(label, 2);
                canvas.Children.Add(label);
            }
            
            // Time axis labels
            var timeBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
            var nowLabel = new TextBlock { Text = "now", FontSize = 9, Foreground = timeBrush };
            Canvas.SetLeft(nowLabel, 4);
            Canvas.SetTop(nowLabel, 4);
            canvas.Children.Add(nowLabel);
            
            int histSec = _waterfallHistory.Count / Math.Max(_targetFps, 1);
            if (histSec > 0)
            {
                var agoLabel = new TextBlock { Text = $"~{histSec}s ago", FontSize = 9, Foreground = timeBrush };
                Canvas.SetLeft(agoLabel, 4);
                Canvas.SetBottom(agoLabel, 16);
                canvas.Children.Add(agoLabel);
            }
        }
        
        #endregion
    }
    
    /// <summary>
    /// Represents a particle for the Federation transporter effect.
    /// </summary>
    internal class TransporterParticle
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double VelocityY { get; set; }
        public double Size { get; set; }
        public double Phase { get; set; }
        public double Brightness { get; set; }
        public int ColorType { get; set; }  // 0=blue, 1=gold, 2=white
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
    /// Represents a lightsaber for the Jedi visualization.
    /// </summary>
    internal class Lightsaber
    {
        public double X { get; set; }
        public double BaseHeight { get; set; }
        public double CurrentHeight { get; set; }
        public int ColorType { get; set; } // 0=blue, 1=green, 2=purple, 3=red
        public double GlowPhase { get; set; }
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
        public int ColorType { get; set; }  // 0=white, 1=yellow, 2=red, 3=blue
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
        public char[] Characters { get; set; } = new char[20];  // Characters in this column (more for longer trails)
    }
    
    /// <summary>
    /// A FrameworkElement that hosts a DrawingVisual for efficient Canvas rendering.
    /// Used by Matrix visualizer to avoid creating many TextBlock elements.
    /// </summary>
    internal class DrawingVisualHost : FrameworkElement
    {
        private readonly DrawingVisual _visual;
        
        public DrawingVisualHost(DrawingVisual visual)
        {
            _visual = visual;
            AddVisualChild(visual);
        }
        
        protected override int VisualChildrenCount => 1;
        
        protected override Visual GetVisualChild(int index)
        {
            if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
            return _visual;
        }
    }
}
