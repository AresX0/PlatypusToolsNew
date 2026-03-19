using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class NetworkConnectionViewModel : BindableBase
    {
        private string _protocol = string.Empty;
        public string Protocol { get => _protocol; set => SetProperty(ref _protocol, value); }

        private string _localAddress = string.Empty;
        public string LocalAddress { get => _localAddress; set => SetProperty(ref _localAddress, value); }

        private string _remoteAddress = string.Empty;
        public string RemoteAddress { get => _remoteAddress; set => SetProperty(ref _remoteAddress, value); }

        private string _state = string.Empty;
        public string State { get => _state; set => SetProperty(ref _state, value); }

        private int _pid;
        public int PID { get => _pid; set => SetProperty(ref _pid, value); }
    }

    public class NetworkAdapterViewModel : BindableBase
    {
        private string _name = string.Empty;
        public string Name { get => _name; set => SetProperty(ref _name, value); }

        private string _description = string.Empty;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private string _ipAddress = string.Empty;
        public string IPAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }

        private string _macAddress = string.Empty;
        public string MACAddress { get => _macAddress; set => SetProperty(ref _macAddress, value); }

        private string _status = string.Empty;
        public string Status { get => _status; set => SetProperty(ref _status, value); }

        private long _bytesReceived;
        public long BytesReceived { get => _bytesReceived; set => SetProperty(ref _bytesReceived, value); }

        private long _bytesSent;
        public long BytesSent { get => _bytesSent; set => SetProperty(ref _bytesSent, value); }
    }

    /// <summary>
    /// Represents a port scan result for a single port.
    /// </summary>
    public class PortScanResultViewModel : BindableBase
    {
        private int _port;
        public int Port { get => _port; set => SetProperty(ref _port, value); }

        private string _state = string.Empty;
        public string State { get => _state; set => SetProperty(ref _state, value); }

        private string _service = string.Empty;
        public string Service { get => _service; set => SetProperty(ref _service, value); }

        private string _banner = string.Empty;
        public string Banner { get => _banner; set => SetProperty(ref _banner, value); }
    }

    /// <summary>
    /// Represents a discovered device on the network.
    /// </summary>
    public class DiscoveredDeviceViewModel : BindableBase
    {
        private string _ipAddress = string.Empty;
        public string IPAddress { get => _ipAddress; set => SetProperty(ref _ipAddress, value); }

        private string _hostname = string.Empty;
        public string Hostname { get => _hostname; set => SetProperty(ref _hostname, value); }

        private string _macAddress = string.Empty;
        public string MACAddress { get => _macAddress; set => SetProperty(ref _macAddress, value); }

        private string _responseTime = string.Empty;
        public string ResponseTime { get => _responseTime; set => SetProperty(ref _responseTime, value); }

        private string _status = string.Empty;
        public string Status { get => _status; set => SetProperty(ref _status, value); }
    }

    /// <summary>
    /// Represents real-time bandwidth stats for an adapter.
    /// </summary>
    public class BandwidthEntryViewModel : BindableBase
    {
        private string _adapterName = string.Empty;
        public string AdapterName { get => _adapterName; set => SetProperty(ref _adapterName, value); }

        private string _downloadSpeed = string.Empty;
        public string DownloadSpeed { get => _downloadSpeed; set => SetProperty(ref _downloadSpeed, value); }

        private string _uploadSpeed = string.Empty;
        public string UploadSpeed { get => _uploadSpeed; set => SetProperty(ref _uploadSpeed, value); }

        private string _totalDownloaded = string.Empty;
        public string TotalDownloaded { get => _totalDownloaded; set => SetProperty(ref _totalDownloaded, value); }

        private string _totalUploaded = string.Empty;
        public string TotalUploaded { get => _totalUploaded; set => SetProperty(ref _totalUploaded, value); }

        private string _status = string.Empty;
        public string Status { get => _status; set => SetProperty(ref _status, value); }
    }

    public class NetworkToolsViewModel : BindableBase
    {
        private readonly NetworkToolsService _networkToolsService;
        private CancellationTokenSource? _portScanCts;
        private CancellationTokenSource? _discoveryCts;
        private CancellationTokenSource? _bandwidthCts;

        public NetworkToolsViewModel()
        {
            _networkToolsService = new NetworkToolsService();

            PingCommand = new RelayCommand(async _ => await PingAsync(), _ => !IsRunning && !string.IsNullOrWhiteSpace(TargetHost));
            TracerouteCommand = new RelayCommand(async _ => await TracerouteAsync(), _ => !IsRunning && !string.IsNullOrWhiteSpace(TargetHost));
            RefreshConnectionsCommand = new RelayCommand(async _ => await RefreshConnectionsAsync(), _ => !IsRunning);
            RefreshAdaptersCommand = new RelayCommand(async _ => await RefreshAdaptersAsync(), _ => !IsRunning);

            // Port Scanner
            PortScanCommand = new RelayCommand(async _ => await PortScanAsync(), _ => !IsRunning && !string.IsNullOrWhiteSpace(PortScanHost));
            CancelPortScanCommand = new RelayCommand(_ => _portScanCts?.Cancel(), _ => IsRunning);

            // DNS Lookup
            DnsLookupCommand = new RelayCommand(async _ => await DnsLookupAsync(), _ => !IsRunning && !string.IsNullOrWhiteSpace(DnsLookupHost));

            // Network Discovery
            NetworkDiscoveryCommand = new RelayCommand(async _ => await NetworkDiscoveryAsync(), _ => !IsRunning);
            CancelDiscoveryCommand = new RelayCommand(_ => _discoveryCts?.Cancel(), _ => IsRunning);

            // Bandwidth Monitor
            StartBandwidthMonitorCommand = new RelayCommand(_ => StartBandwidthMonitor(), _ => !IsBandwidthMonitoring);
            StopBandwidthMonitorCommand = new RelayCommand(_ => StopBandwidthMonitor(), _ => IsBandwidthMonitoring);

            // WHOIS
            WhoisLookupCommand = new RelayCommand(async _ => await WhoisLookupAsync(), _ => !IsRunning && !string.IsNullOrWhiteSpace(WhoisDomain));
        }

        public ObservableCollection<string> PingResults { get; } = new();
        public ObservableCollection<NetworkConnectionViewModel> Connections { get; } = new();
        public ObservableCollection<NetworkAdapterViewModel> Adapters { get; } = new();
        public ObservableCollection<PortScanResultViewModel> PortScanResults { get; } = new();
        public ObservableCollection<string> DnsResults { get; } = new();
        public ObservableCollection<DiscoveredDeviceViewModel> DiscoveredDevices { get; } = new();
        public ObservableCollection<BandwidthEntryViewModel> BandwidthEntries { get; } = new();
        public ObservableCollection<string> WhoisResults { get; } = new();

        private string _targetHost = string.Empty;
        public string TargetHost 
        { 
            get => _targetHost; 
            set 
            { 
                SetProperty(ref _targetHost, value); 
                ((RelayCommand)PingCommand).RaiseCanExecuteChanged();
                ((RelayCommand)TracerouteCommand).RaiseCanExecuteChanged();
            } 
        }

        private bool _isRunning;
        public bool IsRunning 
        { 
            get => _isRunning; 
            set 
            { 
                SetProperty(ref _isRunning, value); 
                ((RelayCommand)PingCommand).RaiseCanExecuteChanged();
                ((RelayCommand)TracerouteCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshConnectionsCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RefreshAdaptersCommand).RaiseCanExecuteChanged();
                ((RelayCommand)PortScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelPortScanCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DnsLookupCommand).RaiseCanExecuteChanged();
                ((RelayCommand)NetworkDiscoveryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelDiscoveryCommand).RaiseCanExecuteChanged();
                ((RelayCommand)WhoisLookupCommand).RaiseCanExecuteChanged();
            } 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _selectedTabIndex;
        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

        // Port Scanner properties
        private string _portScanHost = string.Empty;
        public string PortScanHost
        {
            get => _portScanHost;
            set { SetProperty(ref _portScanHost, value); ((RelayCommand)PortScanCommand).RaiseCanExecuteChanged(); }
        }

        private string _portRange = "1-1024";
        public string PortRange { get => _portRange; set => SetProperty(ref _portRange, value); }

        private int _portScanTimeout = 500;
        public int PortScanTimeout { get => _portScanTimeout; set => SetProperty(ref _portScanTimeout, value); }

        private int _portScanProgress;
        public int PortScanProgress { get => _portScanProgress; set => SetProperty(ref _portScanProgress, value); }

        private bool _showOpenOnly = true;
        public bool ShowOpenOnly { get => _showOpenOnly; set => SetProperty(ref _showOpenOnly, value); }

        // DNS Lookup properties
        private string _dnsLookupHost = string.Empty;
        public string DnsLookupHost
        {
            get => _dnsLookupHost;
            set { SetProperty(ref _dnsLookupHost, value); ((RelayCommand)DnsLookupCommand).RaiseCanExecuteChanged(); }
        }

        // Network Discovery properties
        private string _discoverySubnet = string.Empty;
        public string DiscoverySubnet { get => _discoverySubnet; set => SetProperty(ref _discoverySubnet, value); }

        private int _discoveryProgress;
        public int DiscoveryProgress { get => _discoveryProgress; set => SetProperty(ref _discoveryProgress, value); }

        // Bandwidth Monitor properties
        private bool _isBandwidthMonitoring;
        public bool IsBandwidthMonitoring
        {
            get => _isBandwidthMonitoring;
            set
            {
                SetProperty(ref _isBandwidthMonitoring, value);
                ((RelayCommand)StartBandwidthMonitorCommand).RaiseCanExecuteChanged();
                ((RelayCommand)StopBandwidthMonitorCommand).RaiseCanExecuteChanged();
            }
        }

        // WHOIS properties
        private string _whoisDomain = string.Empty;
        public string WhoisDomain
        {
            get => _whoisDomain;
            set { SetProperty(ref _whoisDomain, value); ((RelayCommand)WhoisLookupCommand).RaiseCanExecuteChanged(); }
        }

        public ICommand PingCommand { get; }
        public ICommand TracerouteCommand { get; }
        public ICommand RefreshConnectionsCommand { get; }
        public ICommand RefreshAdaptersCommand { get; }
        public ICommand PortScanCommand { get; }
        public ICommand CancelPortScanCommand { get; }
        public ICommand DnsLookupCommand { get; }
        public ICommand NetworkDiscoveryCommand { get; }
        public ICommand CancelDiscoveryCommand { get; }
        public ICommand StartBandwidthMonitorCommand { get; }
        public ICommand StopBandwidthMonitorCommand { get; }
        public ICommand WhoisLookupCommand { get; }

        public async Task PingAsync()
        {
            if (string.IsNullOrWhiteSpace(TargetHost))
            {
                StatusMessage = "Please enter a target host";
                return;
            }

            IsRunning = true;
            StatusMessage = $"Pinging {TargetHost}...";
            PingResults.Clear();

            try
            {
                var result = await _networkToolsService.PingHost(TargetHost, 4);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (result.Success)
                    {
                        PingResults.Add($"Reply from {result.Address}: time={result.RoundtripTime}ms TTL={result.Ttl}");
                    }
                    else
                    {
                        PingResults.Add($"Ping failed: {result.ErrorMessage ?? result.Status.ToString()}");
                    }
                });

                StatusMessage = "Ping complete";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        public async Task TracerouteAsync()
        {
            if (string.IsNullOrWhiteSpace(TargetHost))
            {
                StatusMessage = "Please enter a target host";
                return;
            }

            IsRunning = true;
            StatusMessage = $"Tracing route to {TargetHost}...";
            PingResults.Clear();

            try
            {
                var results = await _networkToolsService.TracerouteHost(TargetHost);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var result in results)
                    {
                        PingResults.Add(result);
                    }
                });

                StatusMessage = "Traceroute complete";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        public async Task RefreshConnectionsAsync()
        {
            IsRunning = true;
            StatusMessage = "Loading network connections...";
            Connections.Clear();

            try
            {
                var connections = await _networkToolsService.GetActiveConnections();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var conn in connections)
                    {
                        Connections.Add(new NetworkConnectionViewModel
                        {
                            Protocol = conn.Protocol,
                            LocalAddress = conn.LocalAddress,
                            RemoteAddress = conn.RemoteAddress,
                            State = conn.State,
                            PID = conn.ProcessId ?? 0
                        });
                    }
                });

                StatusMessage = $"Loaded {Connections.Count} connections";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        public async Task RefreshAdaptersAsync()
        {
            IsRunning = true;
            StatusMessage = "Loading network adapters...";
            Adapters.Clear();

            try
            {
                var adapters = await _networkToolsService.GetNetworkAdapters();
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var adapter in adapters)
                    {
                        Adapters.Add(new NetworkAdapterViewModel
                        {
                            Name = adapter.Name,
                            Description = adapter.Description,
                            IPAddress = adapter.IpAddress ?? "N/A",
                            MACAddress = adapter.MacAddress ?? "N/A",
                            Status = adapter.IsUp ? "Up" : "Down",
                            BytesReceived = adapter.BytesReceived,
                            BytesSent = adapter.BytesSent
                        });
                    }
                });

                StatusMessage = $"Loaded {Adapters.Count} adapters";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        #region Port Scanner

        private static readonly Dictionary<int, string> WellKnownPorts = new()
        {
            {20, "FTP-Data"}, {21, "FTP"}, {22, "SSH"}, {23, "Telnet"}, {25, "SMTP"},
            {53, "DNS"}, {80, "HTTP"}, {110, "POP3"}, {111, "RPC"}, {135, "MSRPC"},
            {139, "NetBIOS"}, {143, "IMAP"}, {443, "HTTPS"}, {445, "SMB"}, {993, "IMAPS"},
            {995, "POP3S"}, {1433, "MSSQL"}, {1521, "Oracle"}, {3306, "MySQL"}, {3389, "RDP"},
            {5432, "PostgreSQL"}, {5900, "VNC"}, {6379, "Redis"}, {8080, "HTTP-Alt"},
            {8443, "HTTPS-Alt"}, {27017, "MongoDB"}, {5985, "WinRM"}, {5986, "WinRM-S"},
            {389, "LDAP"}, {636, "LDAPS"}, {88, "Kerberos"}, {464, "Kpasswd"},
            {587, "SMTP-Sub"}, {8888, "HTTP-Alt2"}, {9090, "Prometheus"}
        };

        public async Task PortScanAsync()
        {
            if (string.IsNullOrWhiteSpace(PortScanHost)) return;

            _portScanCts?.Cancel();
            _portScanCts = new CancellationTokenSource();
            var ct = _portScanCts.Token;

            IsRunning = true;
            PortScanResults.Clear();
            PortScanProgress = 0;

            try
            {
                var (startPort, endPort) = ParsePortRange(PortRange);
                var totalPorts = endPort - startPort + 1;
                StatusMessage = $"Scanning {PortScanHost} ports {startPort}-{endPort}...";

                var scanned = 0;
                using var semaphore = new SemaphoreSlim(100); // Max 100 concurrent connections

                var tasks = new List<Task>();
                for (int port = startPort; port <= endPort; port++)
                {
                    ct.ThrowIfCancellationRequested();
                    var p = port;
                    await semaphore.WaitAsync(ct);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            var isOpen = false;
                            var banner = string.Empty;
                            try
                            {
                                using var client = new TcpClient();
                                var connectTask = client.ConnectAsync(PortScanHost, p);
                                if (await Task.WhenAny(connectTask, Task.Delay(PortScanTimeout, ct)) == connectTask && client.Connected)
                                {
                                    isOpen = true;
                                    // Try banner grab
                                    try
                                    {
                                        client.ReceiveTimeout = 500;
                                        var stream = client.GetStream();
                                        if (stream.DataAvailable)
                                        {
                                            var buf = new byte[256];
                                            var read = await stream.ReadAsync(buf, 0, buf.Length);
                                            if (read > 0) banner = Encoding.ASCII.GetString(buf, 0, read).Trim();
                                        }
                                    }
                                    catch { }
                                }
                            }
                            catch { }

                            if (isOpen || !ShowOpenOnly)
                            {
                                var service = WellKnownPorts.TryGetValue(p, out var svc) ? svc : string.Empty;
                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    PortScanResults.Add(new PortScanResultViewModel
                                    {
                                        Port = p,
                                        State = isOpen ? "Open" : "Closed",
                                        Service = service,
                                        Banner = banner
                                    });
                                });
                            }

                            Interlocked.Increment(ref scanned);
                            var progress = (int)((double)scanned / totalPorts * 100);
                            await Application.Current.Dispatcher.InvokeAsync(() => PortScanProgress = progress);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, ct));
                }

                await Task.WhenAll(tasks);
                StatusMessage = $"Scan complete. {PortScanResults.Count} open port(s) found.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Port scan cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                PortScanProgress = 100;
            }
        }

        private static (int start, int end) ParsePortRange(string range)
        {
            if (string.IsNullOrWhiteSpace(range)) return (1, 1024);
            var parts = range.Split('-');
            var start = int.TryParse(parts[0].Trim(), out var s) ? Math.Max(1, Math.Min(s, 65535)) : 1;
            var end = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var e) ? Math.Max(1, Math.Min(e, 65535)) : start;
            return (Math.Min(start, end), Math.Max(start, end));
        }

        #endregion

        #region DNS Lookup

        public async Task DnsLookupAsync()
        {
            if (string.IsNullOrWhiteSpace(DnsLookupHost)) return;

            IsRunning = true;
            DnsResults.Clear();
            StatusMessage = $"Looking up {DnsLookupHost}...";

            try
            {
                await Task.Run(async () =>
                {
                    // Forward lookup
                    try
                    {
                        var hostEntry = await Dns.GetHostEntryAsync(DnsLookupHost);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            DnsResults.Add($"--- DNS Lookup for: {DnsLookupHost} ---");
                            DnsResults.Add($"Host Name: {hostEntry.HostName}");
                            DnsResults.Add(string.Empty);

                            if (hostEntry.AddressList.Length > 0)
                            {
                                DnsResults.Add("IP Addresses:");
                                foreach (var addr in hostEntry.AddressList)
                                {
                                    var type = addr.AddressFamily == AddressFamily.InterNetworkV6 ? "IPv6" : "IPv4";
                                    DnsResults.Add($"  [{type}] {addr}");
                                }
                            }

                            if (hostEntry.Aliases.Length > 0)
                            {
                                DnsResults.Add(string.Empty);
                                DnsResults.Add("Aliases:");
                                foreach (var alias in hostEntry.Aliases)
                                    DnsResults.Add($"  {alias}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            DnsResults.Add($"Forward lookup failed: {ex.Message}");
                        });
                    }

                    // Reverse lookup if input looks like an IP
                    if (System.Net.IPAddress.TryParse(DnsLookupHost, out var ip))
                    {
                        try
                        {
                            var reverseEntry = await Dns.GetHostEntryAsync(ip);
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                DnsResults.Add(string.Empty);
                                DnsResults.Add("--- Reverse DNS ---");
                                DnsResults.Add($"PTR: {reverseEntry.HostName}");
                            });
                        }
                        catch (Exception ex)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                DnsResults.Add(string.Empty);
                                DnsResults.Add($"Reverse lookup failed: {ex.Message}");
                            });
                        }
                    }

                    // NSLookup via process for additional records
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = "nslookup",
                            Arguments = $"-type=ANY {DnsLookupHost}",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };
                        using var proc = Process.Start(psi);
                        if (proc != null)
                        {
                            var output = await proc.StandardOutput.ReadToEndAsync();
                            await proc.WaitForExitAsync();

                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                DnsResults.Add(string.Empty);
                                DnsResults.Add("--- nslookup Output ---");
                                foreach (var line in output.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                                    DnsResults.Add(line.TrimEnd());
                            });
                        }
                    }
                    catch { }
                });

                StatusMessage = "DNS lookup complete.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        #endregion

        #region Network Discovery

        public async Task NetworkDiscoveryAsync()
        {
            _discoveryCts?.Cancel();
            _discoveryCts = new CancellationTokenSource();
            var ct = _discoveryCts.Token;

            IsRunning = true;
            DiscoveredDevices.Clear();
            DiscoveryProgress = 0;

            try
            {
                // Auto-detect subnet if not specified
                var subnet = DiscoverySubnet;
                if (string.IsNullOrWhiteSpace(subnet))
                {
                    subnet = await DetectLocalSubnetAsync();
                    if (string.IsNullOrWhiteSpace(subnet))
                    {
                        StatusMessage = "Could not detect local subnet. Enter a base IP (e.g., 192.168.1).";
                        IsRunning = false;
                        return;
                    }
                }

                StatusMessage = $"Scanning {subnet}.0/24...";

                var scanned = 0;
                using var semaphore = new SemaphoreSlim(50);
                var tasks = new List<Task>();

                for (int i = 1; i <= 254; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var ip = $"{subnet}.{i}";
                    var idx = i;

                    await semaphore.WaitAsync(ct);
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            using var ping = new Ping();
                            var reply = await ping.SendPingAsync(ip, 1000);

                            if (reply.Status == IPStatus.Success)
                            {
                                var hostname = string.Empty;
                                try
                                {
                                    var hostEntry = await Dns.GetHostEntryAsync(ip);
                                    hostname = hostEntry.HostName;
                                }
                                catch { hostname = "Unknown"; }

                                var mac = await GetMacFromArpAsync(ip);

                                await Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    DiscoveredDevices.Add(new DiscoveredDeviceViewModel
                                    {
                                        IPAddress = ip,
                                        Hostname = hostname,
                                        MACAddress = mac,
                                        ResponseTime = $"{reply.RoundtripTime}ms",
                                        Status = "Online"
                                    });
                                });
                            }

                            Interlocked.Increment(ref scanned);
                            var progress = (int)((double)scanned / 254 * 100);
                            await Application.Current.Dispatcher.InvokeAsync(() => DiscoveryProgress = progress);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, ct));
                }

                await Task.WhenAll(tasks);
                StatusMessage = $"Discovery complete. {DiscoveredDevices.Count} device(s) found.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Network discovery cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                DiscoveryProgress = 100;
            }
        }

        private static async Task<string> DetectLocalSubnetAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                        .Where(i => i.OperationalStatus == OperationalStatus.Up
                                    && i.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                    foreach (var iface in interfaces)
                    {
                        var props = iface.GetIPProperties();
                        var unicast = props.UnicastAddresses
                            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                        if (unicast != null)
                        {
                            var parts = unicast.Address.ToString().Split('.');
                            if (parts.Length == 4)
                                return $"{parts[0]}.{parts[1]}.{parts[2]}";
                        }
                    }
                }
                catch { }
                return string.Empty;
            });
        }

        private static async Task<string> GetMacFromArpAsync(string ip)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "arp",
                    Arguments = $"-a {ip}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    var output = await proc.StandardOutput.ReadToEndAsync();
                    await proc.WaitForExitAsync();

                    var match = System.Text.RegularExpressions.Regex.Match(output,
                        @"([0-9a-fA-F]{2}[:-]){5}[0-9a-fA-F]{2}");
                    if (match.Success) return match.Value.ToUpper();
                }
            }
            catch { }
            return "N/A";
        }

        #endregion

        #region Bandwidth Monitor

        private void StartBandwidthMonitor()
        {
            if (IsBandwidthMonitoring) return;

            _bandwidthCts?.Cancel();
            _bandwidthCts = new CancellationTokenSource();
            IsBandwidthMonitoring = true;
            BandwidthEntries.Clear();
            StatusMessage = "Bandwidth monitoring started...";

            _ = Task.Run(async () =>
            {
                var previousStats = new Dictionary<string, (long received, long sent)>();

                while (!_bandwidthCts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                            .Where(i => i.OperationalStatus == OperationalStatus.Up
                                        && i.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                            .ToList();

                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            // Update entries in place
                            foreach (var iface in interfaces)
                            {
                                var stats = iface.GetIPv4Statistics();
                                var name = iface.Name;
                                var dlSpeed = "0 KB/s";
                                var ulSpeed = "0 KB/s";

                                if (previousStats.TryGetValue(name, out var prev))
                                {
                                    var dlDelta = stats.BytesReceived - prev.received;
                                    var ulDelta = stats.BytesSent - prev.sent;
                                    dlSpeed = FormatBytesPerSec(dlDelta);
                                    ulSpeed = FormatBytesPerSec(ulDelta);
                                }

                                previousStats[name] = (stats.BytesReceived, stats.BytesSent);

                                var existing = BandwidthEntries.FirstOrDefault(b => b.AdapterName == name);
                                if (existing != null)
                                {
                                    existing.DownloadSpeed = dlSpeed;
                                    existing.UploadSpeed = ulSpeed;
                                    existing.TotalDownloaded = FormatBytes(stats.BytesReceived);
                                    existing.TotalUploaded = FormatBytes(stats.BytesSent);
                                    existing.Status = iface.OperationalStatus.ToString();
                                }
                                else
                                {
                                    BandwidthEntries.Add(new BandwidthEntryViewModel
                                    {
                                        AdapterName = name,
                                        DownloadSpeed = dlSpeed,
                                        UploadSpeed = ulSpeed,
                                        TotalDownloaded = FormatBytes(stats.BytesReceived),
                                        TotalUploaded = FormatBytes(stats.BytesSent),
                                        Status = iface.OperationalStatus.ToString()
                                    });
                                }
                            }
                        });

                        await Task.Delay(1000, _bandwidthCts.Token);
                    }
                    catch (OperationCanceledException) { break; }
                    catch { }
                }
            });
        }

        private void StopBandwidthMonitor()
        {
            _bandwidthCts?.Cancel();
            IsBandwidthMonitoring = false;
            StatusMessage = "Bandwidth monitoring stopped.";
        }

        private static string FormatBytesPerSec(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B/s";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB/s";
            return $"{bytes / (1024.0 * 1024.0):F2} MB/s";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }

        #endregion

        #region WHOIS Lookup

        public async Task WhoisLookupAsync()
        {
            if (string.IsNullOrWhiteSpace(WhoisDomain)) return;

            IsRunning = true;
            WhoisResults.Clear();
            StatusMessage = $"WHOIS lookup for {WhoisDomain}...";

            try
            {
                await Task.Run(async () =>
                {
                    // Use whois via nslookup + system whois or direct socket
                    var whoisServer = "whois.iana.org";
                    var domain = WhoisDomain.Trim();

                    // First determine the correct WHOIS server
                    var referralServer = await QueryWhoisServerAsync(whoisServer, domain);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        WhoisResults.Add($"--- WHOIS for: {domain} ---");
                        WhoisResults.Add($"Queried: {whoisServer}");
                        WhoisResults.Add(string.Empty);
                    });

                    // If we got a referral, query the actual WHOIS server
                    if (!string.IsNullOrEmpty(referralServer) && referralServer != whoisServer)
                    {
                        var detailedResult = await QueryWhoisServerAsync(referralServer, domain);
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            WhoisResults.Add($"Referral server: {referralServer}");
                            WhoisResults.Add(string.Empty);
                            if (!string.IsNullOrEmpty(detailedResult))
                            {
                                foreach (var line in detailedResult.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                                    WhoisResults.Add(line.TrimEnd());
                            }
                        });
                    }
                    else if (!string.IsNullOrEmpty(referralServer))
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            foreach (var line in referralServer.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)))
                                WhoisResults.Add(line.TrimEnd());
                        });
                    }
                });

                StatusMessage = "WHOIS lookup complete.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                WhoisResults.Add($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        private static async Task<string> QueryWhoisServerAsync(string server, string query)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(server, 43);
                using var stream = client.GetStream();

                var request = Encoding.ASCII.GetBytes(query + "\r\n");
                await stream.WriteAsync(request, 0, request.Length);

                var sb = new StringBuilder();
                var buffer = new byte[4096];
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    sb.Append(Encoding.ASCII.GetString(buffer, 0, bytesRead));
                }

                var result = sb.ToString();

                // Check for referral
                var referMatch = System.Text.RegularExpressions.Regex.Match(result,
                    @"refer:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (referMatch.Success)
                {
                    return referMatch.Groups[1].Value;
                }

                var whoisMatch = System.Text.RegularExpressions.Regex.Match(result,
                    @"whois:\s*(\S+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (whoisMatch.Success)
                {
                    return whoisMatch.Groups[1].Value;
                }

                return result;
            }
            catch (Exception ex)
            {
                return $"WHOIS query failed: {ex.Message}";
            }
        }

        #endregion
    }
}
