using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Interface for bridging remote video library and playback commands.
/// </summary>
public interface IVideoServiceBridge
{
    /// <summary>
    /// Gets all video files from configured library folders.
    /// </summary>
    Task<IEnumerable<VideoLibraryItemDto>> GetVideoLibraryAsync();

    /// <summary>
    /// Searches video library by query string.
    /// </summary>
    Task<IEnumerable<VideoLibraryItemDto>> SearchVideoLibraryAsync(string query);

    /// <summary>
    /// Gets the configured video library folders (reads from Media Library folders).
    /// </summary>
    Task<IEnumerable<string>> GetVideoFoldersAsync();

    /// <summary>
    /// Rescans all video library folders for new/removed files.
    /// </summary>
    Task RescanLibraryAsync();

    /// <summary>
    /// Gets a thumbnail for a video file (extracted via FFmpeg).
    /// Returns base64-encoded JPEG data, or null if unavailable.
    /// </summary>
    Task<string?> GetVideoThumbnailAsync(string filePath);
}

/// <summary>
/// Video library item DTO for browsing video files.
/// </summary>
public class VideoLibraryItemDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Resolution => Width > 0 && Height > 0 ? $"{Width}x{Height}" : string.Empty;
    public long FileSizeBytes { get; set; }
    public string FileSizeMB => FileSizeBytes > 0 ? $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB" : string.Empty;
    public string? ThumbnailBase64 { get; set; }
    public string Folder { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
}
