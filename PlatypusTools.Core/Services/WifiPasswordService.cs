using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service to retrieve saved WiFi profiles and their passwords via netsh.
    /// </summary>
    public class WifiPasswordService
    {
        /// <summary>
        /// Gets all saved WiFi profiles with their passwords.
        /// </summary>
        public async Task<List<WifiProfile>> GetSavedProfilesAsync(CancellationToken ct = default)
        {
            var profiles = new List<WifiProfile>();

            // Get list of all saved WiFi profiles
            var profileNames = await GetProfileNamesAsync(ct);

            foreach (var name in profileNames)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    var profile = await GetProfileDetailsAsync(name, ct);
                    if (profile != null)
                        profiles.Add(profile);
                }
                catch (Exception ex)
                {
                    profiles.Add(new WifiProfile
                    {
                        ProfileName = name,
                        Error = ex.Message
                    });
                }
            }

            return profiles;
        }

        /// <summary>
        /// Gets the password for a specific WiFi profile.
        /// </summary>
        public async Task<string?> GetPasswordAsync(string profileName, CancellationToken ct = default)
        {
            var profile = await GetProfileDetailsAsync(profileName, ct);
            return profile?.Password;
        }

        /// <summary>
        /// Exports all WiFi profiles to XML files.
        /// </summary>
        public async Task ExportProfilesAsync(string outputDir, CancellationToken ct = default)
        {
            var output = await RunNetshAsync($"wlan export profile folder=\"{outputDir}\" key=clear", ct);
        }

        /// <summary>
        /// Deletes a saved WiFi profile.
        /// </summary>
        public async Task<bool> DeleteProfileAsync(string profileName, CancellationToken ct = default)
        {
            var output = await RunNetshAsync($"wlan delete profile name=\"{profileName}\"", ct);
            return output.Contains("deleted", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Gets the current WiFi connection info.
        /// </summary>
        public async Task<WifiConnectionInfo?> GetCurrentConnectionAsync(CancellationToken ct = default)
        {
            var output = await RunNetshAsync("wlan show interfaces", ct);
            if (string.IsNullOrWhiteSpace(output))
                return null;

            var info = new WifiConnectionInfo();
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(':', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Contains("SSID") && !key.Contains("BSSID"))
                    info.Ssid = value;
                else if (key.Contains("State"))
                    info.State = value;
                else if (key.Contains("Signal"))
                    info.Signal = value;
                else if (key.Contains("Radio type"))
                    info.RadioType = value;
                else if (key.Contains("Authentication"))
                    info.Authentication = value;
                else if (key.Contains("Cipher"))
                    info.Cipher = value;
                else if (key.Contains("Channel"))
                    info.Channel = value;
                else if (key.Contains("Receive rate"))
                    info.ReceiveRate = value;
                else if (key.Contains("Transmit rate"))
                    info.TransmitRate = value;
                else if (key.Contains("Band"))
                    info.Band = value;
            }

            return string.IsNullOrEmpty(info.Ssid) ? null : info;
        }

        private async Task<List<string>> GetProfileNamesAsync(CancellationToken ct)
        {
            var output = await RunNetshAsync("wlan show profiles", ct);
            var names = new List<string>();

            foreach (var line in output.Split('\n'))
            {
                // Parse "All User Profile     : ProfileName"
                if (line.Contains(":", StringComparison.Ordinal))
                {
                    var parts = line.Split(':', 2);
                    if (parts.Length == 2 && parts[0].Contains("Profile", StringComparison.OrdinalIgnoreCase))
                    {
                        var name = parts[1].Trim();
                        if (!string.IsNullOrWhiteSpace(name))
                            names.Add(name);
                    }
                }
            }

            return names;
        }

        private async Task<WifiProfile?> GetProfileDetailsAsync(string profileName, CancellationToken ct)
        {
            var output = await RunNetshAsync($"wlan show profile name=\"{profileName}\" key=clear", ct);
            if (string.IsNullOrWhiteSpace(output))
                return null;

            var profile = new WifiProfile { ProfileName = profileName };

            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(':', 2);
                if (parts.Length != 2) continue;

                var key = parts[0].Trim();
                var value = parts[1].Trim();

                if (key.Contains("SSID name") && !key.Contains("Number"))
                    profile.Ssid = value.Trim('"');
                else if (key.Contains("Network type"))
                    profile.NetworkType = value;
                else if (key.Contains("Authentication"))
                    profile.Authentication = value;
                else if (key.Contains("Cipher"))
                    profile.Cipher = value;
                else if (key.Contains("Key Content"))
                    profile.Password = value;
                else if (key.Contains("Connection mode"))
                    profile.ConnectionMode = value;
                else if (key.Contains("Cost"))
                    profile.Cost = value;
            }

            return profile;
        }

        private static async Task<string> RunNetshAsync(string arguments, CancellationToken ct)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            return output;
        }
    }

    public class WifiProfile
    {
        public string ProfileName { get; set; } = "";
        public string Ssid { get; set; } = "";
        public string NetworkType { get; set; } = "";
        public string Authentication { get; set; } = "";
        public string Cipher { get; set; } = "";
        public string Password { get; set; } = "";
        public string ConnectionMode { get; set; } = "";
        public string Cost { get; set; } = "";
        public string? Error { get; set; }

        public bool HasPassword => !string.IsNullOrEmpty(Password);
        public string SecurityDisplay => $"{Authentication} / {Cipher}";
        public string PasswordDisplay => HasPassword ? Password : "(none)";
    }

    public class WifiConnectionInfo
    {
        public string Ssid { get; set; } = "";
        public string State { get; set; } = "";
        public string Signal { get; set; } = "";
        public string RadioType { get; set; } = "";
        public string Authentication { get; set; } = "";
        public string Cipher { get; set; } = "";
        public string Channel { get; set; } = "";
        public string ReceiveRate { get; set; } = "";
        public string TransmitRate { get; set; } = "";
        public string Band { get; set; } = "";
    }
}
