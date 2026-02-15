using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Manages workspaces, workspace persistence, and workspace history.
    /// Static methods for backwards compatibility with original API.
    /// </summary>
    public static class WorkspaceManager
    {
        private static string AppFolder => SettingsManager.DataDirectory;
        private static string RecentFile => Path.Combine(AppFolder, "recent_workspaces.json");

        public static void EnsureAppFolder() => Directory.CreateDirectory(AppFolder);

        public static IReadOnlyList<string> LoadRecent()
        {
            try
            {
                if (!File.Exists(RecentFile)) return Array.Empty<string>();
                var txt = File.ReadAllText(RecentFile);
                var list = JsonSerializer.Deserialize<List<string>>(txt);
                return (IReadOnlyList<string>)(list ?? new List<string>());
            }
            catch { return Array.Empty<string>(); }
        }

        public static void SaveRecent(IEnumerable<string> items)
        {
            try
            {
                EnsureAppFolder();
                var txt = JsonSerializer.Serialize(items);
                File.WriteAllText(RecentFile, txt);
            }
            catch { }
        }

        public static bool SaveWorkspace(string path, object workspace)
        {
            try
            {
                var txt = JsonSerializer.Serialize(workspace, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, txt);
                AddToRecent(path);
                return true;
            }
            catch { return false; }
        }

        public static T? LoadWorkspace<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return default;
                var txt = File.ReadAllText(path);
                return JsonSerializer.Deserialize<T>(txt);
            }
            catch { return default; }
        }

        public static void AddToRecent(string path)
        {
            try
            {
                var recent = new List<string>(LoadRecent());
                recent.Remove(path);
                recent.Insert(0, path);
                while (recent.Count > 10) recent.RemoveAt(recent.Count - 1);
                SaveRecent(recent);
            }
            catch { }
        }

        public static void RemoveFromRecent(string path)
        {
            try
            {
                var recent = new List<string>(LoadRecent());
                recent.Remove(path);
                SaveRecent(recent);
            }
            catch { }
        }

        // New async methods for enhanced functionality
        public static async Task<bool> SaveWorkspaceAsync(string path, object workspace)
        {
            try
            {
                var txt = JsonSerializer.Serialize(workspace, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(path, txt);
                AddToRecent(path);
                return true;
            }
            catch { return false; }
        }

        public static async Task<T?> LoadWorkspaceAsync<T>(string path)
        {
            try
            {
                if (!File.Exists(path)) return default;
                var txt = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<T>(txt);
            }
            catch { return default; }
        }
    }

    public class WorkspaceState
    {
        public string Id { get; set; } = "";
        public string? Name { get; set; }
        public string? FilePath { get; set; }
        public string? RootFolder { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime ModifiedAt { get; set; }
        public string? SelectedFolder { get; set; }
        public string? FileCleanerTarget { get; set; }
        public string? RecentTargets { get; set; }
        public string? DuplicatesFolder { get; set; }
    }
}
