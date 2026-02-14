using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using PlatypusTools.UI.Interop;

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
        private bool _glassEnabled = false;
        private GlassLevel _glassLevel = GlassLevel.Auto;
        private string _language = "en-US";
        
        // Audio Visualizer Settings
        private bool _visualizerEnabled = true;
        private bool _showVisualizerDuringPlayback = true;
        private string _visualizerPreset = "Default";
        private double _visualizerOpacity = 0.9;
        private int _visualizerBarCount = 32;
        private string _visualizerPrimaryColor = "#1E90FF";
        private string _visualizerBackgroundColor = "#0A0E27";
        private bool _visualizerGpuAcceleration = true;
        private bool _visualizerNormalize = true;

        // Font Settings
        private string _customFontFamily = "Default";
        private double _fontScale = 1.0;

        // Session Restore Settings (IDEA-009)
        private bool _restoreLastSession = true;
        private int _lastSelectedMainTabIndex = 0;
        private Dictionary<string, int> _lastSelectedSubTabIndices = new();
        private double _windowWidth = 0;
        private double _windowHeight = 0;
        private double _windowTop = double.NaN;
        private double _windowLeft = double.NaN;
        private int _windowState = 0; // 0=Normal, 2=Maximized

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
        /// Gets or sets the application language culture code (e.g., "en-US", "es-ES").
        /// </summary>
        public string Language
        {
            get => _language;
            set { _language = value ?? "en-US"; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether glass effects are enabled.
        /// </summary>
        public bool GlassEnabled
        {
            get => _glassEnabled;
            set { _glassEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the glass effect level.
        /// </summary>
        public GlassLevel GlassLevel
        {
            get => _glassLevel;
            set { _glassLevel = value; OnPropertyChanged(); }
        }

        // Audio Visualizer Settings
        public bool VisualizerEnabled
        {
            get => _visualizerEnabled;
            set { _visualizerEnabled = value; OnPropertyChanged(); }
        }

        public bool ShowVisualizerDuringPlayback
        {
            get => _showVisualizerDuringPlayback;
            set { _showVisualizerDuringPlayback = value; OnPropertyChanged(); }
        }

        public string VisualizerPreset
        {
            get => _visualizerPreset;
            set { _visualizerPreset = value ?? "Default"; OnPropertyChanged(); }
        }

        public double VisualizerOpacity
        {
            get => _visualizerOpacity;
            set { _visualizerOpacity = Math.Clamp(value, 0.1, 1.0); OnPropertyChanged(); }
        }

        public int VisualizerBarCount
        {
            get => _visualizerBarCount;
            set { _visualizerBarCount = Math.Clamp(value, 8, 128); OnPropertyChanged(); }
        }

        public string VisualizerPrimaryColor
        {
            get => _visualizerPrimaryColor;
            set { _visualizerPrimaryColor = value ?? "#1E90FF"; OnPropertyChanged(); }
        }

        public string VisualizerBackgroundColor
        {
            get => _visualizerBackgroundColor;
            set { _visualizerBackgroundColor = value ?? "#0A0E27"; OnPropertyChanged(); }
        }

        public bool VisualizerGpuAcceleration
        {
            get => _visualizerGpuAcceleration;
            set { _visualizerGpuAcceleration = value; OnPropertyChanged(); }
        }

        public bool VisualizerNormalize
        {
            get => _visualizerNormalize;
            set { _visualizerNormalize = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the custom font family. "Default" means use theme default.
        /// Available: Default, klingon font, Okuda, Monofonto, Overseer, Consolas, Segoe UI
        /// </summary>
        public string CustomFontFamily
        {
            get => _customFontFamily;
            set { _customFontFamily = value ?? "Default"; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the font scale multiplier (0.5 to 2.0).
        /// 1.0 = default theme sizes, 1.5 = 50% larger, etc.
        /// </summary>
        public double FontScale
        {
            get => _fontScale;
            set { _fontScale = Math.Clamp(value, 0.5, 2.0); OnPropertyChanged(); }
        }

        // Admin rights setting
        private bool _requireAdminRights = true;

        /// <summary>
        /// Gets or sets whether the application should request admin rights on next launch.
        /// When changed, this takes effect on the next application restart.
        /// </summary>
        public bool RequireAdminRights
        {
            get => _requireAdminRights;
            set { _requireAdminRights = value; OnPropertyChanged(); }
        }

        // First-run and dependency settings
        private bool _hasSeenDependencyPrompt = false;
        private bool _autoInstallDependencies = false;
        private bool _skipSystemRequirementsCheck = false;

        /// <summary>
        /// Gets or sets whether the user has seen the dependency setup prompt.
        /// </summary>
        public bool HasSeenDependencyPrompt
        {
            get => _hasSeenDependencyPrompt;
            set { _hasSeenDependencyPrompt = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether to automatically install missing dependencies.
        /// </summary>
        public bool AutoInstallDependencies
        {
            get => _autoInstallDependencies;
            set { _autoInstallDependencies = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether to skip the system requirements check on startup.
        /// </summary>
        public bool SkipSystemRequirementsCheck
        {
            get => _skipSystemRequirementsCheck;
            set { _skipSystemRequirementsCheck = value; OnPropertyChanged(); }
        }

        // AD Security Analyzer license key protection
        private string _adSecurityLicenseKey = string.Empty;
        private bool _adSecurityHidden = true;

        // Last.fm Scrobbling Settings
        private string _lastFmApiKey = string.Empty;
        private string _lastFmApiSecret = string.Empty;
        private string _lastFmSessionKey = string.Empty;

        /// <summary>
        /// Gets or sets the Last.fm API key for scrobbling.
        /// Get one at https://www.last.fm/api/account/create
        /// </summary>
        public string LastFmApiKey
        {
            get => _lastFmApiKey;
            set { _lastFmApiKey = value ?? string.Empty; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the Last.fm API shared secret.
        /// </summary>
        public string LastFmApiSecret
        {
            get => _lastFmApiSecret;
            set { _lastFmApiSecret = value ?? string.Empty; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the Last.fm session key obtained after OAuth authentication.
        /// </summary>
        public string LastFmSessionKey
        {
            get => _lastFmSessionKey;
            set { _lastFmSessionKey = value ?? string.Empty; OnPropertyChanged(); }
        }

        // Platypus Remote Server Settings
        private bool _remoteServerEnabled = false;
        private int _remoteServerPort = 47392;
        private bool _remoteServerAutoStart = false;

        /// <summary>
        /// Gets or sets whether the Platypus Remote Server is enabled.
        /// When enabled, allows remote control of audio playback via web/PWA.
        /// </summary>
        public bool RemoteServerEnabled
        {
            get => _remoteServerEnabled;
            set { _remoteServerEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the port for the Remote Server (default: 47392).
        /// </summary>
        public int RemoteServerPort
        {
            get => _remoteServerPort;
            set { _remoteServerPort = Math.Clamp(value, 1024, 65535); OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether the Remote Server should auto-start with the application.
        /// </summary>
        public bool RemoteServerAutoStart
        {
            get => _remoteServerAutoStart;
            set { _remoteServerAutoStart = value; OnPropertyChanged(); }
        }

        // Cloudflare Tunnel Settings
        private bool _cloudflareTunnelEnabled = false;
        private bool _cloudflareTunnelAutoStart = false;
        private string _cloudflareTunnelHostname = "";
        private bool _cloudflareTunnelUseQuickTunnel = true;

        /// <summary>
        /// Gets or sets whether Cloudflare Tunnel is enabled for external access.
        /// </summary>
        public bool CloudflareTunnelEnabled
        {
            get => _cloudflareTunnelEnabled;
            set { _cloudflareTunnelEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether the Cloudflare Tunnel should auto-start with the Remote Server.
        /// </summary>
        public bool CloudflareTunnelAutoStart
        {
            get => _cloudflareTunnelAutoStart;
            set { _cloudflareTunnelAutoStart = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the custom hostname for named tunnels (e.g., platypus.josephtheplatypus.com).
        /// Leave empty to use quick tunnels with random *.trycloudflare.com URLs.
        /// </summary>
        public string CloudflareTunnelHostname
        {
            get => _cloudflareTunnelHostname;
            set { _cloudflareTunnelHostname = value ?? string.Empty; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether to use Quick Tunnel (no account required) vs Named Tunnel (requires CF account).
        /// </summary>
        public bool CloudflareTunnelUseQuickTunnel
        {
            get => _cloudflareTunnelUseQuickTunnel;
            set { _cloudflareTunnelUseQuickTunnel = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets the stored license key for the AD Security Analyzer tab.
        /// Empty string means no license key is registered (tab is inaccessible).
        /// </summary>
        public string AdSecurityLicenseKey
        {
            get => _adSecurityLicenseKey;
            set { _adSecurityLicenseKey = value ?? string.Empty; OnPropertyChanged(); }
        }

        /// <summary>
        /// Gets or sets whether the AD Security Analyzer tab is hidden by default.
        /// When true, users must unlock it with the password (if set) or keyboard shortcut.
        /// </summary>
        public bool AdSecurityHidden
        {
            get => _adSecurityHidden;
            set { _adSecurityHidden = value; OnPropertyChanged(); }
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

        // ======================== Session Restore (IDEA-009) ========================

        /// <summary>
        /// IDEA-003: Whether to minimize to system tray instead of closing.
        /// </summary>
        private bool _minimizeToTray;
        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set { _minimizeToTray = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether the navigation tab bars are auto-hidden.
        /// When enabled, all tab strips collapse and show a thin reveal bar on hover.
        /// </summary>
        private bool _autoHideNavigation;
        public bool AutoHideNavigation
        {
            get => _autoHideNavigation;
            set { _autoHideNavigation = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether to restore the last session's tab selection and window position on startup.
        /// </summary>
        public bool RestoreLastSession
        {
            get => _restoreLastSession;
            set { _restoreLastSession = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// The index of the last selected main (top-level) tab.
        /// </summary>
        public int LastSelectedMainTabIndex
        {
            get => _lastSelectedMainTabIndex;
            set { _lastSelectedMainTabIndex = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Maps main tab names to their last selected sub-tab index.
        /// </summary>
        public Dictionary<string, int> LastSelectedSubTabIndices
        {
            get => _lastSelectedSubTabIndices;
            set { _lastSelectedSubTabIndices = value ?? new Dictionary<string, int>(); OnPropertyChanged(); }
        }

        /// <summary>Window width in device-independent pixels. 0 = use default.</summary>
        public double WindowWidth
        {
            get => _windowWidth;
            set { _windowWidth = value; OnPropertyChanged(); }
        }

        /// <summary>Window height in device-independent pixels. 0 = use default.</summary>
        public double WindowHeight
        {
            get => _windowHeight;
            set { _windowHeight = value; OnPropertyChanged(); }
        }

        /// <summary>Window top position. NaN = use default.</summary>
        public double WindowTop
        {
            get => _windowTop;
            set { _windowTop = value; OnPropertyChanged(); }
        }

        /// <summary>Window left position. NaN = use default.</summary>
        public double WindowLeft
        {
            get => _windowLeft;
            set { _windowLeft = value; OnPropertyChanged(); }
        }

        /// <summary>Window state: 0=Normal, 2=Maximized.</summary>
        public int WindowStateValue
        {
            get => _windowState;
            set { _windowState = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Session-based unlock state (not persisted - resets on app restart)
        private bool _isAdSecurityUnlocked = false;
        
        /// <summary>
        /// Gets or sets whether the AD Security Analyzer has been unlocked for this session.
        /// This is NOT persisted - resets to false on app restart.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsAdSecurityUnlocked
        {
            get => _isAdSecurityUnlocked;
            set 
            { 
                _isAdSecurityUnlocked = value; 
                OnPropertyChanged();
                // Trigger tab visibility refresh
                TabVisibilityChanged?.Invoke(this, "Security.AdSecurityAnalyzer");
            }
        }

        /// <summary>
        /// Validates a license key for AD Security Analyzer using the LicenseKeyService.
        /// </summary>
        public bool ValidateLicenseKey(string licenseKey)
        {
            // Check if already licensed
            if (!string.IsNullOrEmpty(AdSecurityLicenseKey))
            {
                // Already has a stored key - check if it's still valid
                if (PlatypusTools.Core.Services.LicenseKeyService.ValidateKey(AdSecurityLicenseKey))
                {
                    IsAdSecurityUnlocked = true;
                    return true;
                }
            }
            
            // Validate the provided key
            if (PlatypusTools.Core.Services.LicenseKeyService.ValidateKey(licenseKey))
            {
                // Store the valid key
                AdSecurityLicenseKey = PlatypusTools.Core.Services.LicenseKeyService.FormatKey(licenseKey);
                IsAdSecurityUnlocked = true;
                return true;
            }
            
            return false;
        }

        /// <summary>
        /// Checks if the stored license key is valid (for auto-unlock on app start).
        /// </summary>
        public bool HasValidLicenseKey()
        {
            if (string.IsNullOrEmpty(AdSecurityLicenseKey))
                return false;
            return PlatypusTools.Core.Services.LicenseKeyService.ValidateKey(AdSecurityLicenseKey);
        }

        /// <summary>
        /// Gets whether a tab should be visible. Returns true by default if not configured,
        /// except for deprecated tabs which default to hidden.
        /// </summary>
        public bool IsTabVisible(string tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return true;
            
            // Special handling for AD Security Analyzer - requires valid license + session unlock
            if (tabKey == "Security.AdSecurityAnalyzer")
            {
                // Must have valid license AND be unlocked this session
                if (!HasValidLicenseKey() || !IsAdSecurityUnlocked)
                    return false;
                    
                // If unlocked and licensed, use saved visibility setting (defaults to true if not set)
                if (VisibleTabs.TryGetValue(tabKey, out var adVisible))
                    return adVisible;
                return true; // Default to visible once licensed and unlocked
            }
            
            // Explicitly check if user has set visibility
            if (VisibleTabs.TryGetValue(tabKey, out var visible))
                return visible;
            
            // Deprecated tabs default to hidden
            if (tabKey == "Multimedia.Audio.AudioPlayer")
                return false;
            
            return true;
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
        /// <summary>
        /// Whether the app is running in portable mode (IDEA-017).
        /// Portable mode stores settings next to the exe instead of in AppData.
        /// Detected by the presence of a "portable.txt" file alongside the executable.
        /// </summary>
        public static bool IsPortableMode { get; }

        private static readonly string _appFolder;
        private static readonly string _settingsFile;

        static SettingsManager()
        {
            // Check for portable mode marker file (IDEA-017)
            var exeDir = AppContext.BaseDirectory;
            var portableMarker = Path.Combine(exeDir, "portable.txt");
            IsPortableMode = File.Exists(portableMarker);

            if (IsPortableMode)
            {
                _appFolder = Path.Combine(exeDir, "PlatypusData");
            }
            else
            {
                _appFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools");
            }
            _settingsFile = Path.Combine(_appFolder, "settings.json");
        }

        private static string AppFolder => _appFolder;
        private static string SettingsFile => _settingsFile;

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
                new("FileManagement.Robocopy", "Robocopy", "FileManagement"),

                new("Multimedia", "🎬 Multimedia", null),
                new("Multimedia.Audio", "🎵 Audio", "Multimedia"),
                new("Multimedia.Audio.AudioPlayer", "Audio Player", "Multimedia.Audio"),
                new("Multimedia.Audio.EnhancedAudioPlayer", "Enhanced Player", "Multimedia.Audio"),
                new("Multimedia.Audio.AudioTrim", "Audio Trim", "Multimedia.Audio"),
                new("Multimedia.Image", "🖼️ Image", "Multimedia"),
                new("Multimedia.Image.ImageEdit", "Image Edit", "Multimedia.Image"),
                new("Multimedia.Image.ImageConverter", "Image Converter", "Multimedia.Image"),
                new("Multimedia.Image.ImageResizer", "Image Resizer", "Multimedia.Image"),
                new("Multimedia.Image.IconConverter", "ICO Converter", "Multimedia.Image"),
                new("Multimedia.Image.BatchWatermark", "Batch Watermark", "Multimedia.Image"),
                new("Multimedia.Image.ImageScaler", "Image Scaler", "Multimedia.Image"),
                new("Multimedia.Image.Model3DEditor", "3D Model Editor", "Multimedia.Image"),
                new("Multimedia.Video", "🎬 Video", "Multimedia"),
                new("Multimedia.Video.VideoPlayer", "Video Player", "Multimedia.Video"),
                new("Multimedia.Video.VideoEditor", "Video Editor", "Multimedia.Video"),
                new("Multimedia.Video.Upscaler", "Upscaler", "Multimedia.Video"),
                new("Multimedia.Video.VideoCombiner", "Video Combiner", "Multimedia.Video"),
                new("Multimedia.Video.VideoConverter", "Video Converter", "Multimedia.Video"),
                new("Multimedia.Video.ScreenRecorder", "Screen Recorder", "Multimedia.Video"),
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
                new("Security.SecureWipe", "Secure Wipe", "Security"),
                new("Security.AdvancedForensics", "🔬 Advanced Forensics", "Security"),
                new("Security.HashScanner", "Hash Scanner", "Security"),
                new("Security.AdvancedForensics.Memory", "Memory Analysis", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Artifacts", "Artifacts", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Timeline", "Timeline", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Kusto", "Kusto", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.LocalKQL", "Local KQL", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Malware", "Malware Analysis", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Extract", "Extract", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.OpenSearch", "OpenSearch", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.OSINT", "OSINT", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Schedule", "Task Scheduler", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Browser", "Browser Forensics", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.IOC", "IOC Scanner", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Registry", "Registry Diff", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.PCAP", "PCAP Parser", "Security.AdvancedForensics"),
                new("Security.AdvancedForensics.Results", "Results", "Security.AdvancedForensics"),
                new("Security.AdSecurityAnalyzer", "AD Security Analyzer", "Security"),
                new("Security.CveSearch", "CVE Search", "Security"),

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
                new("Tools.FtpClient", "FTP Client", "Tools"),
                new("Tools.TerminalClient", "Terminal Client", "Tools"),
                new("Tools.SimpleBrowser", "Simple Browser", "Tools"),
                new("Tools.PlexBackup", "Plex Backup", "Tools"),
                new("Tools.IntunePackager", "Intune Packager", "Tools"),
            };
        }
    }

    /// <summary>
    /// Defines a tab for the settings UI.
    /// </summary>
    public record TabDefinition(string Key, string DisplayName, string? ParentKey);
}