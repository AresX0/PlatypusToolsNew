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
        public MainWindowViewModel()
        {
            // Debug entry
            SimpleLogger.Debug("Initializing MainWindowViewModel");
            
            try
            {
                SimpleLogger.Debug("Creating RecentCleanupViewModel");
                Recent = new RecentCleanupViewModel();
                
                SimpleLogger.Debug("Creating FileCleanerViewModel");
                FileCleaner = new FileCleanerViewModel();
                
                SimpleLogger.Debug("Creating HiderViewModel");
                Hider = new HiderViewModel();
                
                SimpleLogger.Debug("Creating DuplicatesViewModel");
                Duplicates = new DuplicatesViewModel();
                
                SimpleLogger.Debug("Creating VideoCombinerViewModel");
                VideoCombiner = new VideoCombinerViewModel(new PlatypusTools.Core.Services.VideoCombinerService());
                
                SimpleLogger.Debug("Creating ImageConverterViewModel");
                ImageConverter = new ImageConverterViewModel();
                ImageResizer = new ImageResizerViewModel();
                IconConverter = new IconConverterViewModel();
                Upscaler = new UpscalerViewModel();
                BatchUpscale = new BatchUpscaleViewModel();
                FileRenamer = new FileRenamerViewModel();
                DiskCleanup = new DiskCleanupViewModel();
                PrivacyCleaner = new PrivacyCleanerViewModel();
                VideoConverter = new VideoConverterViewModel();
                
                SimpleLogger.Debug("Creating AudioPlayerViewModel");
                AudioPlayer = new AudioPlayerViewModel();
                
                SimpleLogger.Debug("Creating MetadataEditorViewModel");
                MetadataEditor = new MetadataEditorViewModel();
                
                SimpleLogger.Debug("Creating MultimediaEditorViewModel");
                MultimediaEditor = new MultimediaEditorViewModel();
                
                SimpleLogger.Debug("Creating SystemAuditViewModel");
                SystemAudit = new SystemAuditViewModel();
                
                SimpleLogger.Debug("Creating StartupManagerViewModel");
                StartupManager = new StartupManagerViewModel();
                
                SimpleLogger.Debug("Creating remaining ViewModels");
                WebsiteDownloader = new WebsiteDownloaderViewModel();
                FileAnalyzer = new FileAnalyzerViewModel();
                MediaLibrary = new MediaLibraryViewModel();
                DiskSpaceAnalyzer = new DiskSpaceAnalyzerViewModel();
                NetworkTools = new NetworkToolsViewModel();
                ProcessManager = new ProcessManagerViewModel();
                RegistryCleaner = new RegistryCleanerViewModel();
                ScheduledTasks = new ScheduledTasksViewModel();
                SystemRestore = new SystemRestoreViewModel();
                ArchiveManager = new ArchiveManagerViewModel();
                BootableUSB = new BootableUSBViewModel();
                EmptyFolderScanner = new EmptyFolderScannerViewModel();

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
            // Apply theme from settings
            try
            {
                var s = PlatypusTools.UI.Services.SettingsManager.Load();
                IsDarkTheme = string.Equals(s.Theme, PlatypusTools.UI.Services.ThemeManager.Dark, StringComparison.OrdinalIgnoreCase);
                PlatypusTools.UI.Services.ThemeManager.ApplyTheme(s.Theme);
            }
            catch { }
            
                SimpleLogger.Debug("MainWindowViewModel initialized successfully");
                // Debug exit
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("MainWindowViewModel constructor" + " - " + ex.Message);
                throw;
            }
        }

        public RecentCleanupViewModel Recent { get; }
        public FileCleanerViewModel FileCleaner { get; }
        public HiderViewModel Hider { get; }
        public DuplicatesViewModel Duplicates { get; }
        public VideoCombinerViewModel VideoCombiner { get; } 
        public ImageConverterViewModel ImageConverter { get; }
        public ImageResizerViewModel ImageResizer { get; }
        public IconConverterViewModel IconConverter { get; }
        public UpscalerViewModel Upscaler { get; }
        public BatchUpscaleViewModel BatchUpscale { get; }
        public FileRenamerViewModel FileRenamer { get; }
        public DiskCleanupViewModel DiskCleanup { get; }
        public PrivacyCleanerViewModel PrivacyCleaner { get; }
        public VideoConverterViewModel VideoConverter { get; }
        public AudioPlayerViewModel AudioPlayer { get; }
        public MetadataEditorViewModel MetadataEditor { get; }
        public MultimediaEditorViewModel MultimediaEditor { get; }
        public SystemAuditViewModel SystemAudit { get; }
        public StartupManagerViewModel StartupManager { get; }
        public WebsiteDownloaderViewModel WebsiteDownloader { get; }
        public FileAnalyzerViewModel FileAnalyzer { get; }
        public MediaLibraryViewModel MediaLibrary { get; }
        public DiskSpaceAnalyzerViewModel DiskSpaceAnalyzer { get; }
        public NetworkToolsViewModel NetworkTools { get; }
        public ProcessManagerViewModel ProcessManager { get; }
        public RegistryCleanerViewModel RegistryCleaner { get; }
        public ScheduledTasksViewModel ScheduledTasks { get; }
        public SystemRestoreViewModel SystemRestore { get; }
        public ArchiveManagerViewModel ArchiveManager { get; }
        public BootableUSBViewModel BootableUSB { get; }
        public EmptyFolderScannerViewModel EmptyFolderScanner { get; }
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
                helpWindow.Owner = System.Windows.Application.Current?.MainWindow;
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
                var cur = System.IO.Directory.GetCurrentDirectory();
                while (!string.IsNullOrEmpty(cur))
                {
                    var candidate = System.IO.Path.Combine(cur, "ArchivedScripts");
                    if (System.IO.Directory.Exists(candidate)) { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer", $"\"{candidate}\"") { UseShellExecute = true }); return; }
                    var parent = System.IO.Directory.GetParent(cur);
                    cur = parent?.FullName;
                }
            }
            catch { }
        }

        private void RunArchivedScript()
        {
            try
            {
                var wnd = new PlatypusTools.UI.Views.ArchivedScriptRunnerWindow();
                wnd.Owner = System.Windows.Application.Current?.MainWindow;
                wnd.ShowDialog();
            }
            catch { }
        }

        private void RunParityTests()
        {
            try
            {
                // Run dotnet test for the core test project and show output in a simple window
                var psi = new ProcessStartInfo("dotnet", "test --filter Category!=Integration") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
                using var p = Process.Start(psi);
                if (p == null)
                {
                    var wndFail = new PlatypusTools.UI.Views.ScriptOutputWindow("Failed to start dotnet test process.") { Owner = System.Windows.Application.Current?.MainWindow };
                    wndFail.ShowDialog();
                    return;
                }
                var outStr = p.StandardOutput.ReadToEnd();
                var errStr = p.StandardError.ReadToEnd();
                p.WaitForExit();
                var wnd = new PlatypusTools.UI.Views.ScriptOutputWindow(outStr + "\n" + errStr) { Owner = System.Windows.Application.Current?.MainWindow };
                wnd.ShowDialog();
            }
            catch { }
        }

        private void OpenCredentialManager()
        {
            try
            {
                var wnd = new PlatypusTools.UI.Views.CredentialManagerWindow();
                wnd.Owner = System.Windows.Application.Current?.MainWindow;
                wnd.ShowDialog();
            }
            catch { }
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
                wnd.Owner = System.Windows.Application.Current?.MainWindow;
                wnd.ShowDialog();
                if (wnd.SelectedWorkspace != null)
                {
                    var ws = PlatypusTools.UI.Services.WorkspaceManager.LoadWorkspace<PlatypusTools.UI.Models.Workspace>(wnd.SelectedWorkspace);
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
                var s = PlatypusTools.UI.Services.SettingsManager.Load();
                s.Theme = theme;
                PlatypusTools.UI.Services.SettingsManager.Save(s);
            }
            catch { }
        }
    }
}
