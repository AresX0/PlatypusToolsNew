using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using PlatypusTools.UI.Utilities;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Bridges image browsing operations from the web app REST API to ThumbnailCacheService
/// and the file system. Reads library folders from media_library_settings.json.
/// </summary>
public class ImageBrowserBridge : IImageBrowserBridge
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp", ".ico"
    };

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlatypusTools", "media_library_settings.json");

    public async Task<IEnumerable<string>> GetImageFoldersAsync()
    {
        var folders = await LoadLibraryFoldersAsync();
        // Also add user's Pictures folder if not already present
        var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (!string.IsNullOrEmpty(picturesPath) && Directory.Exists(picturesPath))
        {
            if (!folders.Any(f => f.Equals(picturesPath, StringComparison.OrdinalIgnoreCase)))
            {
                folders.Add(picturesPath);
            }
        }
        return folders.Where(Directory.Exists);
    }

    public async Task<ImageBrowseResultDto> GetImagesAsync(string? folder = null, int page = 0, int pageSize = 50, string? search = null)
    {
        var allImages = new List<ImageItemDto>();

        IEnumerable<string> foldersToScan;
        if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
        {
            foldersToScan = new[] { folder };
        }
        else
        {
            foldersToScan = await GetImageFoldersAsync();
        }

        foreach (var dir in foldersToScan)
        {
            try
            {
                var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
                    .Where(f => ImageExtensions.Contains(Path.GetExtension(f)));

                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        allImages.Add(new ImageItemDto
                        {
                            FilePath = file,
                            FileName = fi.Name,
                            FileSizeBytes = fi.Length,
                            LastModified = fi.LastWriteTimeUtc,
                            Extension = fi.Extension.ToLowerInvariant()
                        });
                    }
                    catch { /* skip inaccessible files */ }
                }
            }
            catch { /* skip inaccessible folders */ }
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim().ToLowerInvariant();
            allImages = allImages.Where(i =>
                i.FileName.ToLowerInvariant().Contains(q) ||
                i.FilePath.ToLowerInvariant().Contains(q)).ToList();
        }

        // Sort by modified date descending (most recent first)
        allImages = allImages.OrderByDescending(i => i.LastModified).ToList();

        var totalCount = allImages.Count;
        var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        var paged = allImages.Skip(page * pageSize).Take(pageSize).ToList();

        return new ImageBrowseResultDto
        {
            Images = paged,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages,
            Folder = folder
        };
    }

    public async Task<byte[]?> GetThumbnailBytesAsync(string filePath, int size = 200)
    {
        if (!File.Exists(filePath)) return null;
        if (!ImageExtensions.Contains(Path.GetExtension(filePath))) return null;

        try
        {
            // Use ThumbnailCacheService to get/generate cached thumbnail
            // The service stores disk cache as JPEG files - read them directly for efficiency
            var cacheService = ThumbnailCacheService.Instance;

            // Trigger thumbnail generation/caching (runs on WPF thread internally)
            await cacheService.GetThumbnailAsync(filePath, size);

            // Now read the disk cache file directly as bytes
            var cacheKey = GetCacheKey(filePath, size);
            var diskCachePath = GetDiskCachePath(cacheKey);

            if (File.Exists(diskCachePath))
            {
                return await File.ReadAllBytesAsync(diskCachePath);
            }

            // Fallback: encode the BitmapSource to JPEG bytes
            var thumbnail = await cacheService.GetThumbnailAsync(filePath, size);
            if (thumbnail != null)
            {
                return EncodeBitmapSourceToJpeg(thumbnail);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<byte[]?> GetFullImageBytesAsync(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        if (!ImageExtensions.Contains(Path.GetExtension(filePath))) return null;

        try
        {
            return await File.ReadAllBytesAsync(filePath);
        }
        catch
        {
            return null;
        }
    }

    public ImageInfoDto? GetImageInfo(string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var fi = new FileInfo(filePath);
            var dto = new ImageInfoDto
            {
                FilePath = filePath,
                FileName = fi.Name,
                FileSizeBytes = fi.Length,
                LastModified = fi.LastWriteTimeUtc,
                Extension = fi.Extension.ToLowerInvariant()
            };

            // Try to get image dimensions
            try
            {
                var bitmap = ImageHelper.LoadFromFile(filePath);
                if (bitmap != null)
                {
                    dto.Width = bitmap.PixelWidth;
                    dto.Height = bitmap.PixelHeight;
                }
            }
            catch { /* dimensions unavailable */ }

            return dto;
        }
        catch
        {
            return null;
        }
    }

    public ImageCacheStatsDto GetCacheStats()
    {
        return new ImageCacheStatsDto
        {
            DiskCacheSizeBytes = ThumbnailCacheService.Instance.GetDiskCacheSize()
        };
    }

    public void ClearCache()
    {
        ThumbnailCacheService.Instance.ClearMemoryCache();
        ThumbnailCacheService.Instance.ClearDiskCache();
    }

    #region Private helpers

    private static async Task<List<string>> LoadLibraryFoldersAsync()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new List<string>();

            var json = await File.ReadAllTextAsync(SettingsPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var folders = new List<string>();

            if (root.TryGetProperty("LibraryFolders", out var foldersEl) && foldersEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in foldersEl.EnumerateArray())
                {
                    var path = item.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        folders.Add(path);
                }
            }
            else if (root.TryGetProperty("PrimaryLibraryPath", out var primaryEl))
            {
                var path = primaryEl.GetString();
                if (!string.IsNullOrWhiteSpace(path))
                    folders.Add(path);
            }

            return folders;
        }
        catch
        {
            return new List<string>();
        }
    }

    /// <summary>
    /// Mirrors ThumbnailCacheService.GetCacheKey for reading disk cache files.
    /// </summary>
    private static string GetCacheKey(string filePath, int size)
    {
        var lastWrite = File.GetLastWriteTimeUtc(filePath);
        return $"{filePath}|{size}|{lastWrite.Ticks}";
    }

    /// <summary>
    /// Mirrors ThumbnailCacheService.GetDiskCachePath for reading disk cache files.
    /// </summary>
    private static string GetDiskCachePath(string cacheKey)
    {
        var hash = cacheKey.GetHashCode().ToString("X");
        var diskCachePath = Path.Combine(SettingsManager.DataDirectory, "Cache", "Thumbnails");
        return Path.Combine(diskCachePath, $"{hash}.jpg");
    }

    /// <summary>
    /// Encodes a WPF BitmapSource to JPEG bytes.
    /// </summary>
    private static byte[] EncodeBitmapSourceToJpeg(BitmapSource source)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    #endregion
}
