using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Service for managing filter presets and favorites.
    /// Provides save/load functionality for filter configurations.
    /// </summary>
    public class FilterPresetService
    {
        private static readonly Lazy<FilterPresetService> _instance = 
            new(() => new FilterPresetService());
        
        public static FilterPresetService Instance => _instance.Value;
        
        private readonly string _presetsPath;
        private readonly string _favoritesPath;
        private List<FilterPreset> _presets = new();
        private HashSet<string> _favoriteFilterNames = new();
        
        private FilterPresetService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools");
            
            Directory.CreateDirectory(appDataPath);
            
            _presetsPath = Path.Combine(appDataPath, "filter_presets.json");
            _favoritesPath = Path.Combine(appDataPath, "filter_favorites.json");
            
            LoadPresets();
            LoadFavorites();
        }
        
        /// <summary>
        /// Gets all saved presets.
        /// </summary>
        public IReadOnlyList<FilterPreset> Presets => _presets.AsReadOnly();
        
        /// <summary>
        /// Gets presets for a specific filter.
        /// </summary>
        public IEnumerable<FilterPreset> GetPresetsForFilter(string filterName)
        {
            return _presets.Where(p => p.FilterName == filterName);
        }
        
        /// <summary>
        /// Saves a new preset.
        /// </summary>
        public void SavePreset(FilterPreset preset)
        {
            _presets.Add(preset);
            SavePresets();
        }
        
        /// <summary>
        /// Creates and saves a preset from a filter's current settings.
        /// </summary>
        public FilterPreset CreatePreset(Filter filter, string presetName)
        {
            var preset = FilterPreset.FromFilter(filter, presetName);
            SavePreset(preset);
            return preset;
        }
        
        /// <summary>
        /// Deletes a preset.
        /// </summary>
        public void DeletePreset(string presetId)
        {
            _presets.RemoveAll(p => p.Id == presetId);
            SavePresets();
        }
        
        /// <summary>
        /// Renames a preset.
        /// </summary>
        public void RenamePreset(string presetId, string newName)
        {
            var preset = _presets.FirstOrDefault(p => p.Id == presetId);
            if (preset != null)
            {
                preset.Name = newName;
                SavePresets();
            }
        }
        
        /// <summary>
        /// Checks if a filter is marked as favorite.
        /// </summary>
        public bool IsFavorite(string filterName)
        {
            return _favoriteFilterNames.Contains(filterName);
        }
        
        /// <summary>
        /// Toggles the favorite status of a filter.
        /// </summary>
        public bool ToggleFavorite(string filterName)
        {
            if (_favoriteFilterNames.Contains(filterName))
            {
                _favoriteFilterNames.Remove(filterName);
                SaveFavorites();
                return false;
            }
            else
            {
                _favoriteFilterNames.Add(filterName);
                SaveFavorites();
                return true;
            }
        }
        
        /// <summary>
        /// Sets the favorite status of a filter.
        /// </summary>
        public void SetFavorite(string filterName, bool isFavorite)
        {
            if (isFavorite)
            {
                _favoriteFilterNames.Add(filterName);
            }
            else
            {
                _favoriteFilterNames.Remove(filterName);
            }
            SaveFavorites();
        }
        
        /// <summary>
        /// Gets all favorite filter names.
        /// </summary>
        public IReadOnlyCollection<string> FavoriteFilterNames => _favoriteFilterNames;
        
        private void LoadPresets()
        {
            try
            {
                if (File.Exists(_presetsPath))
                {
                    var json = File.ReadAllText(_presetsPath);
                    _presets = JsonSerializer.Deserialize<List<FilterPreset>>(json) ?? new();
                }
            }
            catch
            {
                _presets = new();
            }
        }
        
        private void SavePresets()
        {
            try
            {
                var json = JsonSerializer.Serialize(_presets, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllTextAsync(_presetsPath, json).ConfigureAwait(false);
            }
            catch
            {
                // Log error
            }
        }
        
        private void LoadFavorites()
        {
            try
            {
                if (File.Exists(_favoritesPath))
                {
                    var json = File.ReadAllText(_favoritesPath);
                    var favorites = JsonSerializer.Deserialize<List<string>>(json) ?? new();
                    _favoriteFilterNames = new HashSet<string>(favorites);
                }
            }
            catch
            {
                _favoriteFilterNames = new();
            }
        }
        
        private void SaveFavorites()
        {
            try
            {
                var json = JsonSerializer.Serialize(_favoriteFilterNames.ToList(), new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                _ = File.WriteAllTextAsync(_favoritesPath, json);
            }
            catch
            {
                // Log error
            }
        }
    }
}
