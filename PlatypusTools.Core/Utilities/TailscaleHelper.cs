using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Utilities
{
    /// <summary>
    /// Helper for detecting Tailscale VPN and providing remote access URLs.
    /// Tailscale enables secure remote access to PlatypusTools Remote without
    /// port forwarding or complex firewall configuration.
    /// </summary>
    public static class TailscaleHelper
    {
        /// <summary>
        /// Standard Tailscale network adapter name prefix.
        /// </summary>
        private const string TailscaleAdapterName = "Tailscale";

        /// <summary>
        /// Standard Tailscale CIDR range (100.64.0.0/10).
        /// </summary>
        private const string TailscaleIpPrefix = "100.";

        /// <summary>
        /// Gets whether Tailscale appears to be installed on this system.
        /// </summary>
        public static bool IsInstalled
        {
            get
            {
                // Check common install paths
                var paths = new[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Tailscale"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Tailscale"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tailscale")
                };

                return paths.Any(Directory.Exists);
            }
        }

        /// <summary>
        /// Gets whether Tailscale has an active connection (network adapter is up).
        /// </summary>
        public static bool IsConnected
        {
            get
            {
                try
                {
                    return NetworkInterface.GetAllNetworkInterfaces()
                        .Any(ni => ni.Name.Contains(TailscaleAdapterName, StringComparison.OrdinalIgnoreCase)
                                && ni.OperationalStatus == OperationalStatus.Up);
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets the Tailscale IP address of this machine, or null if not connected.
        /// </summary>
        public static string? GetTailscaleIp()
        {
            try
            {
                var tailscaleInterface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(ni => ni.Name.Contains(TailscaleAdapterName, StringComparison.OrdinalIgnoreCase)
                                       && ni.OperationalStatus == OperationalStatus.Up);

                if (tailscaleInterface == null)
                    return null;

                var ipProps = tailscaleInterface.GetIPProperties();
                var unicast = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork
                                      && a.Address.ToString().StartsWith(TailscaleIpPrefix));

                return unicast?.Address.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the Tailscale hostname of this machine (from `tailscale status --self`).
        /// Returns null if not available.
        /// </summary>
        public static async Task<string?> GetTailscaleHostnameAsync()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "tailscale",
                    Arguments = "status --self --json",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0) return null;

                // Simple JSON parse for "DNSName" field
                var dnsIdx = output.IndexOf("\"DNSName\"", StringComparison.Ordinal);
                if (dnsIdx >= 0)
                {
                    var colonIdx = output.IndexOf(':', dnsIdx);
                    var quoteStart = output.IndexOf('"', colonIdx + 1);
                    var quoteEnd = output.IndexOf('"', quoteStart + 1);
                    if (quoteStart >= 0 && quoteEnd > quoteStart)
                    {
                        var dnsName = output.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                        return dnsName.TrimEnd('.');
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets a complete Tailscale status report.
        /// </summary>
        public static TailscaleStatus GetStatus()
        {
            var status = new TailscaleStatus
            {
                IsInstalled = IsInstalled,
                IsConnected = IsConnected,
                TailscaleIp = GetTailscaleIp()
            };

            return status;
        }

        /// <summary>
        /// Generates the remote access URL for PlatypusTools Remote via Tailscale.
        /// </summary>
        /// <param name="port">The port PlatypusRemoteServer is running on.</param>
        /// <returns>The Tailscale remote URL, or null if Tailscale is not connected.</returns>
        public static string? GetRemoteUrl(int port = 47392)
        {
            var ip = GetTailscaleIp();
            if (ip == null) return null;
            return $"https://{ip}:{port}";
        }
    }

    /// <summary>
    /// Tailscale connection status information.
    /// </summary>
    public class TailscaleStatus
    {
        /// <summary>
        /// Whether Tailscale is installed.
        /// </summary>
        public bool IsInstalled { get; set; }

        /// <summary>
        /// Whether Tailscale is currently connected (adapter up).
        /// </summary>
        public bool IsConnected { get; set; }

        /// <summary>
        /// The Tailscale IP (100.x.y.z) of this machine.
        /// </summary>
        public string? TailscaleIp { get; set; }

        /// <summary>
        /// The Tailscale DNS hostname.
        /// </summary>
        public string? Hostname { get; set; }

        /// <summary>
        /// Summary message for display.
        /// </summary>
        public string SummaryMessage
        {
            get
            {
                if (!IsInstalled)
                    return "Tailscale not installed. Install from https://tailscale.com for secure remote access.";
                if (!IsConnected)
                    return "Tailscale installed but not connected. Sign in to Tailscale to enable remote access.";
                return $"Tailscale connected at {TailscaleIp}. Remote access available via Tailscale network.";
            }
        }
    }
}
