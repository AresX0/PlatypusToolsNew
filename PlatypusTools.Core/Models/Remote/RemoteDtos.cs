namespace PlatypusTools.Core.Models.Remote;

/// <summary>
/// DTO for current playback status sent to remote clients.
/// </summary>
public class NowPlayingDto
{
    public bool IsPlaying { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public TimeSpan Position { get; set; }
    public double PositionPercent { get; set; }
    public double Volume { get; set; }
    public string? AlbumArtUrl { get; set; }
    public int QueueIndex { get; set; }
    public int QueueLength { get; set; }
}

/// <summary>
/// DTO for queue items sent to remote clients.
/// </summary>
public class QueueItemDto
{
    public int Index { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public bool IsCurrentTrack { get; set; }
}

/// <summary>
/// DTO for library folder information.
/// </summary>
public class LibraryFolderDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
}

/// <summary>
/// DTO for library file information.
/// </summary>
public class LibraryFileDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
}
