using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for managing Butterchurn/ProjectM Milkdrop visualizer presets.
    /// Butterchurn is a WebGL-based Milkdrop 2 compatible visualizer that supports
    /// the full library of 130,000+ Milkdrop presets (.milk files).
    /// 
    /// Architecture:
    /// - Butterchurn runs in a WebView2 control via JavaScript
    /// - Audio spectrum data is pumped from NAudio → this service → WebView2 postMessage
    /// - Preset .milk files are loaded from a local presets directory
    /// - The service manages preset loading, switching, and history
    /// 
    /// Future integration steps:
    /// 1. Add WebView2 control to AudioVisualizerView (alongside SkiaSharp)
    /// 2. Bundle butterchurn.min.js + butterchurnPresets.min.js
    /// 3. Create HTML host page that initializes Butterchurn with a canvas
    /// 4. Pump spectrum data via WebView2.CoreWebView2.PostWebMessageAsJson()
    /// 5. Handle preset switching via JS interop
    /// </summary>
    public class ButterchurnVisualizerService : BindableBase
    {
        private static readonly Lazy<ButterchurnVisualizerService> _instance = new(() => new ButterchurnVisualizerService());
        public static ButterchurnVisualizerService Instance => _instance.Value;

        private string _presetsDirectory = string.Empty;
        private List<MilkdropPresetInfo> _availablePresets = new();
        private MilkdropPresetInfo? _currentPreset;
        private bool _isInitialized;
        private bool _isAvailable;
        private int _presetIndex;
        private bool _autoChangeEnabled = true;
        private double _autoChangeSeconds = 30.0;
        private DateTime _lastPresetChange = DateTime.MinValue;

        /// <summary>
        /// Gets whether Butterchurn/WebView2 is available for use.
        /// </summary>
        public bool IsAvailable
        {
            get => _isAvailable;
            private set { _isAvailable = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets whether the service has been initialized.
        /// </summary>
        public bool IsInitialized
        {
            get => _isInitialized;
            private set { _isInitialized = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets the list of available Milkdrop presets.
        /// </summary>
        public IReadOnlyList<MilkdropPresetInfo> AvailablePresets => _availablePresets.AsReadOnly();

        /// <summary>
        /// Gets or sets the currently active preset.
        /// </summary>
        public MilkdropPresetInfo? CurrentPreset
        {
            get => _currentPreset;
            set { _currentPreset = value; OnPropertyChanged(); OnPropertyChanged(nameof(CurrentPresetName)); }
        }

        /// <summary>
        /// Gets the display name of the current preset.
        /// </summary>
        public string CurrentPresetName => _currentPreset?.Name ?? "None";

        /// <summary>
        /// Gets or sets whether presets auto-change on a timer.
        /// </summary>
        public bool AutoChangeEnabled
        {
            get => _autoChangeEnabled;
            set { _autoChangeEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the auto-change interval in seconds.
        /// </summary>
        public double AutoChangeSeconds
        {
            get => _autoChangeSeconds;
            set { _autoChangeSeconds = Math.Clamp(value, 5.0, 300.0); OnPropertyChanged(); }
        }

        /// <summary>
        /// Event raised when presets are loaded or changed.
        /// </summary>
        public event EventHandler? PresetsLoaded;

        /// <summary>
        /// Event raised when the active preset changes.
        /// </summary>
        public event EventHandler<MilkdropPresetInfo>? PresetChanged;

        private ButterchurnVisualizerService() { }

        /// <summary>
        /// Initializes the service and scans for available presets.
        /// </summary>
        public async Task InitializeAsync()
        {
            if (IsInitialized) return;

            // Default presets directory alongside the application
            _presetsDirectory = Path.Combine(AppContext.BaseDirectory, "MilkdropPresets");
            
            // Also check user's AppData for additional presets
            var userPresetsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "MilkdropPresets");

            await Task.Run(() =>
            {
                ScanPresetsDirectory(_presetsDirectory);
                if (Directory.Exists(userPresetsDir))
                    ScanPresetsDirectory(userPresetsDir);
            });

            // Check if WebView2 runtime is available
            IsAvailable = CheckWebView2Available();
            IsInitialized = true;
            PresetsLoaded?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Scans a directory for .milk preset files.
        /// </summary>
        private void ScanPresetsDirectory(string directory)
        {
            if (!Directory.Exists(directory)) return;

            try
            {
                var milkFiles = Directory.EnumerateFiles(directory, "*.milk", SearchOption.AllDirectories);
                foreach (var file in milkFiles)
                {
                    _availablePresets.Add(new MilkdropPresetInfo
                    {
                        FilePath = file,
                        Name = Path.GetFileNameWithoutExtension(file),
                        Category = GetPresetCategory(file),
                        FileSize = new FileInfo(file).Length
                    });
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }

        /// <summary>
        /// Gets the category from the preset's parent directory name.
        /// </summary>
        private static string GetPresetCategory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (string.IsNullOrEmpty(dir)) return "Uncategorized";
            return Path.GetFileName(dir);
        }

        /// <summary>
        /// Checks if WebView2 runtime is installed on the system.
        /// </summary>
        private static bool CheckWebView2Available()
        {
            try
            {
                // Check for WebView2 runtime via registry or API
                var version = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Loads the next preset in the list.
        /// </summary>
        public void NextPreset()
        {
            if (_availablePresets.Count == 0) return;
            _presetIndex = (_presetIndex + 1) % _availablePresets.Count;
            CurrentPreset = _availablePresets[_presetIndex];
            _lastPresetChange = DateTime.Now;
            PresetChanged?.Invoke(this, CurrentPreset);
        }

        /// <summary>
        /// Loads the previous preset in the list.
        /// </summary>
        public void PreviousPreset()
        {
            if (_availablePresets.Count == 0) return;
            _presetIndex = (_presetIndex - 1 + _availablePresets.Count) % _availablePresets.Count;
            CurrentPreset = _availablePresets[_presetIndex];
            _lastPresetChange = DateTime.Now;
            PresetChanged?.Invoke(this, CurrentPreset);
        }

        /// <summary>
        /// Loads a random preset.
        /// </summary>
        public void RandomPreset()
        {
            if (_availablePresets.Count == 0) return;
            _presetIndex = Random.Shared.Next(_availablePresets.Count);
            CurrentPreset = _availablePresets[_presetIndex];
            _lastPresetChange = DateTime.Now;
            PresetChanged?.Invoke(this, CurrentPreset);
        }

        /// <summary>
        /// Loads a specific preset by name.
        /// </summary>
        public bool LoadPreset(string name)
        {
            var preset = _availablePresets.FirstOrDefault(p => 
                p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (preset == null) return false;

            _presetIndex = _availablePresets.IndexOf(preset);
            CurrentPreset = preset;
            _lastPresetChange = DateTime.Now;
            PresetChanged?.Invoke(this, CurrentPreset);
            return true;
        }

        /// <summary>
        /// Checks if auto-change timer has elapsed and switches preset if so.
        /// Called from the render loop.
        /// </summary>
        public void CheckAutoChange()
        {
            if (!AutoChangeEnabled || _availablePresets.Count == 0) return;
            if ((DateTime.Now - _lastPresetChange).TotalSeconds >= AutoChangeSeconds)
            {
                RandomPreset();
            }
        }

        /// <summary>
        /// Gets presets filtered by category.
        /// </summary>
        public IEnumerable<MilkdropPresetInfo> GetPresetsByCategory(string category)
        {
            return _availablePresets.Where(p => 
                p.Category.Equals(category, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets all unique preset categories.
        /// </summary>
        public IEnumerable<string> GetCategories()
        {
            return _availablePresets.Select(p => p.Category).Distinct().OrderBy(c => c);
        }

        /// <summary>
        /// Searches presets by name.
        /// </summary>
        public IEnumerable<MilkdropPresetInfo> SearchPresets(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return _availablePresets;
            return _availablePresets.Where(p => 
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Information about a Milkdrop preset file.
    /// </summary>
    public class MilkdropPresetInfo
    {
        /// <summary>Full path to the .milk file.</summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>Display name (filename without extension).</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Category (parent folder name).</summary>
        public string Category { get; set; } = "Uncategorized";

        /// <summary>File size in bytes.</summary>
        public long FileSize { get; set; }

        /// <summary>Whether this preset is marked as a favorite.</summary>
        public bool IsFavorite { get; set; }

        public override string ToString() => Name;
    }
}
