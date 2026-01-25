using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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

    public class NetworkToolsViewModel : BindableBase
    {
        private readonly NetworkToolsService _networkToolsService;

        public NetworkToolsViewModel()
        {
            _networkToolsService = new NetworkToolsService();

            PingCommand = new RelayCommand(async _ => await PingAsync(), _ => !IsRunning && !string.IsNullOrWhiteSpace(TargetHost));
            TracerouteCommand = new RelayCommand(async _ => await TracerouteAsync(), _ => !IsRunning && !string.IsNullOrWhiteSpace(TargetHost));
            RefreshConnectionsCommand = new RelayCommand(async _ => await RefreshConnectionsAsync(), _ => !IsRunning);
            RefreshAdaptersCommand = new RelayCommand(async _ => await RefreshAdaptersAsync(), _ => !IsRunning);
        }

        public ObservableCollection<string> PingResults { get; } = new();
        public ObservableCollection<NetworkConnectionViewModel> Connections { get; } = new();
        public ObservableCollection<NetworkAdapterViewModel> Adapters { get; } = new();

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
            } 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _selectedTabIndex;
        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }

        public ICommand PingCommand { get; }
        public ICommand TracerouteCommand { get; }
        public ICommand RefreshConnectionsCommand { get; }
        public ICommand RefreshAdaptersCommand { get; }

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
    }
}
