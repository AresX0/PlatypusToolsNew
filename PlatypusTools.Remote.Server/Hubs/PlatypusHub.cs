using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using PlatypusTools.Remote.Server.Models;
using PlatypusTools.Remote.Server.Services;

namespace PlatypusTools.Remote.Server.Hubs;

/// <summary>
/// SignalR hub for real-time audio player updates.
/// All connections require authentication via Entra ID.
/// </summary>
[Authorize]
public class PlatypusHub : Hub
{
    private readonly IRemoteAudioService _audioService;
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<PlatypusHub> _logger;

    public PlatypusHub(
        IRemoteAudioService audioService,
        ISessionManager sessionManager,
        ILogger<PlatypusHub> logger)
    {
        _audioService = audioService;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirst("oid")?.Value 
                  ?? Context.User?.FindFirst("sub")?.Value 
                  ?? "anonymous";
        var userEmail = Context.User?.FindFirst("preferred_username")?.Value 
                     ?? Context.User?.FindFirst("email")?.Value 
                     ?? "unknown";

        _logger.LogInformation("Client connected: {ConnectionId} User: {UserEmail}", 
            Context.ConnectionId, userEmail);

        // Register session
        await _sessionManager.RegisterSessionAsync(userId, new SessionInfo
        {
            SessionId = Context.ConnectionId,
            UserEmail = userEmail,
            ConnectedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            UserAgent = Context.GetHttpContext()?.Request.Headers["User-Agent"].ToString() ?? "Unknown"
        });

        // Send current state to new client
        var nowPlaying = await _audioService.GetNowPlayingAsync();
        await Clients.Caller.SendAsync("NowPlayingUpdated", nowPlaying);

        var queue = await _audioService.GetQueueAsync();
        await Clients.Caller.SendAsync("QueueUpdated", queue);

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirst("oid")?.Value 
                  ?? Context.User?.FindFirst("sub")?.Value;

        if (!string.IsNullOrEmpty(userId))
        {
            await _sessionManager.EndSessionAsync(userId, Context.ConnectionId);
        }

        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Play current track
    /// </summary>
    public async Task Play()
    {
        await _audioService.PlayAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Pause playback
    /// </summary>
    public async Task Pause()
    {
        await _audioService.PauseAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Stop playback
    /// </summary>
    public async Task Stop()
    {
        await _audioService.StopAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Skip to next track
    /// </summary>
    public async Task NextTrack()
    {
        await _audioService.NextTrackAsync();
        await BroadcastNowPlaying();
        await BroadcastQueue();
    }

    /// <summary>
    /// Go to previous track
    /// </summary>
    public async Task PreviousTrack()
    {
        await _audioService.PreviousTrackAsync();
        await BroadcastNowPlaying();
        await BroadcastQueue();
    }

    /// <summary>
    /// Seek to position (0.0 - 1.0)
    /// </summary>
    public async Task Seek(double position)
    {
        await _audioService.SeekAsync(position);
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Set volume (0.0 - 1.0)
    /// </summary>
    public async Task SetVolume(double volume)
    {
        await _audioService.SetVolumeAsync(volume);
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Clear the queue
    /// </summary>
    public async Task ClearQueue()
    {
        await _audioService.ClearQueueAsync();
        await BroadcastQueue();
    }

    /// <summary>
    /// Shuffle the queue
    /// </summary>
    public async Task ShuffleQueue()
    {
        await _audioService.ShuffleQueueAsync();
        await BroadcastQueue();
    }

    /// <summary>
    /// Remove item from queue by index
    /// </summary>
    public async Task RemoveFromQueue(int index)
    {
        await _audioService.RemoveFromQueueAsync(index);
        await BroadcastQueue();
    }

    /// <summary>
    /// Play specific queue item
    /// </summary>
    public async Task PlayQueueItem(int index)
    {
        await _audioService.PlayQueueItemAsync(index);
        await BroadcastNowPlaying();
        await BroadcastQueue();
    }

    /// <summary>
    /// Add file to queue
    /// </summary>
    public async Task AddToQueue(string path)
    {
        await _audioService.AddToQueueAsync(path);
        await BroadcastQueue();
    }

    private async Task BroadcastNowPlaying()
    {
        var nowPlaying = await _audioService.GetNowPlayingAsync();
        await Clients.All.SendAsync("NowPlayingUpdated", nowPlaying);
    }

    private async Task BroadcastQueue()
    {
        var queue = await _audioService.GetQueueAsync();
        await Clients.All.SendAsync("QueueUpdated", queue);
    }
}
