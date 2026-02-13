using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.SignalR.Client;
using PlatypusTools.Remote.Client.Models;

namespace PlatypusTools.Remote.Client.Services;

/// <summary>
/// Manages the SignalR connection to the Platypus Remote server.
/// Handles authentication and reconnection automatically.
/// </summary>
public class PlatypusHubConnection : IAsyncDisposable
{
    private readonly IAccessTokenProvider _tokenProvider;
    private readonly NavigationManager _navigationManager;
    private HubConnection? _hubConnection;
    
    public event Action<NowPlayingDto>? OnNowPlayingUpdated;
    public event Action<List<QueueItemDto>>? OnQueueUpdated;
    public event Action<bool>? OnConnectionStateChanged;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    public PlatypusHubConnection(IAccessTokenProvider tokenProvider, NavigationManager navigationManager)
    {
        _tokenProvider = tokenProvider;
        _navigationManager = navigationManager;
    }

    public async Task StartAsync()
    {
        if (_hubConnection != null)
        {
            return;
        }

        var baseUri = new Uri(_navigationManager.BaseUri);
        var hubUrl = $"https://{baseUri.Host}:47392/hub/platypus";

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    var result = await _tokenProvider.RequestAccessToken();
                    if (result.TryGetToken(out var token))
                    {
                        return token.Value;
                    }
                    return null;
                };
            })
            .WithAutomaticReconnect()
            .Build();

        // Register event handlers
        _hubConnection.On<NowPlayingDto>("NowPlayingUpdated", nowPlaying =>
        {
            OnNowPlayingUpdated?.Invoke(nowPlaying);
        });

        _hubConnection.On<List<QueueItemDto>>("QueueUpdated", queue =>
        {
            OnQueueUpdated?.Invoke(queue);
        });

        _hubConnection.Reconnected += _ =>
        {
            OnConnectionStateChanged?.Invoke(true);
            return Task.CompletedTask;
        };

        _hubConnection.Closed += _ =>
        {
            OnConnectionStateChanged?.Invoke(false);
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync();
            OnConnectionStateChanged?.Invoke(true);
        }
        catch (Exception)
        {
            OnConnectionStateChanged?.Invoke(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.StopAsync();
            OnConnectionStateChanged?.Invoke(false);
        }
    }

    // Playback controls
    public async Task PlayAsync() => await InvokeAsync("Play");
    public async Task PauseAsync() => await InvokeAsync("Pause");
    public async Task StopPlaybackAsync() => await InvokeAsync("Stop");
    public async Task NextTrackAsync() => await InvokeAsync("NextTrack");
    public async Task PreviousTrackAsync() => await InvokeAsync("PreviousTrack");
    public async Task SeekAsync(double position) => await InvokeAsync("Seek", position);
    public async Task SetVolumeAsync(double volume) => await InvokeAsync("SetVolume", volume);

    // Queue controls
    public async Task ClearQueueAsync() => await InvokeAsync("ClearQueue");
    public async Task ShuffleQueueAsync() => await InvokeAsync("ShuffleQueue");
    public async Task RemoveFromQueueAsync(int index) => await InvokeAsync("RemoveFromQueue", index);
    public async Task PlayQueueItemAsync(int index) => await InvokeAsync("PlayQueueItem", index);
    public async Task AddToQueueAsync(string path) => await InvokeAsync("AddToQueue", path);

    private async Task InvokeAsync(string method, params object[] args)
    {
        if (_hubConnection?.State == HubConnectionState.Connected)
        {
            await _hubConnection.InvokeCoreAsync(method, args);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_hubConnection != null)
        {
            await _hubConnection.DisposeAsync();
        }
    }
}
