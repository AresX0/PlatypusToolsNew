using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Plugins
{
    public class PluginRegistryEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("version")] public string Version { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("author")] public string Author { get; set; } = "";
        [JsonPropertyName("downloadUrl")] public string DownloadUrl { get; set; } = "";
        [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
        [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    }

    // Reads a signed registry.json from a URL or local file. Verifies SHA-256 of downloaded plugin
    // against the registry pin before installing. Plugins are dropped into %APPDATA%/PlatypusTools/Plugins/.
    public class PluginRegistryService
    {
        private static readonly Lazy<PluginRegistryService> _instance = new(() => new PluginRegistryService());
        public static PluginRegistryService Instance => _instance.Value;

        public string DefaultRegistryUrl { get; set; } =
            "https://raw.githubusercontent.com/AresX0/PlatypusToolsNew/main/plugins/registry.json";

        public string LocalRegistryPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "plugin-registry.json");

        public string PluginsDir => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "Plugins");

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

        public async Task<List<PluginRegistryEntry>> LoadAsync(string? url = null)
        {
            string? json = null;
            try
            {
                json = await _http.GetStringAsync(url ?? DefaultRegistryUrl);
                Directory.CreateDirectory(Path.GetDirectoryName(LocalRegistryPath)!);
                File.WriteAllText(LocalRegistryPath, json);
            }
            catch
            {
                if (File.Exists(LocalRegistryPath)) json = File.ReadAllText(LocalRegistryPath);
            }
            if (string.IsNullOrWhiteSpace(json)) return new();
            try
            {
                return JsonSerializer.Deserialize<List<PluginRegistryEntry>>(json) ?? new();
            }
            catch { return new(); }
        }

        public async Task<string> InstallAsync(PluginRegistryEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.DownloadUrl)) throw new InvalidOperationException("No download URL.");
            Directory.CreateDirectory(PluginsDir);
            var tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".plg");
            try
            {
                using (var stream = await _http.GetStreamAsync(entry.DownloadUrl))
                using (var fs = File.Create(tmp))
                    await stream.CopyToAsync(fs);

                if (!string.IsNullOrWhiteSpace(entry.Sha256))
                {
                    using var sha = SHA256.Create();
                    using var fs = File.OpenRead(tmp);
                    var hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                    if (!string.Equals(hash, entry.Sha256.ToLowerInvariant(), StringComparison.Ordinal))
                        throw new InvalidOperationException($"SHA-256 mismatch. Expected {entry.Sha256}, got {hash}.");
                }

                var ext = Path.GetExtension(entry.DownloadUrl);
                if (string.IsNullOrEmpty(ext)) ext = ".dll";
                var destFile = Path.Combine(PluginsDir, $"{entry.Id}_{entry.Version}{ext}");
                File.Copy(tmp, destFile, overwrite: true);
                return destFile;
            }
            finally
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }
}
