using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Embedded web server for Platypus Remote - enables remote audio control via web/PWA.
/// Hosts SignalR hub and REST API for real-time playback control.
/// </summary>
public class PlatypusRemoteServer : IDisposable
{
    private static PlatypusRemoteServer? _current;
    
    /// <summary>
    /// Gets the current active server instance (set when server is created in AudioLibraryView).
    /// </summary>
    public static PlatypusRemoteServer? Current
    {
        get => _current;
        set => _current = value;
    }

    private IHost? _host;
    private readonly int _port;
    private bool _isRunning;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// IP allowlist for remote connections. When empty, all IPs are allowed.
    /// Supports individual IPs (192.168.1.100) and CIDR notation (192.168.1.0/24).
    /// Loopback (127.0.0.1, ::1) is always allowed.
    /// </summary>
    private readonly List<string> _ipAllowlist = new();
    private bool _ipAllowlistEnabled;

    public event EventHandler<string>? LogMessage;
    public event EventHandler<bool>? ServerStateChanged;
    public event EventHandler<RemoteClientInfo>? ClientConnected;
    public event EventHandler<string>? ClientDisconnected;

    public bool IsRunning => _isRunning;
    public int Port => _port;
    public string ServerUrl => $"https://localhost:{_port}";

    /// <summary>
    /// Gets or sets whether the IP allowlist is enabled.
    /// </summary>
    public bool IpAllowlistEnabled
    {
        get => _ipAllowlistEnabled;
        set => _ipAllowlistEnabled = value;
    }

    /// <summary>
    /// Gets the current IP allowlist entries.
    /// </summary>
    public IReadOnlyList<string> IpAllowlist => _ipAllowlist;

    public PlatypusRemoteServer(int port = 47392)
    {
        _port = port;
        LoadIpAllowlist();
    }

    /// <summary>
    /// Adds an IP or CIDR range to the allowlist.
    /// </summary>
    public void AddToAllowlist(string ipOrCidr)
    {
        if (!string.IsNullOrWhiteSpace(ipOrCidr) && !_ipAllowlist.Contains(ipOrCidr, StringComparer.OrdinalIgnoreCase))
        {
            _ipAllowlist.Add(ipOrCidr.Trim());
            SaveIpAllowlist();
            Log($"Added to IP allowlist: {ipOrCidr}");
        }
    }

    /// <summary>
    /// Removes an IP or CIDR range from the allowlist.
    /// </summary>
    public void RemoveFromAllowlist(string ipOrCidr)
    {
        if (_ipAllowlist.Remove(ipOrCidr))
        {
            SaveIpAllowlist();
            Log($"Removed from IP allowlist: {ipOrCidr}");
        }
    }

    /// <summary>
    /// Clears all IP allowlist entries.
    /// </summary>
    public void ClearAllowlist()
    {
        _ipAllowlist.Clear();
        SaveIpAllowlist();
        Log("IP allowlist cleared");
    }

    /// <summary>
    /// Checks if a remote IP address is allowed.
    /// </summary>
    private bool IsIpAllowed(IPAddress? remoteIp)
    {
        if (!_ipAllowlistEnabled || _ipAllowlist.Count == 0)
            return true; // Allowlist disabled or empty = allow all

        if (remoteIp == null)
            return false;

        // Loopback is always allowed
        if (IPAddress.IsLoopback(remoteIp))
            return true;

        var remoteStr = remoteIp.ToString();

        foreach (var entry in _ipAllowlist)
        {
            // CIDR notation support (e.g., 192.168.1.0/24)
            if (entry.Contains('/'))
            {
                if (IsInCidrRange(remoteIp, entry))
                    return true;
            }
            else
            {
                // Exact IP match
                if (IPAddress.TryParse(entry, out var allowedIp) && allowedIp.Equals(remoteIp))
                    return true;
                // String match fallback
                if (string.Equals(entry, remoteStr, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if an IP address is within a CIDR range.
    /// </summary>
    private static bool IsInCidrRange(IPAddress address, string cidr)
    {
        try
        {
            var parts = cidr.Split('/');
            if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var prefixLength))
                return false;

            var networkBytes = network.GetAddressBytes();
            var addressBytes = address.GetAddressBytes();

            if (networkBytes.Length != addressBytes.Length)
                return false;

            var totalBits = networkBytes.Length * 8;
            if (prefixLength > totalBits)
                return false;

            for (int i = 0; i < networkBytes.Length; i++)
            {
                if (prefixLength >= 8)
                {
                    if (networkBytes[i] != addressBytes[i])
                        return false;
                    prefixLength -= 8;
                }
                else if (prefixLength > 0)
                {
                    var mask = (byte)(0xFF << (8 - prefixLength));
                    if ((networkBytes[i] & mask) != (addressBytes[i] & mask))
                        return false;
                    prefixLength = 0;
                }
                // Remaining bits don't need to match
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void LoadIpAllowlist()
    {
        try
        {
            var configPath = Path.Combine(SettingsManager.DataDirectory, "remote_ip_allowlist.json");
            if (File.Exists(configPath))
            {
                var json = File.ReadAllText(configPath);
                var config = System.Text.Json.JsonSerializer.Deserialize<IpAllowlistConfig>(json);
                if (config != null)
                {
                    _ipAllowlistEnabled = config.Enabled;
                    _ipAllowlist.Clear();
                    _ipAllowlist.AddRange(config.AllowedIps ?? Array.Empty<string>());
                }
            }
        }
        catch { }
    }

    private void SaveIpAllowlist()
    {
        try
        {
            var folder = SettingsManager.DataDirectory;
            Directory.CreateDirectory(folder);
            var configPath = Path.Combine(folder, "remote_ip_allowlist.json");
            var config = new IpAllowlistConfig
            {
                Enabled = _ipAllowlistEnabled,
                AllowedIps = _ipAllowlist.ToArray()
            };
            File.WriteAllText(configPath, System.Text.Json.JsonSerializer.Serialize(config,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    private class IpAllowlistConfig
    {
        public bool Enabled { get; set; }
        public string[] AllowedIps { get; set; } = Array.Empty<string>();
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;

        try
        {
            Log($"Starting Platypus Remote Server on port {_port}...");

            _host = Host.CreateDefaultBuilder()
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseKestrel(options =>
                    {
                        options.Listen(IPAddress.Any, _port, listenOptions =>
                        {
                            listenOptions.UseHttps(); // Use dev cert
                        });
                    });
                    webBuilder.ConfigureServices(services =>
                    {
                        // Add SignalR
                        services.AddSignalR(options =>
                        {
                            options.EnableDetailedErrors = true;
                        });

                        // Add CORS for PWA access
                        services.AddCors(options =>
                        {
                            options.AddDefaultPolicy(policy =>
                            {
                                policy.AllowAnyOrigin()
                                      .AllowAnyMethod()
                                      .AllowAnyHeader();
                            });
                        });

                        // Register audio service bridge
                        services.AddSingleton<IAudioServiceBridge>(sp => new AudioServiceBridge(
                            EnhancedAudioPlayerService.Instance,
                            this
                        ));

                        // Register video service bridge
                        services.AddSingleton<IVideoServiceBridge>(sp => new VideoServiceBridge());

                        // Register vault service bridge
                        services.AddSingleton<IVaultServiceBridge>(sp => new VaultServiceBridge(
                            new Vault.EncryptedVaultService()
                        ));

                        // Register image browser bridge
                        services.AddSingleton<IImageBrowserBridge>(sp => new ImageBrowserBridge());
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCors();
                        app.UseWebSockets();

                        // IP Allowlist middleware - reject connections not in allowlist
                        app.Use(async (context, next) =>
                        {
                            var remoteIp = context.Connection.RemoteIpAddress;
                            if (!IsIpAllowed(remoteIp))
                            {
                                Log($"Blocked connection from {remoteIp} - not in IP allowlist");
                                context.Response.StatusCode = 403;
                                await context.Response.WriteAsync("Forbidden: IP not in allowlist");
                                return;
                            }
                            await next();
                        });

                        // Security headers
                        app.Use(async (context, next) =>
                        {
                            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
                            context.Response.Headers.Append("X-Frame-Options", "DENY");
                            await next();
                        });

                        // Serve embedded PWA static files
                        app.Use(async (context, next) =>
                        {
                            var path = context.Request.Path.Value ?? "/";
                            if (await ServeEmbeddedResourceAsync(context, path))
                                return;
                            await next();
                        });

                        app.UseEndpoints(endpoints =>
                        {
                            // Health check — enhanced (IDEA-019)
                            endpoints.MapGet("/health", async context =>
                            {
                                var tailscale = TailscaleHelper.GetStatus();
                                var process = System.Diagnostics.Process.GetCurrentProcess();
                                var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
                                var appVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
                                
                                // Audio status
                                IAudioServiceBridge? audioBridge = null;
                                object? audioStatus = null;
                                try
                                {
                                    audioBridge = context.RequestServices.GetService<IAudioServiceBridge>();
                                    if (audioBridge != null)
                                    {
                                        var np = await audioBridge.GetNowPlayingAsync();
                                        audioStatus = new { playing = np != null, title = np?.Title, artist = np?.Artist };
                                    }
                                }
                                catch { audioStatus = new { playing = false, title = (string?)null, artist = (string?)null }; }

                                await context.Response.WriteAsJsonAsync(new
                                {
                                    status = "healthy",
                                    timestamp = DateTime.UtcNow,
                                    version = appVersion,
                                    uptime = new
                                    {
                                        totalSeconds = (int)uptime.TotalSeconds,
                                        display = $"{(int)uptime.TotalHours}h {uptime.Minutes}m {uptime.Seconds}s"
                                    },
                                    memory = new
                                    {
                                        workingSetMB = process.WorkingSet64 / (1024 * 1024),
                                        peakWorkingSetMB = process.PeakWorkingSet64 / (1024 * 1024),
                                        gcTotalMemoryMB = GC.GetTotalMemory(false) / (1024 * 1024)
                                    },
                                    system = new
                                    {
                                        os = Environment.OSVersion.ToString(),
                                        processors = Environment.ProcessorCount,
                                        is64Bit = Environment.Is64BitProcess,
                                        dotnetVersion = Environment.Version.ToString()
                                    },
                                    server = new
                                    {
                                        port = _port,
                                        portableMode = SettingsManager.IsPortableMode
                                    },
                                    audio = audioStatus,
                                    tailscale = new
                                    {
                                        installed = tailscale.IsInstalled,
                                        connected = tailscale.IsConnected,
                                        ip = tailscale.TailscaleIp,
                                        remoteUrl = TailscaleHelper.GetRemoteUrl(_port)
                                    }
                                });
                            });

                            // Detailed health — includes disk space and GC stats (IDEA-019)
                            endpoints.MapGet("/api/health/detailed", async context =>
                            {
                                var process = System.Diagnostics.Process.GetCurrentProcess();
                                var drives = new List<object>();
                                foreach (var d in DriveInfo.GetDrives())
                                {
                                    try
                                    {
                                        if (d.IsReady)
                                            drives.Add(new { name = d.Name, totalGB = d.TotalSize / (1024L * 1024 * 1024), freeGB = d.AvailableFreeSpace / (1024L * 1024 * 1024), format = d.DriveFormat });
                                    }
                                    catch { /* skip inaccessible drives */ }
                                }

                                var gcInfo = GC.GetGCMemoryInfo();
                                await context.Response.WriteAsJsonAsync(new
                                {
                                    status = "healthy",
                                    timestamp = DateTime.UtcNow,
                                    process = new
                                    {
                                        id = process.Id,
                                        name = process.ProcessName,
                                        threads = process.Threads.Count,
                                        handles = process.HandleCount,
                                        totalProcessorTimeSec = (int)process.TotalProcessorTime.TotalSeconds,
                                        workingSetMB = process.WorkingSet64 / (1024 * 1024),
                                        privateMemoryMB = process.PrivateMemorySize64 / (1024 * 1024)
                                    },
                                    gc = new
                                    {
                                        gen0Collections = GC.CollectionCount(0),
                                        gen1Collections = GC.CollectionCount(1),
                                        gen2Collections = GC.CollectionCount(2),
                                        totalAllocatedMB = GC.GetTotalAllocatedBytes(false) / (1024 * 1024),
                                        heapSizeMB = gcInfo.HeapSizeBytes / (1024 * 1024)
                                    },
                                    drives
                                });
                            });

                            // Playback API
                            endpoints.MapGet("/api/playback/now-playing", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                var nowPlaying = await bridge.GetNowPlayingAsync();
                                await context.Response.WriteAsJsonAsync(nowPlaying);
                            });

                            endpoints.MapPost("/api/playback/play", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.PlayAsync();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/pause", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.PauseAsync();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/play-pause", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.PlayPauseAsync();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/next", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.NextAsync();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/previous", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.PreviousAsync();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/seek/{position:double}", async context =>
                            {
                                var position = double.Parse(context.Request.RouteValues["position"]?.ToString() ?? "0");
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.SeekAsync(TimeSpan.FromSeconds(position));
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/volume/{level:double}", async context =>
                            {
                                var level = double.Parse(context.Request.RouteValues["level"]?.ToString() ?? "0.5");
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.SetVolumeAsync(level);
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapGet("/api/playback/queue", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                var queue = await bridge.GetQueueAsync();
                                await context.Response.WriteAsJsonAsync(queue);
                            });

                            endpoints.MapPost("/api/playback/queue/play/{index:int}", async context =>
                            {
                                var index = int.Parse(context.Request.RouteValues["index"]?.ToString() ?? "0");
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.PlayQueueItemAsync(index);
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/shuffle", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.ToggleShuffleAsync();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/playback/repeat", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                await bridge.ToggleRepeatAsync();
                                context.Response.StatusCode = 204;
                            });

                            // Library API
                            endpoints.MapGet("/api/library", async context =>
                            {
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                var library = await bridge.GetLibraryAsync();
                                await context.Response.WriteAsJsonAsync(library);
                            });

                            endpoints.MapGet("/api/library/search", async context =>
                            {
                                var query = context.Request.Query["q"].ToString();
                                var bridge = context.RequestServices.GetRequiredService<IAudioServiceBridge>();
                                var results = await bridge.SearchLibraryAsync(query);
                                await context.Response.WriteAsJsonAsync(results);
                            });

                            // Streaming API
                            endpoints.MapGet("/api/stream", async context =>
                            {
                                var filePath = context.Request.Query["path"].ToString();
                                if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
                                {
                                    context.Response.StatusCode = 404;
                                    return;
                                }

                                // Security: Only allow audio and video files
                                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                                var allowedExtensions = new[]
                                {
                                    // Audio
                                    ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma",
                                    // Video
                                    ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
                                    ".mpeg", ".mpg", ".m4v", ".3gp", ".ts", ".m2ts"
                                };
                                if (!allowedExtensions.Contains(ext))
                                {
                                    context.Response.StatusCode = 403;
                                    return;
                                }

                                // Get content type
                                var contentType = ext switch
                                {
                                    // Audio types
                                    ".mp3" => "audio/mpeg",
                                    ".flac" => "audio/flac",
                                    ".wav" => "audio/wav",
                                    ".ogg" => "audio/ogg",
                                    ".m4a" => "audio/mp4",
                                    ".aac" => "audio/aac",
                                    ".wma" => "audio/x-ms-wma",
                                    // Video types
                                    ".mp4" => "video/mp4",
                                    ".mkv" => "video/x-matroska",
                                    ".avi" => "video/x-msvideo",
                                    ".mov" => "video/quicktime",
                                    ".wmv" => "video/x-ms-wmv",
                                    ".flv" => "video/x-flv",
                                    ".webm" => "video/webm",
                                    ".mpeg" or ".mpg" => "video/mpeg",
                                    ".m4v" => "video/mp4",
                                    ".3gp" => "video/3gpp",
                                    ".ts" or ".m2ts" => "video/mp2t",
                                    _ => "application/octet-stream"
                                };

                                context.Response.ContentType = contentType;
                                context.Response.Headers.Append("Accept-Ranges", "bytes");

                                var fileInfo = new System.IO.FileInfo(filePath);
                                var fileLength = fileInfo.Length;

                                // Handle range requests for seeking
                                if (context.Request.Headers.ContainsKey("Range"))
                                {
                                    var rangeHeader = context.Request.Headers["Range"].ToString();
                                    var range = rangeHeader.Replace("bytes=", "").Split('-');
                                    var start = long.Parse(range[0]);
                                    var end = range.Length > 1 && !string.IsNullOrEmpty(range[1]) 
                                        ? long.Parse(range[1]) 
                                        : fileLength - 1;

                                    context.Response.StatusCode = 206;
                                    context.Response.Headers.Append("Content-Range", $"bytes {start}-{end}/{fileLength}");
                                    context.Response.Headers.ContentLength = end - start + 1;

                                    using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                                    fs.Seek(start, SeekOrigin.Begin);
                                    var buffer = new byte[Math.Min(end - start + 1, 64 * 1024)];
                                    var bytesRemaining = end - start + 1;
                                    while (bytesRemaining > 0)
                                    {
                                        var toRead = (int)Math.Min(buffer.Length, bytesRemaining);
                                        var bytesRead = await fs.ReadAsync(buffer.AsMemory(0, toRead));
                                        if (bytesRead == 0) break;
                                        await context.Response.Body.WriteAsync(buffer.AsMemory(0, bytesRead));
                                        bytesRemaining -= bytesRead;
                                    }
                                }
                                else
                                {
                                    context.Response.Headers.ContentLength = fileLength;
                                    await context.Response.SendFileAsync(filePath);
                                }
                            });

                            // SignalR hub for real-time updates
                            endpoints.MapHub<PlatypusHub>("/hub/platypus");

                            // Video Library API
                            endpoints.MapGet("/api/video/library", async context =>
                            {
                                var videoBridge = context.RequestServices.GetRequiredService<IVideoServiceBridge>();
                                var library = await videoBridge.GetVideoLibraryAsync();
                                await context.Response.WriteAsJsonAsync(library);
                            });

                            endpoints.MapGet("/api/video/search", async context =>
                            {
                                var query = context.Request.Query["q"].ToString();
                                var videoBridge = context.RequestServices.GetRequiredService<IVideoServiceBridge>();
                                var results = await videoBridge.SearchVideoLibraryAsync(query);
                                await context.Response.WriteAsJsonAsync(results);
                            });

                            endpoints.MapGet("/api/video/folders", async context =>
                            {
                                var videoBridge = context.RequestServices.GetRequiredService<IVideoServiceBridge>();
                                var folders = await videoBridge.GetVideoFoldersAsync();
                                await context.Response.WriteAsJsonAsync(folders);
                            });

                            endpoints.MapPost("/api/video/rescan", async context =>
                            {
                                var videoBridge = context.RequestServices.GetRequiredService<IVideoServiceBridge>();
                                await videoBridge.RescanLibraryAsync();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapGet("/api/video/thumbnail", async context =>
                            {
                                var filePath = context.Request.Query["path"].ToString();
                                if (string.IsNullOrEmpty(filePath))
                                {
                                    context.Response.StatusCode = 400;
                                    return;
                                }
                                var videoBridge = context.RequestServices.GetRequiredService<IVideoServiceBridge>();
                                var thumbnail = await videoBridge.GetVideoThumbnailAsync(filePath);
                                if (thumbnail == null)
                                {
                                    context.Response.StatusCode = 404;
                                    return;
                                }
                                await context.Response.WriteAsJsonAsync(new { thumbnail });
                            });

                            // ── Photos API endpoints ──

                            endpoints.MapGet("/api/photos/folders", async context =>
                            {
                                var imgBridge = context.RequestServices.GetRequiredService<IImageBrowserBridge>();
                                var folders = await imgBridge.GetImageFoldersAsync();
                                await context.Response.WriteAsJsonAsync(folders);
                            });

                            endpoints.MapGet("/api/photos", async context =>
                            {
                                var imgBridge = context.RequestServices.GetRequiredService<IImageBrowserBridge>();
                                var folder = context.Request.Query["folder"].ToString();
                                int.TryParse(context.Request.Query["page"], out var page);
                                int.TryParse(context.Request.Query["pageSize"], out var pageSize);
                                if (pageSize <= 0 || pageSize > 200) pageSize = 50;
                                var search = context.Request.Query["q"].ToString();
                                var result = await imgBridge.GetImagesAsync(
                                    string.IsNullOrEmpty(folder) ? null : folder,
                                    page, pageSize,
                                    string.IsNullOrEmpty(search) ? null : search);
                                await context.Response.WriteAsJsonAsync(result);
                            });

                            endpoints.MapGet("/api/photos/thumbnail", async context =>
                            {
                                var filePath = context.Request.Query["path"].ToString();
                                if (string.IsNullOrEmpty(filePath))
                                {
                                    context.Response.StatusCode = 400;
                                    return;
                                }
                                int.TryParse(context.Request.Query["size"], out var size);
                                if (size <= 0) size = 200;
                                var imgBridge = context.RequestServices.GetRequiredService<IImageBrowserBridge>();
                                var bytes = await imgBridge.GetThumbnailBytesAsync(filePath, size);
                                if (bytes == null)
                                {
                                    context.Response.StatusCode = 404;
                                    return;
                                }
                                context.Response.ContentType = "image/jpeg";
                                context.Response.Headers["Cache-Control"] = "public, max-age=86400";
                                await context.Response.Body.WriteAsync(bytes);
                            });

                            endpoints.MapGet("/api/photos/full", async context =>
                            {
                                var filePath = context.Request.Query["path"].ToString();
                                if (string.IsNullOrEmpty(filePath))
                                {
                                    context.Response.StatusCode = 400;
                                    return;
                                }
                                var imgBridge = context.RequestServices.GetRequiredService<IImageBrowserBridge>();
                                var bytes = await imgBridge.GetFullImageBytesAsync(filePath);
                                if (bytes == null)
                                {
                                    context.Response.StatusCode = 404;
                                    return;
                                }
                                var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                                context.Response.ContentType = ext switch
                                {
                                    ".png" => "image/png",
                                    ".gif" => "image/gif",
                                    ".webp" => "image/webp",
                                    ".bmp" => "image/bmp",
                                    ".ico" => "image/x-icon",
                                    ".tiff" => "image/tiff",
                                    _ => "image/jpeg"
                                };
                                context.Response.Headers["Cache-Control"] = "public, max-age=3600";
                                await context.Response.Body.WriteAsync(bytes);
                            });

                            endpoints.MapGet("/api/photos/info", async context =>
                            {
                                var filePath = context.Request.Query["path"].ToString();
                                if (string.IsNullOrEmpty(filePath))
                                {
                                    context.Response.StatusCode = 400;
                                    return;
                                }
                                var imgBridge = context.RequestServices.GetRequiredService<IImageBrowserBridge>();
                                var info = imgBridge.GetImageInfo(filePath);
                                if (info == null)
                                {
                                    context.Response.StatusCode = 404;
                                    return;
                                }
                                await context.Response.WriteAsJsonAsync(info);
                            });

                            endpoints.MapGet("/api/photos/cache", async context =>
                            {
                                var imgBridge = context.RequestServices.GetRequiredService<IImageBrowserBridge>();
                                await context.Response.WriteAsJsonAsync(imgBridge.GetCacheStats());
                            });

                            endpoints.MapPost("/api/photos/cache/clear", async context =>
                            {
                                var imgBridge = context.RequestServices.GetRequiredService<IImageBrowserBridge>();
                                imgBridge.ClearCache();
                                context.Response.StatusCode = 204;
                            });

                            // ── Vault API endpoints ──

                            endpoints.MapGet("/api/vault/status", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                await context.Response.WriteAsJsonAsync(vault.GetStatus());
                            });

                            endpoints.MapPost("/api/vault/unlock", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                var body = await System.Text.Json.JsonSerializer.DeserializeAsync<UnlockRequest>(
                                    context.Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (body?.MasterPassword == null)
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsJsonAsync(new { error = "Master password required" });
                                    return;
                                }
                                var ok = await vault.UnlockAsync(body.MasterPassword);
                                if (!ok)
                                {
                                    context.Response.StatusCode = 401;
                                    await context.Response.WriteAsJsonAsync(new { error = "Invalid master password" });
                                    return;
                                }
                                await context.Response.WriteAsJsonAsync(vault.GetStatus());
                            });

                            endpoints.MapPost("/api/vault/lock", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                vault.Lock();
                                context.Response.StatusCode = 204;
                            });

                            endpoints.MapPost("/api/vault/mfa/verify", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsMfaPending)
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsJsonAsync(new { error = "No MFA verification pending" });
                                    return;
                                }
                                var body = await System.Text.Json.JsonSerializer.DeserializeAsync<MfaVerifyRequest>(
                                    context.Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (string.IsNullOrWhiteSpace(body?.Code))
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsJsonAsync(new { error = "MFA code required" });
                                    return;
                                }
                                if (!vault.VerifyMfa(body.Code.Trim()))
                                {
                                    context.Response.StatusCode = 401;
                                    await context.Response.WriteAsJsonAsync(new { error = "Invalid MFA code" });
                                    return;
                                }
                                await context.Response.WriteAsJsonAsync(vault.GetStatus());
                            });

                            endpoints.MapPost("/api/vault/mfa/cancel", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                vault.CancelMfa();
                                await context.Response.WriteAsJsonAsync(vault.GetStatus());
                            });

                            endpoints.MapGet("/api/vault/items", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var search = context.Request.Query["q"].ToString();
                                var folderId = context.Request.Query["folderId"].ToString();
                                var type = context.Request.Query["type"].ToString();
                                var items = vault.GetItems(
                                    string.IsNullOrEmpty(search) ? null : search,
                                    string.IsNullOrEmpty(folderId) ? null : folderId,
                                    string.IsNullOrEmpty(type) ? null : type);
                                await context.Response.WriteAsJsonAsync(items);
                            });

                            endpoints.MapGet("/api/vault/items/{id}", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var id = context.Request.RouteValues["id"]?.ToString();
                                if (id == null) { context.Response.StatusCode = 400; return; }
                                var item = vault.GetItem(id);
                                if (item == null) { context.Response.StatusCode = 404; return; }
                                await context.Response.WriteAsJsonAsync(item);
                            });

                            endpoints.MapPost("/api/vault/items", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var dto = await System.Text.Json.JsonSerializer.DeserializeAsync<VaultItemDto>(
                                    context.Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (dto == null) { context.Response.StatusCode = 400; return; }
                                var result = await vault.AddItemAsync(dto);
                                context.Response.StatusCode = 201;
                                await context.Response.WriteAsJsonAsync(result);
                            });

                            endpoints.MapPut("/api/vault/items/{id}", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var id = context.Request.RouteValues["id"]?.ToString();
                                if (id == null) { context.Response.StatusCode = 400; return; }
                                var dto = await System.Text.Json.JsonSerializer.DeserializeAsync<VaultItemDto>(
                                    context.Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (dto == null) { context.Response.StatusCode = 400; return; }
                                var result = await vault.UpdateItemAsync(id, dto);
                                if (result == null) { context.Response.StatusCode = 404; return; }
                                await context.Response.WriteAsJsonAsync(result);
                            });

                            endpoints.MapDelete("/api/vault/items/{id}", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var id = context.Request.RouteValues["id"]?.ToString();
                                if (id == null) { context.Response.StatusCode = 400; return; }
                                var ok = await vault.DeleteItemAsync(id);
                                context.Response.StatusCode = ok ? 204 : 404;
                            });

                            endpoints.MapGet("/api/vault/folders", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                await context.Response.WriteAsJsonAsync(vault.GetFolders());
                            });

                            endpoints.MapGet("/api/vault/totp/{id}", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var id = context.Request.RouteValues["id"]?.ToString();
                                if (id == null) { context.Response.StatusCode = 400; return; }
                                var code = vault.GetTotpCode(id);
                                if (code == null) { context.Response.StatusCode = 404; return; }
                                await context.Response.WriteAsJsonAsync(new { code, remaining = Vault.TotpService.GetRemainingSeconds() });
                            });

                            endpoints.MapGet("/api/vault/authenticator", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                await context.Response.WriteAsJsonAsync(vault.GetAuthenticatorEntries());
                            });

                            endpoints.MapPost("/api/vault/authenticator", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var body = await System.Text.Json.JsonSerializer.DeserializeAsync<AddAuthRequest>(
                                    context.Request.Body, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                                if (body?.OtpAuthUri == null) { context.Response.StatusCode = 400; return; }
                                var result = await vault.AddAuthenticatorEntryAsync(body.OtpAuthUri);
                                if (result == null)
                                {
                                    context.Response.StatusCode = 400;
                                    await context.Response.WriteAsJsonAsync(new { error = "Invalid OTP Auth URI" });
                                    return;
                                }
                                context.Response.StatusCode = 201;
                                await context.Response.WriteAsJsonAsync(result);
                            });

                            endpoints.MapDelete("/api/vault/authenticator/{id}", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                if (!vault.IsUnlocked) { context.Response.StatusCode = 401; return; }
                                var id = context.Request.RouteValues["id"]?.ToString();
                                if (id == null) { context.Response.StatusCode = 400; return; }
                                var ok = await vault.DeleteAuthenticatorEntryAsync(id);
                                context.Response.StatusCode = ok ? 204 : 404;
                            });

                            endpoints.MapGet("/api/vault/generate", async context =>
                            {
                                var vault = context.RequestServices.GetRequiredService<IVaultServiceBridge>();
                                int.TryParse(context.Request.Query["length"], out var length);
                                if (length < 8 || length > 128) length = 20;
                                var upper = context.Request.Query["upper"] != "false";
                                var lower = context.Request.Query["lower"] != "false";
                                var numbers = context.Request.Query["numbers"] != "false";
                                var special = context.Request.Query["special"] != "false";
                                var password = vault.GeneratePassword(length, upper, lower, numbers, special);
                                await context.Response.WriteAsJsonAsync(new { password });
                            });
                        });
                    });
                })
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
                })
                .Build();

            await _host.StartAsync(_cts.Token);
            _isRunning = true;

            // Wire up IHubContext to AudioServiceBridge for background broadcasting
            // IHubContext<PlatypusHub> is a singleton that's always valid (unlike transient Hub instances)
            var bridge = _host.Services.GetRequiredService<IAudioServiceBridge>();
            if (bridge is AudioServiceBridge audioBridge)
            {
                var hubContext = _host.Services.GetRequiredService<IHubContext<PlatypusHub>>();
                audioBridge.SetHubContext(hubContext);
            }

            Log($"Platypus Remote Server started at {ServerUrl}");
            ServerStateChanged?.Invoke(this, true);

            // Check Tailscale availability
            var tailscaleStatus = TailscaleHelper.GetStatus();
            if (tailscaleStatus.IsConnected)
            {
                var tailscaleUrl = TailscaleHelper.GetRemoteUrl(_port);
                Log($"Tailscale detected! Remote access available at: {tailscaleUrl}");
            }
            else if (tailscaleStatus.IsInstalled)
            {
                Log("Tailscale installed but not connected. Connect Tailscale for remote access.");
            }
        }
        catch (Exception ex)
        {
            Log($"Failed to start server: {ex.Message}");
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!_isRunning || _host == null) return;

        try
        {
            Log("Stopping Platypus Remote Server...");
            _cts.Cancel();
            await _host.StopAsync();
            _isRunning = false;
            Log("Platypus Remote Server stopped");
            ServerStateChanged?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            Log($"Error stopping server: {ex.Message}");
        }
    }

    internal void OnClientConnected(RemoteClientInfo client)
    {
        Log($"Client connected: {client.UserAgent} from {client.IpAddress}");
        ClientConnected?.Invoke(this, client);
    }

    internal void OnClientDisconnected(string connectionId)
    {
        Log($"Client disconnected: {connectionId}");
        ClientDisconnected?.Invoke(this, connectionId);
    }

    private void Log(string message)
    {
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
        System.Diagnostics.Debug.WriteLine($"[PlatypusRemote] {message}");
    }

    /// <summary>
    /// Serves PWA files from embedded resources.
    /// </summary>
    private static async Task<bool> ServeEmbeddedResourceAsync(HttpContext context, string path)
    {
        // Map URL paths to embedded resource names
        var resourceMap = new Dictionary<string, (string ResourceName, string ContentType)>
        {
            ["/"] = ("PlatypusTools.UI.Resources.Remote.index.html", "text/html"),
            ["/index.html"] = ("PlatypusTools.UI.Resources.Remote.index.html", "text/html"),
            ["/app.js"] = ("PlatypusTools.UI.Resources.Remote.app.js", "application/javascript"),
            ["/manifest.json"] = ("PlatypusTools.UI.Resources.Remote.manifest.json", "application/json"),
            ["/sw.js"] = ("PlatypusTools.UI.Resources.Remote.sw.js", "application/javascript")
        };

        // Handle icon requests with a placeholder
        if (path == "/icon-192.png" || path == "/icon-512.png")
        {
            return await ServeIconAsync(context);
        }

        if (!resourceMap.TryGetValue(path, out var resource))
            return false;

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resource.ResourceName);
        
        if (stream == null)
            return false;

        context.Response.ContentType = resource.ContentType;
        context.Response.Headers.Append("Cache-Control", "no-cache");
        await stream.CopyToAsync(context.Response.Body);
        return true;
    }

    /// <summary>
    /// Generates a simple PNG icon (platypus emoji placeholder).
    /// </summary>
    private static async Task<bool> ServeIconAsync(HttpContext context)
    {
        // Return a simple 1x1 transparent PNG for now
        // The PWA will still work, just without a custom icon
        context.Response.ContentType = "image/png";
        var emptyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        await context.Response.Body.WriteAsync(emptyPng);
        return true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _host?.Dispose();
        _cts.Dispose();
    }
}

/// <summary>
/// Information about a connected remote client.
/// </summary>
public class RemoteClientInfo
{
    public string ConnectionId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Request body for vault unlock.</summary>
internal class UnlockRequest
{
    public string? MasterPassword { get; set; }
}

/// <summary>Request body for adding an authenticator entry via OTP Auth URI.</summary>
internal class AddAuthRequest
{
    public string? OtpAuthUri { get; set; }
}

/// <summary>Request body for MFA code verification.</summary>
internal class MfaVerifyRequest
{
    public string? Code { get; set; }
}


