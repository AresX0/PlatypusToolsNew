using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services;

/// <summary>
/// Manages Cloudflare Tunnel (cloudflared) for secure external access without port forwarding.
/// Provides automatic installation, configuration, and tunnel management.
/// </summary>
public class CloudflareTunnelService : IDisposable
{
    private static CloudflareTunnelService? _instance;
    public static CloudflareTunnelService Instance => _instance ??= new CloudflareTunnelService();

    private Process? _tunnelProcess;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    private static readonly string CloudflaredPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlatypusTools", "cloudflared", "cloudflared.exe");

    private static readonly string CloudflaredUrl = 
        "https://github.com/cloudflare/cloudflared/releases/latest/download/cloudflared-windows-amd64.exe";

    public event EventHandler<string>? LogMessage;
    public event EventHandler<bool>? TunnelStateChanged;
    public event EventHandler<string>? TunnelUrlGenerated;

    public bool IsRunning => _tunnelProcess != null && !_tunnelProcess.HasExited;
    public bool IsInstalled => File.Exists(CloudflaredPath);
    public string? CurrentTunnelUrl { get; private set; }

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

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5);
            
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
            _cts = new CancellationTokenSource();

            var psi = new ProcessStartInfo
            {
                FileName = CloudflaredPath,
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
                TunnelStateChanged?.Invoke(this, false);
            };

            _tunnelProcess.Start();
            _tunnelProcess.BeginOutputReadLine();
            _tunnelProcess.BeginErrorReadLine();

            Log($"Started quick tunnel for localhost:{localPort}");
            TunnelStateChanged?.Invoke(this, true);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to start tunnel: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Starts a named tunnel with a custom hostname (requires Cloudflare account setup).
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

        try
        {
            _cts = new CancellationTokenSource();

            var psi = new ProcessStartInfo
            {
                FileName = CloudflaredPath,
                Arguments = $"tunnel --url https://localhost:{localPort} --no-tls-verify --hostname {hostname}",
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
                TunnelStateChanged?.Invoke(this, false);
            };

            _tunnelProcess.Start();
            _tunnelProcess.BeginOutputReadLine();
            _tunnelProcess.BeginErrorReadLine();

            CurrentTunnelUrl = $"https://{hostname}";
            Log($"Started named tunnel: {CurrentTunnelUrl}");
            TunnelStateChanged?.Invoke(this, true);
            TunnelUrlGenerated?.Invoke(this, CurrentTunnelUrl);
            return true;
        }
        catch (Exception ex)
        {
            Log($"Failed to start tunnel: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Stops the running tunnel.
    /// </summary>
    public void Stop()
    {
        try
        {
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
                FileName = CloudflaredPath,
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
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Stop();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
