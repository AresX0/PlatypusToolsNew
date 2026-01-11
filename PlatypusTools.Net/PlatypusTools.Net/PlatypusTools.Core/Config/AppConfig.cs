using System;
using System.IO;
using System.Text.Json;

namespace PlatypusTools.Core.Services
{
    public class AppConfig
    {
        public string? LogFile { get; set; }
        public string? BackupPath { get; set; }
        public string? LogLevel { get; set; }

        public static AppConfig Load(string? filePath = null)
        {
            try
            {
                if (string.IsNullOrEmpty(filePath))
                {
                    var root = AppDomain.CurrentDomain.BaseDirectory;
                    filePath = Path.Combine(root, "appsettings.json");
                }

                if (!File.Exists(filePath)) return new AppConfig();
                var txt = File.ReadAllText(filePath);
                var cfg = JsonSerializer.Deserialize<AppConfig>(txt, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return cfg ?? new AppConfig();
            }
            catch { return new AppConfig(); }
        }
    }
}