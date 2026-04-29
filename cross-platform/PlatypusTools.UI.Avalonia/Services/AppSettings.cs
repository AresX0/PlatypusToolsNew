using System;
using System.IO;
using System.Text.Json;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Tiny JSON-backed app settings. Persisted to <see cref="ShellHelper.AppDataDir"/>/settings.json.
/// </summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(ShellHelper.AppDataDir, "settings.json");

    public string LastSelectedPage { get; set; } = "";
    public bool DarkMode { get; set; } = true;

    // Screensaver
    public int ScreensaverIdleMin { get; set; } = 10;
    public bool ScreensaverLock { get; set; } = false;
    public string? ScreensaverFolder { get; set; }
    public int ScreensaverInterval { get; set; } = 5;
    public bool ScreensaverShuffle { get; set; } = true;

    // AI assistant
    public string? AiEndpoint { get; set; }
    public string? AiApiKey { get; set; }
    public string? AiModel { get; set; }

    // Update channel
    public string? UpdateRepo { get; set; } = "Codename-Reborn/PlatypusTools";

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
