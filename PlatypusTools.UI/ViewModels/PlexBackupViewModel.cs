using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Utilities;
using Microsoft.Win32;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for Plex Media Server backup operations.
    /// Uses native C# PlexBackupService for all backup/restore functionality.
    /// </summary>
    public class PlexBackupViewModel : BindableBase
    {
        private readonly PlexBackupService _backupService;
        private CancellationTokenSource? _cts;

        #region Constructor

        public PlexBackupViewModel()
        {
            _backupService = new PlexBackupService();
            _backupService.ProgressChanged += OnProgressChanged;
            _backupService.LogMessage += OnLogMessage;

            // Initialize commands
            ExecuteCommand = new AsyncRelayCommand(ExecuteAsync, CanExecute);
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
            RefreshStatusCommand = new AsyncRelayCommand(RefreshStatusAsync);
            BrowsePlexAppDataCommand = new RelayCommand(_ => BrowsePlexAppData());
            BrowseBackupRootCommand = new RelayCommand(_ => BrowseBackupRoot());
            BrowseBackupDirCommand = new RelayCommand(_ => BrowseBackupDir());
            BrowseTempDirCommand = new RelayCommand(_ => BrowseTempDir());
            Browse7ZipCommand = new RelayCommand(_ => Browse7Zip());
            OpenBackupFolderCommand = new RelayCommand(_ => OpenBackupFolder(), _ => !string.IsNullOrEmpty(BackupRootDir) && Directory.Exists(BackupRootDir));
            OpenPlexFolderCommand = new RelayCommand(_ => OpenPlexFolder(), _ => !string.IsNullOrEmpty(PlexAppDataDir) && Directory.Exists(PlexAppDataDir));
            ClearOutputCommand = new RelayCommand(_ => Output = string.Empty);
            RefreshBackupsCommand = new AsyncRelayCommand(RefreshBackupsAsync);
            DeleteBackupCommand = new AsyncRelayCommand<PlexBackupInfo>(DeleteBackupAsync);

            // Set defaults
            PlexAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plex Media Server");
            BackupRootDir = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            TempDir = Path.GetTempPath();
            SevenZipPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe");

            // Initialize async
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            await RefreshStatusAsync();
            await RefreshBackupsAsync();
        }

        #endregion

        #region Properties - Directories

        private string _plexAppDataDir = string.Empty;
        public string PlexAppDataDir
        {
            get => _plexAppDataDir;
            set { SetProperty(ref _plexAppDataDir, value); UpdateServiceOptions(); }
        }

        private string _backupRootDir = string.Empty;
        public string BackupRootDir
        {
            get => _backupRootDir;
            set { SetProperty(ref _backupRootDir, value); UpdateServiceOptions(); }
        }

        private string _backupDir = string.Empty;
        public string BackupDir
        {
            get => _backupDir;
            set { SetProperty(ref _backupDir, value); UpdateServiceOptions(); }
        }

        private string _tempDir = string.Empty;
        public string TempDir
        {
            get => _tempDir;
            set { SetProperty(ref _tempDir, value); UpdateServiceOptions(); }
        }

        private string _sevenZipPath = string.Empty;
        public string SevenZipPath
        {
            get => _sevenZipPath;
            set 
            { 
                SetProperty(ref _sevenZipPath, value); 
                UpdateServiceOptions();
                RaisePropertyChanged(nameof(Is7ZipAvailable));
            }
        }

        #endregion

        #region Properties - Mode and Type

        private PlexBackupMode _mode = PlexBackupMode.Backup;
        public PlexBackupMode Mode
        {
            get => _mode;
            set { SetProperty(ref _mode, value); UpdateServiceOptions(); }
        }

        private PlexBackupType _backupType = PlexBackupType.Default;
        public PlexBackupType BackupType
        {
            get => _backupType;
            set 
            { 
                SetProperty(ref _backupType, value); 
                UpdateServiceOptions();
                RaisePropertyChanged(nameof(Is7ZipSelected));
            }
        }

        public bool Is7ZipSelected => BackupType == PlexBackupType.SevenZip;
        public bool Is7ZipAvailable => File.Exists(SevenZipPath);

        #endregion

        #region Properties - Options

        private int _keepBackups = 3;
        public int KeepBackups
        {
            get => _keepBackups;
            set { SetProperty(ref _keepBackups, Math.Max(0, value)); UpdateServiceOptions(); }
        }

        private int _retries = 5;
        public int Retries
        {
            get => _retries;
            set { SetProperty(ref _retries, Math.Max(0, value)); UpdateServiceOptions(); }
        }

        private int _retryWaitSec = 10;
        public int RetryWaitSec
        {
            get => _retryWaitSec;
            set { SetProperty(ref _retryWaitSec, Math.Max(0, value)); UpdateServiceOptions(); }
        }

        private bool _testMode;
        public bool TestMode
        {
            get => _testMode;
            set { SetProperty(ref _testMode, value); UpdateServiceOptions(); }
        }

        private bool _noRestart;
        public bool NoRestart
        {
            get => _noRestart;
            set { SetProperty(ref _noRestart, value); UpdateServiceOptions(); }
        }

        private bool _noVersionCheck;
        public bool NoVersionCheck
        {
            get => _noVersionCheck;
            set { SetProperty(ref _noVersionCheck, value); UpdateServiceOptions(); }
        }

        private bool _inactive;
        public bool Inactive
        {
            get => _inactive;
            set { SetProperty(ref _inactive, value); UpdateServiceOptions(); }
        }

        // Exclude directories
        private bool _excludeDiagnostics = true;
        public bool ExcludeDiagnostics
        {
            get => _excludeDiagnostics;
            set { SetProperty(ref _excludeDiagnostics, value); UpdateExcludeDirs(); }
        }

        private bool _excludeCrashReports = true;
        public bool ExcludeCrashReports
        {
            get => _excludeCrashReports;
            set { SetProperty(ref _excludeCrashReports, value); UpdateExcludeDirs(); }
        }

        private bool _excludeUpdates = true;
        public bool ExcludeUpdates
        {
            get => _excludeUpdates;
            set { SetProperty(ref _excludeUpdates, value); UpdateExcludeDirs(); }
        }

        private bool _excludeLogs = true;
        public bool ExcludeLogs
        {
            get => _excludeLogs;
            set { SetProperty(ref _excludeLogs, value); UpdateExcludeDirs(); }
        }

        private bool _excludeThumbnails = true;
        public bool ExcludeThumbnails
        {
            get => _excludeThumbnails;
            set { SetProperty(ref _excludeThumbnails, value); UpdateExcludeFiles(); }
        }

        private bool _excludeTranscode = true;
        public bool ExcludeTranscode
        {
            get => _excludeTranscode;
            set { SetProperty(ref _excludeTranscode, value); UpdateExcludeFiles(); }
        }

        #endregion

        #region Properties - Status

        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set 
            { 
                SetProperty(ref _isRunning, value);
                RaisePropertyChanged(nameof(IsNotRunning));
            }
        }

        public bool IsNotRunning => !IsRunning;

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { SetProperty(ref _progress, value); }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { SetProperty(ref _statusMessage, value); }
        }

        private string _output = string.Empty;
        public string Output
        {
            get => _output;
            set { SetProperty(ref _output, value); }
        }

        // Plex Status
        private bool _plexIsRunning;
        public bool PlexIsRunning
        {
            get => _plexIsRunning;
            set { SetProperty(ref _plexIsRunning, value); }
        }

        private bool _plexAppDataExists;
        public bool PlexAppDataExists
        {
            get => _plexAppDataExists;
            set { SetProperty(ref _plexAppDataExists, value); }
        }

        private bool _plexRegistryExists;
        public bool PlexRegistryExists
        {
            get => _plexRegistryExists;
            set { SetProperty(ref _plexRegistryExists, value); }
        }

        private string _plexVersion = "Unknown";
        public string PlexVersion
        {
            get => _plexVersion;
            set { SetProperty(ref _plexVersion, value); }
        }

        private string _plexExecutablePath = string.Empty;
        public string PlexExecutablePath
        {
            get => _plexExecutablePath;
            set { SetProperty(ref _plexExecutablePath, value); }
        }

        private ObservableCollection<string> _plexServices = new();
        public ObservableCollection<string> PlexServices
        {
            get => _plexServices;
            set { SetProperty(ref _plexServices, value); }
        }

        // Available Backups
        private ObservableCollection<PlexBackupInfo> _availableBackups = new();
        public ObservableCollection<PlexBackupInfo> AvailableBackups
        {
            get => _availableBackups;
            set { SetProperty(ref _availableBackups, value); }
        }

        private PlexBackupInfo? _selectedBackup;
        public PlexBackupInfo? SelectedBackup
        {
            get => _selectedBackup;
            set 
            { 
                SetProperty(ref _selectedBackup, value);
                if (value != null && Mode == PlexBackupMode.Restore)
                {
                    BackupDir = value.Path;
                }
            }
        }

        #endregion

        #region Commands

        public ICommand ExecuteCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RefreshStatusCommand { get; }
        public ICommand BrowsePlexAppDataCommand { get; }
        public ICommand BrowseBackupRootCommand { get; }
        public ICommand BrowseBackupDirCommand { get; }
        public ICommand BrowseTempDirCommand { get; }
        public ICommand Browse7ZipCommand { get; }
        public ICommand OpenBackupFolderCommand { get; }
        public ICommand OpenPlexFolderCommand { get; }
        public ICommand ClearOutputCommand { get; }
        public ICommand RefreshBackupsCommand { get; }
        public ICommand DeleteBackupCommand { get; }

        #endregion

        #region Command Implementations

        private bool CanExecute()
        {
            if (IsRunning) return false;

            if (Mode == PlexBackupMode.Backup || Mode == PlexBackupMode.Continue)
            {
                return !string.IsNullOrEmpty(PlexAppDataDir) && 
                       Directory.Exists(PlexAppDataDir) &&
                       !string.IsNullOrEmpty(BackupRootDir);
            }
            else // Restore
            {
                return !string.IsNullOrEmpty(BackupDir) && Directory.Exists(BackupDir);
            }
        }

        private async Task ExecuteAsync()
        {
            if (IsRunning) return;

            IsRunning = true;
            Progress = 0;
            Output = string.Empty;
            _cts = new CancellationTokenSource();

            try
            {
                UpdateServiceOptions();

                PlexBackupResult result;

                if (Mode == PlexBackupMode.Restore)
                {
                    result = await _backupService.RestoreAsync(BackupDir, _cts.Token);
                }
                else
                {
                    result = await _backupService.BackupAsync(_cts.Token);
                }

                if (result.Success)
                {
                    StatusMessage = $"{Mode} completed successfully!";
                    AppendOutput($"\n=== {Mode} COMPLETED SUCCESSFULLY ===");
                    AppendOutput($"Duration: {result.Duration:hh\\:mm\\:ss}");
                    
                    if (Mode != PlexBackupMode.Restore)
                    {
                        AppendOutput($"Files: {result.FileCount}");
                        AppendOutput($"Size: {FormatSize(result.TotalSizeBytes)}");
                        AppendOutput($"Location: {result.BackupPath}");
                    }
                }
                else
                {
                    StatusMessage = $"{Mode} failed: {result.ErrorMessage}";
                    AppendOutput($"\n=== {Mode} FAILED ===");
                    AppendOutput($"Error: {result.ErrorMessage}");
                }

                // Refresh status and backups after operation
                await RefreshStatusAsync();
                await RefreshBackupsAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppendOutput($"\n=== ERROR ===\n{ex.Message}");
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void Cancel()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                AppendOutput("\nCancelling operation...");
                _cts.Cancel();
                StatusMessage = "Cancelling...";
            }
        }

        private async Task RefreshStatusAsync()
        {
            try
            {
                var status = await Task.Run(() => _backupService.GetPlexStatus());

                Application.Current.Dispatcher.Invoke(() =>
                {
                    PlexIsRunning = status.IsRunning;
                    PlexAppDataExists = status.AppDataFolderExists;
                    PlexRegistryExists = status.RegistryKeyExists;
                    PlexVersion = status.Version ?? "Unknown";
                    PlexExecutablePath = status.ExecutablePath ?? "Not found";
                    
                    PlexServices.Clear();
                    foreach (var service in status.RunningServices)
                    {
                        PlexServices.Add(service);
                    }

                    if (string.IsNullOrEmpty(PlexAppDataDir) && !string.IsNullOrEmpty(status.AppDataPath))
                    {
                        PlexAppDataDir = status.AppDataPath;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing Plex status: {ex.Message}");
            }
        }

        private async Task RefreshBackupsAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(BackupRootDir) || !Directory.Exists(BackupRootDir))
                {
                    AvailableBackups.Clear();
                    return;
                }

                _backupService.Options.BackupRootDir = BackupRootDir;
                var backups = await Task.Run(() => _backupService.GetAvailableBackups());

                Application.Current.Dispatcher.Invoke(() =>
                {
                    AvailableBackups.Clear();
                    foreach (var backup in backups)
                    {
                        AvailableBackups.Add(backup);
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing backups: {ex.Message}");
            }
        }

        private async Task DeleteBackupAsync(PlexBackupInfo? backup)
        {
            if (backup == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete this backup?\n\n{backup.Name}\n{backup.FormattedSize}",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                await Task.Run(() => Directory.Delete(backup.Path, true));
                await RefreshBackupsAsync();
                AppendOutput($"Deleted backup: {backup.Name}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete backup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Browse Commands

        private void BrowsePlexAppData()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Plex Media Server App Data Folder",
                SelectedPath = PlexAppDataDir,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                PlexAppDataDir = dialog.SelectedPath;
            }
        }

        private void BrowseBackupRoot()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Backup Root Folder",
                SelectedPath = BackupRootDir,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                BackupRootDir = dialog.SelectedPath;
                _ = RefreshBackupsAsync();
            }
        }

        private void BrowseBackupDir()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Backup Folder to Restore From",
                SelectedPath = BackupDir,
                ShowNewFolderButton = false
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                BackupDir = dialog.SelectedPath;
            }
        }

        private void BrowseTempDir()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Temporary Folder",
                SelectedPath = TempDir,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                TempDir = dialog.SelectedPath;
            }
        }

        private void Browse7Zip()
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select 7-Zip Executable",
                Filter = "7-Zip (7z.exe)|7z.exe|All Executables (*.exe)|*.exe",
                FileName = SevenZipPath,
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                SevenZipPath = dialog.FileName;
            }
        }

        private void OpenBackupFolder()
        {
            if (!string.IsNullOrEmpty(BackupRootDir) && Directory.Exists(BackupRootDir))
            {
                Process.Start("explorer.exe", BackupRootDir);
            }
        }

        private void OpenPlexFolder()
        {
            if (!string.IsNullOrEmpty(PlexAppDataDir) && Directory.Exists(PlexAppDataDir))
            {
                Process.Start("explorer.exe", PlexAppDataDir);
            }
        }

        #endregion

        #region Helper Methods

        private void UpdateServiceOptions()
        {
            _backupService.Options = new PlexBackupOptions
            {
                Mode = Mode,
                BackupType = BackupType,
                PlexAppDataDir = PlexAppDataDir,
                BackupRootDir = BackupRootDir,
                BackupDir = BackupDir,
                TempDir = TempDir,
                SevenZipPath = SevenZipPath,
                KeepBackups = KeepBackups,
                Retries = Retries,
                RetryWaitSec = RetryWaitSec,
                TestMode = TestMode,
                NoRestart = NoRestart,
                NoVersionCheck = NoVersionCheck,
                Inactive = Inactive
            };

            UpdateExcludeDirs();
            UpdateExcludeFiles();
        }

        private void UpdateExcludeDirs()
        {
            var excludeDirs = new System.Collections.Generic.List<string>();
            if (ExcludeDiagnostics) excludeDirs.Add("Diagnostics");
            if (ExcludeCrashReports) excludeDirs.Add("Crash Reports");
            if (ExcludeUpdates) excludeDirs.Add("Updates");
            if (ExcludeLogs) excludeDirs.Add("Logs");
            _backupService.Options.ExcludeDirs = excludeDirs.ToArray();
        }

        private void UpdateExcludeFiles()
        {
            var excludeFiles = new System.Collections.Generic.List<string>();
            if (ExcludeThumbnails) excludeFiles.Add("*.bif");
            if (ExcludeTranscode) excludeFiles.Add("Transcode");
            _backupService.Options.ExcludeFiles = excludeFiles.ToArray();
        }

        private void OnProgressChanged(object? sender, PlexBackupProgressEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Progress = e.Percentage;
                StatusMessage = e.Status;
            });
        }

        private void OnLogMessage(object? sender, PlexBackupLogEventArgs e)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var prefix = e.Level switch
                {
                    PlexLogLevel.Error => "[ERROR]",
                    PlexLogLevel.Warning => "[WARN]",
                    PlexLogLevel.Debug => "[DEBUG]",
                    _ => "[INFO]"
                };

                AppendOutput($"[{e.Timestamp:HH:mm:ss}] {prefix} {e.Message}");
            });
        }

        private void AppendOutput(string text)
        {
            Output += text + Environment.NewLine;
        }

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        #endregion
    }
}
