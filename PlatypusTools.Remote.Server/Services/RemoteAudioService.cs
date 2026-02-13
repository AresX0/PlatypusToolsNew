using PlatypusTools.Remote.Server.Models;

namespace PlatypusTools.Remote.Server.Services;

/// <summary>
/// Implementation of remote audio service.
/// In production, this will communicate with the PlatypusTools.UI audio player
/// via a shared interface or inter-process communication.
/// 
/// For now, this is a placeholder that returns mock data for testing.
/// </summary>
public class RemoteAudioService : IRemoteAudioService
{
    private readonly ILogger<RemoteAudioService> _logger;
    
    // Mock state for testing
    private bool _isPlaying;
    private double _currentPosition;
    private double _volume = 0.75;
    private int _currentTrackIndex;
    private readonly List<QueueItemDto> _queue = new();

    public RemoteAudioService(ILogger<RemoteAudioService> logger)
    {
        _logger = logger;
        
        // Initialize with mock data
        _queue.AddRange(new[]
        {
            new QueueItemDto { Index = 0, Title = "Sample Track 1", Artist = "Artist A", Duration = TimeSpan.FromMinutes(3.5), FilePath = "C:\\Music\\track1.mp3" },
            new QueueItemDto { Index = 1, Title = "Sample Track 2", Artist = "Artist B", Duration = TimeSpan.FromMinutes(4.2), FilePath = "C:\\Music\\track2.mp3" },
            new QueueItemDto { Index = 2, Title = "Sample Track 3", Artist = "Artist C", Duration = TimeSpan.FromMinutes(2.8), FilePath = "C:\\Music\\track3.mp3" },
        });
    }

    public Task PlayAsync()
    {
        _isPlaying = true;
        _logger.LogInformation("Remote: Play");
        return Task.CompletedTask;
    }

    public Task PauseAsync()
    {
        _isPlaying = false;
        _logger.LogInformation("Remote: Pause");
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _isPlaying = false;
        _currentPosition = 0;
        _logger.LogInformation("Remote: Stop");
        return Task.CompletedTask;
    }

    public Task NextTrackAsync()
    {
        if (_queue.Count > 0)
        {
            _currentTrackIndex = (_currentTrackIndex + 1) % _queue.Count;
            _currentPosition = 0;
        }
        _logger.LogInformation("Remote: Next track -> {Index}", _currentTrackIndex);
        return Task.CompletedTask;
    }

    public Task PreviousTrackAsync()
    {
        if (_queue.Count > 0)
        {
            _currentTrackIndex = (_currentTrackIndex - 1 + _queue.Count) % _queue.Count;
            _currentPosition = 0;
        }
        _logger.LogInformation("Remote: Previous track -> {Index}", _currentTrackIndex);
        return Task.CompletedTask;
    }

    public Task SeekAsync(double position)
    {
        _currentPosition = Math.Clamp(position, 0, 1);
        _logger.LogInformation("Remote: Seek to {Position:P0}", _currentPosition);
        return Task.CompletedTask;
    }

    public Task SetVolumeAsync(double volume)
    {
        _volume = Math.Clamp(volume, 0, 1);
        _logger.LogInformation("Remote: Volume set to {Volume:P0}", _volume);
        return Task.CompletedTask;
    }

    public Task<NowPlayingDto> GetNowPlayingAsync()
    {
        var current = _queue.Count > 0 && _currentTrackIndex < _queue.Count 
            ? _queue[_currentTrackIndex] 
            : null;

        return Task.FromResult(new NowPlayingDto
        {
            IsPlaying = _isPlaying,
            Title = current?.Title ?? "Nothing Playing",
            Artist = current?.Artist ?? "",
            Album = "Sample Album",
            Duration = current?.Duration ?? TimeSpan.Zero,
            Position = TimeSpan.FromSeconds((current?.Duration.TotalSeconds ?? 0) * _currentPosition),
            PositionPercent = _currentPosition,
            Volume = _volume,
            AlbumArtUrl = null,
            QueueIndex = _currentTrackIndex,
            QueueLength = _queue.Count
        });
    }

    public Task<IReadOnlyList<QueueItemDto>> GetQueueAsync()
    {
        // Update indices
        for (int i = 0; i < _queue.Count; i++)
        {
            _queue[i].Index = i;
            _queue[i].IsCurrentTrack = i == _currentTrackIndex;
        }
        return Task.FromResult<IReadOnlyList<QueueItemDto>>(_queue.AsReadOnly());
    }

    public Task ClearQueueAsync()
    {
        _queue.Clear();
        _currentTrackIndex = 0;
        _isPlaying = false;
        _logger.LogInformation("Remote: Queue cleared");
        return Task.CompletedTask;
    }

    public Task ShuffleQueueAsync()
    {
        var random = new Random();
        for (int i = _queue.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (_queue[i], _queue[j]) = (_queue[j], _queue[i]);
        }
        _logger.LogInformation("Remote: Queue shuffled");
        return Task.CompletedTask;
    }

    public Task RemoveFromQueueAsync(int index)
    {
        if (index >= 0 && index < _queue.Count)
        {
            _queue.RemoveAt(index);
            if (_currentTrackIndex >= _queue.Count)
                _currentTrackIndex = Math.Max(0, _queue.Count - 1);
        }
        _logger.LogInformation("Remote: Removed queue item at {Index}", index);
        return Task.CompletedTask;
    }

    public Task PlayQueueItemAsync(int index)
    {
        if (index >= 0 && index < _queue.Count)
        {
            _currentTrackIndex = index;
            _currentPosition = 0;
            _isPlaying = true;
        }
        _logger.LogInformation("Remote: Playing queue item {Index}", index);
        return Task.CompletedTask;
    }

    public Task AddToQueueAsync(string path)
    {
        _queue.Add(new QueueItemDto
        {
            Index = _queue.Count,
            Title = Path.GetFileNameWithoutExtension(path),
            Artist = "Unknown Artist",
            Duration = TimeSpan.FromMinutes(3),
            FilePath = path
        });
        _logger.LogInformation("Remote: Added to queue: {Path}", path);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LibraryFolderDto>> GetLibraryFoldersAsync()
    {
        // Mock data - in production, this would read from PlatypusTools settings
        var folders = new List<LibraryFolderDto>
        {
            new() { Path = "C:\\Music", Name = "Music", FileCount = 150 },
            new() { Path = "C:\\Downloads\\Audio", Name = "Downloads", FileCount = 25 }
        };
        return Task.FromResult<IReadOnlyList<LibraryFolderDto>>(folders);
    }

    public Task<IReadOnlyList<LibraryFileDto>> GetLibraryFilesAsync(string path)
    {
        // Mock data - in production, this would browse actual files
        var files = new List<LibraryFileDto>
        {
            new() { Path = Path.Combine(path, "song1.mp3"), Name = "song1.mp3", Size = 5_000_000, IsDirectory = false },
            new() { Path = Path.Combine(path, "song2.mp3"), Name = "song2.mp3", Size = 4_500_000, IsDirectory = false },
            new() { Path = Path.Combine(path, "Subfolder"), Name = "Subfolder", Size = 0, IsDirectory = true }
        };
        return Task.FromResult<IReadOnlyList<LibraryFileDto>>(files);
    }
}
