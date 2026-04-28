using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web;
using PlatypusTools.Remote.Server.Hubs;
using PlatypusTools.Remote.Server.Models;
using PlatypusTools.Remote.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Generate or load a self-signed certificate for HTTPS
var certPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "PlatypusTools", "server-cert.pfx");
Directory.CreateDirectory(Path.GetDirectoryName(certPath)!);

X509Certificate2? serverCert = null;
if (File.Exists(certPath))
{
    try
    {
#pragma warning disable CA2000 // Certificate is intentionally long-lived, used by Kestrel for HTTPS
        var existing = new X509Certificate2(certPath, (string?)null, X509KeyStorageFlags.Exportable);
#pragma warning restore CA2000
        if (existing.NotAfter > DateTime.UtcNow.AddDays(30))
            serverCert = existing;
        else
            existing.Dispose();
    }
    catch { }
}
if (serverCert == null)
{
    using var rsa = RSA.Create(2048);
    var req = new CertificateRequest("CN=PlatypusTools Remote Server", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
    req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection { new("1.3.6.1.5.5.7.3.1") }, false));
    var san = new SubjectAlternativeNameBuilder();
    san.AddDnsName("localhost");
    san.AddDnsName(Environment.MachineName);
    san.AddIpAddress(System.Net.IPAddress.Loopback);
    san.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
    req.CertificateExtensions.Add(san.Build());
    var tmp = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(730));
#pragma warning disable CA2000 // Certificate is intentionally long-lived, used by Kestrel for HTTPS
    serverCert = new X509Certificate2(tmp.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
#pragma warning restore CA2000
    try { File.WriteAllBytes(certPath, serverCert.Export(X509ContentType.Pfx)); } catch { }
}

// Configure Kestrel for port 47392 with self-signed cert
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(47392, listenOptions =>
    {
        listenOptions.UseHttps(serverCert);
    });
});

// Add Entra ID (Azure AD) authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

builder.Services.AddAuthorization();

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add CORS for Blazor PWA
builder.Services.AddCors(options =>
{
    options.AddPolicy("PlatypusPWA", policy =>
    {
        policy.WithOrigins(
                builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? new[] { "https://localhost:47392" })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Add rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("authenticated", limiter =>
    {
        limiter.PermitLimit = 100;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 10;
    });
});

// Register services
builder.Services.AddSingleton<IRemoteAudioService, RemoteAudioService>();
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddSingleton<IVaultService, VaultService>();

builder.Services.AddOpenApi();

var app = builder.Build();

// Security headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval' https://unpkg.com; style-src 'self' 'unsafe-inline'; connect-src 'self' wss:; media-src 'self' blob:; img-src 'self' data: blob:;";
    
    if (context.Request.IsHttps)
    {
        context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
    }
    
    await next();
});

app.UseHttpsRedirection();
app.UseCors("PlatypusPWA");

// Serve Blazor WebAssembly client
app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map SignalR hub
app.MapHub<PlatypusHub>("/hub/platypus").RequireAuthorization();

// Health check (no auth required)
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }))
    .WithName("HealthCheck");

// API endpoints (all require authentication)
var api = app.MapGroup("/api").RequireAuthorization();

// Now Playing
api.MapGet("/nowplaying", async (IRemoteAudioService audioService) =>
{
    var nowPlaying = await audioService.GetNowPlayingAsync();
    return Results.Ok(nowPlaying);
}).WithName("GetNowPlaying");

// Playback control
api.MapPost("/playback/play", async (IRemoteAudioService audioService) =>
{
    await audioService.PlayAsync();
    return Results.Ok();
}).WithName("Play");

api.MapPost("/playback/pause", async (IRemoteAudioService audioService) =>
{
    await audioService.PauseAsync();
    return Results.Ok();
}).WithName("Pause");

api.MapPost("/playback/stop", async (IRemoteAudioService audioService) =>
{
    await audioService.StopAsync();
    return Results.Ok();
}).WithName("Stop");

api.MapPost("/playback/next", async (IRemoteAudioService audioService) =>
{
    await audioService.NextTrackAsync();
    return Results.Ok();
}).WithName("NextTrack");

api.MapPost("/playback/previous", async (IRemoteAudioService audioService) =>
{
    await audioService.PreviousTrackAsync();
    return Results.Ok();
}).WithName("PreviousTrack");

api.MapPost("/playback/seek", async (double position, IRemoteAudioService audioService) =>
{
    await audioService.SeekAsync(position);
    return Results.Ok();
}).WithName("Seek");

api.MapPost("/playback/volume", async (double volume, IRemoteAudioService audioService) =>
{
    await audioService.SetVolumeAsync(volume);
    return Results.Ok();
}).WithName("SetVolume");

// Queue management
api.MapGet("/queue", async (IRemoteAudioService audioService) =>
{
    var queue = await audioService.GetQueueAsync();
    return Results.Ok(queue);
}).WithName("GetQueue");

api.MapPost("/queue/clear", async (IRemoteAudioService audioService) =>
{
    await audioService.ClearQueueAsync();
    return Results.Ok();
}).WithName("ClearQueue");

api.MapPost("/queue/shuffle", async (IRemoteAudioService audioService) =>
{
    await audioService.ShuffleQueueAsync();
    return Results.Ok();
}).WithName("ShuffleQueue");

api.MapDelete("/queue/{index:int}", async (int index, IRemoteAudioService audioService) =>
{
    await audioService.RemoveFromQueueAsync(index);
    return Results.Ok();
}).WithName("RemoveFromQueue");

api.MapPost("/queue/play/{index:int}", async (int index, IRemoteAudioService audioService) =>
{
    await audioService.PlayQueueItemAsync(index);
    return Results.Ok();
}).WithName("PlayQueueItem");

// Library browse
api.MapGet("/library/folders", async (IRemoteAudioService audioService) =>
{
    var folders = await audioService.GetLibraryFoldersAsync();
    return Results.Ok(folders);
}).WithName("GetLibraryFolders");

api.MapGet("/library/files", async (string path, IRemoteAudioService audioService) =>
{
    var files = await audioService.GetLibraryFilesAsync(path);
    return Results.Ok(files);
}).WithName("GetLibraryFiles");

api.MapPost("/library/add", async (string path, IRemoteAudioService audioService) =>
{
    await audioService.AddToQueueAsync(path);
    return Results.Ok();
}).WithName("AddToQueue");

// Audio Streaming - stream audio file to phone for remote playback
api.MapGet("/stream", (string path, HttpContext context) =>
{
    if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        return Results.NotFound("File not found");

    var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
    var contentType = extension switch
    {
        ".mp3" => "audio/mpeg",
        ".wav" => "audio/wav",
        ".flac" => "audio/flac",
        ".ogg" => "audio/ogg",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".wma" => "audio/x-ms-wma",
        ".opus" => "audio/opus",
        _ => "application/octet-stream"
    };

    var fileStream = System.IO.File.OpenRead(path);
    return Results.File(fileStream, contentType, enableRangeProcessing: true);
}).WithName("StreamAudio");

// Session management
api.MapGet("/sessions", async (ISessionManager sessionManager, HttpContext context) =>
{
    var userId = context.User.FindFirst("oid")?.Value ?? context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();
    
    var sessions = await sessionManager.GetUserSessionsAsync(userId);
    return Results.Ok(sessions);
}).WithName("GetSessions");

api.MapDelete("/sessions/{sessionId}", async (string sessionId, ISessionManager sessionManager, HttpContext context) =>
{
    var userId = context.User.FindFirst("oid")?.Value ?? context.User.FindFirst("sub")?.Value;
    if (string.IsNullOrEmpty(userId))
        return Results.Unauthorized();
    
    await sessionManager.EndSessionAsync(userId, sessionId);
    return Results.Ok();
}).WithName("EndSession");

// Fallback to Blazor client for SPA routing
app.MapFallbackToFile("index.html");

// Vault API endpoints
var vault = api.MapGroup("/vault");

vault.MapGet("/status", (IVaultService vaultService) =>
    Results.Ok(vaultService.GetStatus()))
    .WithName("VaultStatus");

vault.MapPost("/unlock", (VaultUnlockRequest req, IVaultService vaultService) =>
{
    var success = vaultService.Unlock(req.MasterPassword);
    return success ? Results.Ok(vaultService.GetStatus()) : Results.Unauthorized();
}).WithName("VaultUnlock");

vault.MapPost("/lock", (IVaultService vaultService) =>
{
    vaultService.Lock();
    return Results.Ok();
}).WithName("VaultLock");

vault.MapGet("/items", (string? filter, int? type, string? folderId, IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    return Results.Ok(vaultService.GetItems(filter, type, folderId));
}).WithName("VaultGetItems");

vault.MapGet("/items/{id}", (string id, IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    var item = vaultService.GetItem(id);
    return item != null ? Results.Ok(item) : Results.NotFound();
}).WithName("VaultGetItem");

vault.MapPost("/items", (AddVaultItemRequest req, IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    var item = vaultService.AddItem(req);
    return Results.Ok(item);
}).WithName("VaultAddItem");

vault.MapDelete("/items/{id}", (string id, IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    return vaultService.DeleteItem(id) ? Results.Ok() : Results.NotFound();
}).WithName("VaultDeleteItem");

vault.MapGet("/folders", (IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    return Results.Ok(vaultService.GetFolders());
}).WithName("VaultGetFolders");

vault.MapGet("/authenticator", (IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    return Results.Ok(vaultService.GetAuthenticatorEntries());
}).WithName("VaultGetAuthenticator");

vault.MapPost("/authenticator", (AddAuthenticatorRequest req, IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    var entry = vaultService.AddAuthenticatorEntry(req);
    return entry != null ? Results.Ok(entry) : Results.BadRequest("Invalid entry");
}).WithName("VaultAddAuthenticator");

vault.MapDelete("/authenticator/{id}", (string id, IVaultService vaultService) =>
{
    if (!vaultService.IsUnlocked) return Results.Unauthorized();
    return vaultService.DeleteAuthenticatorEntry(id) ? Results.Ok() : Results.NotFound();
}).WithName("VaultDeleteAuthenticator");

vault.MapPost("/generate-password", (GeneratePasswordRequest req, IVaultService vaultService) =>
    Results.Ok(new { password = vaultService.GeneratePassword(req) }))
    .WithName("VaultGeneratePassword");

// ─────────────────────────────────────────────────────────────────────────
// Phase 2.2 — Local /api/v1 surface with file-based Bearer token auth.
// Token lives at %APPDATA%/PlatypusTools/api-token.txt (created on first
// access if missing). This group is INDEPENDENT of the Entra-protected
// /api routes above so local automation/scripting can hit a stable surface.
// ─────────────────────────────────────────────────────────────────────────
static string GetOrCreateLocalApiToken()
{
    var dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlatypusTools");
    Directory.CreateDirectory(dir);
    var path = Path.Combine(dir, "api-token.txt");
    if (!File.Exists(path) || (new FileInfo(path)).Length < 16)
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        var token = Convert.ToBase64String(bytes).Replace("/", "_").Replace("+", "-").TrimEnd('=');
        File.WriteAllText(path, token);
    }
    return File.ReadAllText(path).Trim();
}

var v1 = app.MapGroup("/api/v1");
v1.AddEndpointFilter(async (ctx, next) =>
{
    var auth = ctx.HttpContext.Request.Headers["Authorization"].ToString();
    const string prefix = "Bearer ";
    if (!auth.StartsWith(prefix, StringComparison.Ordinal))
        return Results.Unauthorized();
    var presented = auth.Substring(prefix.Length).Trim();
    var expected = GetOrCreateLocalApiToken();
    // Constant-time compare
    var ok = presented.Length == expected.Length;
    for (int i = 0; i < Math.Min(presented.Length, expected.Length); i++)
        ok &= presented[i] == expected[i];
    if (!ok) return Results.Unauthorized();
    return await next(ctx);
});

v1.MapGet("/health", () => Results.Ok(new { status = "healthy", api = "v1", timestamp = DateTime.UtcNow }));
v1.MapGet("/info", () => Results.Ok(new
{
    product = "PlatypusTools",
    api = "v1",
    machine = Environment.MachineName,
    user = Environment.UserName,
    osVersion = Environment.OSVersion.VersionString
}));
v1.MapGet("/audio/nowplaying", async (IRemoteAudioService a) => Results.Ok(await a.GetNowPlayingAsync()));
v1.MapGet("/audio/queue", async (IRemoteAudioService a) => Results.Ok(await a.GetQueueAsync()));
v1.MapPost("/audio/play", async (IRemoteAudioService a) => { await a.PlayAsync(); return Results.NoContent(); });
v1.MapPost("/audio/pause", async (IRemoteAudioService a) => { await a.PauseAsync(); return Results.NoContent(); });
v1.MapPost("/audio/next", async (IRemoteAudioService a) => { await a.NextTrackAsync(); return Results.NoContent(); });
v1.MapPost("/audio/previous", async (IRemoteAudioService a) => { await a.PreviousTrackAsync(); return Results.NoContent(); });
v1.MapGet("/vault/items", (IVaultService v) =>
    !v.IsUnlocked ? Results.Unauthorized() : Results.Ok(v.GetItems()));

// Phase 2.1 / 4.1 — Forensics surface for the $Platypus.Forensics PowerShell/Python proxy.
// Reads cached threat-feed indicators that the UI's ThreatFeedScheduler writes to
// %APPDATA%/PlatypusTools/threat-cache/. Returns an empty list when no cache is present.
v1.MapGet("/forensics/iocs", () =>
{
    try
    {
        var cacheDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "threat-cache");
        if (!System.IO.Directory.Exists(cacheDir)) return Results.Ok(Array.Empty<object>());
        var list = new System.Collections.Generic.List<object>();
        foreach (var f in System.IO.Directory.EnumerateFiles(cacheDir, "*.json"))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(f));
                list.Add(new { source = System.IO.Path.GetFileNameWithoutExtension(f), data = doc.RootElement.Clone() });
            }
            catch { }
        }
        return Results.Ok(list);
    }
    catch { return Results.Ok(Array.Empty<object>()); }
});

// Phase 4.5 — Encrypted clipboard sync (server is opaque blob store; payload is AES-GCM ciphertext).
v1.MapGet("/clipboard", () => PlatypusTools.Remote.Server.ClipboardStore.Latest is null ? Results.NoContent() : Results.Ok(PlatypusTools.Remote.Server.ClipboardStore.Latest));
v1.MapPost("/clipboard", (System.Text.Json.JsonElement body) => { PlatypusTools.Remote.Server.ClipboardStore.Latest = System.Text.Json.JsonSerializer.Deserialize<object>(body.GetRawText()); return Results.NoContent(); });
// Phase 4.4 — Browser extension plain-text endpoint (cleartext on the wire; intended for loopback/LAN trusted scenarios).
v1.MapPost("/clipboard/plain", (System.Text.Json.JsonElement body) => { if (body.TryGetProperty("text", out var t)) PlatypusTools.Remote.Server.ClipboardStore.Plain = t.GetString() ?? string.Empty; return Results.NoContent(); });
v1.MapGet("/clipboard/plain", () => Results.Ok(new { text = PlatypusTools.Remote.Server.ClipboardStore.Plain }));

app.Run();
