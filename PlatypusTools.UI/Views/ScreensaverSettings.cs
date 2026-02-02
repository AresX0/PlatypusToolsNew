using System;
using System.IO;
using System.Text.Json;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Settings for the Windows screensaver mode.
    /// Stored in the user's AppData folder.
    /// </summary>
    public class ScreensaverSettings
    {
        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "screensaver.json");
        
        /// <summary>
        /// The visualizer mode to use (e.g., "Starfield", "Matrix", "Klingon").
        /// </summary>
        public string VisualizerMode { get; set; } = "Starfield";
        
        /// <summary>
        /// The color scheme index (0-10).
        /// </summary>
        public int ColorSchemeIndex { get; set; } = 0;
        
        /// <summary>
        /// Loads the screensaver settings from disk.
        /// </summary>
        public static ScreensaverSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<ScreensaverSettings>(json) ?? new ScreensaverSettings();
                }
            }
            catch
            {
                // Ignore errors, return defaults
            }
            return new ScreensaverSettings();
        }
        
        /// <summary>
        /// Saves the screensaver settings to disk.
        /// </summary>
        public void Save()
        {
            try
            {
                string? directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Ignore errors
            }
        }
    }
}
