using System;
using System.Windows;
using System.Windows.Media;
using PlatypusTools.UI.Interop;
using PlatypusTools.Core.Services;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Interface for glass theme management.
    /// </summary>
    public interface IGlassThemeService
    {
        /// <summary>Gets or sets the current glass level.</summary>
        GlassLevel CurrentLevel { get; set; }

        /// <summary>Gets whether glass effects are currently active.</summary>
        bool IsGlassActive { get; }

        /// <summary>Gets whether glass effects are supported on this system.</summary>
        bool IsSupported { get; }

        /// <summary>Applies glass effect to a window.</summary>
        bool Apply(Window window, GlassLevel level);

        /// <summary>Removes glass effect from a window.</summary>
        void Remove(Window window);

        /// <summary>Refreshes glass effect after theme change.</summary>
        void RefreshForThemeChange();

        /// <summary>Event raised when glass settings change.</summary>
        event EventHandler? GlassSettingsChanged;
    }

    /// <summary>
    /// Service for managing Aero Glass theme effects.
    /// Provides glass/blur/acrylic effects using DWM APIs.
    /// </summary>
    public class GlassThemeService : IGlassThemeService
    {
        private static GlassThemeService? _instance;
        private GlassLevel _currentLevel = GlassLevel.Off;
        private Window? _currentWindow;
        private Color _lightTintColor = Color.FromArgb(0xA0, 0xC0, 0xD8, 0xF0);  // Blue-tinted glass
        private Color _darkTintColor = Color.FromArgb(0xA0, 0x10, 0x20, 0x40);   // Dark blue glass

        /// <summary>
        /// Gets the singleton instance.
        /// </summary>
        public static GlassThemeService Instance => _instance ??= new GlassThemeService();

        /// <summary>
        /// Private constructor for singleton.
        /// </summary>
        private GlassThemeService()
        {
            // Listen for system theme changes
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }

        /// <inheritdoc/>
        public GlassLevel CurrentLevel
        {
            get => _currentLevel;
            set
            {
                if (_currentLevel != value)
                {
                    _currentLevel = value;
                    RefreshForThemeChange();
                    GlassSettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <inheritdoc/>
        public bool IsGlassActive => _currentLevel != GlassLevel.Off && IsSupported;

        /// <inheritdoc/>
        public bool IsSupported => DwmGlassHelper.IsGlassSupported;

        /// <summary>Gets whether running on Windows 11.</summary>
        public bool IsWindows11 => DwmGlassHelper.IsWindows11;

        /// <summary>Gets whether running on Windows 10 with acrylic support.</summary>
        public bool IsWindows10WithAcrylic => DwmGlassHelper.IsWindows10WithAcrylic;

        /// <summary>Gets or sets the tint color for light theme.</summary>
        public Color LightTintColor
        {
            get => _lightTintColor;
            set
            {
                _lightTintColor = value;
                RefreshForThemeChange();
            }
        }

        /// <summary>Gets or sets the tint color for dark theme.</summary>
        public Color DarkTintColor
        {
            get => _darkTintColor;
            set
            {
                _darkTintColor = value;
                RefreshForThemeChange();
            }
        }

        /// <inheritdoc/>
        public event EventHandler? GlassSettingsChanged;

        /// <inheritdoc/>
        public bool Apply(Window window, GlassLevel level)
        {
            SimpleLogger.Debug($"GlassThemeService.Apply: level={level}, isSupported={IsSupported}");
            
            if (window == null)
            {
                SimpleLogger.Warn("GlassThemeService.Apply: Window is null");
                return false;
            }

            _currentWindow = window;
            _currentLevel = level;

            if (level == GlassLevel.Off || !IsSupported)
            {
                SimpleLogger.Debug($"GlassThemeService.Apply: Removing glass (level=Off or not supported)");
                Remove(window);
                return false;
            }

            var effectiveLevel = level == GlassLevel.Auto ? GetAutoLevel() : level;
            var isDark = ThemeManager.Instance.IsDarkTheme;
            var tintColor = isDark ? _darkTintColor : _lightTintColor;

            SimpleLogger.Debug($"GlassThemeService.Apply: effectiveLevel={effectiveLevel}, isDark={isDark}, tint={tintColor}");

            // Set dark mode for title bar to match theme
            DwmGlassHelper.SetDarkMode(window, isDark);

            // Make window background transparent for glass to show through
            window.Background = Brushes.Transparent;

            var result = DwmGlassHelper.EnableGlass(window, effectiveLevel, tintColor);
            SimpleLogger.Debug($"GlassThemeService.Apply: DwmGlassHelper.EnableGlass returned {result}");
            return result;
        }

        /// <inheritdoc/>
        public void Remove(Window window)
        {
            if (window == null)
                return;

            DwmGlassHelper.DisableGlass(window);

            // Restore appropriate background
            var isDark = ThemeManager.Instance.IsDarkTheme;
            window.Background = new SolidColorBrush(isDark 
                ? Color.FromRgb(0x1E, 0x1E, 0x1E)  // Dark theme background
                : Color.FromRgb(0xF5, 0xF5, 0xF5)  // Light theme background
            );
        }

        /// <inheritdoc/>
        public void RefreshForThemeChange()
        {
            if (_currentWindow != null && _currentLevel != GlassLevel.Off)
            {
                Apply(_currentWindow, _currentLevel);
            }
        }

        /// <summary>
        /// Gets the recommended glass level based on system capabilities.
        /// </summary>
        private GlassLevel GetAutoLevel()
        {
            if (!IsSupported)
                return GlassLevel.Off;

            if (DwmGlassHelper.IsTransparencyDisabled)
                return GlassLevel.Off;

            if (IsWindows11)
                return GlassLevel.High;  // Mica looks great on Windows 11

            if (IsWindows10WithAcrylic)
                return GlassLevel.Medium;

            return GlassLevel.Low;  // Basic blur for older systems
        }

        /// <summary>
        /// Handles system preference changes (theme, transparency settings).
        /// </summary>
        private void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
            {
                // System transparency or theme may have changed
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    RefreshForThemeChange();
                });
            }
        }

        /// <summary>
        /// Gets information about glass support on this system.
        /// </summary>
        public string GetSupportInfo()
        {
            var build = DwmGlassHelper.WindowsBuildNumber;
            if (IsWindows11)
                return $"Windows 11 (Build {build}) - Mica/Acrylic supported";
            if (IsWindows10WithAcrylic)
                return $"Windows 10 (Build {build}) - Acrylic supported";
            if (IsSupported)
                return $"Windows (Build {build}) - Basic blur supported";
            return "Glass effects not supported on this system";
        }
    }
}
