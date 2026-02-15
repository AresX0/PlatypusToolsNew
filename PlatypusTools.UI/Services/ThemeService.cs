using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing application themes
    /// </summary>
    public sealed class ThemeService
    {
        private static readonly Lazy<ThemeService> _instance = new(() => new ThemeService());
        public static ThemeService Instance => _instance.Value;

        private readonly string _themesFolder;
        private readonly Dictionary<string, ThemeDefinition> _themes = new();
        private string _currentTheme = "Dark";

        public string CurrentTheme => _currentTheme;
        public bool IsDarkTheme => _currentTheme.Contains("Dark", StringComparison.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, ThemeDefinition> AvailableThemes => _themes;

        public event EventHandler<string>? ThemeChanged;

        private ThemeService()
        {
            _themesFolder = Path.Combine(
                SettingsManager.DataDirectory, "Themes");
            
            Directory.CreateDirectory(_themesFolder);
            LoadBuiltInThemes();
            LoadCustomThemes();
        }

        private void LoadBuiltInThemes()
        {
            _themes["Light"] = new ThemeDefinition
            {
                Name = "Light",
                IsBuiltIn = true,
                Colors = new Dictionary<string, string>
                {
                    ["BackgroundBrush"] = "#FFFFFF",
                    ["ForegroundBrush"] = "#1E1E1E",
                    ["ForegroundDimBrush"] = "#6E6E6E",
                    ["AccentBrush"] = "#0078D4",
                    ["AccentHoverBrush"] = "#106EBE",
                    ["BorderBrush"] = "#E0E0E0",
                    ["HeaderBackgroundBrush"] = "#F5F5F5",
                    ["SelectionBrush"] = "#CCE8FF",
                    ["ErrorBrush"] = "#E81123",
                    ["WarningBrush"] = "#FF8C00",
                    ["SuccessBrush"] = "#107C10"
                }
            };

            _themes["Dark"] = new ThemeDefinition
            {
                Name = "Dark",
                IsBuiltIn = true,
                Colors = new Dictionary<string, string>
                {
                    ["BackgroundBrush"] = "#1E1E1E",
                    ["ForegroundBrush"] = "#FFFFFF",
                    ["ForegroundDimBrush"] = "#9E9E9E",
                    ["AccentBrush"] = "#0078D4",
                    ["AccentHoverBrush"] = "#1E90FF",
                    ["BorderBrush"] = "#3E3E3E",
                    ["HeaderBackgroundBrush"] = "#2D2D2D",
                    ["SelectionBrush"] = "#264F78",
                    ["ErrorBrush"] = "#F44747",
                    ["WarningBrush"] = "#CCA700",
                    ["SuccessBrush"] = "#4EC9B0"
                }
            };

            _themes["Blue"] = new ThemeDefinition
            {
                Name = "Blue",
                IsBuiltIn = true,
                Colors = new Dictionary<string, string>
                {
                    ["BackgroundBrush"] = "#0D1B2A",
                    ["ForegroundBrush"] = "#E0E1DD",
                    ["ForegroundDimBrush"] = "#778DA9",
                    ["AccentBrush"] = "#415A77",
                    ["AccentHoverBrush"] = "#1B263B",
                    ["BorderBrush"] = "#1B263B",
                    ["HeaderBackgroundBrush"] = "#1B263B",
                    ["SelectionBrush"] = "#415A77",
                    ["ErrorBrush"] = "#E63946",
                    ["WarningBrush"] = "#F4A261",
                    ["SuccessBrush"] = "#2A9D8F"
                }
            };
        }

        private void LoadCustomThemes()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_themesFolder, "*.json"))
                {
                    var json = File.ReadAllText(file);
                    var theme = JsonSerializer.Deserialize<ThemeDefinition>(json);
                    if (theme != null && !string.IsNullOrEmpty(theme.Name))
                    {
                        theme.IsBuiltIn = false;
                        _themes[theme.Name] = theme;
                    }
                }
            }
            catch { /* Ignore custom theme loading errors */ }
        }

        public void ApplyTheme(string themeName)
        {
            if (!_themes.TryGetValue(themeName, out var theme))
                theme = _themes["Dark"];

            var resources = Application.Current.Resources;
            
            foreach (var kvp in theme.Colors)
            {
                if (TryParseColor(kvp.Value, out var color))
                {
                    resources[kvp.Key] = new SolidColorBrush(color);
                }
            }

            _currentTheme = themeName;
            ThemeChanged?.Invoke(this, themeName);
        }

        public void ToggleTheme()
        {
            ApplyTheme(IsDarkTheme ? "Light" : "Dark");
        }

        public void SaveCustomTheme(ThemeDefinition theme)
        {
            if (string.IsNullOrEmpty(theme.Name))
                throw new ArgumentException("Theme name is required");

            theme.IsBuiltIn = false;
            var path = Path.Combine(_themesFolder, $"{theme.Name}.json");
            var json = JsonSerializer.Serialize(theme, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
            
            _themes[theme.Name] = theme;
        }

        public void DeleteCustomTheme(string themeName)
        {
            if (_themes.TryGetValue(themeName, out var theme) && !theme.IsBuiltIn)
            {
                var path = Path.Combine(_themesFolder, $"{themeName}.json");
                if (File.Exists(path))
                    File.Delete(path);
                
                _themes.Remove(themeName);
            }
        }

        public ThemeDefinition CreateThemeFromCurrent(string name)
        {
            var currentResources = Application.Current.Resources;
            var colors = new Dictionary<string, string>();

            string[] colorKeys = { "BackgroundBrush", "ForegroundBrush", "ForegroundDimBrush", 
                "AccentBrush", "AccentHoverBrush", "BorderBrush", "HeaderBackgroundBrush", 
                "SelectionBrush", "ErrorBrush", "WarningBrush", "SuccessBrush" };

            foreach (var key in colorKeys)
            {
                if (currentResources[key] is SolidColorBrush brush)
                {
                    colors[key] = brush.Color.ToString();
                }
            }

            return new ThemeDefinition { Name = name, Colors = colors };
        }

        private static bool TryParseColor(string value, out Color color)
        {
            try
            {
                color = (Color)ColorConverter.ConvertFromString(value);
                return true;
            }
            catch
            {
                color = Colors.Transparent;
                return false;
            }
        }
    }

    public class ThemeDefinition
    {
        public string Name { get; set; } = "";
        public bool IsBuiltIn { get; set; }
        public Dictionary<string, string> Colors { get; set; } = new();
    }
}
