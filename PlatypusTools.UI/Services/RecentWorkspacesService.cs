using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing recent workspaces, projects, and file paths.
    /// Provides persistence and quick access to frequently used locations.
    /// </summary>
    public class RecentWorkspacesService
    {
        private static readonly Lazy<RecentWorkspacesService> _instance = new(() => new RecentWorkspacesService());
        public static RecentWorkspacesService Instance => _instance.Value;
        
        private const int MaxRecentItems = 20;
        private readonly string _dataFile;
        private readonly RecentWorkspacesData _data;
        
        public ObservableCollection<RecentWorkspace> RecentWorkspaces { get; } = new();
        public ObservableCollection<RecentFile> RecentFiles { get; } = new();
        public ObservableCollection<string> PinnedPaths { get; } = new();
        
        public event EventHandler? WorkspacesChanged;
        
        private RecentWorkspacesService()
        {
            var appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools");
            Directory.CreateDirectory(appFolder);
            _dataFile = Path.Combine(appFolder, "workspaces.json");
            _data = LoadData();
            RefreshCollections();
        }
        
        /// <summary>
        /// Adds or updates a workspace in the recent list.
        /// </summary>
        public void AddWorkspace(string path, string? name = null, string? module = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            
            // Remove existing entry
            var existing = _data.Workspaces.FirstOrDefault(w => 
                string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _data.Workspaces.Remove(existing);
            }
            
            // Add to front
            _data.Workspaces.Insert(0, new RecentWorkspace
            {
                Path = path,
                Name = name ?? Path.GetFileName(path),
                Module = module ?? "General",
                LastAccessed = DateTime.Now,
                AccessCount = (existing?.AccessCount ?? 0) + 1
            });
            
            // Trim to max size
            while (_data.Workspaces.Count > MaxRecentItems)
            {
                _data.Workspaces.RemoveAt(_data.Workspaces.Count - 1);
            }
            
            SaveData();
            RefreshCollections();
        }
        
        /// <summary>
        /// Adds a file to the recent files list.
        /// </summary>
        public void AddRecentFile(string filePath, string? associatedModule = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            
            var existing = _data.Files.FirstOrDefault(f => 
                string.Equals(f.Path, filePath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _data.Files.Remove(existing);
            }
            
            _data.Files.Insert(0, new RecentFile
            {
                Path = filePath,
                Name = Path.GetFileName(filePath),
                Module = associatedModule,
                LastAccessed = DateTime.Now,
                FileSize = new FileInfo(filePath).Length
            });
            
            while (_data.Files.Count > MaxRecentItems * 2)
            {
                _data.Files.RemoveAt(_data.Files.Count - 1);
            }
            
            SaveData();
            RefreshCollections();
        }
        
        /// <summary>
        /// Pins a path to keep it at the top of the list.
        /// </summary>
        public void PinPath(string path)
        {
            if (!_data.PinnedPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _data.PinnedPaths.Add(path);
                SaveData();
                RefreshCollections();
            }
        }
        
        /// <summary>
        /// Unpins a previously pinned path.
        /// </summary>
        public void UnpinPath(string path)
        {
            var existing = _data.PinnedPaths.FirstOrDefault(p => 
                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _data.PinnedPaths.Remove(existing);
                SaveData();
                RefreshCollections();
            }
        }
        
        /// <summary>
        /// Checks if a path is pinned.
        /// </summary>
        public bool IsPinned(string path)
        {
            return _data.PinnedPaths.Contains(path, StringComparer.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// Removes a workspace from the recent list.
        /// </summary>
        public void RemoveWorkspace(string path)
        {
            var existing = _data.Workspaces.FirstOrDefault(w => 
                string.Equals(w.Path, path, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _data.Workspaces.Remove(existing);
                SaveData();
                RefreshCollections();
            }
        }
        
        /// <summary>
        /// Clears all recent workspaces.
        /// </summary>
        public void ClearAll()
        {
            _data.Workspaces.Clear();
            _data.Files.Clear();
            _data.PinnedPaths.Clear();
            SaveData();
            RefreshCollections();
        }
        
        /// <summary>
        /// Clears all recent workspaces (alias for ClearAll).
        /// </summary>
        public void Clear() => ClearAll();
        
        /// <summary>
        /// Gets workspaces filtered by module.
        /// </summary>
        public IEnumerable<RecentWorkspace> GetByModule(string module)
        {
            return _data.Workspaces.Where(w => 
                string.Equals(w.Module, module, StringComparison.OrdinalIgnoreCase));
        }
        
        /// <summary>
        /// Gets frequently accessed workspaces.
        /// </summary>
        public IEnumerable<RecentWorkspace> GetFrequentWorkspaces(int count = 5)
        {
            return _data.Workspaces.OrderByDescending(w => w.AccessCount).Take(count);
        }
        
        private RecentWorkspacesData LoadData()
        {
            try
            {
                if (File.Exists(_dataFile))
                {
                    var json = File.ReadAllText(_dataFile);
                    return JsonSerializer.Deserialize<RecentWorkspacesData>(json) ?? new RecentWorkspacesData();
                }
            }
            catch { }
            return new RecentWorkspacesData();
        }
        
        private void SaveData()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_data, options);
                File.WriteAllText(_dataFile, json);
            }
            catch { }
        }
        
        private void RefreshCollections()
        {
            RecentWorkspaces.Clear();
            foreach (var w in _data.Workspaces) RecentWorkspaces.Add(w);
            
            RecentFiles.Clear();
            foreach (var f in _data.Files) RecentFiles.Add(f);
            
            PinnedPaths.Clear();
            foreach (var p in _data.PinnedPaths) PinnedPaths.Add(p);
            
            WorkspacesChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public class RecentWorkspacesData
    {
        public List<RecentWorkspace> Workspaces { get; set; } = new();
        public List<RecentFile> Files { get; set; } = new();
        public List<string> PinnedPaths { get; set; } = new();
    }
    
    public class RecentWorkspace
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Module { get; set; }
        public DateTime LastAccessed { get; set; }
        public int AccessCount { get; set; }
        
        public string DisplayName => string.IsNullOrEmpty(Name) ? System.IO.Path.GetFileName(Path) : Name;
        public bool Exists => Directory.Exists(Path);
        public string LastAccessedDisplay => LastAccessed.ToString("MMM d, yyyy h:mm tt");
    }
    
    public class RecentFile
    {
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Module { get; set; }
        public DateTime LastAccessed { get; set; }
        public long FileSize { get; set; }
        
        public bool Exists => File.Exists(Path);
        public string FileSizeDisplay => FileSize < 1024 
            ? $"{FileSize} B" 
            : FileSize < 1024 * 1024 
                ? $"{FileSize / 1024.0:F1} KB" 
                : $"{FileSize / (1024.0 * 1024.0):F1} MB";
    }
}
