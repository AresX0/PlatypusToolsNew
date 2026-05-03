using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Avalonia.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly AppSettings _settings;
    private readonly ThemeService _theme = new();

    [ObservableProperty] private NavigationItem? _selectedItem;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _isDarkMode = true;

    /// <summary>Master list (unfiltered).</summary>
    public ObservableCollection<NavigationCategory> AllCategories { get; }

    /// <summary>What the TreeView binds to.</summary>
    public ObservableCollection<NavigationCategory> Categories { get; } = new();

    public MainWindowViewModel()
    {
        AllCategories = new()
        {
            new NavigationCategory("Home", new()
            {
                new("Dashboard",           "🏠",  new DashboardViewModel()),
                new("Command Palette",     "🎯",  new CommandPaletteViewModel()),
                new("Changelog",           "📰",  new ChangelogViewModel()),
                new("Keyboard Shortcuts",  "⌨",  new KeyboardShortcutsViewModel()),
            }),
            new NavigationCategory("Media", new()
            {
                new("Audio Player",        "🎵",  new AudioPlayerViewModel()),
                new("Audio Library",       "💿",  new AudioLibraryViewModel()),
                new("Image Viewer",        "🖼️",  new ImageViewerViewModel()),
                new("Video Converter",     "🎬",  new VideoConverterViewModel()),
                new("Video Combiner",      "🔗",  new VideoCombinerViewModel()),
                new("GIF Maker",           "🎞",  new GifMakerViewModel()),
                new("Audio / Video Trim",  "✂",  new AudioTrimViewModel()),
                new("Image Resizer",       "📐",  new ImageResizerViewModel()),
                new("Image Converter",     "🔄",  new ImageConverterViewModel()),
                new("Batch Watermark",     "💧",  new BatchWatermarkViewModel()),
                new("Batch Upscale",       "🔍",  new BatchUpscaleViewModel()),
                new("Icon Converter",      "🎯",  new IconConverterViewModel()),
                new("Video Metadata",      "📊",  new VideoMetadataViewModel()),
                new("Screen Recorder",     "📹",  new ScreenRecorderViewModel()),
                new("Screenshot",          "📸",  new ScreenshotViewModel()),
                new("QR Code Generator",   "🔳",  new QrCodeViewModel()),
                new("Audio Transcription", "🎙",  new AudioTranscriptionViewModel()),
                new("Native Audio Trim",   "✂",  new NativeAudioTrimViewModel()),
                new("Native Image Edit",   "🖌",  new NativeImageEditViewModel()),
                new("Native Video Player", "🎬",  new NativeVideoPlayerViewModel()),
                new("Media Hub",           "🎞",  new MediaHubViewModel()),
                new("Media Library",       "📚",  new MediaLibraryViewModel()),
                new("Multimedia Editor",   "🎥",  new MultimediaEditorViewModel()),
                new("3D Editor",           "🧊",  new Model3DEditorViewModel()),
                new("Shotcut Editor",      "🎥",  new ShotcutNativeEditorViewModel()),
                new("Upscaler",            "🔍",  new UpscalerViewModel()),
            }),
            new NavigationCategory("Files", new()
            {
                new("File Manager",        "📁",  new FileManagerViewModel()),
                new("Duplicates",          "🗂️",  new DuplicatesViewModel()),
                new("Empty Folder Scan",   "📭",  new EmptyFolderScannerViewModel()),
                new("File Diff",           "🔀",  new FileDiffViewModel()),
                new("Bulk Checksum",       "🔢",  new BulkChecksumViewModel()),
                new("Bulk File Mover",     "📤",  new BulkFileMoverViewModel()),
                new("Smart Rename",        "✏",  new SmartRenameViewModel()),
                new("Symlink Manager",     "🪢",  new SymlinkManagerViewModel()),
                new("File Analyzer",       "🔬",  new FileAnalyzerViewModel()),
                new("File Integrity",      "🛡",  new FileIntegrityViewModel()),
                new("Archive (ZIP)",       "📦",  new ArchiveManagerViewModel()),
                new("PDF Tools",           "📄",  new PdfToolsViewModel()),
                new("File Sync",           "🔁",  new FileSyncViewModel()),
                new("Metadata Editor",     "🏷",  new MetadataEditorViewModel()),
                new("Cloud Sync",          "☁",  new CloudSyncViewModel()),
                new("Tree-Map Disk",       "📊",  new TreeMapDiskViewModel()),
                new("Hider",               "🙈",  new HiderViewModel()),
                new("Scheduled Backup",    "📦",  new ScheduledBackupViewModel()),
            }),
            new NavigationCategory("Security", new()
            {
                new("Hash Scanner",        "🔐",  new HashScannerViewModel()),
                new("Vault",               "🔒",  new VaultViewModel()),
                new("File Encryption",     "🛡",  new FileEncryptionViewModel()),
                new("Secure Wipe",         "🧨",  new SecureWipeViewModel()),
                new("Privacy Cleaner",     "🧹",  new PrivacyCleanerViewModel()),
                new("Wi-Fi Passwords",     "📶",  new WifiPasswordsViewModel()),
                new("Certificate Manager", "🔏",  new CertificateManagerViewModel()),
                new("SSH Key Manager",     "🔑",  new SshKeyManagerViewModel()),
                new("CVE Search",          "🛡",  new CveSearchViewModel()),
                new("YARA Scanner",        "🪤",  new YaraScannerViewModel()),
                new("Forensics Analyzer",  "🔬",  new ForensicsAnalyzerViewModel()),
                new("Credential Manager",  "🗝",  new CredentialManagerViewModel()),
                new("Advanced Forensics",  "🔬",  new AdvancedForensicsViewModel()),
                new("Encrypted Clipboard", "🔐",  new EncryptedClipboardViewModel()),
                new("Directory Analyzer",  "🏛",  new DirectorySecurityAnalyzerViewModel()),
                new("Platytalk",           "🛸",  new PlatytalkViewModel()),
            }),
            new NavigationCategory("System", new()
            {
                new("Process Manager",     "⚙",  new ProcessManagerViewModel()),
                new("Disk Space",          "💾",  new DiskSpaceAnalyzerViewModel()),
                new("Disk Cleanup",        "🧽",  new DiskCleanupViewModel()),
                new("Network Tools",       "🌐",  new NetworkToolsViewModel()),
                new("Network Traffic",     "📡",  new NetworkTrafficViewModel()),
                new("System Audit",        "🩺",  new SystemAuditViewModel()),
                new("Scheduled Tasks",     "⏰",  new ScheduledTasksViewModel()),
                new("Wallpaper Rotator",   "🖼",  new WallpaperRotatorViewModel()),
                new("Screensaver / Lock",  "🌙",  new ScreensaverViewModel()),
                new("Environment Vars",    "🌳",  new EnvironmentVariablesViewModel()),
                new("Startup Manager",     "🚀",  new StartupManagerViewModel()),
                new("Dependencies",        "🔌",  new DependenciesViewModel()),
                new("Disk Health",         "💉",  new DiskHealthViewModel()),
                new("Bootable USB",        "💽",  new BootableUsbViewModel()),
                new("Remote Desktop",      "🖥",  new RemoteDesktopViewModel()),
                new("Screensaver Config",  "🌙",  new ScreensaverConfigViewModel()),
                new("Slideshow Saver",     "🖼",  new SlideshowScreensaverViewModel()),
                new("Reboot Analyzer",     "🔁",  new RebootAnalyzerViewModel()),
                new("Recent Cleanup",      "🧹",  new RecentCleanupViewModel()),
                new("File Cleaner",        "🧽",  new FileCleanerViewModel()),
                new("System Hardening",    "🛡",  new SystemHardeningViewModel()),
                new("System Restore",      "⏮",  new SystemRestoreViewModel()),
                new("Plex Backup",         "🎬",  new PlexBackupViewModel()),
                new("Intune Backup",       "☁",  new IntuneBackupViewModel()),
                new("Update Checker",      "⬆",  new UpdateCheckerViewModel()),
                new("Service Manager",     "🔧",  new ServiceManagerViewModel()),
            }),
            new NavigationCategory("Tools", new()
            {
                new("Color Picker",        "🎨",  new ColorPickerViewModel()),
                new("Text Editor",         "📝",  new TextEditorViewModel()),
                new("Log Viewer",          "📜",  new LogViewerViewModel()),
                new("Clipboard History",   "📋",  new ClipboardHistoryViewModel()),
                new("Website Downloader",  "⬇",  new WebsiteDownloaderViewModel()),
                new("Scripting Console",   "💻",  new ScriptingConsoleViewModel()),
                new("Recent Workspaces",   "🕒",  new RecentWorkspacesViewModel()),
                new("Notification Center", "🔔",  new NotificationCenterViewModel()),
                new("Activity Log",        "📜",  new ActivityLogViewModel()),
                new("FTP Client",          "📡",  new FtpClientViewModel()),
                new("Terminal",            "🖥",  new TerminalClientViewModel()),
                new("Mail Sender",         "✉",  new MailSenderViewModel()),
                new("Batch Job Queue",     "📥",  new BatchJobQueueViewModel()),
                new("Plugin Manager",      "🧩",  new PluginManagerViewModel()),
                new("Simple Browser",      "🌐",  new SimpleBrowserViewModel()),
                new("AI Assistant",        "🤖",  new AIAssistantViewModel()),
                new("Workflow Designer",   "🪢",  new WorkflowDesignerViewModel()),
                new("Fleet View",          "🚀",  new FleetViewModel()),
                new("Remote Dashboard",    "🛰",  new RemoteDashboardViewModel()),
                new("Plugin Marketplace",  "🛒",  new PluginMarketplaceViewModel()),
                new("Global Search",       "🔎",  new GlobalSearchViewModel()),
                new("Theme Builder",       "🎨",  new ThemeBuilderViewModel()),
                new("Mail Client",         "📧",  new MailClientViewModel()),
                new("Export Queue",        "📤",  new ExportQueueViewModel()),
            }),
            new NavigationCategory("Settings", new()
            {
                new("Settings",            "⚙️",  new SettingsViewModel()),
                new("About / Help",        "ℹ",  new AboutViewModel()),
            }),
        };

        RebuildCategories(null);

        _settings = AppSettings.Load();
        IsDarkMode = _settings.DarkMode;
        _theme.ApplyTheme(IsDarkMode);

        var lastTitle = _settings.LastSelectedPage;
        NavigationItem? toSelect = null;
        if (!string.IsNullOrEmpty(lastTitle))
            toSelect = AllCategories.SelectMany(c => c.Items).FirstOrDefault(i => i.Title == lastTitle);
        SelectedItem = toSelect ?? AllCategories.First().Items.First();
    }

    partial void OnFilterChanged(string value) => RebuildCategories(value);

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is null || _settings is null) return;
        _settings.LastSelectedPage = value.Title;
        _settings.Save();
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        _theme.ApplyTheme(value);
        if (_settings is not null) { _settings.DarkMode = value; _settings.Save(); }
    }

    [RelayCommand]
    private void ToggleTheme() => IsDarkMode = !IsDarkMode;

    private void RebuildCategories(string? filter)
    {
        Categories.Clear();
        var f = (filter ?? "").Trim();
        var mediaOnly = EditionService.Current == AppEdition.Media;
        foreach (var cat in AllCategories)
        {
            if (mediaOnly && !IsCategoryAllowedInMediaEdition(cat.Title))
                continue;

            if (string.IsNullOrEmpty(f))
            {
                Categories.Add(cat);
                continue;
            }
            var matches = cat.Items.Where(i => i.Title.Contains(f, System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0) continue;
            var copy = new NavigationCategory(cat.Title, new ObservableCollection<NavigationItem>(matches));
            Categories.Add(copy);
        }
    }

    private static bool IsCategoryAllowedInMediaEdition(string categoryTitle)
    {
        // Media edition keeps multimedia + minimal navigation. Other top-level
        // categories (Files / Security / System / Tools) are hidden.
        return categoryTitle is "Home" or "Media" or "Settings";
    }

    /// <summary>
    /// Used by Dashboard tile clicks to jump to a feature by VM type name.
    /// </summary>
    public void NavigateToVmTypeName(string typeName)
    {
        var match = AllCategories.SelectMany(c => c.Items)
            .FirstOrDefault(i => i.Content.GetType().Name == typeName);
        if (match is not null) SelectedItem = match;
    }
}

public sealed class NavigationCategory
{
    public string Title { get; }
    public ObservableCollection<NavigationItem> Items { get; }

    public NavigationCategory(string title, ObservableCollection<NavigationItem> items)
    {
        Title = title;
        Items = items;
    }
}

public partial class NavigationItem : ObservableObject
{
    public string Title { get; }
    public string Icon { get; }
    public ObservableObject Content { get; }

    public NavigationItem(string title, string icon, ObservableObject content)
    {
        Title = title;
        Icon = icon;
        Content = content;
    }
}
