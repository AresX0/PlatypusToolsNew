using System.Collections.Generic;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Interface for bridging image/photo browsing from the remote web app to ThumbnailCacheService.
/// </summary>
public interface IImageBrowserBridge
{
    /// <summary>
    /// Gets the configured library folders that may contain images.
    /// </summary>
    Task<IEnumerable<string>> GetImageFoldersAsync();

    /// <summary>
    /// Lists image files in a folder with pagination.
    /// </summary>
    Task<ImageBrowseResultDto> GetImagesAsync(string? folder = null, int page = 0, int pageSize = 50, string? search = null);

    /// <summary>
    /// Gets a thumbnail for an image file as JPEG bytes.
    /// Uses ThumbnailCacheService for caching.
    /// </summary>
    Task<byte[]?> GetThumbnailBytesAsync(string filePath, int size = 200);

    /// <summary>
    /// Gets the full-resolution image file bytes for viewing.
    /// </summary>
    Task<byte[]?> GetFullImageBytesAsync(string filePath);

    /// <summary>
    /// Gets image metadata (dimensions, size, modified date).
    /// </summary>
    ImageInfoDto? GetImageInfo(string filePath);

    /// <summary>
    /// Gets cache statistics.
    /// </summary>
    ImageCacheStatsDto GetCacheStats();

    /// <summary>
    /// Clears the thumbnail cache.
    /// </summary>
    void ClearCache();
}

/// <summary>
/// Result DTO for paginated image browsing.
/// </summary>
public class ImageBrowseResultDto
{
    public List<ImageItemDto> Images { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
    public string? Folder { get; set; }
}

/// <summary>
/// DTO for an image file in the browse list.
/// </summary>
public class ImageItemDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string FileSize => FileSizeBytes > 1024 * 1024
        ? $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
        : $"{FileSizeBytes / 1024.0:F0} KB";
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
}

/// <summary>
/// DTO for detailed image info.
/// </summary>
public class ImageInfoDto
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Resolution => Width > 0 && Height > 0 ? $"{Width}x{Height}" : string.Empty;
    public long FileSizeBytes { get; set; }
    public string FileSize => FileSizeBytes > 1024 * 1024
        ? $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
        : $"{FileSizeBytes / 1024.0:F0} KB";
    public DateTime LastModified { get; set; }
    public string Extension { get; set; } = string.Empty;
}

/// <summary>
/// DTO for cache statistics.
/// </summary>
public class ImageCacheStatsDto
{
    public long DiskCacheSizeBytes { get; set; }
    public string DiskCacheSize => DiskCacheSizeBytes > 1024 * 1024
        ? $"{DiskCacheSizeBytes / (1024.0 * 1024.0):F1} MB"
        : $"{DiskCacheSizeBytes / 1024.0:F0} KB";
}
