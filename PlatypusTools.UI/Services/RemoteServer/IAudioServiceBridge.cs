using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Interface for bridging remote control commands to the audio player.
/// </summary>
public interface IAudioServiceBridge
{
    Task<NowPlayingDto> GetNowPlayingAsync();
    Task<IEnumerable<QueueItemDto>> GetQueueAsync();
    Task PlayAsync();
    Task PauseAsync();
    Task PlayPauseAsync();
    Task StopAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task SeekAsync(TimeSpan position);
    Task SetVolumeAsync(double volume);
    Task PlayQueueItemAsync(int index);
    Task ToggleShuffleAsync();
    Task ToggleRepeatAsync();
    
    // Library methods
    Task<IEnumerable<LibraryItemDto>> GetLibraryAsync();
    Task<IEnumerable<LibraryItemDto>> SearchLibraryAsync(string query);
    Task PlayFileAsync(string filePath);
    Task AddToQueueAsync(string filePath);
    
    // Events for real-time updates
    event EventHandler<NowPlayingDto>? PlaybackStateChanged;
    event EventHandler<TimeSpan>? PositionChanged;
    event EventHandler<IEnumerable<QueueItemDto>>? QueueChanged;
}

/// <summary>
/// Now playing information DTO.
/// </summary>
public class NowPlayingDto
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string? AlbumArtUrl { get; set; }
    public byte[]? AlbumArtData { get; set; }
    public double DurationSeconds { get; set; }
    public double PositionSeconds { get; set; }
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public double Volume { get; set; }
    public bool IsMuted { get; set; }
    public bool IsShuffle { get; set; }
    public int RepeatMode { get; set; } // 0=None, 1=All, 2=One
    public int CurrentIndex { get; set; }
    public int QueueCount { get; set; }
}

/// <summary>
/// Queue item DTO.
/// </summary>
public class QueueItemDto
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public bool IsCurrentTrack { get; set; }
    public string? ThumbnailBase64 { get; set; }
}

/// <summary>
/// Library item DTO for browsing music files.
/// </summary>
public class LibraryItemDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public string? ThumbnailBase64 { get; set; }
}
