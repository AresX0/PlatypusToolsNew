using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// IDEA-005: Dashboard / Home Tab ViewModel.
    /// Shows disk usage, recent files, now-playing, server status, and quick actions.
    /// </summary>
    public class DashboardViewModel : BindableBase
    {
        private readonly DispatcherTimer _refreshTimer;

        public DashboardViewModel()
        {
            RefreshCommand = new AsyncRelayCommand(RefreshAsync);
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings());
            OpenCommandPaletteCommand = new RelayCommand(_ => OpenCommandPalette());
            CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);

            _refreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _refreshTimer.Tick += async (s, e) => await RefreshAsync();
            _refreshTimer.Start();

            // Load widget customization preferences
            LoadWidgetPrefs();

            // Initial load
            _ = RefreshAsync();
        }

        #region Properties

        private string _greeting = string.Empty;
        public string Greeting
        {
            get => _greeting;
            set => SetProperty(ref _greeting, value);
        }

        private string _currentTime = string.Empty;
        public string CurrentTime
        {
            get => _currentTime;
            set => SetProperty(ref _currentTime, value);
        }

        private string _appVersion = string.Empty;
        public string AppVersion
        {
            get => _appVersion;
            set => SetProperty(ref _appVersion, value);
        }

        // Disk Usage
        public ObservableCollection<DiskInfoItem> DiskUsage { get; } = new();

        // Now Playing
        private string _nowPlayingTitle = "No track playing";
        public string NowPlayingTitle
        {
            get => _nowPlayingTitle;
            set => SetProperty(ref _nowPlayingTitle, value);
        }

        private string _nowPlayingArtist = string.Empty;
        public string NowPlayingArtist
        {
            get => _nowPlayingArtist;
            set => SetProperty(ref _nowPlayingArtist, value);
        }

        private bool _isPlaying;
        public bool IsPlaying
        {
            get => _isPlaying;
            set => SetProperty(ref _isPlaying, value);
        }

        // Server Status
        private string _serverStatus = "Stopped";
        public string ServerStatus
        {
            get => _serverStatus;
            set => SetProperty(ref _serverStatus, value);
        }

        private bool _isServerRunning;
        public bool IsServerRunning
        {
            get => _isServerRunning;
            set => SetProperty(ref _isServerRunning, value);
        }

        // System Info
        private string _cpuUsage = "0%";
        public string CpuUsage
        {
            get => _cpuUsage;
            set => SetProperty(ref _cpuUsage, value);
        }

        private string _memoryUsage = "0 MB";
        public string MemoryUsage
        {
            get => _memoryUsage;
            set => SetProperty(ref _memoryUsage, value);
        }

        private string _uptimeDisplay = "0h 0m";
        public string UptimeDisplay
        {
            get => _uptimeDisplay;
            set => SetProperty(ref _uptimeDisplay, value);
        }

        // Recent Notifications
        public ObservableCollection<Services.ToastNotification> RecentNotifications =>
            Services.ToastNotificationService.Instance.NotificationHistory;

        // Quick Stats
        private int _undoCount;
        public int UndoCount
        {
            get => _undoCount;
            set => SetProperty(ref _undoCount, value);
        }

        // Widget Visibility
        private bool _showQuickActions = true;
        public bool ShowQuickActions
        {
            get => _showQuickActions;
            set { if (SetProperty(ref _showQuickActions, value)) SaveWidgetPrefs(); }
        }

        private bool _showNowPlaying = true;
        public bool ShowNowPlaying
        {
            get => _showNowPlaying;
            set { if (SetProperty(ref _showNowPlaying, value)) SaveWidgetPrefs(); }
        }

        private bool _showDiskUsage = true;
        public bool ShowDiskUsage
        {
            get => _showDiskUsage;
            set { if (SetProperty(ref _showDiskUsage, value)) SaveWidgetPrefs(); }
        }

        private bool _showSystemStatus = true;
        public bool ShowSystemStatus
        {
            get => _showSystemStatus;
            set { if (SetProperty(ref _showSystemStatus, value)) SaveWidgetPrefs(); }
        }

        private bool _showRemoteServer = true;
        public bool ShowRemoteServer
        {
            get => _showRemoteServer;
            set { if (SetProperty(ref _showRemoteServer, value)) SaveWidgetPrefs(); }
        }

        private bool _showUndoStack = true;
        public bool ShowUndoStack
        {
            get => _showUndoStack;
            set { if (SetProperty(ref _showUndoStack, value)) SaveWidgetPrefs(); }
        }

        private bool _showKeyboardShortcuts = true;
        public bool ShowKeyboardShortcuts
        {
            get => _showKeyboardShortcuts;
            set { if (SetProperty(ref _showKeyboardShortcuts, value)) SaveWidgetPrefs(); }
        }

        private bool _isCustomizePanelOpen;
        public bool IsCustomizePanelOpen
        {
            get => _isCustomizePanelOpen;
            set => SetProperty(ref _isCustomizePanelOpen, value);
        }

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand OpenCommandPaletteCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }

        #endregion

        #region Methods

        private async Task RefreshAsync()
        {
            try
            {
                // Greeting
                var hour = DateTime.Now.Hour;
                Greeting = hour < 12 ? "☀️ Good Morning" : hour < 17 ? "🌤️ Good Afternoon" : "🌙 Good Evening";
                CurrentTime = DateTime.Now.ToString("dddd, MMMM d, yyyy  HH:mm");

                // Version
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                AppVersion = $"v{asm.GetName().Version}";

                // Disk Usage
                await Task.Run(() =>
                {
                    var drives = DriveInfo.GetDrives()
                        .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
                        .Select(d => new DiskInfoItem
                        {
                            DriveName = d.Name,
                            Label = d.VolumeLabel,
                            TotalGB = d.TotalSize / (1024.0 * 1024 * 1024),
                            FreeGB = d.AvailableFreeSpace / (1024.0 * 1024 * 1024),
                            UsedPercent = (1.0 - ((double)d.AvailableFreeSpace / d.TotalSize)) * 100
                        })
                        .ToList();

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        DiskUsage.Clear();
                        foreach (var d in drives)
                            DiskUsage.Add(d);
                    });
                });

                // Now Playing
                var player = Services.EnhancedAudioPlayerService.Instance;
                if (player?.CurrentTrack != null)
                {
                    NowPlayingTitle = player.CurrentTrack.Title ?? "Unknown";
                    NowPlayingArtist = player.CurrentTrack.Artist ?? "Unknown Artist";
                    IsPlaying = player.IsPlaying;
                }
                else
                {
                    NowPlayingTitle = "No track playing";
                    NowPlayingArtist = "";
                    IsPlaying = false;
                }

                // Server status
                var server = Services.RemoteServer.PlatypusRemoteServer.Current;
                IsServerRunning = server?.IsRunning ?? false;
                ServerStatus = IsServerRunning ? $"Running on port {Services.SettingsManager.Current.RemoteServerPort}" : "Stopped";

                // System
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                var perfMon = Services.PerformanceMonitorService.Instance;
                CpuUsage = $"{perfMon.CpuPercent:F0}%";
                MemoryUsage = $"{proc.WorkingSet64 / (1024 * 1024)} MB";
                var uptime = DateTime.Now - proc.StartTime;
                UptimeDisplay = $"{(int)uptime.TotalHours}h {uptime.Minutes}m";

                // Undo
                UndoCount = Services.UndoRedoService.Instance.UndoCount;
            }
            catch
            {
                // Dashboard refresh should never crash
            }
        }

        private void OpenSettings()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                {
                    var settings = new Views.SettingsWindow { Owner = mainWindow };
                    settings.ShowDialog();
                }
            }
            catch { }
        }

        private void OpenCommandPalette()
        {
            try
            {
                var mainWindow = System.Windows.Application.Current?.MainWindow;
                if (mainWindow != null)
                    Services.CommandService.Instance.ShowCommandPalette(mainWindow);
            }
            catch { }
        }

        private async Task CheckForUpdatesAsync()
        {
            var update = await Services.UpdateService.Instance.CheckForUpdatesAsync();
            if (update != null)
            {
                Services.ToastNotificationService.Instance.ShowInfo(
                    $"Update {update.Version} available!", "Update Check");
            }
            else
            {
                Services.ToastNotificationService.Instance.ShowSuccess(
                    "You're running the latest version.", "Update Check");
            }
        }

        private void SaveWidgetPrefs()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var folder = Path.Combine(appData, "PlatypusTools");
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, "dashboard_widgets.json");
                var json = System.Text.Json.JsonSerializer.Serialize(new
                {
                    ShowQuickActions,
                    ShowNowPlaying,
                    ShowDiskUsage,
                    ShowSystemStatus,
                    ShowRemoteServer,
                    ShowUndoStack,
                    ShowKeyboardShortcuts
                });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving widget prefs: {ex.Message}");
            }
        }

        private void LoadWidgetPrefs()
        {
            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var path = Path.Combine(appData, "PlatypusTools", "dashboard_widgets.json");
                if (!File.Exists(path)) return;
                var json = File.ReadAllText(path);
                var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("ShowQuickActions", out var v1)) _showQuickActions = v1.GetBoolean();
                if (root.TryGetProperty("ShowNowPlaying", out var v2)) _showNowPlaying = v2.GetBoolean();
                if (root.TryGetProperty("ShowDiskUsage", out var v3)) _showDiskUsage = v3.GetBoolean();
                if (root.TryGetProperty("ShowSystemStatus", out var v4)) _showSystemStatus = v4.GetBoolean();
                if (root.TryGetProperty("ShowRemoteServer", out var v5)) _showRemoteServer = v5.GetBoolean();
                if (root.TryGetProperty("ShowUndoStack", out var v6)) _showUndoStack = v6.GetBoolean();
                if (root.TryGetProperty("ShowKeyboardShortcuts", out var v7)) _showKeyboardShortcuts = v7.GetBoolean();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading widget prefs: {ex.Message}");
            }
        }

        #endregion
    }

    public class DiskInfoItem
    {
        public string DriveName { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public double TotalGB { get; set; }
        public double FreeGB { get; set; }
        public double UsedPercent { get; set; }
        public string DisplayText => $"{DriveName} {Label}  —  {FreeGB:F1} GB free / {TotalGB:F1} GB ({UsedPercent:F0}% used)";
    }
}
