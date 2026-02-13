using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;

namespace PlatypusTools.UI.Services;

/// <summary>
/// Manages the system tray icon for PlatypusTools.
/// Provides minimize to tray, playback controls, and quick access features.
/// </summary>
public class SystemTrayService : IDisposable
{
    private static SystemTrayService? _instance;
    public static SystemTrayService Instance => _instance ??= new SystemTrayService();

    private TaskbarIcon? _trayIcon;
    private Window? _mainWindow;
    private bool _isDisposed;

    public bool IsMinimizedToTray { get; private set; }

    /// <summary>
    /// Event fired when user requests to show the main window from tray.
    /// </summary>
    public event EventHandler? ShowWindowRequested;

    /// <summary>
    /// Event fired when user requests to exit from tray.
    /// </summary>
    public event EventHandler? ExitRequested;

    private SystemTrayService()
    {
    }

    /// <summary>
    /// Initializes the system tray icon.
    /// </summary>
    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;

        try
        {
            _trayIcon = new TaskbarIcon
            {
                ToolTipText = "PlatypusTools",
                Visibility = Visibility.Collapsed // Hidden by default, show when minimized
            };

            // Try to load the icon from resources
            try
            {
                var iconUri = new Uri("pack://application:,,,/Assets/platypus.png");
                var iconStream = System.Windows.Application.GetResourceStream(iconUri);
                if (iconStream != null)
                {
                    using var bitmap = new Bitmap(iconStream.Stream);
                    _trayIcon.Icon = Icon.FromHandle(bitmap.GetHicon());
                }
            }
            catch
            {
                // Use default icon if custom icon fails
                _trayIcon.Icon = SystemIcons.Application;
            }

            // Create context menu
            _trayIcon.ContextMenu = CreateContextMenu();

            // Double-click to show window
            _trayIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();

            PlatypusTools.Core.Services.SimpleLogger.Info("System tray initialized");
        }
        catch (Exception ex)
        {
            PlatypusTools.Core.Services.SimpleLogger.Error($"Failed to initialize system tray: {ex.Message}");
        }
    }

    private ContextMenu CreateContextMenu()
    {
        var menu = new ContextMenu();

        // Show/Hide
        var showItem = new MenuItem { Header = "🦆 Show PlatypusTools" };
        showItem.Click += (s, e) => ShowMainWindow();
        menu.Items.Add(showItem);

        menu.Items.Add(new Separator());

        // Audio Player controls (when audio is available)
        var playPauseItem = new MenuItem { Header = "⏯️ Play/Pause" };
        playPauseItem.Click += (s, e) => EnhancedAudioPlayerService.Instance?.PlayPause();
        menu.Items.Add(playPauseItem);

        var nextItem = new MenuItem { Header = "⏭️ Next Track" };
        nextItem.Click += async (s, e) => await (EnhancedAudioPlayerService.Instance?.NextAsync() ?? Task.CompletedTask);
        menu.Items.Add(nextItem);

        var prevItem = new MenuItem { Header = "⏮️ Previous Track" };
        prevItem.Click += async (s, e) => await (EnhancedAudioPlayerService.Instance?.PreviousAsync() ?? Task.CompletedTask);
        menu.Items.Add(prevItem);

        menu.Items.Add(new Separator());

        // Remote server toggle
        var remoteItem = new MenuItem { Header = "🌐 Remote Control" };
        var enableRemoteItem = new MenuItem { Header = "Enable Server" };
        enableRemoteItem.Click += async (s, e) =>
        {
            var server = RemoteServer.PlatypusRemoteServer.Current;
            if (server != null && !server.IsRunning)
            {
                await server.StartAsync();
                UpdateRemoteMenuItem(menu);
            }
        };
        var disableRemoteItem = new MenuItem { Header = "Disable Server" };
        disableRemoteItem.Click += async (s, e) =>
        {
            var server = RemoteServer.PlatypusRemoteServer.Current;
            if (server != null && server.IsRunning)
            {
                await server.StopAsync();
                UpdateRemoteMenuItem(menu);
            }
        };
        remoteItem.Items.Add(enableRemoteItem);
        remoteItem.Items.Add(disableRemoteItem);
        menu.Items.Add(remoteItem);

        menu.Items.Add(new Separator());

        // Exit
        var exitItem = new MenuItem { Header = "❌ Exit" };
        exitItem.Click += (s, e) =>
        {
            ExitRequested?.Invoke(this, EventArgs.Empty);
        };
        menu.Items.Add(exitItem);

        // Update menu items when opening
        menu.Opened += (s, e) => UpdateMenuItems(menu);

        return menu;
    }

    private void UpdateMenuItems(ContextMenu menu)
    {
        var player = EnhancedAudioPlayerService.Instance;
        var isPlaying = player?.IsPlaying ?? false;
        var hasTrack = player?.CurrentTrack != null;

        // Update tooltip with current track
        if (hasTrack && player?.CurrentTrack != null)
        {
            var track = player.CurrentTrack;
            _trayIcon!.ToolTipText = $"🦆 {track.Title ?? "Unknown"}\n{track.Artist ?? "Unknown Artist"}";
        }
        else
        {
            _trayIcon!.ToolTipText = "PlatypusTools";
        }
    }

    private void UpdateRemoteMenuItem(ContextMenu menu)
    {
        // Remote item is at index 6 (after separator)
        if (menu.Items.Count > 6 && menu.Items[6] is MenuItem remoteItem)
        {
            var server = RemoteServer.PlatypusRemoteServer.Current;
            var isRunning = server?.IsRunning ?? false;

            if (remoteItem.Items.Count >= 2)
            {
                if (remoteItem.Items[0] is MenuItem enableItem)
                    enableItem.IsEnabled = !isRunning;
                if (remoteItem.Items[1] is MenuItem disableItem)
                    disableItem.IsEnabled = isRunning;
            }
        }
    }

    /// <summary>
    /// Minimizes the main window to the system tray.
    /// </summary>
    public void MinimizeToTray()
    {
        if (_mainWindow == null || _trayIcon == null) return;

        IsMinimizedToTray = true;
        _mainWindow.Hide();
        _trayIcon.Visibility = Visibility.Visible;

        // Show balloon tip
        _trayIcon.ShowBalloonTip(
            "PlatypusTools",
            "Application minimized to system tray. Double-click to restore.",
            BalloonIcon.Info);

        PlatypusTools.Core.Services.SimpleLogger.Debug("Minimized to system tray");
    }

    /// <summary>
    /// Shows the main window from the system tray.
    /// </summary>
    public void ShowMainWindow()
    {
        if (_mainWindow == null || _trayIcon == null) return;

        IsMinimizedToTray = false;
        _trayIcon.Visibility = Visibility.Collapsed;
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();

        ShowWindowRequested?.Invoke(this, EventArgs.Empty);

        PlatypusTools.Core.Services.SimpleLogger.Debug("Restored from system tray");
    }

    /// <summary>
    /// Updates the tray icon tooltip with current track info.
    /// </summary>
    public void UpdateTrackInfo(string? title, string? artist)
    {
        if (_trayIcon == null) return;

        if (!string.IsNullOrEmpty(title))
        {
            _trayIcon.ToolTipText = $"🦆 {title}\n{artist ?? "Unknown Artist"}";
        }
        else
        {
            _trayIcon.ToolTipText = "PlatypusTools";
        }
    }

    /// <summary>
    /// Shows a notification balloon from the tray.
    /// </summary>
    public void ShowNotification(string title, string message, BalloonIcon icon = BalloonIcon.Info)
    {
        _trayIcon?.ShowBalloonTip(title, message, icon);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _trayIcon?.Dispose();
        _trayIcon = null;
        _instance = null;
    }
}
