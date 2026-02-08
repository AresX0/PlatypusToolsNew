using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Fullscreen screensaver window that displays audio visualizers.
    /// Can be launched as a Windows screensaver or standalone fullscreen mode.
    /// 
    /// Features:
    /// - Fullscreen, borderless, topmost window
    /// - Hides cursor
    /// - Exits on any key press, mouse click, or significant mouse movement
    /// - Right-click to access settings panel
    /// - Supports all visualizer modes
    /// 
    /// To install as Windows screensaver:
    /// 1. Copy PlatypusTools.UI.exe to C:\Windows\System32 and rename to PlatypusScreensaver.scr
    /// 2. Or use the "Install as Screensaver" feature in the app
    /// </summary>
    public partial class ScreensaverWindow : Window
    {
        private Point _lastMousePosition;
        private bool _mousePositionInitialized = false;
        private DispatcherTimer? _fadeTimer;
        private DispatcherTimer? _animationTimer;
        private string _currentMode = "Starfield";
        private int _currentColorScheme = 0;
        private readonly Random _animRandom = new();
        
        // Threshold for mouse movement to exit (prevents accidental exit from small movements)
        private const double MouseMoveThreshold = 50;
        
        public ScreensaverWindow()
        {
            InitializeComponent();
            
            Loaded += ScreensaverWindow_Loaded;
            Closing += ScreensaverWindow_Closing;
        }
        
        /// <summary>
        /// Creates a screensaver window with the specified mode and color scheme.
        /// </summary>
        public ScreensaverWindow(string mode, int colorScheme) : this()
        {
            _currentMode = mode;
            _currentColorScheme = colorScheme;
        }
        
        private void ScreensaverWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Apply settings
            ApplyVisualizerSettings();
            
            // Set combo boxes to current values
            SelectComboBoxItem(ModeComboBox, _currentMode);
            if (ColorSchemeComboBox.Items.Count > _currentColorScheme)
                ColorSchemeComboBox.SelectedIndex = _currentColorScheme;
            
            // Start continuous animation timer — pumps synthetic spectrum data every frame
            _animationTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(45) };
            _animationTimer.Tick += (s, a) => ApplyVisualizerSettings();
            _animationTimer.Start();
            
            // Start fade timer for instruction text
            StartInstructionFadeTimer();
            
            // Force focus
            Focus();
            Activate();
        }
        
        private void ScreensaverWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _fadeTimer?.Stop();
            _animationTimer?.Stop();
        }
        
        private void ApplyVisualizerSettings()
        {
            // Generate evolving idle animation data — uses time for continuous motion
            var idleData = new double[128];
            double phase = DateTime.Now.Ticks / 10000000.0;
            
            for (int i = 0; i < 128; i++)
            {
                double freq = (double)i / 128;
                double wave1 = Math.Sin(phase * 1.2 + i * 0.15) * 0.25;
                double wave2 = Math.Sin(phase * 0.7 + i * 0.25) * 0.15;
                double wave3 = Math.Sin(phase * 2.0 + i * 0.1) * 0.1;
                double wave4 = Math.Sin(phase * 0.3 + i * 0.4) * 0.12;
                // Bass-heavy: more energy in lower frequencies
                double bassBoost = (1.0 - freq) * 0.15;
                idleData[i] = Math.Clamp(0.25 + wave1 + wave2 + wave3 + wave4 + bassBoost, 0.05, 0.95);
            }
            
            // Update visualizer with current settings
            VisualizerView.UpdateSpectrumData(idleData, _currentMode, 64, _currentColorScheme, 1.0, 22);
        }
        
        private void StartInstructionFadeTimer()
        {
            _fadeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _fadeTimer.Tick += (s, e) =>
            {
                _fadeTimer?.Stop();
                
                // Fade out instruction
                var fadeOut = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(1)
                };
                fadeOut.Completed += (s2, e2) => InstructionBorder.Visibility = Visibility.Collapsed;
                InstructionBorder.BeginAnimation(OpacityProperty, fadeOut);
            };
            _fadeTimer.Start();
        }
        
        private void SelectComboBoxItem(ComboBox comboBox, string value)
        {
            for (int i = 0; i < comboBox.Items.Count; i++)
            {
                if (comboBox.Items[i] is ComboBoxItem item && 
                    item.Content?.ToString()?.Equals(value, StringComparison.OrdinalIgnoreCase) == true)
                {
                    comboBox.SelectedIndex = i;
                    return;
                }
            }
        }
        
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            // Escape always closes
            if (e.Key == Key.Escape)
            {
                Close();
                return;
            }
            
            // Space toggles settings panel
            if (e.Key == Key.Space)
            {
                ToggleSettingsPanel();
                e.Handled = true;
                return;
            }
            
            // Any other key closes if settings panel is hidden
            if (SettingsPanel.Visibility != Visibility.Visible)
            {
                Close();
            }
        }
        
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Right-click toggles settings
            if (e.RightButton == MouseButtonState.Pressed)
            {
                ToggleSettingsPanel();
                e.Handled = true;
                return;
            }
            
            // Left-click closes if settings panel is hidden
            if (SettingsPanel.Visibility != Visibility.Visible && e.LeftButton == MouseButtonState.Pressed)
            {
                Close();
            }
        }
        
        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            Point currentPosition = e.GetPosition(this);
            
            if (!_mousePositionInitialized)
            {
                _lastMousePosition = currentPosition;
                _mousePositionInitialized = true;
                return;
            }
            
            // Calculate distance moved
            double distance = Math.Sqrt(
                Math.Pow(currentPosition.X - _lastMousePosition.X, 2) +
                Math.Pow(currentPosition.Y - _lastMousePosition.Y, 2));
            
            // Only exit if significant movement and settings panel is hidden
            if (distance > MouseMoveThreshold && SettingsPanel.Visibility != Visibility.Visible)
            {
                Close();
            }
        }
        
        private void ToggleSettingsPanel()
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
            {
                SettingsPanel.Visibility = Visibility.Collapsed;
                Cursor = Cursors.None;
            }
            else
            {
                SettingsPanel.Visibility = Visibility.Visible;
                Cursor = Cursors.Arrow;
            }
        }
        
        private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModeComboBox.SelectedItem is ComboBoxItem item && item.Content != null)
            {
                _currentMode = item.Content.ToString() ?? "Starfield";
                ApplyVisualizerSettings();
            }
        }
        
        private void ColorSchemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentColorScheme = ColorSchemeComboBox.SelectedIndex;
            ApplyVisualizerSettings();
        }
        
        private void CloseSettings_Click(object sender, RoutedEventArgs e)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            Cursor = Cursors.None;
        }
        
        /// <summary>
        /// Launches the screensaver in fullscreen mode.
        /// </summary>
        public static void Launch(string mode = "Starfield", int colorScheme = 0)
        {
            var window = new ScreensaverWindow(mode, colorScheme);
            window.Show();
        }
        
        /// <summary>
        /// Launches the screensaver in preview mode (for Windows screensaver preview).
        /// </summary>
        public static void LaunchPreview(IntPtr parentHandle)
        {
            // For Windows screensaver preview, we'd need to embed in the preview window
            // For now, just launch a small preview window
            var window = new ScreensaverWindow("Starfield", 0)
            {
                WindowState = WindowState.Normal,
                Width = 300,
                Height = 200,
                Topmost = false,
                ShowInTaskbar = true
            };
            window.Show();
        }
    }
}
