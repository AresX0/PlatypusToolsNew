using PlatypusTools.Remote.Client.Models;

namespace PlatypusTools.Remote.Client.Services;

/// <summary>
/// Player modes: Remote Control (PC plays) vs Remote Stream (Phone plays).
/// </summary>
public enum PlayerMode
{
    /// <summary>
    /// Phone controls PC playback - audio plays on PC.
    /// </summary>
    RemoteControl,
    
    /// <summary>
    /// Phone streams and plays audio locally.
    /// </summary>
    RemoteStream
}

/// <summary>
/// Manages the current player state for the UI.
/// Supports both Remote Control (PC plays) and Remote Stream (Phone plays) modes.
/// </summary>
public class PlayerStateService
{
    private readonly PlatypusHubConnection _hubConnection;
    private readonly LocalAudioPlayerService _localPlayer;
    private string _serverBaseUrl = "";
    
    public PlayerMode Mode { get; private set; } = PlayerMode.RemoteControl;
    
    public NowPlayingDto NowPlaying { get; private set; } = new();
    public List<QueueItemDto> Queue { get; private set; } = new();
    public bool IsConnected => _hubConnection.IsConnected;
    
    public event Action? OnStateChanged;
    public event Action<PlayerMode>? OnModeChanged;

    public PlayerStateService(PlatypusHubConnection hubConnection, LocalAudioPlayerService localPlayer)
    {
        _hubConnection = hubConnection;
        _localPlayer = localPlayer;
        
        // Remote control mode updates from hub
        _hubConnection.OnNowPlayingUpdated += nowPlaying =>
        {
            if (Mode == PlayerMode.RemoteControl)
            {
                NowPlaying = nowPlaying;
                OnStateChanged?.Invoke();
            }
        };
        
        _hubConnection.OnQueueUpdated += queue =>
        {
            if (Mode == PlayerMode.RemoteControl)
            {
                Queue = queue;
                OnStateChanged?.Invoke();
            }
        };
        
        _hubConnection.OnConnectionStateChanged += _ =>
        {
            OnStateChanged?.Invoke();
        };
        
        // Local streaming mode updates
        _localPlayer.OnStateChanged += () =>
        {
            if (Mode == PlayerMode.RemoteStream)
            {
                UpdateNowPlayingFromLocal();
                OnStateChanged?.Invoke();
            }
        };
    }

    public void SetServerBaseUrl(string url)
    {
        _serverBaseUrl = url.TrimEnd('/');
    }

    public async Task SetModeAsync(PlayerMode mode)
    {
        if (Mode == mode) return;
        
        Mode = mode;
        
        if (mode == PlayerMode.RemoteStream)
        {
            await _localPlayer.InitializeAsync();
        }
        
        OnModeChanged?.Invoke(mode);
        OnStateChanged?.Invoke();
    }

    public async Task ConnectAsync()
    {
        await _hubConnection.StartAsync();
    }

    public async Task DisconnectAsync()
    {
        await _hubConnection.StopAsync();
    }

    // Playback controls - delegate based on mode
    public async Task PlayAsync()
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.PlayAsync();
        else
            await _hubConnection.PlayAsync();
    }

    public async Task PauseAsync()
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.PauseAsync();
        else
            await _hubConnection.PauseAsync();
    }

    public async Task StopAsync()
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.StopAsync();
        else
            await _hubConnection.StopPlaybackAsync();
    }

    public async Task NextTrackAsync()
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.NextAsync();
        else
            await _hubConnection.NextTrackAsync();
    }

    public async Task PreviousTrackAsync()
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.PreviousAsync();
        else
            await _hubConnection.PreviousTrackAsync();
    }

    public async Task SeekAsync(double position)
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.SeekAsync(position);
        else
            await _hubConnection.SeekAsync(position);
    }

    public async Task SetVolumeAsync(double volume)
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.SetVolumeAsync(volume);
        else
            await _hubConnection.SetVolumeAsync(volume);
    }

    // Queue controls
    public async Task ClearQueueAsync()
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.ClearQueueAsync();
        else
            await _hubConnection.ClearQueueAsync();
    }

    public async Task ShuffleQueueAsync()
    {
        // Shuffle only available in remote control mode for now
        if (Mode == PlayerMode.RemoteControl)
            await _hubConnection.ShuffleQueueAsync();
    }

    public async Task RemoveFromQueueAsync(int index)
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.RemoveFromQueueAsync(index);
        else
            await _hubConnection.RemoveFromQueueAsync(index);
    }

    public async Task PlayQueueItemAsync(int index)
    {
        if (Mode == PlayerMode.RemoteStream)
            await _localPlayer.PlayAtIndexAsync(index);
        else
            await _hubConnection.PlayQueueItemAsync(index);
    }

    // Library browsing (always via hub)
    public Task<List<LibraryFolderDto>> GetLibraryFoldersAsync() 
        => _hubConnection.GetLibraryFoldersAsync();

    public Task<List<LibraryFileDto>> GetLibraryFilesAsync(string path) 
        => _hubConnection.GetLibraryFilesAsync(path);

    // Add to queue / Play file - mode-specific
    public async Task AddToQueueAsync(string path)
    {
        if (Mode == PlayerMode.RemoteStream)
        {
            var streamUrl = $"{_serverBaseUrl}/api/stream?path={Uri.EscapeDataString(path)}";
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            await _localPlayer.AddToQueueAsync(path, fileName, "Unknown Artist", streamUrl);
        }
        else
        {
            await _hubConnection.AddToQueueAsync(path);
        }
    }

    public async Task PlayFileAsync(string path)
    {
        if (Mode == PlayerMode.RemoteStream)
        {
            var streamUrl = $"{_serverBaseUrl}/api/stream?path={Uri.EscapeDataString(path)}";
            var fileName = System.IO.Path.GetFileNameWithoutExtension(path);
            await _localPlayer.PlayUrlAsync(streamUrl, fileName, "Unknown Artist");
        }
        else
        {
            await _hubConnection.PlayFileAsync(path);
        }
    }

    private void UpdateNowPlayingFromLocal()
    {
        var track = _localPlayer.CurrentTrack;
        NowPlaying = new NowPlayingDto
        {
            IsPlaying = _localPlayer.IsPlaying,
            Title = track?.Title ?? "Nothing Playing",
            Artist = track?.Artist ?? "",
            Album = "",
            Duration = TimeSpan.FromSeconds(_localPlayer.Duration),
            Position = TimeSpan.FromSeconds(_localPlayer.CurrentTime),
            PositionPercent = _localPlayer.Duration > 0 
                ? _localPlayer.CurrentTime / _localPlayer.Duration 
                : 0,
            Volume = _localPlayer.Volume,
            QueueIndex = _localPlayer.QueueIndex,
            QueueLength = _localPlayer.QueueLength
        };
    }
}
