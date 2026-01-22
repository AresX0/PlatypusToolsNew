using System.Windows.Input;
using System.Windows.Forms;
using System;
using System.Diagnostics;
using PlatypusTools.Core.Utilities;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class MainWindowViewModel : BindableBase
    {
        /// <summary>
        /// Safely gets the MainWindow as an owner for child windows.
        /// Returns null if MainWindow is not available, not shown, or would cause a circular reference.
        /// </summary>
        private static System.Windows.Window? GetSafeOwner(System.Windows.Window? childWindow = null)
        {
            var mainWindow = System.Windows.Application.Current?.MainWindow;
            if (mainWindow == null) return null;
            if (!mainWindow.IsLoaded) return null;
            if (childWindow != null && ReferenceEquals(mainWindow, childWindow)) return null;
            return mainWindow;
        }

        #region Lazy ViewModel Initialization
        
        // Lazy<T> wrappers for all ViewModels - only created when first accessed
        private readonly Lazy<RecentCleanupViewModel> _recent = new(() => new RecentCleanupViewModel());
        private readonly Lazy<FileCleanerViewModel> _fileCleaner = new(() => new FileCleanerViewModel());
        private readonly Lazy<HiderViewModel> _hider = new(() => new HiderViewModel());
        private readonly Lazy<DuplicatesViewModel> _duplicates = new(() => new DuplicatesViewModel());
        private readonly Lazy<VideoCombinerViewModel> _videoCombiner = new(() => new VideoCombinerViewModel(Services.ServiceLocator.VideoCombiner));
        private readonly Lazy<ImageConverterViewModel> _imageConverter = new(() => new ImageConverterViewModel());
        private readonly Lazy<ImageResizerViewModel> _imageResizer = new(() => new ImageResizerViewModel());
        private readonly Lazy<IconConverterViewModel> _iconConverter = new(() => new IconConverterViewModel());
        private readonly Lazy<UpscalerViewModel> _upscaler = new(() => new UpscalerViewModel());
        private readonly Lazy<BatchUpscaleViewModel> _batchUpscale = new(() => new BatchUpscaleViewModel());
        private readonly Lazy<DiskCleanupViewModel> _diskCleanup = new(() => new DiskCleanupViewModel());
        private readonly Lazy<PrivacyCleanerViewModel> _privacyCleaner = new(() => new PrivacyCleanerViewModel());
        private readonly Lazy<VideoConverterViewModel> _videoConverter = new(() => new VideoConverterViewModel());
        private readonly Lazy<AudioPlayerViewModel> _audioPlayer = new(() => new AudioPlayerViewModel());
        private readonly Lazy<MetadataEditorViewModel> _metadataEditor = new(() => new MetadataEditorViewModel());
        private readonly Lazy<MultimediaEditorViewModel> _multimediaEditor = new(() => new MultimediaEditorViewModel());
        private readonly Lazy<SystemAuditViewModel> _systemAudit = new(() => new SystemAuditViewModel());
        private readonly Lazy<ForensicsAnalyzerViewModel> _forensicsAnalyzer = new(() => new ForensicsAnalyzerViewModel());
        private readonly Lazy<StartupManagerViewModel> _startupManager = new(() => new StartupManagerViewModel());
        private readonly Lazy<WebsiteDownloaderViewModel> _websiteDownloader = new(() => new WebsiteDownloaderViewModel());
        private readonly Lazy<FileAnalyzerViewModel> _fileAnalyzer = new(() => new FileAnalyzerViewModel());
        private readonly Lazy<MediaLibraryViewModel> _mediaLibrary = new(() => new MediaLibraryViewModel());
        private readonly Lazy<DiskSpaceAnalyzerViewModel> _diskSpaceAnalyzer = new(() => new DiskSpaceAnalyzerViewModel());
        private readonly Lazy<NetworkToolsViewModel> _networkTools = new(() => new NetworkToolsViewModel());
        private readonly Lazy<ProcessManagerViewModel> _processManager = new(() => new ProcessManagerViewModel());
        private readonly Lazy<RegistryCleanerViewModel> _registryCleaner = new(() => new RegistryCleanerViewModel());
        private readonly Lazy<ScheduledTasksViewModel> _scheduledTasks = new(() => new ScheduledTasksViewModel());
        private readonly Lazy<SystemRestoreViewModel> _systemRestore = new(() => new SystemRestoreViewModel());
        private readonly Lazy<ArchiveManagerViewModel> _archiveManager = new(() => new ArchiveManagerViewModel());
        private readonly Lazy<PdfToolsViewModel> _pdfTools = new(() => new PdfToolsViewModel());
        private readonly Lazy<BatchWatermarkViewModel> _batchWatermark = new(() => new BatchWatermarkViewModel());
        private readonly Lazy<ScreenshotViewModel> _screenshot = new(() => new ScreenshotViewModel());
        private readonly Lazy<BootableUSBViewModel> _bootableUSB = new(() => new BootableUSBViewModel());
        private readonly Lazy<EmptyFolderScannerViewModel> _emptyFolderScanner = new(() => new EmptyFolderScannerViewModel());
        private readonly Lazy<PluginManagerViewModel> _pluginManager = new(() => new PluginManagerViewModel());
        private readonly Lazy<RobocopyViewModel> _robocopy = new(() => new RobocopyViewModel());
        private readonly Lazy<FtpClientViewModel> _ftpClient = new(() => new FtpClientViewModel());
        private readonly Lazy<TerminalClientViewModel> _terminalClient = new(() => new TerminalClientViewModel());
        private readonly Lazy<SimpleBrowserViewModel> _simpleBrowser = new(() => new SimpleBrowserViewModel());
        
        #endregion

        public MainWindowViewModel()
        {
            // Debug entry
            SimpleLogger.Debug("Initializing MainWindowViewModel with lazy loading");
            
            try
            {
                SimpleLogger.Debug("Creating commands");
                BrowseCommand = new RelayCommand(_ => Browse());
                SaveWorkspaceCommand = new RelayCommand(_ => SaveWorkspace());
                ExportConfigCommand = new RelayCommand(_ => ExportConfig());
                ImportConfigCommand = new RelayCommand(_ => ImportConfig());
                OpenRecentWorkspacesCommand = new RelayCommand(_ => OpenRecentWorkspaces());
                ExitCommand = new RelayCommand(_ => System.Windows.Application.Current?.Shutdown());
                OpenHelpCommand = new RelayCommand(_ => OpenHelp());
                OpenArchivedScriptsCommand = new RelayCommand(_ => OpenArchivedScripts());
                RunArchivedScriptCommand = new RelayCommand(_ => RunArchivedScript());
                RunParityTestsCommand = new RelayCommand(_ => RunParityTests());
                OpenCredentialManagerCommand = new RelayCommand(_ => OpenCredentialManager());
                ToggleStagingInDuplicatesCommand = new RelayCommand(_ => Duplicates.StagingVisible = !Duplicates.StagingVisible);
                TogglePreviewPanelCommand = new RelayCommand(_ => Duplicates.PreviewVisible = !Duplicates.PreviewVisible);
                ToggleThemeCommand = new RelayCommand(_ => ToggleTheme());

                SimpleLogger.Debug("Commands created successfully");
                
                // Sync IsDarkTheme property with current theme (theme is loaded by App.xaml.cs before window creation)
                try
                {
                    var theme = PlatypusTools.UI.Services.ThemeManager.Instance.CurrentTheme;
                    IsDarkTheme = string.Equals(theme, PlatypusTools.UI.Services.ThemeManager.Dark, StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(theme, PlatypusTools.UI.Services.ThemeManager.LCARS, StringComparison.OrdinalIgnoreCase);
                }
                catch { }
            
                SimpleLogger.Debug("MainWindowViewModel initialized successfully (ViewModels will be created on-demand)");
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("MainWindowViewModel constructor" + " - " + ex.Message);
                throw;
            }
        }

        #region ViewModel Properties (Lazy-loaded)
        
        public RecentCleanupViewModel Recent => _recent.Value;
        public FileCleanerViewModel FileCleaner => _fileCleaner.Value;
        public HiderViewModel Hider => _hider.Value;
        public DuplicatesViewModel Duplicates => _duplicates.Value;
        public VideoCombinerViewModel VideoCombiner => _videoCombiner.Value;
        public ImageConverterViewModel ImageConverter => _imageConverter.Value;
        public ImageResizerViewModel ImageResizer => _imageResizer.Value;
        public IconConverterViewModel IconConverter => _iconConverter.Value;
        public UpscalerViewModel Upscaler => _upscaler.Value;
        public BatchUpscaleViewModel BatchUpscale => _batchUpscale.Value;
        public DiskCleanupViewModel DiskCleanup => _diskCleanup.Value;
        public PrivacyCleanerViewModel PrivacyCleaner => _privacyCleaner.Value;
        public VideoConverterViewModel VideoConverter => _videoConverter.Value;
        public AudioPlayerViewModel AudioPlayer => _audioPlayer.Value;
        public MetadataEditorViewModel MetadataEditor => _metadataEditor.Value;
        public MultimediaEditorViewModel MultimediaEditor => _multimediaEditor.Value;
        public SystemAuditViewModel SystemAudit => _systemAudit.Value;
        public ForensicsAnalyzerViewModel ForensicsAnalyzer => _forensicsAnalyzer.Value;
        public StartupManagerViewModel StartupManager => _startupManager.Value;
        public WebsiteDownloaderViewModel WebsiteDownloader => _websiteDownloader.Value;
        public FileAnalyzerViewModel FileAnalyzer => _fileAnalyzer.Value;
        public MediaLibraryViewModel MediaLibrary => _mediaLibrary.Value;
        public DiskSpaceAnalyzerViewModel DiskSpaceAnalyzer => _diskSpaceAnalyzer.Value;
        public NetworkToolsViewModel NetworkTools => _networkTools.Value;
        public ProcessManagerViewModel ProcessManager => _processManager.Value;
        public RegistryCleanerViewModel RegistryCleaner => _registryCleaner.Value;
        public ScheduledTasksViewModel ScheduledTasks => _scheduledTasks.Value;
        public SystemRestoreViewModel SystemRestore => _systemRestore.Value;
        public ArchiveManagerViewModel ArchiveManager => _archiveManager.Value;
        public PdfToolsViewModel PdfTools => _pdfTools.Value;
        public BatchWatermarkViewModel BatchWatermark => _batchWatermark.Value;
        public ScreenshotViewModel Screenshot => _screenshot.Value;
        public BootableUSBViewModel BootableUSB => _bootableUSB.Value;
        public EmptyFolderScannerViewModel EmptyFolderScanner => _emptyFolderScanner.Value;
        public PluginManagerViewModel PluginManager => _pluginManager.Value;
        public RobocopyViewModel Robocopy => _robocopy.Value;
        public FtpClientViewModel FtpClient => _ftpClient.Value;
        public TerminalClientViewModel TerminalClient => _terminalClient.Value;
        public SimpleBrowserViewModel SimpleBrowser => _simpleBrowser.Value;
        
        #endregion

        private string _selectedFolder = string.Empty;
        public string SelectedFolder { get => _selectedFolder; set { _selectedFolder = value; RaisePropertyChanged(); PropagateSelectedFolder(); } }

        public ICommand BrowseCommand { get; }
        public ICommand SaveWorkspaceCommand { get; }
        public ICommand ExportConfigCommand { get; }
        public ICommand ImportConfigCommand { get; }
        public ICommand OpenRecentWorkspacesCommand { get; }
        public ICommand ExitCommand { get; }
        public ICommand OpenHelpCommand { get; }
        public ICommand OpenArchivedScriptsCommand { get; }
        public ICommand RunArchivedScriptCommand { get; }
        public ICommand RunParityTestsCommand { get; }
        public ICommand OpenCredentialManagerCommand { get; }
        public ICommand ToggleStagingInDuplicatesCommand { get; }
        public ICommand TogglePreviewPanelCommand { get; }
        public ICommand ToggleThemeCommand { get; }

        private bool _isDarkTheme;
        public bool IsDarkTheme { get => _isDarkTheme; set { _isDarkTheme = value; RaisePropertyChanged(); } }

        private void Browse()
        {
            using var dlg = new FolderBrowserDialog();
            dlg.ShowNewFolderButton = true;
            if (!string.IsNullOrWhiteSpace(SelectedFolder)) dlg.SelectedPath = SelectedFolder;
            var res = dlg.ShowDialog();
            if (res == DialogResult.OK)
            {
                SelectedFolder = dlg.SelectedPath;
            }
        }

        private void PropagateSelectedFolder()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(SelectedFolder))
                {
                    FileCleaner.FolderPath = SelectedFolder;
                    Duplicates.FolderPath = SelectedFolder;
                    // For Recent cleaner, use as single target
                    Recent.TargetDirs = SelectedFolder;
                }
            }
            catch { }
        }

        private void OpenHelp()
        {
            try
            {
                var helpWindow = new PlatypusTools.UI.Views.HelpWindow();
                helpWindow.Owner = GetSafeOwner(helpWindow);
                helpWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening help: {ex.Message}", "Help Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void OpenArchivedScripts()
        {
            try
            {
                // Try to find ArchivedScripts folder starting from current directory and going up
                var cur = System.IO.Directory.GetCurrentDirectory();
                while (!string.IsNullOrEmpty(cur))
                {
                    var candidate = System.IO.Path.Combine(cur, "ArchivedScripts");
                    if (System.IO.Directory.Exists(candidate))
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"\"{candidate}\"") { UseShellExecute = true });
                        return;
                    }
                    var parent = System.IO.Directory.GetParent(cur);
                    cur = parent?.FullName;
                }
                
                // Also try relative to app directory
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var appCandidate = System.IO.Path.Combine(appDir, "ArchivedScripts");
                if (System.IO.Directory.Exists(appCandidate))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"\"{appCandidate}\"") { UseShellExecute = true });
                    return;
                }
                
                System.Windows.MessageBox.Show("ArchivedScripts folder not found. This feature is for development use.", "Not Found", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening Archived Scripts: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void RunArchivedScript()
        {
            try
            {
                var wnd = new PlatypusTools.UI.Views.ArchivedScriptRunnerWindow();
                wnd.Owner = GetSafeOwner(wnd);
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening Script Runner: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void RunParityTests()
        {
            try
            {
                System.Windows.MessageBox.Show("Running parity tests... This may take a moment.", "Parity Tests", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                
                // Run dotnet test for the core test project and show output in a simple window
                var psi = new ProcessStartInfo("dotnet", "test --filter Category!=Integration") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p == null)
                {
                    var wndFail = new PlatypusTools.UI.Views.ScriptOutputWindow("Failed to start dotnet test process.");
                    wndFail.Owner = GetSafeOwner(wndFail);
                    wndFail.ShowDialog();
                    return;
                }
                var outStr = p.StandardOutput.ReadToEnd();
                var errStr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                var wnd = new PlatypusTools.UI.Views.ScriptOutputWindow(outStr + "\n" + errStr);
                wnd.Owner = GetSafeOwner(wnd);
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error running Parity Tests: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void OpenCredentialManager()
        {
            try
            {
                var wnd = new PlatypusTools.UI.Views.CredentialManagerWindow();
                wnd.Owner = GetSafeOwner(wnd);
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error opening Credential Manager: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        private void SaveWorkspace()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.Filter = "Workspace files (*.ptws)|*.ptws|All files (*.*)|*.*";
                dlg.FileName = "workspace.ptws";
                var res = dlg.ShowDialog();
                if (res != true) return;

                var ws = new PlatypusTools.UI.Models.Workspace
                {
                    SelectedFolder = SelectedFolder,
                    FileCleanerTarget = FileCleaner.FolderPath,
                    FileCleanerFilter = FileCleaner.FileTypeFilter.ToString(),
                    RecentTargets = Recent.TargetDirs,
                    DuplicatesFolder = Duplicates.FolderPath
                };
                PlatypusTools.UI.Services.WorkspaceManager.SaveWorkspace(dlg.FileName, ws);
            }
            catch { }
        }

        private void ExportConfig()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                dlg.FileName = "platypus_config.json";
                if (dlg.ShowDialog() != true) return;

                var cfg = new
                {
                    SelectedFolder = SelectedFolder,
                    FileCleaner = new { Target = FileCleaner.FolderPath, Filter = FileCleaner.FileTypeFilter.ToString() },
                    Recent = new { Targets = Recent.TargetDirs },
                    Duplicates = new { Folder = Duplicates.FolderPath }
                };
                var txt = System.Text.Json.JsonSerializer.Serialize(cfg, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(dlg.FileName, txt);
            }
            catch { }
        }

        private void ImportConfig()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*";
                if (dlg.ShowDialog() != true) return;
                var txt = System.IO.File.ReadAllText(dlg.FileName);
                var doc = System.Text.Json.JsonDocument.Parse(txt);
                if (doc.RootElement.TryGetProperty("SelectedFolder", out var sf)) SelectedFolder = sf.GetString() ?? SelectedFolder;
                if (doc.RootElement.TryGetProperty("FileCleaner", out var fc))
                {
                    if (fc.TryGetProperty("Target", out var targ)) FileCleaner.FolderPath = targ.GetString() ?? FileCleaner.FolderPath;
                }
                if (doc.RootElement.TryGetProperty("Recent", out var rc)) { if (rc.TryGetProperty("Targets", out var rt)) Recent.TargetDirs = rt.GetString() ?? Recent.TargetDirs; }
                if (doc.RootElement.TryGetProperty("Duplicates", out var dd)) { if (dd.TryGetProperty("Folder", out var df)) Duplicates.FolderPath = df.GetString() ?? Duplicates.FolderPath; }
            }
            catch { }
        }

        private void OpenRecentWorkspaces()
        {
            try
            {
                var wnd = new PlatypusTools.UI.Views.RecentWorkspacesWindow();
                wnd.Owner = GetSafeOwner(wnd);
                wnd.ShowDialog();
                if (wnd.SelectedPath != null && !wnd.IsFile)
                {
                    var ws = PlatypusTools.UI.Services.WorkspaceManager.LoadWorkspace<PlatypusTools.UI.Models.Workspace>(wnd.SelectedPath);
                    if (ws != null)
                    {
                        SelectedFolder = ws.SelectedFolder ?? SelectedFolder;
                        FileCleaner.FolderPath = ws.FileCleanerTarget ?? FileCleaner.FolderPath;
                        Recent.TargetDirs = ws.RecentTargets ?? Recent.TargetDirs;
                        Duplicates.FolderPath = ws.DuplicatesFolder ?? Duplicates.FolderPath;
                    }
                }
            }
            catch { }
        }

        private void ToggleTheme()
        {
            try
            {
                IsDarkTheme = !IsDarkTheme;
                var theme = IsDarkTheme ? PlatypusTools.UI.Services.ThemeManager.Dark : PlatypusTools.UI.Services.ThemeManager.Light;
                PlatypusTools.UI.Services.ThemeManager.ApplyTheme(theme);
                PlatypusTools.UI.Services.SettingsManager.Current.Theme = theme;
                PlatypusTools.UI.Services.SettingsManager.SaveCurrent();
            }
            catch { }
        }
    }
}
