using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Observable settings class that notifies when tab visibility changes.
    /// This enables immediate UI updates when settings are modified.
    /// </summary>
    public class AppSettings : INotifyPropertyChanged
    {
        private string _theme = ThemeManager.Light;
        private bool _checkForUpdatesOnStartup = true;
        private Dictionary<string, bool> _visibleTabs = new();

        public string Theme
        {
            get => _theme;
            set { _theme = value; OnPropertyChanged(); }
        }

        public bool CheckForUpdatesOnStartup
        {
            get => _checkForUpdatesOnStartup;
            set { _checkForUpdatesOnStartup = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Dictionary mapping tab keys to their visibility state.
        /// Keys follow the pattern: "MainTab" or "MainTab.SubTab" for nested tabs.
        /// Default is visible (true) if not present in dictionary.
        /// </summary>
        public Dictionary<string, bool> VisibleTabs
        {
            get => _visibleTabs;
            set { _visibleTabs = value ?? new Dictionary<string, bool>(); OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Gets whether a tab should be visible. Returns true by default if not configured.
        /// </summary>
        public bool IsTabVisible(string tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return true;
            return !VisibleTabs.TryGetValue(tabKey, out var visible) || visible;
        }

        /// <summary>
        /// Sets a tab's visibility and raises change notification.
        /// </summary>
        public void SetTabVisible(string tabKey, bool visible)
        {
            if (string.IsNullOrEmpty(tabKey)) return;
            VisibleTabs[tabKey] = visible;
            OnPropertyChanged(nameof(VisibleTabs));
            TabVisibilityChanged?.Invoke(this, tabKey);
        }

        /// <summary>
        /// Event raised when any tab's visibility changes.
        /// The string parameter is the tab key that changed.
        /// </summary>
        public event EventHandler<string>? TabVisibilityChanged;
    }

    public static class SettingsManager
    {
        private static string AppFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools");
        private static string SettingsFile => Path.Combine(AppFolder, "settings.json");

        private static AppSettings? _cachedSettings;

        /// <summary>
        /// Gets the cached settings instance or loads from disk.
        /// Use this for binding to enable real-time updates.
        /// </summary>
        public static AppSettings Current => _cachedSettings ??= Load();

        public static AppSettings Load()
        {
            try
            {
                if (!Directory.Exists(AppFolder)) Directory.CreateDirectory(AppFolder);
                if (!File.Exists(SettingsFile))
                {
                    _cachedSettings = new AppSettings();
                    return _cachedSettings;
                }
                var txt = File.ReadAllText(SettingsFile);
                _cachedSettings = JsonSerializer.Deserialize<AppSettings>(txt) ?? new AppSettings();
                return _cachedSettings;
            }
            catch
            {
                _cachedSettings = new AppSettings();
                return _cachedSettings;
            }
        }

        public static void Save(AppSettings s)
        {
            try
            {
                if (!Directory.Exists(AppFolder)) Directory.CreateDirectory(AppFolder);
                var txt = JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, txt);
                _cachedSettings = s;
            }
            catch { }
        }

        /// <summary>
        /// Saves the current cached settings to disk.
        /// </summary>
        public static void SaveCurrent()
        {
            if (_cachedSettings != null)
                Save(_cachedSettings);
        }

        /// <summary>
        /// Gets all available tab keys with their display names.
        /// This defines the complete tab structure for the settings UI.
        /// </summary>
        public static IReadOnlyList<TabDefinition> GetAllTabDefinitions()
        {
            return new List<TabDefinition>
            {
                // Main tabs
                new("FileManagement", "📁 File Management", null),
                new("FileManagement.FileCleaner", "File Cleaner", "FileManagement"),
                new("FileManagement.Duplicates", "Duplicates", "FileManagement"),
                new("FileManagement.EmptyFolderScanner", "Empty Folder Scanner", "FileManagement"),

                new("Multimedia", "🎬 Multimedia", null),
                new("Multimedia.Audio", "🎵 Audio", "Multimedia"),
                new("Multimedia.Audio.AudioPlayer", "Audio Player", "Multimedia.Audio"),
                new("Multimedia.Audio.AudioTrim", "Audio Trim", "Multimedia.Audio"),
                new("Multimedia.Image", "🖼️ Image", "Multimedia"),
                new("Multimedia.Image.ImageEdit", "Image Edit", "Multimedia.Image"),
                new("Multimedia.Image.ImageConverter", "Image Converter", "Multimedia.Image"),
                new("Multimedia.Image.ImageResizer", "Image Resizer", "Multimedia.Image"),
                new("Multimedia.Image.IconConverter", "ICO Converter", "Multimedia.Image"),
                new("Multimedia.Image.BatchWatermark", "Batch Watermark", "Multimedia.Image"),
                new("Multimedia.Image.ImageScaler", "Image Scaler", "Multimedia.Image"),
                new("Multimedia.Video", "🎬 Video", "Multimedia"),
                new("Multimedia.Video.VideoPlayer", "Video Player", "Multimedia.Video"),
                new("Multimedia.Video.VideoEditor", "Video Editor", "Multimedia.Video"),
                new("Multimedia.Video.Upscaler", "Upscaler", "Multimedia.Video"),
                new("Multimedia.Video.VideoCombiner", "Video Combiner", "Multimedia.Video"),
                new("Multimedia.Video.VideoConverter", "Video Converter", "Multimedia.Video"),
                new("Multimedia.MediaLibrary", "📚 Media Library", "Multimedia"),
                new("Multimedia.ExternalTools", "🔧 External Tools", "Multimedia"),

                new("System", "🖥️ System", null),
                new("System.DiskCleanup", "Disk Cleanup", "System"),
                new("System.PrivacyCleaner", "Privacy Cleaner", "System"),
                new("System.RecentCleaner", "Recent Cleaner", "System"),
                new("System.StartupManager", "Startup Manager", "System"),
                new("System.ProcessManager", "Process Manager", "System"),
                new("System.RegistryCleaner", "Registry Cleaner", "System"),
                new("System.ScheduledTasks", "Scheduled Tasks", "System"),
                new("System.SystemRestore", "System Restore", "System"),

                new("Security", "🔒 Security", null),
                new("Security.FolderHider", "Folder Hider", "Security"),
                new("Security.SystemAudit", "System Audit", "Security"),
                new("Security.ForensicsAnalyzer", "Forensics Analyzer", "Security"),

                new("Metadata", "📋 Metadata", null),

                new("Tools", "🔧 Tools", null),
                new("Tools.WebsiteDownloader", "Website Downloader", "Tools"),
                new("Tools.FileAnalyzer", "File Analyzer", "Tools"),
                new("Tools.DiskSpaceAnalyzer", "Disk Space Analyzer", "Tools"),
                new("Tools.NetworkTools", "Network Tools", "Tools"),
                new("Tools.ArchiveManager", "Archive Manager", "Tools"),
                new("Tools.PdfTools", "PDF Tools", "Tools"),
                new("Tools.Screenshot", "Screenshot", "Tools"),
                new("Tools.BootableUSB", "Bootable USB Creator", "Tools"),
                new("Tools.Robocopy", "Robocopy", "Tools"),
                new("Tools.PluginManager", "Plugin Manager", "Tools"),
            };
        }
    }

    /// <summary>
    /// Defines a tab for the settings UI.
    /// </summary>
    public record TabDefinition(string Key, string DisplayName, string? ParentKey);
}