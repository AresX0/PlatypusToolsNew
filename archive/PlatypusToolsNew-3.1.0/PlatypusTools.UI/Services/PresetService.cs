using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing operation presets.
    /// Allows saving and loading configurations for quick reuse.
    /// </summary>
    public class PresetService
    {
        private static readonly Lazy<PresetService> _instance = new(() => new PresetService());
        public static PresetService Instance => _instance.Value;
        
        private readonly string _presetsFolder;
        private readonly Dictionary<string, List<OperationPreset>> _presets = new();
        
        public ObservableCollection<OperationPreset> AllPresets { get; } = new();
        public event EventHandler? PresetsChanged;
        
        private PresetService()
        {
            _presetsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "Presets");
            Directory.CreateDirectory(_presetsFolder);
            LoadAllPresets();
        }
        
        /// <summary>
        /// Gets presets for a specific module.
        /// </summary>
        public IEnumerable<OperationPreset> GetPresets(string module)
        {
            if (_presets.TryGetValue(module.ToLower(), out var presets))
            {
                return presets.OrderByDescending(p => p.IsFavorite).ThenBy(p => p.Name);
            }
            return Enumerable.Empty<OperationPreset>();
        }
        
        /// <summary>
        /// Saves a new preset.
        /// </summary>
        public void SavePreset(OperationPreset preset)
        {
            preset.Id = preset.Id == Guid.Empty ? Guid.NewGuid() : preset.Id;
            preset.CreatedDate = preset.CreatedDate == default ? DateTime.Now : preset.CreatedDate;
            preset.ModifiedDate = DateTime.Now;
            
            var moduleKey = preset.Module.ToLower();
            if (!_presets.ContainsKey(moduleKey))
            {
                _presets[moduleKey] = new List<OperationPreset>();
            }
            
            var existing = _presets[moduleKey].FirstOrDefault(p => p.Id == preset.Id);
            if (existing != null)
            {
                _presets[moduleKey].Remove(existing);
                AllPresets.Remove(existing);
            }
            
            _presets[moduleKey].Add(preset);
            AllPresets.Add(preset);
            
            SaveModulePresets(moduleKey);
            PresetsChanged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Deletes a preset.
        /// </summary>
        public void DeletePreset(Guid presetId)
        {
            foreach (var kvp in _presets)
            {
                var preset = kvp.Value.FirstOrDefault(p => p.Id == presetId);
                if (preset != null)
                {
                    kvp.Value.Remove(preset);
                    AllPresets.Remove(preset);
                    SaveModulePresets(kvp.Key);
                    PresetsChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }
        
        /// <summary>
        /// Toggles a preset as favorite.
        /// </summary>
        public void ToggleFavorite(Guid presetId)
        {
            foreach (var kvp in _presets)
            {
                var preset = kvp.Value.FirstOrDefault(p => p.Id == presetId);
                if (preset != null)
                {
                    preset.IsFavorite = !preset.IsFavorite;
                    preset.ModifiedDate = DateTime.Now;
                    SaveModulePresets(kvp.Key);
                    PresetsChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }
        }
        
        /// <summary>
        /// Duplicates a preset.
        /// </summary>
        public OperationPreset? DuplicatePreset(Guid presetId)
        {
            foreach (var kvp in _presets)
            {
                var preset = kvp.Value.FirstOrDefault(p => p.Id == presetId);
                if (preset != null)
                {
                    var duplicate = new OperationPreset
                    {
                        Id = Guid.NewGuid(),
                        Name = $"{preset.Name} (Copy)",
                        Description = preset.Description,
                        Module = preset.Module,
                        OperationType = preset.OperationType,
                        Settings = new Dictionary<string, object>(preset.Settings),
                        CreatedDate = DateTime.Now,
                        ModifiedDate = DateTime.Now,
                        IsFavorite = false
                    };
                    
                    kvp.Value.Add(duplicate);
                    AllPresets.Add(duplicate);
                    SaveModulePresets(kvp.Key);
                    PresetsChanged?.Invoke(this, EventArgs.Empty);
                    return duplicate;
                }
            }
            return null;
        }
        
        /// <summary>
        /// Exports a preset to a file.
        /// </summary>
        public void ExportPreset(Guid presetId, string filePath)
        {
            var preset = AllPresets.FirstOrDefault(p => p.Id == presetId);
            if (preset != null)
            {
                var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);
            }
        }
        
        /// <summary>
        /// Imports a preset from a file.
        /// </summary>
        public OperationPreset? ImportPreset(string filePath)
        {
            try
            {
                var json = File.ReadAllText(filePath);
                var preset = JsonSerializer.Deserialize<OperationPreset>(json);
                if (preset != null)
                {
                    preset.Id = Guid.NewGuid(); // Generate new ID on import
                    preset.CreatedDate = DateTime.Now;
                    preset.ModifiedDate = DateTime.Now;
                    SavePreset(preset);
                    return preset;
                }
            }
            catch { }
            return null;
        }
        
        private void LoadAllPresets()
        {
            _presets.Clear();
            AllPresets.Clear();
            
            foreach (var file in Directory.GetFiles(_presetsFolder, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var presets = JsonSerializer.Deserialize<List<OperationPreset>>(json);
                    if (presets != null)
                    {
                        var moduleName = Path.GetFileNameWithoutExtension(file).ToLower();
                        _presets[moduleName] = presets;
                        foreach (var preset in presets)
                        {
                            AllPresets.Add(preset);
                        }
                    }
                }
                catch { }
            }
        }
        
        private void SaveModulePresets(string module)
        {
            try
            {
                if (_presets.TryGetValue(module, out var presets))
                {
                    var filePath = Path.Combine(_presetsFolder, $"{module}.json");
                    var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(filePath, json);
                }
            }
            catch { }
        }
    }
    
    public class OperationPreset
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = "New Preset";
        public string Description { get; set; } = "";
        public string Module { get; set; } = "General";
        public string OperationType { get; set; } = "";
        public Dictionary<string, object> Settings { get; set; } = new();
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        public bool IsFavorite { get; set; }
        public string Icon { get; set; } = "⚙";
        
        public string CreatedDisplay => CreatedDate.ToString("MMM d, yyyy");
        public string ModifiedDisplay => ModifiedDate.ToString("MMM d, yyyy");
        
        public T? GetSetting<T>(string key, T? defaultValue = default)
        {
            if (Settings.TryGetValue(key, out var value))
            {
                try
                {
                    if (value is JsonElement element)
                    {
                        return JsonSerializer.Deserialize<T>(element.GetRawText());
                    }
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch { }
            }
            return defaultValue;
        }
        
        public void SetSetting<T>(string key, T value)
        {
            Settings[key] = value!;
        }
    }
}
