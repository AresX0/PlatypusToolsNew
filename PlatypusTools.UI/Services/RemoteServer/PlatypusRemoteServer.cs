using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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

    public event EventHandler<string>? LogMessage;
    public event EventHandler<bool>? ServerStateChanged;
    public event EventHandler<RemoteClientInfo>? ClientConnected;
    public event EventHandler<string>? ClientDisconnected;

    public bool IsRunning => _isRunning;
    public int Port => _port;
    public string ServerUrl => $"https://localhost:{_port}";

    public PlatypusRemoteServer(int port = 47392)
    {
        _port = port;
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
                    });
                    webBuilder.Configure(app =>
                    {
                        app.UseRouting();
                        app.UseCors();

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
                            // Health check
                            endpoints.MapGet("/health", async context =>
                            {
                                await context.Response.WriteAsJsonAsync(new
                                {
                                    status = "healthy",
                                    timestamp = DateTime.UtcNow,
                                    version = "1.0.0"
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

                                // Security: Only allow audio files
                                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                                var allowedExtensions = new[] { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma" };
                                if (!allowedExtensions.Contains(ext))
                                {
                                    context.Response.StatusCode = 403;
                                    return;
                                }

                                // Get content type
                                var contentType = ext switch
                                {
                                    ".mp3" => "audio/mpeg",
                                    ".flac" => "audio/flac",
                                    ".wav" => "audio/wav",
                                    ".ogg" => "audio/ogg",
                                    ".m4a" => "audio/mp4",
                                    ".aac" => "audio/aac",
                                    ".wma" => "audio/x-ms-wma",
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
            Log($"Platypus Remote Server started at {ServerUrl}");
            ServerStateChanged?.Invoke(this, true);
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
