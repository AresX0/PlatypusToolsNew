using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using PlatypusTools.Core.Models.Audio;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Bridge between the EnhancedAudioPlayerService and the remote API.
/// Translates between WPF dispatcher-bound calls and async API patterns.
/// </summary>
public class AudioServiceBridge : IAudioServiceBridge
{
    private readonly EnhancedAudioPlayerService _playerService;
    private readonly PlatypusRemoteServer _server;

    public event EventHandler<NowPlayingDto>? PlaybackStateChanged;
    public event EventHandler<TimeSpan>? PositionChanged;
    public event EventHandler<IEnumerable<QueueItemDto>>? QueueChanged;

    public AudioServiceBridge(EnhancedAudioPlayerService playerService, PlatypusRemoteServer server)
    {
        _playerService = playerService;
        _server = server;

        // Subscribe to player events to broadcast to remote clients
        _playerService.TrackChanged += OnTrackChanged;
        _playerService.PlaybackStateChanged += OnPlaybackStateChanged;
        _playerService.PositionChanged += OnPositionChanged;
    }

    private void OnTrackChanged(object? sender, AudioTrack? e)
    {
        var nowPlaying = GetNowPlayingSync();
        PlaybackStateChanged?.Invoke(this, nowPlaying);
    }

    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        var nowPlaying = GetNowPlayingSync();
        PlaybackStateChanged?.Invoke(this, nowPlaying);
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        PositionChanged?.Invoke(this, position);
    }

    public Task<NowPlayingDto> GetNowPlayingAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            return GetNowPlayingSync();
        });
    }

    private NowPlayingDto GetNowPlayingSync()
    {
        var track = _playerService.CurrentTrack;
        
        return new NowPlayingDto
        {
            Title = track?.Title ?? "No track playing",
            Artist = track?.Artist ?? string.Empty,
            Album = track?.Album ?? string.Empty,
            FilePath = track?.FilePath ?? string.Empty,
            AlbumArtData = track?.AlbumArt,
            DurationSeconds = _playerService.Duration.TotalSeconds,
            PositionSeconds = _playerService.Position.TotalSeconds,
            IsPlaying = _playerService.IsPlaying,
            IsPaused = !_playerService.IsPlaying && _playerService.CurrentTrack != null,
            Volume = _playerService.Volume,
            IsMuted = _playerService.IsMuted,
            IsShuffle = _playerService.IsShuffle,
            RepeatMode = (int)_playerService.Repeat,
            CurrentIndex = _playerService.CurrentIndex,
            QueueCount = _playerService.Queue.Count
        };
    }

    public Task<IEnumerable<QueueItemDto>> GetQueueAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            var queue = _playerService.Queue;
            var currentIndex = _playerService.CurrentIndex;

            return queue.Select((track, index) => new QueueItemDto
            {
                Index = index,
                Title = track.Title ?? "Unknown",
                Artist = track.Artist ?? "Unknown Artist",
                Album = track.Album ?? "Unknown Album",
                DurationSeconds = track.Duration.TotalSeconds,
                IsCurrentTrack = index == currentIndex,
                ThumbnailBase64 = track.AlbumArt != null 
                    ? Convert.ToBase64String(track.AlbumArt) 
                    : null
            }).AsEnumerable();
        });
    }

    public Task PlayAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            _playerService.Play();
        });
    }

    public Task PauseAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            _playerService.Pause();
        });
    }

    public Task PlayPauseAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            _playerService.PlayPause();
        });
    }

    public Task StopAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            _playerService.Stop();
        });
    }

    public Task NextAsync()
    {
        return InvokeOnDispatcherAsync(async () =>
        {
            await _playerService.NextAsync();
        });
    }

    public Task PreviousAsync()
    {
        return InvokeOnDispatcherAsync(async () =>
        {
            await _playerService.PreviousAsync();
        });
    }

    public Task SeekAsync(TimeSpan position)
    {
        return InvokeOnDispatcher(() =>
        {
            _playerService.Seek(position);
        });
    }

    public Task SetVolumeAsync(double volume)
    {
        return InvokeOnDispatcher(() =>
        {
            _playerService.Volume = Math.Clamp(volume, 0, 1);
        });
    }

    public Task PlayQueueItemAsync(int index)
    {
        return InvokeOnDispatcherAsync(async () =>
        {
            if (index >= 0 && index < _playerService.Queue.Count)
            {
                var queue = _playerService.Queue.ToList();
                _playerService.SetQueue(queue);
                // Set the index and play
                var track = queue[index];
                await _playerService.PlayTrackAsync(track);
            }
        });
    }

    public Task ToggleShuffleAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            _playerService.IsShuffle = !_playerService.IsShuffle;
        });
    }

    public Task ToggleRepeatAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            // Cycle through: None -> All -> One -> None
            _playerService.Repeat = _playerService.Repeat switch
            {
                EnhancedAudioPlayerService.RepeatMode.None => EnhancedAudioPlayerService.RepeatMode.All,
                EnhancedAudioPlayerService.RepeatMode.All => EnhancedAudioPlayerService.RepeatMode.One,
                EnhancedAudioPlayerService.RepeatMode.One => EnhancedAudioPlayerService.RepeatMode.None,
                _ => EnhancedAudioPlayerService.RepeatMode.None
            };
        });
    }

    public Task<IEnumerable<LibraryItemDto>> GetLibraryAsync()
    {
        return InvokeOnDispatcher(() =>
        {
            // Get recent tracks from the queue as a starting point
            var queue = _playerService.Queue.ToList();
            
            return queue.Select(track => new LibraryItemDto
            {
                FilePath = track.FilePath ?? string.Empty,
                FileName = Path.GetFileName(track.FilePath ?? string.Empty),
                Title = track.Title ?? Path.GetFileNameWithoutExtension(track.FilePath ?? "Unknown"),
                Artist = track.Artist ?? "Unknown Artist",
                Album = track.Album ?? "Unknown Album",
                DurationSeconds = track.Duration.TotalSeconds
            }).AsEnumerable();
        });
    }

    public Task<IEnumerable<LibraryItemDto>> SearchLibraryAsync(string query)
    {
        return InvokeOnDispatcher(() =>
        {
            if (string.IsNullOrWhiteSpace(query))
                return Enumerable.Empty<LibraryItemDto>();

            var queryLower = query.ToLowerInvariant();
            var queue = _playerService.Queue.ToList();
            
            return queue
                .Where(track => 
                    (track.Title?.ToLowerInvariant().Contains(queryLower) ?? false) ||
                    (track.Artist?.ToLowerInvariant().Contains(queryLower) ?? false) ||
                    (track.Album?.ToLowerInvariant().Contains(queryLower) ?? false) ||
                    (track.FilePath?.ToLowerInvariant().Contains(queryLower) ?? false))
                .Select(track => new LibraryItemDto
                {
                    FilePath = track.FilePath ?? string.Empty,
                    FileName = Path.GetFileName(track.FilePath ?? string.Empty),
                    Title = track.Title ?? Path.GetFileNameWithoutExtension(track.FilePath ?? "Unknown"),
                    Artist = track.Artist ?? "Unknown Artist",
                    Album = track.Album ?? "Unknown Album",
                    DurationSeconds = track.Duration.TotalSeconds
                }).AsEnumerable();
        });
    }

    public Task PlayFileAsync(string filePath)
    {
        return InvokeOnDispatcherAsync(async () =>
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                var track = new AudioTrack { FilePath = filePath };
                await _playerService.PlayTrackAsync(track);
            }
        });
    }

    public Task AddToQueueAsync(string filePath)
    {
        return InvokeOnDispatcher(() =>
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                var track = new AudioTrack { FilePath = filePath };
                var currentQueue = _playerService.Queue.ToList();
                currentQueue.Add(track);
                _playerService.SetQueue(currentQueue);
            }
        });
    }

    // Helper to invoke on WPF dispatcher
    private Task<T> InvokeOnDispatcher<T>(Func<T> action)
    {
        var tcs = new TaskCompletionSource<T>();
        
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                var result = action();
                tcs.SetResult(result);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    private Task InvokeOnDispatcher(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }

    private Task InvokeOnDispatcherAsync(Func<Task> asyncAction)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await asyncAction();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}
