using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.UI.Services;
using PlatypusTools.UI.Services.RemoteServer;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// ViewModel for the Remote Management Dashboard — monitors remote server,
/// connected clients, audit logs, Cloudflare tunnel, and network status.
/// </summary>
public class RemoteDashboardViewModel : BindableBase
{
    #region Fields

    private bool _isServerRunning;
    private string _serverUrl = string.Empty;
    private int _serverPort = 47392;
    private string _serverStatus = "Stopped";
    private string _statusMessage = string.Empty;
    private bool _isTunnelRunning;
    private string _tunnelUrl = string.Empty;
    private string _tunnelStatus = "Not Connected";
    private string _localIpAddress = string.Empty;
    private string _tailscaleIp = string.Empty;
    private bool _isTailscaleConnected;
    private bool _isIpAllowlistEnabled;
    private string _newIpEntry = string.Empty;
    private int _totalConnections;
    private int _totalActions;
    private int _uniqueClients;
    private string _selectedLogFilter = "All";
    private bool _isLoading;
    private string _qrCodeData = string.Empty;

    #endregion

    #region Properties

    public bool IsServerRunning
    {
        get => _isServerRunning;
        set { SetProperty(ref _isServerRunning, value); RaisePropertyChanged(nameof(ServerToggleText)); }
    }

    public string ServerUrl
    {
        get => _serverUrl;
        set => SetProperty(ref _serverUrl, value);
    }

    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value);
    }

    public string ServerStatus
    {
        get => _serverStatus;
        set => SetProperty(ref _serverStatus, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ServerToggleText => IsServerRunning ? "Stop Server" : "Start Server";

    public bool IsTunnelRunning
    {
        get => _isTunnelRunning;
        set { SetProperty(ref _isTunnelRunning, value); RaisePropertyChanged(nameof(TunnelToggleText)); }
    }

    public string TunnelUrl
    {
        get => _tunnelUrl;
        set => SetProperty(ref _tunnelUrl, value);
    }

    public string TunnelStatus
    {
        get => _tunnelStatus;
        set => SetProperty(ref _tunnelStatus, value);
    }

    public string TunnelToggleText => IsTunnelRunning ? "Stop Tunnel" : "Start Quick Tunnel";

    public string LocalIpAddress
    {
        get => _localIpAddress;
        set => SetProperty(ref _localIpAddress, value);
    }

    public string TailscaleIp
    {
        get => _tailscaleIp;
        set => SetProperty(ref _tailscaleIp, value);
    }

    public bool IsTailscaleConnected
    {
        get => _isTailscaleConnected;
        set => SetProperty(ref _isTailscaleConnected, value);
    }

    public bool IsIpAllowlistEnabled
    {
        get => _isIpAllowlistEnabled;
        set
        {
            if (SetProperty(ref _isIpAllowlistEnabled, value))
            {
                var server = PlatypusRemoteServer.Current;
                if (server != null)
                    server.IpAllowlistEnabled = value;
            }
        }
    }

    public string NewIpEntry
    {
        get => _newIpEntry;
        set => SetProperty(ref _newIpEntry, value);
    }

    public int TotalConnections
    {
        get => _totalConnections;
        set => SetProperty(ref _totalConnections, value);
    }

    public int TotalActions
    {
        get => _totalActions;
        set => SetProperty(ref _totalActions, value);
    }

    public int UniqueClients
    {
        get => _uniqueClients;
        set => SetProperty(ref _uniqueClients, value);
    }

    public string SelectedLogFilter
    {
        get => _selectedLogFilter;
        set
        {
            if (SetProperty(ref _selectedLogFilter, value))
                FilterLogs();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string QrCodeData
    {
        get => _qrCodeData;
        set => SetProperty(ref _qrCodeData, value);
    }

    public ObservableCollection<RemoteClientInfo> ConnectedClients { get; } = new();
    public ObservableCollection<AuditLogEntry> AuditLogEntries { get; } = new();
    public ObservableCollection<AuditLogEntry> FilteredLogEntries { get; } = new();
    public ObservableCollection<string> IpAllowlist { get; } = new();
    public ObservableCollection<string> ServerLogs { get; } = new();
    public ObservableCollection<string> LogFilterOptions { get; } = new()
    {
        "All", "Connected", "Disconnected", "Play", "Pause", "Stop",
        "Next", "Previous", "Seek", "Volume", "Queue"
    };

    #endregion

    #region Commands

    public ICommand ToggleServerCommand { get; }
    public ICommand ToggleTunnelCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CopyServerUrlCommand { get; }
    public ICommand CopyTunnelUrlCommand { get; }
    public ICommand CopyLocalIpCommand { get; }
    public ICommand AddIpCommand { get; }
    public ICommand RemoveIpCommand { get; }
    public ICommand ClearLogsCommand { get; }
    public ICommand ExportLogsCommand { get; }
    public ICommand CleanupOldLogsCommand { get; }
    public ICommand CopyLogEntryCommand { get; }

    #endregion

    public RemoteDashboardViewModel()
    {
        ToggleServerCommand = new RelayCommand(_ => ToggleServer());
        ToggleTunnelCommand = new RelayCommand(_ => ToggleTunnel());
        RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
        CopyServerUrlCommand = new RelayCommand(_ => CopyToClipboard(ServerUrl));
        CopyTunnelUrlCommand = new RelayCommand(_ => CopyToClipboard(TunnelUrl), _ => !string.IsNullOrEmpty(TunnelUrl));
        CopyLocalIpCommand = new RelayCommand(_ => CopyToClipboard($"https://{LocalIpAddress}:{ServerPort}"));
        AddIpCommand = new RelayCommand(_ => AddIpToAllowlist(), _ => !string.IsNullOrEmpty(NewIpEntry));
        RemoveIpCommand = new RelayCommand(p => RemoveIpFromAllowlist(p as string));
        ClearLogsCommand = new RelayCommand(_ => ClearLogs());
        ExportLogsCommand = new RelayCommand(async _ => await ExportLogsAsync());
        CleanupOldLogsCommand = new RelayCommand(_ => CleanupOldLogs());
        CopyLogEntryCommand = new RelayCommand(p => CopyLogEntry(p as AuditLogEntry));

        // Detect network info
        DetectNetworkInfo();

        // Subscribe to server events
        SubscribeToEvents();

        // Initial load
        _ = RefreshAsync();
    }

    #region Server Control

    private async void ToggleServer()
    {
        try
        {
            IsLoading = true;
            StatusMessage = string.Empty;

            if (IsServerRunning)
            {
                // Stop server
                var server = PlatypusRemoteServer.Current;
                if (server != null)
                {
                    await server.StopAsync();
                    StatusMessage = "Server stopped";
                }
            }
            else
            {
                // Start server — stored in static Current, lives for app lifetime
#pragma warning disable CA2000
                var server = PlatypusRemoteServer.Current ?? new PlatypusRemoteServer(ServerPort);
#pragma warning restore CA2000
                PlatypusRemoteServer.Current = server;

                // Subscribe to events
                SubscribeToServerEvents(server);

                await server.StartAsync();
                StatusMessage = "Server started";
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async void ToggleTunnel()
    {
        try
        {
            IsLoading = true;
            StatusMessage = string.Empty;

            var tunnel = CloudflareTunnelService.Instance;

            if (IsTunnelRunning)
            {
                tunnel.Stop();
                TunnelStatus = "Stopped";
                TunnelUrl = string.Empty;
                IsTunnelRunning = false;
                StatusMessage = "Tunnel stopped";
            }
            else
            {
                if (!tunnel.IsInstalled)
                {
                    StatusMessage = "Installing Cloudflare Tunnel...";
                    var installed = await tunnel.InstallAsync(new Progress<string>(s => StatusMessage = s));
                    if (!installed)
                    {
                        StatusMessage = "Failed to install cloudflared";
                        return;
                    }
                }

                StatusMessage = "Starting Quick Tunnel...";
                var started = await tunnel.StartQuickTunnelAsync(ServerPort);
                if (started)
                {
                    IsTunnelRunning = true;
                    TunnelStatus = "Connecting...";
                    StatusMessage = "Tunnel starting — URL will appear when ready";
                    // URL arrives via TunnelUrlGenerated event
                }
                else
                {
                    StatusMessage = "Failed to start tunnel";
                }
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Tunnel error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    #endregion

    #region Refresh & Data

    private async Task RefreshAsync()
    {
        try
        {
            IsLoading = true;

            // Server status
            var server = PlatypusRemoteServer.Current;
            if (server != null)
            {
                IsServerRunning = server.IsRunning;
                ServerPort = server.Port;
                ServerUrl = server.ServerUrl;
                ServerStatus = server.IsRunning ? "Running" : "Stopped";
                IsIpAllowlistEnabled = server.IpAllowlistEnabled;

                // IP allowlist
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    IpAllowlist.Clear();
                    foreach (var ip in server.IpAllowlist)
                        IpAllowlist.Add(ip);
                });
            }
            else
            {
                IsServerRunning = false;
                ServerStatus = "Not Created";
                ServerUrl = $"https://localhost:{ServerPort}";
            }

            // Tunnel status
            var tunnel = CloudflareTunnelService.Instance;
            IsTunnelRunning = tunnel.IsRunning;
            TunnelUrl = tunnel.CurrentTunnelUrl ?? string.Empty;
            TunnelStatus = tunnel.IsRunning ? "Connected" : "Not Connected";

            // Tailscale
            DetectTailscale();

            // Audit log summary
            await LoadAuditSummaryAsync();

            // Recent audit entries
            LoadRecentAuditEntries();

            // Update QR code data
            if (IsServerRunning && !string.IsNullOrEmpty(LocalIpAddress))
                QrCodeData = $"https://{LocalIpAddress}:{ServerPort}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadAuditSummaryAsync()
    {
        try
        {
            var audit = RemoteAuditLogService.Instance;
            var summary = await audit.GetSummaryAsync(DateTime.UtcNow.AddDays(-7), DateTime.UtcNow);
            TotalActions = summary.TotalActions;
            UniqueClients = summary.UniqueClients.Count;
            TotalConnections = summary.ActionCounts.TryGetValue("Connected", out var c) ? c : 0;
        }
        catch { }
    }

    private void LoadRecentAuditEntries()
    {
        try
        {
            var audit = RemoteAuditLogService.Instance;
            var entries = audit.RecentEntries;

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                AuditLogEntries.Clear();
                foreach (var entry in entries.OrderByDescending(e => e.Timestamp).Take(200))
                    AuditLogEntries.Add(entry);

                FilterLogs();
            });
        }
        catch { }
    }

    private void FilterLogs()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            FilteredLogEntries.Clear();
            var filtered = SelectedLogFilter == "All"
                ? AuditLogEntries
                : new ObservableCollection<AuditLogEntry>(
                    AuditLogEntries.Where(e =>
                        e.Action.Contains(SelectedLogFilter, StringComparison.OrdinalIgnoreCase)));

            foreach (var entry in filtered)
                FilteredLogEntries.Add(entry);
        });
    }

    #endregion

    #region Network Detection

    private void DetectNetworkInfo()
    {
        try
        {
            // Get local IP
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;

                foreach (var ip in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        LocalIpAddress = ip.Address.ToString();
                        return;
                    }
                }
            }
        }
        catch
        {
            LocalIpAddress = "Unknown";
        }
    }

    private void DetectTailscale()
    {
        try
        {
            var ts = PlatypusTools.Core.Utilities.TailscaleHelper.GetStatus();
            IsTailscaleConnected = ts.IsInstalled && ts.IsConnected;
            TailscaleIp = ts.TailscaleIp ?? string.Empty;
        }
        catch
        {
            IsTailscaleConnected = false;
            TailscaleIp = string.Empty;
        }
    }

    #endregion

    #region IP Allowlist

    private void AddIpToAllowlist()
    {
        var ip = NewIpEntry?.Trim();
        if (string.IsNullOrEmpty(ip)) return;

        // Validate IP/CIDR
        if (!IsValidIpOrCidr(ip))
        {
            StatusMessage = "Invalid IP address or CIDR notation";
            return;
        }

        var server = PlatypusRemoteServer.Current;
        if (server != null)
        {
            server.AddToAllowlist(ip);
            IpAllowlist.Add(ip);
            NewIpEntry = string.Empty;
            StatusMessage = $"Added {ip} to allowlist";
        }
    }

    private void RemoveIpFromAllowlist(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return;

        var server = PlatypusRemoteServer.Current;
        if (server != null)
        {
            server.RemoveFromAllowlist(ip);
            IpAllowlist.Remove(ip);
            StatusMessage = $"Removed {ip} from allowlist";
        }
    }

    private static bool IsValidIpOrCidr(string entry)
    {
        // Check for CIDR notation
        if (entry.Contains('/'))
        {
            var parts = entry.Split('/');
            if (parts.Length != 2) return false;
            if (!IPAddress.TryParse(parts[0], out _)) return false;
            if (!int.TryParse(parts[1], out var prefix) || prefix < 0 || prefix > 32) return false;
            return true;
        }

        return IPAddress.TryParse(entry, out _);
    }

    #endregion

    #region Log Management

    private void ClearLogs()
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            AuditLogEntries.Clear();
            FilteredLogEntries.Clear();
            ServerLogs.Clear();
        });
        StatusMessage = "Logs cleared from view";
    }

    private async Task ExportLogsAsync()
    {
        try
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = $"PlatypusRemote_AuditLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                Title = "Export Audit Logs"
            };

            if (dialog.ShowDialog() == true)
            {
                var lines = AuditLogEntries
                    .OrderBy(e => e.Timestamp)
                    .Select(e => e.ToString());

                await System.IO.File.WriteAllLinesAsync(dialog.FileName, lines);
                StatusMessage = $"Exported {AuditLogEntries.Count} entries to {System.IO.Path.GetFileName(dialog.FileName)}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export error: {ex.Message}";
        }
    }

    private void CleanupOldLogs()
    {
        try
        {
            var deleted = RemoteAuditLogService.Instance.CleanupOldLogs(30);
            StatusMessage = $"Cleaned up {deleted} old log file(s)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Cleanup error: {ex.Message}";
        }
    }

    private void CopyLogEntry(AuditLogEntry? entry)
    {
        if (entry == null) return;
        CopyToClipboard(entry.ToString());
    }

    #endregion

    #region Event Subscriptions

    private void SubscribeToEvents()
    {
        var server = PlatypusRemoteServer.Current;
        if (server != null)
            SubscribeToServerEvents(server);

        // Audit log
        RemoteAuditLogService.Instance.EntryLogged += OnAuditEntryLogged;

        // Tunnel
        var tunnel = CloudflareTunnelService.Instance;
        tunnel.TunnelStateChanged += (_, running) =>
        {
            IsTunnelRunning = running;
            TunnelStatus = running ? "Connected" : "Not Connected";
        };
        tunnel.TunnelUrlGenerated += (_, url) =>
        {
            TunnelUrl = url;
            QrCodeData = url;
        };
    }

    private void SubscribeToServerEvents(PlatypusRemoteServer server)
    {
        server.LogMessage += (_, msg) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ServerLogs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {msg}");
                while (ServerLogs.Count > 500) ServerLogs.RemoveAt(ServerLogs.Count - 1);
            });
        };

        server.ServerStateChanged += (_, running) =>
        {
            IsServerRunning = running;
            ServerStatus = running ? "Running" : "Stopped";
        };

        server.ClientConnected += (_, client) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                ConnectedClients.Add(client);
            });
        };

        server.ClientDisconnected += (_, connectionId) =>
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                var client = ConnectedClients.FirstOrDefault(c => c.ConnectionId == connectionId);
                if (client != null) ConnectedClients.Remove(client);
            });
        };
    }

    private void OnAuditEntryLogged(object? sender, AuditLogEntry entry)
    {
        Application.Current?.Dispatcher?.Invoke(() =>
        {
            AuditLogEntries.Insert(0, entry);
            while (AuditLogEntries.Count > 200)
                AuditLogEntries.RemoveAt(AuditLogEntries.Count - 1);

            if (SelectedLogFilter == "All" ||
                entry.Action.Contains(SelectedLogFilter, StringComparison.OrdinalIgnoreCase))
            {
                FilteredLogEntries.Insert(0, entry);
            }

            // Update stats
            TotalActions++;
            if (entry.Action == "Connected") TotalConnections++;
        });
    }

    #endregion

    #region Helpers

    private static void CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { Clipboard.SetText(text); }
        catch { }
    }

    #endregion
}
