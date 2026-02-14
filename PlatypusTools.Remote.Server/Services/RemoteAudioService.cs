using PlatypusTools.Core.Models.Remote;
using PlatypusTools.Core.Services.Remote;

namespace PlatypusTools.Remote.Server.Services;

/// <summary>
/// Implementation of remote audio service that bridges to the actual PlatypusTools.UI audio player
/// via the AudioPlayerBridge. Falls back gracefully if no bridge is registered.
/// </summary>
public class RemoteAudioService : IRemoteAudioService
{
    private readonly ILogger<RemoteAudioService> _logger;

    public RemoteAudioService(ILogger<RemoteAudioService> logger)
    {
        _logger = logger;
    }

    private bool HasProvider => AudioPlayerBridge.Provider != null;

    public Task PlayAsync()
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.Play();
            _logger.LogInformation("Remote: Play (bridged)");
        }
        else
        {
            _logger.LogWarning("Remote: Play - no provider registered");
        }
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.Pause();
            _logger.LogInformation("Remote: Pause (bridged)");
        }
        else
        {
            _logger.LogWarning("Remote: Pause - no provider registered");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.Stop();
            _logger.LogInformation("Remote: Stop (bridged)");
        }
        return Task.CompletedTask;
    }

    public Task NextTrackAsync()
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.NextTrack();
            _logger.LogInformation("Remote: Next track (bridged)");
        }
        return Task.CompletedTask;
    }

    public Task PreviousTrackAsync()
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.PreviousTrack();
            _logger.LogInformation("Remote: Previous track (bridged)");
        }
        return Task.CompletedTask;
    }

    public Task SeekAsync(double position)
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.Seek(position);
            _logger.LogInformation("Remote: Seek to {Position:P0} (bridged)", position);
        }
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume)
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.SetVolume(volume);
            _logger.LogInformation("Remote: Volume set to {Volume:P0} (bridged)", volume);
        }
        return Task.CompletedTask;
    }

    public Task<NowPlayingDto> GetNowPlayingAsync()
    {
        if (HasProvider)
        {
            var provider = AudioPlayerBridge.Provider!;
            var queue = provider.GetQueue();
            
            return Task.FromResult(new NowPlayingDto
            {
                IsPlaying = provider.IsPlaying,
                Title = provider.CurrentTrackTitle ?? "Nothing Playing",
                Artist = provider.CurrentTrackArtist ?? "",
                Album = provider.CurrentTrackAlbum ?? "",
                Duration = provider.Duration,
                Position = provider.Position,
                PositionPercent = provider.Duration.TotalSeconds > 0 
                    ? provider.Position.TotalSeconds / provider.Duration.TotalSeconds 
                    : 0,
                Volume = provider.Volume,
                AlbumArtUrl = null,
                QueueIndex = provider.CurrentTrackIndex,
                QueueLength = queue.Count
            });
        }
        
        return Task.FromResult(new NowPlayingDto
        {
            IsPlaying = false,
            Title = "Not Connected",
            Artist = "No audio player connected",
            Album = "",
            Duration = TimeSpan.Zero,
            Position = TimeSpan.Zero,
            PositionPercent = 0,
            Volume = 0.5,
            AlbumArtUrl = null,
            QueueIndex = 0,
            QueueLength = 0
        });
    }

    public Task<IReadOnlyList<QueueItemDto>> GetQueueAsync()
    {
        if (HasProvider)
        {
            var queue = AudioPlayerBridge.Provider!.GetQueue();
            return Task.FromResult(queue);
        }
        return Task.FromResult<IReadOnlyList<QueueItemDto>>(Array.Empty<QueueItemDto>());
    }

    public Task ClearQueueAsync()
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.ClearQueue();
            _logger.LogInformation("Remote: Queue cleared (bridged)");
        }
        return Task.CompletedTask;
    }

    public Task ShuffleQueueAsync()
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.ShuffleQueue();
            _logger.LogInformation("Remote: Queue shuffled (bridged)");
        }
        return Task.CompletedTask;
    }

    public Task RemoveFromQueueAsync(int index)
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.RemoveFromQueue(index);
            _logger.LogInformation("Remote: Removed queue item {Index} (bridged)", index);
        }
        return Task.CompletedTask;
    }

    public Task PlayQueueItemAsync(int index)
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.PlayQueueItem(index);
            _logger.LogInformation("Remote: Playing queue item {Index} (bridged)", index);
        }
        return Task.CompletedTask;
    }

    public Task AddToQueueAsync(string path)
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.AddToQueue(path);
            _logger.LogInformation("Remote: Added to queue: {Path} (bridged)", path);
        }
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LibraryFolderDto>> GetLibraryFoldersAsync()
    {
        if (HasProvider)
        {
            var folders = AudioPlayerBridge.Provider!.GetLibraryFolders();
            _logger.LogInformation("Remote: Got {Count} library folders (bridged)", folders.Count);
            return Task.FromResult(folders);
        }
        return Task.FromResult<IReadOnlyList<LibraryFolderDto>>(Array.Empty<LibraryFolderDto>());
    }

    public Task<IReadOnlyList<LibraryFileDto>> GetLibraryFilesAsync(string path)
    {
        if (HasProvider)
        {
            var files = AudioPlayerBridge.Provider!.GetLibraryFiles(path);
            _logger.LogInformation("Remote: Got {Count} files from {Path} (bridged)", files.Count, path);
            return Task.FromResult(files);
        }
        return Task.FromResult<IReadOnlyList<LibraryFileDto>>(Array.Empty<LibraryFileDto>());
    }

    public Task PlayFileAsync(string path)
    {
        if (HasProvider)
        {
            AudioPlayerBridge.Provider!.PlayFile(path);
            _logger.LogInformation("Remote: Playing file: {Path} (bridged)", path);
        }
        return Task.CompletedTask;
    }
}
