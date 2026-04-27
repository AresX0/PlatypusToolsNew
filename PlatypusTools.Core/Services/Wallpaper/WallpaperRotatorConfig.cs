using System;
using System.IO;
using System.Text.Json;

namespace PlatypusTools.Core.Services.Wallpaper
{
    /// <summary>
    /// Persisted configuration for the wallpaper rotator and slideshow screensaver.
    /// </summary>
    public class WallpaperRotatorConfig
    {
        public string ImagesDirectory { get; set; } = "";
        public int WallpaperIntervalSeconds { get; set; } = 300;     // 5 min
        public int SlideshowIntervalSeconds { get; set; } = 10;
        public bool Shuffle { get; set; } = true;
        public string FitMode { get; set; } = "fit";                  // fit | fill | stretch | center
        public string Transition { get; set; } = "fade";              // fade | cut
        public bool ShowOverlay { get; set; } = true;                 // screensaver overlay
        public bool BurnOverlayOnWallpaper { get; set; } = true;      // burn into desktop wallpaper
        public bool ApplyToLockScreen { get; set; } = true;
        public double OverlayOpacity { get; set; } = 0.85;
        public int OverlayScrollSpeedSeconds { get; set; } = 30;
        public bool RunAtLogin { get; set; } = false;
        public bool MinimizeOnStart { get; set; } = true;
        public string OverlaySource { get; set; } = "nasa";           // nasa | custom | none
        public string CustomOverlayText { get; set; } = "";
        public bool RotatorRunning { get; set; } = false;             // last desired state

        private static readonly string ConfigPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "wallpaper_rotator.json");

        public static WallpaperRotatorConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    return JsonSerializer.Deserialize<WallpaperRotatorConfig>(json) ?? new WallpaperRotatorConfig();
                }
            }
            catch { }
            return new WallpaperRotatorConfig();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
