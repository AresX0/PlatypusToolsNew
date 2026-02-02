using System;
using System.Windows;
using PlatypusTools.UI.Interop;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Manages application themes including Light, Dark, LCARS, and Glass effects.
    /// </summary>
    public class ThemeManager
    {
        private static ThemeManager? _instance;
        private bool _isDarkTheme;
        private bool _isLcarsTheme;
        private bool _isPipBoyTheme;
        private bool _isGlassEnabled;
        private GlassLevel _glassLevel = GlassLevel.Off;
        private Window? _mainWindow;
        private string _currentTheme = Light;

        public const string Light = "Light";
        public const string Dark = "Dark";
        public const string LCARS = "LCARS";
        public const string Glass = "Glass";
        public const string PipBoy = "PipBoy";
        public const string Klingon = "Klingon";

        /// <summary>Gets the singleton instance.</summary>
        public static ThemeManager Instance => _instance ??= new ThemeManager();

        /// <summary>Gets the current theme name.</summary>
        public string CurrentTheme => _currentTheme;

        /// <summary>Gets whether dark theme is active.</summary>
        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (_isDarkTheme != value)
                {
                    _isDarkTheme = value;
                    if (!_isLcarsTheme)
                    {
                        ApplyTheme(value ? Dark : Light);
                    }
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Gets or sets whether LCARS theme is active.</summary>
        public bool IsLcarsTheme
        {
            get => _isLcarsTheme;
            set
            {
                if (_isLcarsTheme != value)
                {
                    _isLcarsTheme = value;
                    _isPipBoyTheme = false;
                    if (value)
                    {
                        ApplyTheme(LCARS);
                    }
                    else
                    {
                        ApplyTheme(_isDarkTheme ? Dark : Light);
                    }
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Gets or sets whether Pip-Boy theme is active.</summary>
        public bool IsPipBoyTheme
        {
            get => _isPipBoyTheme;
            set
            {
                if (_isPipBoyTheme != value)
                {
                    _isPipBoyTheme = value;
                    _isLcarsTheme = false;
                    _isKlingonTheme = false;
                    if (value)
                    {
                        ApplyTheme(PipBoy);
                    }
                    else
                    {
                        ApplyTheme(_isDarkTheme ? Dark : Light);
                    }
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        
        private bool _isKlingonTheme;
        /// <summary>Gets or sets whether Klingon theme is active.</summary>
        public bool IsKlingonTheme
        {
            get => _isKlingonTheme;
            set
            {
                if (_isKlingonTheme != value)
                {
                    _isKlingonTheme = value;
                    _isLcarsTheme = false;
                    _isPipBoyTheme = false;
                    if (value)
                    {
                        ApplyTheme(Klingon);
                    }
                    else
                    {
                        ApplyTheme(_isDarkTheme ? Dark : Light);
                    }
                    ThemeChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Gets whether glass effects are enabled.</summary>
        public bool IsGlassEnabled
        {
            get => _isGlassEnabled;
            set
            {
                if (_isGlassEnabled != value)
                {
                    _isGlassEnabled = value;
                    if (value)
                    {
                        EnableGlass(_glassLevel);
                    }
                    else
                    {
                        DisableGlass();
                    }
                    GlassSettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Gets or sets the glass effect level.</summary>
        public GlassLevel GlassLevel
        {
            get => _glassLevel;
            set
            {
                if (_glassLevel != value)
                {
                    _glassLevel = value;
                    if (_isGlassEnabled)
                    {
                        EnableGlass(value);
                    }
                    GlassSettingsChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Gets whether glass effects are supported on this system.</summary>
        public bool IsGlassSupported => DwmGlassHelper.IsGlassSupported;

        /// <summary>Event raised when theme changes.</summary>
        public event EventHandler? ThemeChanged;

        /// <summary>Event raised when glass settings change.</summary>
        public event EventHandler? GlassSettingsChanged;

        /// <summary>
        /// Sets the main window for glass effects.
        /// </summary>
        public void SetMainWindow(Window window)
        {
            _mainWindow = window;
            if (_isGlassEnabled)
            {
                EnableGlass(_glassLevel);
            }
        }

        /// <summary>
        /// Checks if a resource dictionary is a theme file.
        /// </summary>
        private static bool IsThemeDictionary(ResourceDictionary md)
        {
            if (md.Source == null) return false;
            
            // Normalize path separators for comparison
            var path = md.Source.OriginalString.Replace('/', '\\').ToLowerInvariant();
            return path.Contains("themes\\light.xaml") || 
                   path.Contains("themes\\dark.xaml") ||
                   path.Contains("themes\\lcars.xaml") ||
                   path.Contains("themes\\glass.xaml") ||
                   path.Contains("themes\\pipboy.xaml") ||
                   path.Contains("themes\\klingon.xaml");
        }

        /// <summary>
        /// Applies a theme by name (Light, Dark, or LCARS).
        /// </summary>
        public static void ApplyTheme(string name)
        {
            try
            {
                var app = Application.Current;
                if (app == null) 
                {
                    SimpleLogger.Warn("ThemeManager: Application.Current is null");
                    return;
                }

                SimpleLogger.Debug($"ThemeManager: Applying theme '{name}'");

                // Update singleton state
                Instance._currentTheme = name;
                Instance._isDarkTheme = name == Dark || name == LCARS || name == PipBoy || name == Klingon; // LCARS, PipBoy, Klingon are dark-based
                Instance._isLcarsTheme = name == LCARS;
                Instance._isPipBoyTheme = name == PipBoy;
                Instance._isKlingonTheme = name == Klingon;

                // Remove existing theme dictionaries
                int removedCount = 0;
                for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var md = app.Resources.MergedDictionaries[i];
                    if (IsThemeDictionary(md))
                    {
                        SimpleLogger.Debug($"ThemeManager: Removing theme dictionary: {md.Source}");
                        app.Resources.MergedDictionaries.RemoveAt(i);
                        removedCount++;
                    }
                }
                SimpleLogger.Debug($"ThemeManager: Removed {removedCount} existing theme dictionaries, {app.Resources.MergedDictionaries.Count} remaining");

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string themePath;
                
                switch (name)
                {
                    case LCARS:
                        themePath = System.IO.Path.Combine(baseDir, "Themes", "LCARS.xaml");
                        break;
                    case PipBoy:
                        themePath = System.IO.Path.Combine(baseDir, "Themes", "PipBoy.xaml");
                        break;
                    case Klingon:
                        themePath = System.IO.Path.Combine(baseDir, "Themes", "Klingon.xaml");
                        break;
                    case Dark:
                        themePath = System.IO.Path.Combine(baseDir, "Themes", "Dark.xaml");
                        break;
                    default:
                        themePath = System.IO.Path.Combine(baseDir, "Themes", "Light.xaml");
                        break;
                }

                SimpleLogger.Debug($"ThemeManager: Looking for theme at: {themePath}");

                if (!System.IO.File.Exists(themePath))
                {
                    // try AppContext.BaseDirectory for single-file apps
                    var appContextDir = AppContext.BaseDirectory;
                    themePath = System.IO.Path.Combine(appContextDir, "Themes", name + ".xaml");
                    SimpleLogger.Debug($"ThemeManager: Fallback path (AppContext): {themePath}");
                }

                if (System.IO.File.Exists(themePath))
                {
                    var dict = new ResourceDictionary();
                    dict.Source = new Uri(themePath, UriKind.Absolute);
                    app.Resources.MergedDictionaries.Add(dict);
                    SimpleLogger.Info($"ThemeManager: Theme '{name}' loaded successfully from: {themePath}");
                    SimpleLogger.Debug($"ThemeManager: Now have {app.Resources.MergedDictionaries.Count} merged dictionaries");
                }
                else
                {
                    SimpleLogger.Error($"ThemeManager: Theme file not found: {themePath}");
                }

                // Apply Glass theme overlay if enabled (not with LCARS)
                if (Instance._isGlassEnabled && name != LCARS)
                {
                    Instance.ApplyGlassResources();
                }

                // Update title bar dark mode
                if (Instance._mainWindow != null)
                {
                    DwmGlassHelper.SetDarkMode(Instance._mainWindow, Instance._isDarkTheme);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"ThemeManager: Exception applying theme: {ex.Message}");
            }
        }

        /// <summary>
        /// Applies a custom theme from a XAML file.
        /// </summary>
        /// <param name="name">The name of the custom theme.</param>
        /// <param name="xamlPath">The absolute path to the XAML theme file.</param>
        public static void ApplyCustomTheme(string name, string xamlPath)
        {
            try
            {
                var app = Application.Current;
                if (app == null) 
                {
                    SimpleLogger.Warn("ThemeManager: Application.Current is null");
                    return;
                }

                SimpleLogger.Debug($"ThemeManager: Applying custom theme '{name}' from {xamlPath}");

                // Update singleton state
                Instance._currentTheme = name;
                Instance._isDarkTheme = IsCustomThemeDark(xamlPath);
                Instance._isLcarsTheme = false;
                Instance._isPipBoyTheme = false;

                // Remove existing theme dictionaries
                for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var md = app.Resources.MergedDictionaries[i];
                    if (IsThemeDictionary(md) || IsCustomThemeDictionary(md))
                    {
                        app.Resources.MergedDictionaries.RemoveAt(i);
                    }
                }

                if (System.IO.File.Exists(xamlPath))
                {
                    var dict = new ResourceDictionary();
                    dict.Source = new Uri(xamlPath, UriKind.Absolute);
                    app.Resources.MergedDictionaries.Add(dict);
                    SimpleLogger.Info($"ThemeManager: Custom theme '{name}' loaded successfully");
                }
                else
                {
                    SimpleLogger.Error($"ThemeManager: Custom theme file not found: {xamlPath}");
                }

                // Update title bar dark mode
                if (Instance._mainWindow != null)
                {
                    DwmGlassHelper.SetDarkMode(Instance._mainWindow, Instance._isDarkTheme);
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"ThemeManager: Exception applying custom theme: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if a custom theme is dark based on its background color.
        /// </summary>
        private static bool IsCustomThemeDark(string xamlPath)
        {
            try
            {
                var content = System.IO.File.ReadAllText(xamlPath);
                // Look for WindowBackgroundBrush color
                var match = System.Text.RegularExpressions.Regex.Match(content, 
                    @"WindowBackgroundBrush[^>]*Color=""([^""]+)""");
                if (match.Success)
                {
                    var colorStr = match.Groups[1].Value;
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorStr);
                    double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255;
                    return luminance < 0.5;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// Checks if a resource dictionary is a custom theme file.
        /// </summary>
        private static bool IsCustomThemeDictionary(ResourceDictionary md)
        {
            if (md.Source == null) return false;
            var path = md.Source.OriginalString.ToLowerInvariant();
            return path.Contains("customthemes");
        }

        /// <summary>
        /// Gets the list of available custom themes.
        /// </summary>
        public static string[] GetCustomThemeNames()
        {
            try
            {
                var themesDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PlatypusTools", "CustomThemes");
                    
                if (System.IO.Directory.Exists(themesDir))
                {
                    var files = System.IO.Directory.GetFiles(themesDir, "*.xaml");
                    var names = new string[files.Length];
                    for (int i = 0; i < files.Length; i++)
                    {
                        names[i] = System.IO.Path.GetFileNameWithoutExtension(files[i]);
                    }
                    return names;
                }
            }
            catch { }
            return Array.Empty<string>();
        }

        /// <summary>
        /// Gets the path to a custom theme file.
        /// </summary>
        public static string? GetCustomThemePath(string name)
        {
            var themesDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "CustomThemes");
            var safeName = string.Join("_", name.Split(System.IO.Path.GetInvalidFileNameChars()));
            var path = System.IO.Path.Combine(themesDir, $"{safeName}.xaml");
            return System.IO.File.Exists(path) ? path : null;
        }

        /// <summary>
        /// Enables glass effects on the main window.
        /// </summary>
        private void EnableGlass(GlassLevel level)
        {
            SimpleLogger.Debug($"ThemeManager.EnableGlass: level={level}, mainWindow={_mainWindow != null}, isSupported={IsGlassSupported}");
            
            if (_mainWindow == null)
            {
                SimpleLogger.Warn("ThemeManager.EnableGlass: MainWindow is null");
                return;
            }
            
            if (!IsGlassSupported)
            {
                SimpleLogger.Warn("ThemeManager.EnableGlass: Glass not supported on this system");
                return;
            }

            try
            {
                ApplyGlassResources();
                var success = GlassThemeService.Instance.Apply(_mainWindow, level);
                SimpleLogger.Debug($"ThemeManager.EnableGlass: Apply result = {success}");
            }
            catch (System.Exception ex)
            {
                SimpleLogger.Error($"ThemeManager.EnableGlass: Exception - {ex.Message}");
            }
        }

        /// <summary>
        /// Disables glass effects.
        /// </summary>
        private void DisableGlass()
        {
            if (_mainWindow == null)
                return;

            try
            {
                RemoveGlassResources();
                GlassThemeService.Instance.Remove(_mainWindow);
            }
            catch { }
        }

        /// <summary>
        /// Applies Glass theme resource dictionary as an overlay.
        /// </summary>
        private void ApplyGlassResources()
        {
            try
            {
                var app = Application.Current;
                if (app == null)
                {
                    SimpleLogger.Warn("ApplyGlassResources: Application.Current is null");
                    return;
                }

                // Check if Glass resources already loaded
                foreach (var md in app.Resources.MergedDictionaries)
                {
                    if (md.Source != null && md.Source.OriginalString.Contains("Themes/Glass.xaml"))
                    {
                        SimpleLogger.Debug("ApplyGlassResources: Glass.xaml already loaded");
                        return;  // Already loaded
                    }
                }

                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var glassPath = System.IO.Path.Combine(baseDir, "Themes", "Glass.xaml");

                SimpleLogger.Debug($"ApplyGlassResources: Looking for Glass.xaml at: {glassPath}");

                if (System.IO.File.Exists(glassPath))
                {
                    var glassDict = new ResourceDictionary
                    {
                        Source = new Uri(glassPath, UriKind.Absolute)
                    };
                    app.Resources.MergedDictionaries.Add(glassDict);
                    SimpleLogger.Debug($"ApplyGlassResources: Glass.xaml loaded successfully, now have {app.Resources.MergedDictionaries.Count} merged dictionaries");
                }
                else
                {
                    SimpleLogger.Warn($"ApplyGlassResources: Glass.xaml not found at: {glassPath}");
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"ApplyGlassResources: Exception - {ex.Message}");
            }
        }

        /// <summary>
        /// Removes Glass theme resource dictionary.
        /// </summary>
        private void RemoveGlassResources()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return;

                for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                {
                    var md = app.Resources.MergedDictionaries[i];
                    if (md.Source != null && md.Source.OriginalString.Contains("Themes/Glass.xaml"))
                    {
                        app.Resources.MergedDictionaries.RemoveAt(i);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Gets a description of glass support on this system.
        /// </summary>
        public string GetGlassSupportInfo()
        {
            return GlassThemeService.Instance.GetSupportInfo();
        }
    }
}