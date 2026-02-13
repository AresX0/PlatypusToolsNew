using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// SignalR hub for real-time audio control from remote clients.
/// </summary>
public class PlatypusHub : Hub
{
    private readonly IAudioServiceBridge _audioService;

    public PlatypusHub(IAudioServiceBridge audioService)
    {
        _audioService = audioService;

        // Subscribe to service events to broadcast to clients
        _audioService.PlaybackStateChanged += async (s, e) => await BroadcastNowPlaying();
        _audioService.PositionChanged += async (s, e) => await BroadcastPosition(e.TotalSeconds);
    }

    public override async Task OnConnectedAsync()
    {
        System.Diagnostics.Debug.WriteLine($"[PlatypusHub] Client connected: {Context.ConnectionId}");
        
        // Send current state to newly connected client
        var nowPlaying = await _audioService.GetNowPlayingAsync();
        await Clients.Caller.SendAsync("nowPlaying", nowPlaying);
        
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        System.Diagnostics.Debug.WriteLine($"[PlatypusHub] Client disconnected: {Context.ConnectionId}");
        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Play the current track.
    /// </summary>
    public async Task Play()
    {
        await _audioService.PlayAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Pause playback.
    /// </summary>
    public async Task Pause()
    {
        await _audioService.PauseAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Toggle play/pause.
    /// </summary>
    public async Task PlayPause()
    {
        await _audioService.PlayPauseAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Skip to next track.
    /// </summary>
    public async Task Next()
    {
        await _audioService.NextAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Go to previous track.
    /// </summary>
    public async Task Previous()
    {
        await _audioService.PreviousAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Seek to position in seconds.
    /// </summary>
    public async Task Seek(double positionSeconds)
    {
        await _audioService.SeekAsync(TimeSpan.FromSeconds(positionSeconds));
    }

    /// <summary>
    /// Set volume (0.0 - 1.0).
    /// </summary>
    public async Task SetVolume(double volume)
    {
        await _audioService.SetVolumeAsync(volume);
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Play a specific track from the queue.
    /// </summary>
    public async Task PlayQueueItem(int index)
    {
        await _audioService.PlayQueueItemAsync(index);
        await BroadcastNowPlaying();
        await BroadcastQueue();
    }

    /// <summary>
    /// Toggle shuffle mode.
    /// </summary>
    public async Task ToggleShuffle()
    {
        await _audioService.ToggleShuffleAsync();
        await BroadcastNowPlaying();
    }

    /// <summary>
    /// Toggle repeat mode.
    /// </summary>
    public async Task ToggleRepeat()
    {
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
        await _audioService.PlayFileAsync(filePath);
        await BroadcastNowPlaying();
        await BroadcastQueue();
    }

    /// <summary>
    /// Add a file to the queue.
    /// </summary>
    public async Task AddToQueue(string filePath)
    {
        await _audioService.AddToQueueAsync(filePath);
        await BroadcastQueue();
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
