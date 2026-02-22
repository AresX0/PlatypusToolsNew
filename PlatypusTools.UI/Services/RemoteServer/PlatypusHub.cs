using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// SignalR hub for real-time audio control from remote clients.
/// Hubs are TRANSIENT — do NOT subscribe to events in the constructor.
/// Background broadcasting is handled by AudioServiceBridge via IHubContext.
/// </summary>
public class PlatypusHub : Hub
{
    private readonly IAudioServiceBridge _audioService;
    private readonly IVideoServiceBridge _videoService;
    private readonly RemoteAuditLogService _auditLog = RemoteAuditLogService.Instance;

    public PlatypusHub(IAudioServiceBridge audioService, IVideoServiceBridge videoService)
    {
        _audioService = audioService;
        _videoService = videoService;
    }

    private string ClientIp => Context.GetHttpContext()?.Connection?.RemoteIpAddress?.ToString() ?? "unknown";

    public override async Task OnConnectedAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[PlatypusHub] Client connected: {Context.ConnectionId}");
        var userAgent = Context.GetHttpContext()?.Request?.Headers["User-Agent"].ToString() ?? "unknown";
        _auditLog.LogConnection(Context.ConnectionId, ClientIp, userAgent);
        
        // Send current state to newly connected client
        var nowPlaying = await _audioService.GetNowPlayingAsync();
        await Clients.Caller.SendAsync("nowPlaying", nowPlaying);
        
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        System.Diagnostics.Debug.WriteLine($"[PlatypusHub] Client disconnected: {Context.ConnectionId}");
        _auditLog.LogDisconnection(Context.ConnectionId, ClientIp);
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Play the current track.
    /// </summary>
    public async Task Play()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "Play");
        await _audioService.PlayAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public async Task Pause()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "Pause");
        await _audioService.PauseAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Toggle play/pause.
    /// </summary>
    public async Task PlayPause()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "PlayPause");
        await _audioService.PlayPauseAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Skip to next track.
    /// </summary>
    public async Task Next()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "Next");
        await _audioService.NextAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Go to previous track.
    /// </summary>
    public async Task Previous()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "Previous");
        await _audioService.PreviousAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Seek to position in seconds.
    /// </summary>
    public async Task Seek(double positionSeconds)
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "Seek", $"Position: {positionSeconds:F1}s");
        await _audioService.SeekAsync(TimeSpan.FromSeconds(positionSeconds));
    }

    /// <summary>
    /// Set volume (0.0 - 1.0).
    /// </summary>
    public async Task SetVolume(double volume)
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "SetVolume", $"Volume: {volume:P0}");
        await _audioService.SetVolumeAsync(volume);
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Play a specific track from the queue.
    /// </summary>
    public async Task PlayQueueItem(int index)
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "PlayQueueItem", $"Index: {index}");
        await _audioService.PlayQueueItemAsync(index);
        await BroadcastNowPlaying();
        await BroadcastQueue();
    }

    /// <summary>
    /// Toggle shuffle mode.
    /// </summary>
    public async Task ToggleShuffle()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "ToggleShuffle");
        await _audioService.ToggleShuffleAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Toggle repeat mode.
    /// </summary>
    public async Task ToggleRepeat()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "ToggleRepeat");
        await _audioService.ToggleRepeatAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Request current now playing info.
    /// </summary>
    public async Task GetNowPlaying()
    {
        var nowPlaying = await _audioService.GetNowPlayingAsync();
        await Clients.Caller.SendAsync("nowPlaying", nowPlaying);
    }

    /// <summary>
    /// Request current queue.
    /// </summary>
    public async Task GetQueue()
    {
        var queue = await _audioService.GetQueueAsync();
        await Clients.Caller.SendAsync("queue", queue);
    }

    /// <summary>
    /// Play a specific file.
    /// </summary>
    public async Task PlayFile(string filePath)
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "PlayFile", $"File: {filePath}");
        await _audioService.PlayFileAsync(filePath);
        await BroadcastNowPlaying();
        await BroadcastQueue();
    }

    /// <summary>
    /// Add a file to the queue.
    /// </summary>
    public async Task AddToQueue(string filePath)
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "AddToQueue", $"File: {filePath}");
        await _audioService.AddToQueueAsync(filePath);
        await BroadcastQueue();
    }

    // ---- Video Library Methods ----

    /// <summary>
    /// Get the video library listing.
    /// </summary>
    public async Task GetVideoLibrary()
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "GetVideoLibrary");
        var library = await _videoService.GetVideoLibraryAsync();
        await Clients.Caller.SendAsync("videoLibrary", library);
    }

    /// <summary>
    /// Search the video library.
    /// </summary>
    public async Task SearchVideoLibrary(string query)
    {
        _auditLog.LogAction(Context.ConnectionId, ClientIp, "SearchVideoLibrary", $"Query: {query}");
        var results = await _videoService.SearchVideoLibraryAsync(query);
        await Clients.Caller.SendAsync("videoLibrary", results);
    }

    /// <summary>
    /// Get a video thumbnail.
    /// </summary>
    public async Task GetVideoThumbnail(string filePath)
    {
        var thumbnail = await _videoService.GetVideoThumbnailAsync(filePath);
        await Clients.Caller.SendAsync("videoThumbnail", new { filePath, thumbnail });
    }

    private async Task BroadcastNowPlaying()
    {
        var nowPlaying = await _audioService.GetNowPlayingAsync();
        await Clients.All.SendAsync("nowPlaying", nowPlaying);
    }

    private async Task BroadcastPosition(double positionSeconds)
    {
        await Clients.All.SendAsync("position", positionSeconds);
    }

    private async Task BroadcastQueue()
    {
        var queue = await _audioService.GetQueueAsync();
        await Clients.All.SendAsync("queue", queue);
    }
}
