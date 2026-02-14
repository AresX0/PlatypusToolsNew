using Microsoft.JSInterop;

namespace PlatypusTools.Remote.Client.Services;

/// <summary>
/// Provides JS interop for the HTML5 audio player in streaming mode.
/// This runs on the phone and plays audio locally via the browser.
/// </summary>
public class LocalAudioPlayerService : IAsyncDisposable
{
    private readonly IJSRuntime _jsRuntime;
    private DotNetObjectReference<LocalAudioPlayerService>? _dotNetRef;
    private bool _initialized;
    
    // Current state
    public bool IsPlaying { get; private set; }
    public double CurrentTime { get; private set; }
    public double Duration { get; private set; }
    public double Volume { get; private set; } = 0.7;
    public int QueueIndex { get; private set; } = -1;
    public int QueueLength { get; private set; }
    public LocalTrackInfo? CurrentTrack { get; private set; }
    
    public event Action? OnStateChanged;
    public event Action<string>? OnError;

    public LocalAudioPlayerService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        
        try
        {
            _dotNetRef = DotNetObjectReference.Create(this);
            await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.initialize", _dotNetRef);
            _initialized = true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to initialize audio player: {ex.Message}");
        }
    }

    public async Task PlayUrlAsync(string url, string? title = null, string? artist = null)
    {
        await InitializeAsync();
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.playUrl", url, title, artist);
        CurrentTrack = new LocalTrackInfo { Title = title ?? "Unknown", Artist = artist ?? "" };
    }

    public async Task PlayAsync()
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.play");
    }

    public async Task PauseAsync()
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.pause");
    }

    public async Task StopAsync()
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.stop");
        IsPlaying = false;
        CurrentTime = 0;
        OnStateChanged?.Invoke();
    }

    public async Task SeekAsync(double position)
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.seek", position);
    }

    public async Task SetVolumeAsync(double volume)
    {
        Volume = volume;
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.setVolume", volume);
    }

    public async Task NextAsync()
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.next");
    }

    public async Task PreviousAsync()
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.previous");
    }

    public async Task ClearQueueAsync()
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.clearQueue");
        QueueLength = 0;
        QueueIndex = -1;
        OnStateChanged?.Invoke();
    }

    public async Task<int> AddToQueueAsync(string path, string title, string artist, string streamUrl)
    {
        var item = new { path, title, artist, streamUrl };
        var length = await _jsRuntime.InvokeAsync<int>("PlatypusAudioPlayer.addToQueue", item);
        QueueLength = length;
        OnStateChanged?.Invoke();
        return length;
    }

    public async Task PlayAtIndexAsync(int index)
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.playAtIndex", index);
    }

    public async Task RemoveFromQueueAsync(int index)
    {
        await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.removeFromQueue", index);
        await RefreshStateAsync();
    }

    public async Task RefreshStateAsync()
    {
        try
        {
            var state = await _jsRuntime.InvokeAsync<LocalPlayerState>("PlatypusAudioPlayer.getState");
            IsPlaying = state.IsPlaying;
            CurrentTime = state.CurrentTime;
            Duration = state.Duration;
            Volume = state.Volume;
            QueueIndex = state.QueueIndex;
            QueueLength = state.QueueLength;
            if (state.CurrentTrack != null)
            {
                CurrentTrack = new LocalTrackInfo 
                { 
                    Title = state.CurrentTrack.Title ?? "Unknown",
                    Artist = state.CurrentTrack.Artist ?? "",
                    Path = state.CurrentTrack.Path ?? ""
                };
            }
            OnStateChanged?.Invoke();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to get player state: {ex.Message}");
        }
    }

    // JS callbacks
    [JSInvokable]
    public void OnTimeUpdate(double currentTime, double duration, bool isPlaying)
    {
        CurrentTime = currentTime;
        Duration = duration;
        IsPlaying = isPlaying;
        OnStateChanged?.Invoke();
    }

    [JSInvokable]
    public void OnPlayStateChanged(bool isPlaying)
    {
        IsPlaying = isPlaying;
        OnStateChanged?.Invoke();
    }

    [JSInvokable]
    public void OnMetadataLoaded(double duration)
    {
        Duration = duration;
        OnStateChanged?.Invoke();
    }

    [JSInvokable]
    public void OnAudioError(string error)
    {
        Console.WriteLine($"Audio error: {error}");
        OnError?.Invoke(error);
    }

    public async ValueTask DisposeAsync()
    {
        if (_initialized)
        {
            await _jsRuntime.InvokeVoidAsync("PlatypusAudioPlayer.dispose");
        }
        _dotNetRef?.Dispose();
    }
}

public class LocalTrackInfo
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Path { get; set; } = "";
}

public class LocalPlayerState
{
    public bool IsPlaying { get; set; }
    public double CurrentTime { get; set; }
    public double Duration { get; set; }
    public double Volume { get; set; }
    public int QueueIndex { get; set; }
    public int QueueLength { get; set; }
    public LocalTrackInfo? CurrentTrack { get; set; }
}
