using System;
using System.IO;
using System.Text.Json;

namespace PlatypusTools.UI.Services
{
    public class AppSettings
    {
        public string Theme { get; set; } = ThemeManager.Light;
        public bool CheckForUpdatesOnStartup { get; set; } = true;
    }

    public static class SettingsManager
    {
        private static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools");
        private static string SettingsFile => Path.Combine(AppFolder, "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (!Directory.Exists(AppFolder)) Directory.CreateDirectory(AppFolder);
                if (!File.Exists(SettingsFile)) return new AppSettings();
                var txt = File.ReadAllText(SettingsFile);
                return JsonSerializer.Deserialize<AppSettings>(txt) ?? new AppSettings();
            }
            catch { return new AppSettings(); }
        }

        public static void Save(AppSettings s)
        {
            try
            {
                if (!Directory.Exists(AppFolder)) Directory.CreateDirectory(AppFolder);
                var txt = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, txt);
            }
            catch { }
        }
    }
}