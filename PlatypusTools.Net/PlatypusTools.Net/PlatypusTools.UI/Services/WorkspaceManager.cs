using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace PlatypusTools.UI.Services
{
    public static class WorkspaceManager
    {
        private static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools");
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
                var list = new List<string>(LoadRecent());
                list.RemoveAll(x => string.Equals(x, path, StringComparison.OrdinalIgnoreCase));
                list.Insert(0, path);
                if (list.Count > 20) list.RemoveRange(20, list.Count - 20);
                SaveRecent(list);
            }
            catch { }
        }
    }
}