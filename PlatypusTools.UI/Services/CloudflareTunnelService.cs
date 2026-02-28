using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services;

/// <summary>
/// Manages Cloudflare Tunnel (cloudflared) for secure external access without port forwarding.
/// Provides automatic installation, configuration, and tunnel management with health monitoring.
/// </summary>
public class CloudflareTunnelService : IDisposable
{
    private static CloudflareTunnelService? _instance;
    public static CloudflareTunnelService Instance => _instance ??= new CloudflareTunnelService();

    private Process? _tunnelProcess;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _healthCheckCts;
    private bool _isDisposed;
    private System.Timers.Timer? _healthCheckTimer;
    private int _consecutiveFailures;
    private const int MaxConsecutiveFailures = 3;
    private string? _lastTunnelName;
    private int _lastLocalPort = 47392;
    private bool _useNamedTunnel;
    
    /// <summary>
    /// Gets information about the current tunnel connection status.
    /// </summary>
    public TunnelDiagnostics Diagnostics { get; } = new();
    
    public event EventHandler<TunnelDiagnostics>? DiagnosticsUpdated;

    private static readonly string CloudflaredPath = Path.Combine(
        SettingsManager.DataDirectory, "cloudflared", "cloudflared.exe");

    private static readonly string CloudflaredUrl = 
        "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

    public event EventHandler<string>? LogMessage;
    public event EventHandler<bool>? TunnelStateChanged;
    public event EventHandler<string>? TunnelUrlGenerated;

    public bool IsRunning => _tunnelProcess != null && !_tunnelProcess.HasExited;
    public bool IsInstalled => File.Exists(ResolveCloudflaredPath());
    public string? CurrentTunnelUrl { get; private set; }

    /// <summary>
    /// Resolves the actual path to the cloudflared executable.
    /// Checks: (1) our bundled path, (2) system PATH, (3) common install locations.
    /// </summary>
    private static string ResolveCloudflaredPath()
    {
        // 1. Our bundled location
        if (File.Exists(CloudflaredPath))
            return CloudflaredPath;

        // 2. Check system PATH
        try
        {
            var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
            foreach (var dir in pathDirs)
            {
                if (string.IsNullOrWhiteSpace(dir)) continue;
                var candidate = Path.Combine(dir.Trim(), "cloudflared.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        catch { }

        // 3. Common install locations (winget, choco, scoop)
        var commonPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "cloudflared", "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "cloudflared", "cloudflared.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WinGet", "Packages"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "cloudflared", "current", "cloudflared.exe"),
        };
        foreach (var p in commonPaths)
        {
            if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(p)) return p;
            }
            else if (Directory.Exists(p))
            {
                // Search winget packages directory for cloudflared.exe
                try
                {
                    var found = Directory.GetFiles(p, "cloudflared.exe", SearchOption.AllDirectories);
                    if (found.Length > 0) return found[0];
                }
                catch { }
            }
        }

        // Fallback to bundled path (will fail IsInstalled check, prompting install)
        return CloudflaredPath;
    }

    private CloudflareTunnelService() { }

    /// <summary>
    /// Downloads and installs cloudflared if not present.
    /// </summary>
    public async Task<bool> InstallAsync(IProgress<string>? progress = null)
    {
        if (IsInstalled)
        {
            progress?.Report("cloudflared is already installed");
            return true;
        }

        try
        {
            progress?.Report("Downloading cloudflared...");
            Log("Downloading cloudflared from GitHub...");

            var dir = Path.GetDirectoryName(CloudflaredPath)!;
            Directory.CreateDirectory(dir);

            var httpClient = HttpClientFactory.Download;
            
            var response = await httpClient.GetAsync(CloudflaredUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var receivedBytes = 0L;

            await using var fileStream = new FileStream(CloudflaredPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var downloadStream = await response.Content.ReadAsStreamAsync();
            
            var buffer = new byte[81920];
            int bytesRead;
            
            while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                receivedBytes += bytesRead;
                
                if (totalBytes > 0)
                {
                    var percent = (int)((receivedBytes * 100) / totalBytes);
                    progress?.Report($"Downloading... {percent}%");
                }
            }

            progress?.Report("cloudflared installed successfully!");
            Log($"cloudflared installed to: {CloudflaredPath}");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to install cloudflared: {ex.Message}");
            progress?.Report($"Install failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a quick tunnel (no Cloudflare account required).
    /// Generates a random *.trycloudflare.com URL.
    /// </summary>
    public async Task<bool> StartQuickTunnelAsync(int localPort = 47392)
    {
        if (IsRunning)
        {
            Log("Tunnel is already running");
            return true;
        }

        if (!IsInstalled)
        {
            Log("cloudflared not installed. Installing...");
            var installed = await InstallAsync();
            if (!installed) return false;
        }

        try
        {
            // Save config for auto-restart
            _lastLocalPort = localPort;
            _lastTunnelName = null;
            _useNamedTunnel = false;
            _consecutiveFailures = 0;
            
            _cts = new CancellationTokenSource();

            var resolvedPath = ResolveCloudflaredPath();
            Log($"Using cloudflared at: {resolvedPath}");
            
            var psi = new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = $"tunnel --url https://localhost:{localPort} --no-tls-verify",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _tunnelProcess = new Process { StartInfo = psi };
            _tunnelProcess.OutputDataReceived += OnOutputReceived;
            _tunnelProcess.ErrorDataReceived += OnErrorReceived;
            _tunnelProcess.EnableRaisingEvents = true;
            _tunnelProcess.Exited += (s, e) =>
            {
                Log("Tunnel process exited");
                CurrentTunnelUrl = null;
                Diagnostics.ProcessRunning = false;
                TunnelStateChanged?.Invoke(this, false);
            };

            _tunnelProcess.Start();
            _tunnelProcess.BeginOutputReadLine();
            _tunnelProcess.BeginErrorReadLine();

            Diagnostics.ProcessRunning = true;
            Log($"Started quick tunnel for localhost:{localPort}");
            TunnelStateChanged?.Invoke(this, true);
            
            // Start health monitoring
            StartHealthMonitoring();
            
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to start tunnel: {ex.Message}");
            Diagnostics.LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Starts a named tunnel with a custom hostname (requires Cloudflare account setup).
    /// Uses modern 'cloudflared tunnel run' with a generated config.yml.
    /// </summary>
    public async Task<bool> StartNamedTunnelAsync(string hostname, int localPort = 47392)
    {
        if (IsRunning)
        {
            Log("Tunnel is already running");
            return true;
        }

        if (!IsInstalled)
        {
            var installed = await InstallAsync();
            if (!installed) return false;
        }

        // Get the tunnel name from settings
        var tunnelName = SettingsManager.Current.CloudflareTunnelName;

        try
        {
            // Save config for auto-restart
            _lastLocalPort = localPort;
            _lastTunnelName = !string.IsNullOrWhiteSpace(tunnelName) ? tunnelName : hostname;
            _useNamedTunnel = true;
            _consecutiveFailures = 0;
            
            _cts = new CancellationTokenSource();

            // Generate config.yml for the tunnel
            EnsureConfigYml(tunnelName, hostname, localPort);

            var configFile = Path.Combine(UserConfigPath, "config.yml");
            var args = !string.IsNullOrWhiteSpace(tunnelName)
                ? $"tunnel --config \"{configFile}\" run"
                : $"tunnel --url https://localhost:{localPort} --no-tls-verify --hostname {hostname}";

            var resolvedPath = ResolveCloudflaredPath();
            Log($"Using cloudflared at: {resolvedPath}");
            
            var psi = new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            _tunnelProcess = new Process { StartInfo = psi };
            _tunnelProcess.OutputDataReceived += OnOutputReceived;
            _tunnelProcess.ErrorDataReceived += OnErrorReceived;
            _tunnelProcess.EnableRaisingEvents = true;
            _tunnelProcess.Exited += (s, e) =>
            {
                CurrentTunnelUrl = null;
                Diagnostics.ProcessRunning = false;
                TunnelStateChanged?.Invoke(this, false);
            };

            _tunnelProcess.Start();
            _tunnelProcess.BeginOutputReadLine();
            _tunnelProcess.BeginErrorReadLine();

            CurrentTunnelUrl = $"https://{hostname}";
            Diagnostics.ProcessRunning = true;
            Log($"Started named tunnel: {CurrentTunnelUrl} (name: {tunnelName})");
            TunnelStateChanged?.Invoke(this, true);
            TunnelUrlGenerated?.Invoke(this, CurrentTunnelUrl);
            
            // Start health monitoring
            StartHealthMonitoring();
            
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to start tunnel: {ex.Message}");
            Diagnostics.LastError = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Generates or updates ~/.cloudflared/config.yml with the tunnel settings.
    /// </summary>
    private void EnsureConfigYml(string tunnelName, string hostname, int localPort)
    {
        if (string.IsNullOrWhiteSpace(tunnelName)) return;

        try
        {
            Directory.CreateDirectory(UserConfigPath);
            var configFile = Path.Combine(UserConfigPath, "config.yml");

            // Find the credentials file (created by 'cloudflared tunnel create')
            var credentialsFile = "";
            var credFiles = Directory.GetFiles(UserConfigPath, "*.json");
            if (credFiles.Length > 0)
            {
                // Pick the first JSON file that looks like a credentials file
                credentialsFile = credFiles.FirstOrDefault(f => 
                    !Path.GetFileName(f).Equals("config.json", StringComparison.OrdinalIgnoreCase)) ?? credFiles[0];
            }

            // Use the tunnel UUID from the credentials filename for reliability.
            // The credentials file is named <tunnel-uuid>.json — using the UUID avoids
            // case-sensitivity issues with tunnel names (e.g., "PlatyTools" vs "platytools").
            var tunnelIdentifier = tunnelName;
            if (!string.IsNullOrEmpty(credentialsFile))
            {
                var credFileName = Path.GetFileNameWithoutExtension(credentialsFile);
                if (Guid.TryParse(credFileName, out var tunnelUuid))
                {
                    tunnelIdentifier = tunnelUuid.ToString();
                    Log($"Using tunnel UUID from credentials file: {tunnelIdentifier}");
                }
                else
                {
                    // Try reading the TunnelID from inside the JSON credentials file
                    try
                    {
                        var credJson = File.ReadAllText(credentialsFile);
                        var match = System.Text.RegularExpressions.Regex.Match(credJson, "\"TunnelID\"\\s*:\\s*\"([^\"]+)\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        if (match.Success)
                        {
                            tunnelIdentifier = match.Groups[1].Value;
                            Log($"Using TunnelID from credentials JSON: {tunnelIdentifier}");
                        }
                    }
                    catch { }
                }
            }

            var yaml = $@"tunnel: {tunnelIdentifier}
credentials-file: {credentialsFile.Replace("\\", "/")}

ingress:
  - hostname: {hostname}
    service: https://localhost:{localPort}
    originRequest:
      noTLSVerify: true
  - service: http_status:404
";

            File.WriteAllText(configFile, yaml);
            Log($"Generated config.yml: tunnel={tunnelName}, hostname={hostname}, port={localPort}");
        }
        catch (Exception ex)
        {
            Log($"Warning: Could not write config.yml: {ex.Message}");
        }
    }

    /// <summary>
    /// Stops the running tunnel.
    /// </summary>
    public void Stop()
    {
        try
        {
            // Stop health monitoring
            StopHealthMonitoring();
            
            // Clear saved config to prevent auto-restart
            _lastTunnelName = null;
            
            _cts?.Cancel();

            if (_tunnelProcess != null && !_tunnelProcess.HasExited)
            {
                try
                {
                    _tunnelProcess.Kill(true);
                }
                catch { }
                // Don't wait - just fire and forget
                Log("Tunnel stop requested");
            }

            _tunnelProcess?.Dispose();
            _tunnelProcess = null;
            CurrentTunnelUrl = null;
            Diagnostics.ProcessRunning = false;
            Diagnostics.ActiveConnections = 0;
            TunnelStateChanged?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            Log($"Error stopping tunnel: {ex.Message}");
        }
    }

    /// <summary>
    /// Opens the Cloudflare login page for account authentication.
    /// Required for named tunnels with custom hostnames.
    /// </summary>
    public async Task<bool> LoginAsync()
    {
        if (!IsInstalled)
        {
            var installed = await InstallAsync();
            if (!installed) return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveCloudflaredPath(),
                Arguments = "tunnel login",
                UseShellExecute = false,
                CreateNoWindow = false // Show window for user to see URL
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                return process.ExitCode == 0;
            }
            return false;
        }
        catch (Exception ex)
        {
            Log($"Login failed: {ex.Message}");
            return false;
        }
    }

    private void OnOutputReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        
        Log(e.Data);
        
        // Parse tunnel URL from output
        if (e.Data.Contains("trycloudflare.com") || e.Data.Contains("https://"))
        {
            var url = ExtractUrl(e.Data);
            if (!string.IsNullOrEmpty(url))
            {
                CurrentTunnelUrl = url;
                TunnelUrlGenerated?.Invoke(this, url);
                Log($"Tunnel URL: {url}");
            }
        }
    }

    private void OnErrorReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data)) return;
        
        // cloudflared logs to stderr even for info messages
        Log(e.Data);
        
        // Parse tunnel URL from stderr (cloudflared logs URL here)
        if (e.Data.Contains("trycloudflare.com") || e.Data.Contains("https://"))
        {
            var url = ExtractUrl(e.Data);
            if (!string.IsNullOrEmpty(url))
            {
                CurrentTunnelUrl = url;
                TunnelUrlGenerated?.Invoke(this, url);
                Log($"Tunnel URL: {url}");
            }
        }
    }

    private static string? ExtractUrl(string text)
    {
        // Look for https:// URLs
        var httpsIndex = text.IndexOf("https://", StringComparison.OrdinalIgnoreCase);
        if (httpsIndex >= 0)
        {
            var urlStart = httpsIndex;
            var urlEnd = text.IndexOfAny(new[] { ' ', '\t', '\r', '\n', '"', '\'' }, urlStart);
            if (urlEnd < 0) urlEnd = text.Length;
            
            var url = text.Substring(urlStart, urlEnd - urlStart).Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out _))
            {
                return url;
            }
        }
        return null;
    }

    private void Log(string message)
    {
        SimpleLogger.Info($"[CloudflareTunnel] {message}");
        LogMessage?.Invoke(this, message);
        Diagnostics.LastLogMessage = message;
        Diagnostics.LastLogTime = DateTime.Now;
    }
    
    /// <summary>
    /// Starts periodic health checking for the tunnel.
    /// </summary>
    public void StartHealthMonitoring(int intervalSeconds = 30)
    {
        StopHealthMonitoring();
        
        _healthCheckCts = new CancellationTokenSource();
        _healthCheckTimer = new System.Timers.Timer(intervalSeconds * 1000);
        _healthCheckTimer.Elapsed += async (s, e) => await CheckHealthAsync();
        _healthCheckTimer.AutoReset = true;
        _healthCheckTimer.Start();
        
        Log($"Health monitoring started (interval: {intervalSeconds}s)");
        
        // Do an immediate check
        _ = CheckHealthAsync();
    }
    
    /// <summary>
    /// Stops health monitoring.
    /// </summary>
    public void StopHealthMonitoring()
    {
        _healthCheckTimer?.Stop();
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;
        _healthCheckCts?.Cancel();
        _healthCheckCts?.Dispose();
        _healthCheckCts = null;
    }
    
    /// <summary>
    /// Checks the tunnel health and auto-restarts if needed.
    /// </summary>
    public async Task CheckHealthAsync()
    {
        Diagnostics.LastHealthCheck = DateTime.Now;
        
        try
        {
            // Check if process is running
            var isProcessAlive = _tunnelProcess != null && !_tunnelProcess.HasExited;
            Diagnostics.ProcessRunning = isProcessAlive;
            
            // Get active connection count from cloudflared
            if (IsInstalled)
            {
                var connInfo = await GetTunnelConnectionInfoAsync();
                Diagnostics.ActiveConnections = connInfo.connectionCount;
                Diagnostics.EdgeLocation = connInfo.edgeLocation;
                Diagnostics.TunnelId = connInfo.tunnelId;
            }
            
            // If process should be running but has no connections, restart it
            if (isProcessAlive && Diagnostics.ActiveConnections == 0)
            {
                _consecutiveFailures++;
                Log($"No active tunnel connections (failure {_consecutiveFailures}/{MaxConsecutiveFailures})");
                
                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    Log("Too many consecutive failures - restarting tunnel...");
                    await RestartTunnelAsync();
                }
            }
            else if (Diagnostics.ActiveConnections > 0)
            {
                _consecutiveFailures = 0;
            }
            
            // Check if process unexpectedly died
            if (!isProcessAlive && !string.IsNullOrEmpty(_lastTunnelName))
            {
                Log("Tunnel process died - auto-restarting...");
                await RestartTunnelAsync();
            }
            
            DiagnosticsUpdated?.Invoke(this, Diagnostics);
        }
        catch (Exception ex)
        {
            Log($"Health check error: {ex.Message}");
            Diagnostics.LastError = ex.Message;
            DiagnosticsUpdated?.Invoke(this, Diagnostics);
        }
    }
    
    /// <summary>
    /// Gets tunnel connection information from cloudflared.
    /// </summary>
    private async Task<(int connectionCount, string? edgeLocation, string? tunnelId)> GetTunnelConnectionInfoAsync()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ResolveCloudflaredPath(),
                Arguments = $"tunnel info {_lastTunnelName ?? ""}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = new Process { StartInfo = psi };
            process.Start();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            var fullOutput = output + error;
            
            // Parse connection count from output
            // Looking for "CONNECTOR ID" rows
            var connectionCount = 0;
            var edgeLocation = (string?)null;
            var tunnelId = (string?)null;
            
            var lines = fullOutput.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("ID:"))
                {
                    var parts = line.Split(':');
                    if (parts.Length > 1)
                        tunnelId = parts[1].Trim();
                }
                else if (line.Contains("xdfw") || line.Contains("xiah") || line.Contains("edge"))
                {
                    connectionCount++;
                    // Extract edge location (e.g., "1xdfw08, 2xiah01")
                    var edgeMatch = System.Text.RegularExpressions.Regex.Match(line, @"\d+x\w+\d+");
                    if (edgeMatch.Success)
                        edgeLocation = edgeMatch.Value;
                }
            }
            
            // Simple heuristic: if output mentions connector, there's at least 1 connection
            if (connectionCount == 0 && fullOutput.Contains("CONNECTOR"))
                connectionCount = fullOutput.Split("CONNECTOR").Length - 1;
                
            return (connectionCount, edgeLocation, tunnelId);
        }
        catch
        {
            return (0, null, null);
        }
    }
    
    /// <summary>
    /// Restarts the tunnel using the last known configuration.
    /// </summary>
    public async Task RestartTunnelAsync()
    {
        _consecutiveFailures = 0;
        
        // Kill any zombie processes first
        KillAllCloudflaredProcesses();
        await Task.Delay(2000);
        
        if (_useNamedTunnel && !string.IsNullOrEmpty(_lastTunnelName))
        {
            await StartNamedTunnelAsync(_lastTunnelName, _lastLocalPort);
        }
        else
        {
            await StartQuickTunnelAsync(_lastLocalPort);
        }
    }
    
    /// <summary>
    /// Kills all cloudflared processes (cleanup zombie processes).
    /// </summary>
    public void KillAllCloudflaredProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName("cloudflared");
            foreach (var process in processes)
            {
                try
                {
                    process.Kill(true);
                    Log($"Killed cloudflared process {process.Id}");
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Log($"Error killing cloudflared processes: {ex.Message}");
        }
    }
    
    /// <summary>
    /// Gets the number of running cloudflared processes.
    /// </summary>
    public int GetRunningProcessCount()
    {
        try
        {
            return Process.GetProcessesByName("cloudflared").Length;
        }
        catch
        {
            return 0;
        }
    }

    #region Persistent Tunnel (Background Process + Auto-Start)
    
    private static readonly string UserConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".cloudflared");
    
    private const string AutoStartRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AutoStartValueName = "PlatypusTools-CloudflareTunnel";
    
    private Process? _persistentTunnelProcess;
    
    /// <summary>
    /// Checks if the persistent tunnel background process is running.
    /// </summary>
    public bool IsPersistentTunnelRunning
    {
        get
        {
            try
            {
                // Check our tracked process first
                if (_persistentTunnelProcess != null && !_persistentTunnelProcess.HasExited)
                    return true;
                    
                // Check if any cloudflared process is running with our config
                return Process.GetProcessesByName("cloudflared").Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }
    
    /// <summary>
    /// Checks if auto-start is configured (Registry Run key exists).
    /// </summary>
    public bool IsAutoStartEnabled
    {
        get
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, false);
                var value = key?.GetValue(AutoStartValueName) as string;
                return !string.IsNullOrEmpty(value);
            }
            catch
            {
                return false;
            }
        }
    }
    
    /// <summary>
    /// Gets the persistent tunnel status string.
    /// </summary>
    public string PersistentTunnelStatus
    {
        get
        {
            var running = IsPersistentTunnelRunning;
            var autoStart = IsAutoStartEnabled;
            
            if (running && autoStart) return "Running (Auto-Start)";
            if (running) return "Running";
            if (autoStart) return "Stopped (Auto-Start Enabled)";
            return "Not Configured";
        }
    }
    
    // Keep these for backward compat with UpdateWindowsServiceStatus in UI
    public bool IsWindowsServiceInstalled => IsPersistentTunnelRunning || IsAutoStartEnabled;
    public string WindowsServiceStatus
    {
        get
        {
            if (IsPersistentTunnelRunning) return "Running";
            if (IsAutoStartEnabled) return "Stopped";
            return "Not Installed";
        }
    }
    
    /// <summary>
    /// Starts the persistent tunnel as a background process (no admin needed).
    /// Generates config.yml from saved settings before starting.
    /// If a stale cloudflared process is running (not managed by us), kills it and restarts with current config.
    /// </summary>
    public async Task<bool> StartPersistentTunnelAsync()
    {
        // If WE started a tunnel and it's still running, that's fine
        if (_persistentTunnelProcess != null && !_persistentTunnelProcess.HasExited)
        {
            Log("Tunnel is already running (managed process)");
            return true;
        }
        
        // If there are orphan/stale cloudflared processes we didn't start, kill them
        // so we can restart with the correct, current config
        var staleProcesses = Process.GetProcessesByName("cloudflared");
        if (staleProcesses.Length > 0)
        {
            Log($"Found {staleProcesses.Length} stale cloudflared process(es) — killing and restarting with current config...");
            foreach (var proc in staleProcesses)
            {
                try { proc.Kill(true); proc.Dispose(); } catch { }
            }
            await Task.Delay(1000); // Let processes die
        }
        
        if (!IsInstalled)
        {
            Log("cloudflared not installed");
            return false;
        }
        
        // Generate config.yml from current settings
        var settings = SettingsManager.Current;
        var tunnelName = settings.CloudflareTunnelName;
        var hostname = settings.CloudflareTunnelHostname;
        var port = settings.RemoteServerPort;
        
        if (string.IsNullOrWhiteSpace(tunnelName))
        {
            Log("No tunnel name configured. Go to Settings → Cloudflare Tunnel → Named Tunnel and enter your tunnel name.");
            return false;
        }
        
        EnsureConfigYml(tunnelName, hostname, port);
        
        var configFile = Path.Combine(UserConfigPath, "config.yml");
        if (!File.Exists(configFile))
        {
            Log("No tunnel config found. Please set up a named tunnel first.");
            return false;
        }
        
        try
        {
            Log("Starting persistent tunnel...");
            
            var resolvedPath = ResolveCloudflaredPath();
            Log($"Using cloudflared at: {resolvedPath}");
            
            var psi = new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = $"tunnel --config \"{configFile}\" run",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            
            _persistentTunnelProcess = Process.Start(psi);
            if (_persistentTunnelProcess == null)
            {
                Log("Failed to start cloudflared process");
                return false;
            }
            
            // Read output asynchronously
            _persistentTunnelProcess.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Log($"[tunnel] {e.Data}");
            };
            _persistentTunnelProcess.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                    Log($"[tunnel] {e.Data}");
            };
            _persistentTunnelProcess.BeginErrorReadLine();
            _persistentTunnelProcess.BeginOutputReadLine();
            
            // Wait a moment and verify it started
            await Task.Delay(3000);
            
            if (_persistentTunnelProcess.HasExited)
            {
                Log($"Tunnel process exited immediately with code {_persistentTunnelProcess.ExitCode}");
                return false;
            }
            
            Log($"Persistent tunnel started (PID: {_persistentTunnelProcess.Id})");
            Diagnostics.ProcessRunning = true;
            DiagnosticsUpdated?.Invoke(this, Diagnostics);
            TunnelStateChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to start persistent tunnel: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Stops the persistent tunnel.
    /// </summary>
    public Task<bool> StopPersistentTunnelAsync()
    {
        try
        {
            Log("Stopping persistent tunnel...");
            
            // Kill our tracked process
            if (_persistentTunnelProcess != null && !_persistentTunnelProcess.HasExited)
            {
                _persistentTunnelProcess.Kill(true);
                _persistentTunnelProcess.Dispose();
                _persistentTunnelProcess = null;
            }
            
            // Kill any other cloudflared processes
            foreach (var proc in Process.GetProcessesByName("cloudflared"))
            {
                try { proc.Kill(true); proc.Dispose(); } catch { }
            }
            
            Log("Persistent tunnel stopped");
            Diagnostics.ProcessRunning = false;
            DiagnosticsUpdated?.Invoke(this, Diagnostics);
            TunnelStateChanged?.Invoke(this, false);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            Log($"Failed to stop tunnel: {ex.Message}");
            return Task.FromResult(false);
        }
    }
    
    /// <summary>
    /// Enables auto-start via Registry Run key (no admin needed).
    /// Also starts the tunnel immediately.
    /// </summary>
    public async Task<bool> InstallWindowsServiceAsync()
    {
        if (!IsInstalled)
        {
            Log("cloudflared not installed, installing first...");
            await InstallAsync();
        }
        
        // Generate config.yml from current settings
        var settings = SettingsManager.Current;
        var tunnelName = settings.CloudflareTunnelName;
        var hostname = settings.CloudflareTunnelHostname;
        var port = settings.RemoteServerPort;
        
        if (string.IsNullOrWhiteSpace(tunnelName))
        {
            Log("No tunnel name configured. Go to Settings → Cloudflare Tunnel → Named Tunnel and enter your tunnel name.");
            return false;
        }
        
        EnsureConfigYml(tunnelName, hostname, port);
        
        var configFile = Path.Combine(UserConfigPath, "config.yml");
        if (!File.Exists(configFile))
        {
            Log("No tunnel config found. Please set up a named tunnel first.");
            return false;
        }
        
        try
        {
            Log("Setting up auto-start tunnel via Registry Run key...");
            
            // Set Registry Run key - runs on user login, zero admin needed
            var resolvedPath = ResolveCloudflaredPath();
            var runCommand = $"\"{resolvedPath}\" tunnel --config \"{configFile}\" run";
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true))
            {
                if (key == null)
                {
                    Log("Failed to open Registry Run key");
                    return false;
                }
                key.SetValue(AutoStartValueName, runCommand);
            }
            
            // Verify it was written
            if (!IsAutoStartEnabled)
            {
                Log("Failed to verify Registry Run key");
                return false;
            }
            
            Log("Auto-start registry entry created successfully");
            
            // Start the tunnel now
            var started = await StartPersistentTunnelAsync();
            if (started)
            {
                Log("Tunnel auto-start configured and tunnel is running");
            }
            else
            {
                Log("Auto-start configured but tunnel failed to start now. It will start on next login.");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to set up auto-start: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Disables auto-start and stops the tunnel.
    /// </summary>
    public async Task<bool> UninstallWindowsServiceAsync()
    {
        try
        {
            Log("Removing auto-start and stopping tunnel...");
            
            // Stop the tunnel
            await StopPersistentTunnelAsync();
            
            // Remove the Registry Run key
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(AutoStartRegistryKey, true);
                if (key?.GetValue(AutoStartValueName) != null)
                {
                    key.DeleteValue(AutoStartValueName);
                    Log("Auto-start registry entry removed");
                }
            }
            catch (Exception ex)
            {
                Log($"Warning: Could not remove registry entry: {ex.Message}");
            }
            
            Log("Tunnel uninstalled successfully");
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to uninstall: {ex.Message}");
            return false;
        }
    }
    
    /// <summary>
    /// Starts the tunnel (alias for backward compat with UI).
    /// </summary>
    public Task<bool> StartWindowsServiceAsync() => StartPersistentTunnelAsync();
    
    /// <summary>
    /// Stops the tunnel (alias for backward compat with UI).
    /// </summary>
    public Task<bool> StopWindowsServiceAsync() => StopPersistentTunnelAsync();
    
    /// <summary>
    /// Checks if the tunnel is running.
    /// </summary>
    public Task<bool> IsWindowsServiceRunningAsync() => Task.FromResult(IsPersistentTunnelRunning);
    
    /// <summary>
    /// Checks if auto-start is configured.
    /// </summary>
    public Task<bool> IsWindowsServiceInstalledAsync() => Task.FromResult(IsAutoStartEnabled);
    
    #endregion

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        StopHealthMonitoring();
        Stop();
        
        // Note: Don't kill persistent tunnel on dispose - it should keep running
        _persistentTunnelProcess?.Dispose();
        _persistentTunnelProcess = null;
        
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Diagnostics information for the Cloudflare Tunnel.
/// </summary>
public class TunnelDiagnostics
{
    public bool ProcessRunning { get; set; }
    public int ActiveConnections { get; set; }
    public string? EdgeLocation { get; set; }
    public string? TunnelId { get; set; }
    public string? LastLogMessage { get; set; }
    public string? LastError { get; set; }
    public DateTime? LastHealthCheck { get; set; }
    public DateTime? LastLogTime { get; set; }
    
    public string Status => ProcessRunning && ActiveConnections > 0 
        ? "Connected" 
        : ProcessRunning && ActiveConnections == 0 
            ? "Reconnecting..." 
            : "Disconnected";
    
    public string StatusColor => ProcessRunning && ActiveConnections > 0 
        ? "#FF4CAF50" 
        : ProcessRunning 
            ? "#FFFFC107" 
            : "#FFF44336";
}
