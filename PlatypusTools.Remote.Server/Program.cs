using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Identity.Web;
using PlatypusTools.Remote.Server.Hubs;
using PlatypusTools.Remote.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel for port 47392
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(47392, listenOptions =>
    {
        listenOptions.UseHttps();
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

builder.Services.AddOpenApi();

var app = builder.Build();

// Security headers middleware
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; style-src 'self' 'unsafe-inline'; connect-src 'self' wss:;";
    
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

app.Run();
