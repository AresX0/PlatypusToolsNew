using PlatypusTools.Remote.Client.Models;

namespace PlatypusTools.Remote.Client.Services;

/// <summary>
/// Manages the current player state for the UI.
/// Receives updates from SignalR and notifies components.
/// </summary>
public class PlayerStateService
{
    private readonly PlatypusHubConnection _hubConnection;
    
    public NowPlayingDto NowPlaying { get; private set; } = new();
    public List<QueueItemDto> Queue { get; private set; } = new();
    public bool IsConnected => _hubConnection.IsConnected;
    
    public event Action? OnStateChanged;

    public PlayerStateService(PlatypusHubConnection hubConnection)
    {
        _hubConnection = hubConnection;
        
        _hubConnection.OnNowPlayingUpdated += nowPlaying =>
        {
            NowPlaying = nowPlaying;
            OnStateChanged?.Invoke();
        };
        
        _hubConnection.OnQueueUpdated += queue =>
        {
            Queue = queue;
            OnStateChanged?.Invoke();
        };
        
        _hubConnection.OnConnectionStateChanged += _ =>
        {
            OnStateChanged?.Invoke();
        };
    }

    public async Task ConnectAsync()
    {
        await _hubConnection.StartAsync();
    }

    public async Task DisconnectAsync()
    {
        await _hubConnection.StopAsync();
    }

    // Playback controls
    public Task PlayAsync() => _hubConnection.PlayAsync();
    public Task PauseAsync() => _hubConnection.PauseAsync();
    public Task StopAsync() => _hubConnection.StopPlaybackAsync();
    public Task NextTrackAsync() => _hubConnection.NextTrackAsync();
    public Task PreviousTrackAsync() => _hubConnection.PreviousTrackAsync();
    public Task SeekAsync(double position) => _hubConnection.SeekAsync(position);
    public Task SetVolumeAsync(double volume) => _hubConnection.SetVolumeAsync(volume);

    // Queue controls
    public Task ClearQueueAsync() => _hubConnection.ClearQueueAsync();
    public Task ShuffleQueueAsync() => _hubConnection.ShuffleQueueAsync();
    public Task RemoveFromQueueAsync(int index) => _hubConnection.RemoveFromQueueAsync(index);
    public Task PlayQueueItemAsync(int index) => _hubConnection.PlayQueueItemAsync(index);
    public Task AddToQueueAsync(string path) => _hubConnection.AddToQueueAsync(path);
}
