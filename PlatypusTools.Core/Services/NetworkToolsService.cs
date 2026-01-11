using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for network utilities and diagnostics
    /// </summary>
    public interface INetworkToolsService
    {
        Task<PingResult> PingHost(string hostOrIp, int timeout = 5000);
        Task<List<NetworkConnection>> GetActiveConnections();
        Task<List<NetworkAdapter>> GetNetworkAdapters();
        Task<List<string>> TracerouteHost(string hostOrIp, int maxHops = 30);
    }

    /// <summary>
    /// Represents the result of a ping operation
    /// </summary>
    public class PingResult
    {
        public string Host { get; set; } = string.Empty;
        public bool Success { get; set; }
        public long RoundtripTime { get; set; }
        public string? Address { get; set; }
        public int Ttl { get; set; }
        public string? ErrorMessage { get; set; }
        public IPStatus Status { get; set; }
    }

    /// <summary>
    /// Represents an active network connection
    /// </summary>
    public class NetworkConnection
    {
        public string Protocol { get; set; } = string.Empty;
        public string LocalAddress { get; set; } = string.Empty;
        public string LocalPort { get; set; } = string.Empty;
        public string RemoteAddress { get; set; } = string.Empty;
        public string RemotePort { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public int? ProcessId { get; set; }
        public string? ProcessName { get; set; }
    }

    /// <summary>
    /// Represents a network adapter
    /// </summary>
    public class NetworkAdapter
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public bool IsUp { get; set; }
        public long Speed { get; set; }
        public string? IpAddress { get; set; }
        public string? MacAddress { get; set; }
        public long BytesSent { get; set; }
        public long BytesReceived { get; set; }
    }

    /// <summary>
    /// Implementation of network tools service
    /// </summary>
    public class NetworkToolsService : INetworkToolsService
    {
        /// <summary>
        /// Pings a host or IP address
        /// </summary>
        /// <param name="hostOrIp">Hostname or IP address</param>
        /// <param name="timeout">Timeout in milliseconds</param>
        /// <returns>Ping result</returns>
        public async Task<PingResult> PingHost(string hostOrIp, int timeout = 5000)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(hostOrIp, timeout);

                return new PingResult
                {
                    Host = hostOrIp,
                    Success = reply.Status == IPStatus.Success,
                    RoundtripTime = reply.RoundtripTime,
                    Address = reply.Address?.ToString(),
                    Ttl = reply.Options?.Ttl ?? 0,
                    Status = reply.Status,
                    ErrorMessage = reply.Status != IPStatus.Success ? reply.Status.ToString() : null
                };
            }
            catch (Exception ex)
            {
                return new PingResult
                {
                    Host = hostOrIp,
                    Success = false,
                    ErrorMessage = ex.Message,
                    Status = IPStatus.Unknown
                };
            }
        }

        /// <summary>
        /// Gets all active network connections
        /// </summary>
        /// <returns>List of active connections</returns>
        public async Task<List<NetworkConnection>> GetActiveConnections()
        {
            return await Task.Run(() =>
            {
                var connections = new List<NetworkConnection>();

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = "netstat",
                        Arguments = "-ano",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(startInfo);
                    if (process == null)
                    {
                        throw new InvalidOperationException("Failed to start netstat process");
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit();

                    var lines = output.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);

                    // Skip header lines
                    foreach (var line in lines.Skip(4))
                    {
                        var parts = Regex.Split(line.Trim(), @"\s+");
                        if (parts.Length >= 5)
                        {
                            var connection = new NetworkConnection
                            {
                                Protocol = parts[0]
                            };

                            // Parse local address
                            var localParts = parts[1].Split(':');
                            if (localParts.Length >= 2)
                            {
                                connection.LocalAddress = string.Join(":", localParts.Take(localParts.Length - 1));
                                connection.LocalPort = localParts.Last();
                            }

                            // Parse remote address
                            var remoteParts = parts[2].Split(':');
                            if (remoteParts.Length >= 2)
                            {
                                connection.RemoteAddress = string.Join(":", remoteParts.Take(remoteParts.Length - 1));
                                connection.RemotePort = remoteParts.Last();
                            }

                            connection.State = parts[3];

                            // Parse PID
                            if (parts.Length >= 5 && int.TryParse(parts[4], out var pid))
                            {
                                connection.ProcessId = pid;
                                try
                                {
                                    var proc = Process.GetProcessById(pid);
                                    connection.ProcessName = proc.ProcessName;
                                }
                                catch
                                {
                                    // Process might have ended
                                }
                            }

                            connections.Add(connection);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to get active connections: {ex.Message}", ex);
                }

                return connections;
            });
        }

        /// <summary>
        /// Gets all network adapters on the system
        /// </summary>
        /// <returns>List of network adapters</returns>
        public async Task<List<NetworkAdapter>> GetNetworkAdapters()
        {
            return await Task.Run(() =>
            {
                var adapters = new List<NetworkAdapter>();

                try
                {
                    var interfaces = NetworkInterface.GetAllNetworkInterfaces();

                    foreach (var iface in interfaces)
                    {
                        var props = iface.GetIPProperties();
                        var ipv4Stats = iface.GetIPv4Statistics();

                        var adapter = new NetworkAdapter
                        {
                            Name = iface.Name,
                            Description = iface.Description,
                            Type = iface.NetworkInterfaceType.ToString(),
                            IsUp = iface.OperationalStatus == OperationalStatus.Up,
                            Speed = iface.Speed,
                            MacAddress = iface.GetPhysicalAddress().ToString(),
                            BytesSent = ipv4Stats.BytesSent,
                            BytesReceived = ipv4Stats.BytesReceived
                        };

                        // Get first IPv4 address
                        var unicastAddress = props.UnicastAddresses
                            .FirstOrDefault(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                        if (unicastAddress != null)
                        {
                            adapter.IpAddress = unicastAddress.Address.ToString();
                        }

                        adapters.Add(adapter);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to get network adapters: {ex.Message}", ex);
                }

                return adapters;
            });
        }

        /// <summary>
        /// Performs a traceroute to a host
        /// </summary>
        /// <param name="hostOrIp">Hostname or IP address</param>
        /// <param name="maxHops">Maximum number of hops</param>
        /// <returns>List of hop addresses</returns>
        public async Task<List<string>> TracerouteHost(string hostOrIp, int maxHops = 30)
        {
            return await Task.Run(() =>
            {
                var hops = new List<string>();

                try
                {
                    using var ping = new Ping();
                    var buffer = new byte[32];
                    var timeout = 5000;

                    for (int ttl = 1; ttl <= maxHops; ttl++)
                    {
                        var options = new PingOptions(ttl, true);
                        var reply = ping.Send(hostOrIp, timeout, buffer, options);

                        if (reply.Status == IPStatus.Success || reply.Status == IPStatus.TtlExpired)
                        {
                            var hopAddress = reply.Address?.ToString() ?? "*";
                            hops.Add($"Hop {ttl}: {hopAddress} ({reply.RoundtripTime}ms)");

                            if (reply.Status == IPStatus.Success)
                            {
                                break;
                            }
                        }
                        else
                        {
                            hops.Add($"Hop {ttl}: * (Request timed out)");
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to perform traceroute: {ex.Message}", ex);
                }

                return hops;
            });
        }
    }
}
