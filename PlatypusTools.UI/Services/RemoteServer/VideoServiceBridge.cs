using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Bridge that scans Media Library folders for video files and serves them to the remote API.
/// Reads folders from enhanced_library_folders.json (shared with the Media Library tab).
/// Caches scan results and extracts thumbnails via FFmpeg.
/// </summary>
public class VideoServiceBridge : IVideoServiceBridge
{
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm",
        ".mpeg", ".mpg", ".m4v", ".3gp", ".ts", ".m2ts"
    };

    private readonly ConcurrentDictionary<string, VideoLibraryItemDto> _videoCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string?> _thumbnailCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _thumbnailCacheDir;
    private bool _isScanned;

    public VideoServiceBridge()
    {
        var dataDir = SettingsManager.DataDirectory;
        _thumbnailCacheDir = Path.Combine(dataDir, "Cache", "VideoThumbnails");
    }

    public async Task<IEnumerable<VideoLibraryItemDto>> GetVideoLibraryAsync()
    {
        if (!_isScanned)
        {
            await RescanLibraryAsync();
        }
        return _videoCache.Values
            .OrderBy(v => v.Folder)
            .ThenBy(v => v.FileName)
            .ToList();
    }

    public Task<IEnumerable<VideoLibraryItemDto>> SearchVideoLibraryAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.FromResult(Enumerable.Empty<VideoLibraryItemDto>());

        var results = _videoCache.Values
            .Where(v =>
                v.FileName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.Folder.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.FilePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(v => v.FileName)
            .Take(200)
            .AsEnumerable();

        return Task.FromResult(results);
    }

    public Task<IEnumerable<string>> GetVideoFoldersAsync()
    {
        var folders = LoadMediaLibraryFolders();
        return Task.FromResult<IEnumerable<string>>(folders);
    }

    public async Task RescanLibraryAsync()
    {
        var folders = LoadMediaLibraryFolders();
        Debug.WriteLine($"[VideoLibrary] Scanning {folders.Count} Media Library folder(s) for videos...");
        _videoCache.Clear();

        foreach (var folder in folders)
        {
            if (Directory.Exists(folder))
            {
                await ScanFolderAsync(folder);
            }
        }

        _isScanned = true;
        Debug.WriteLine($"[VideoLibrary] Scan complete. Found {_videoCache.Count} video file(s).");
    }

    public async Task<string?> GetVideoThumbnailAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return null;

        // Check memory cache
        if (_thumbnailCache.TryGetValue(filePath, out var cached))
            return cached;

        // Check disk cache
        var cacheFileName = ComputeCacheKey(filePath) + ".jpg";
        var cachePath = Path.Combine(_thumbnailCacheDir, cacheFileName);

        if (File.Exists(cachePath))
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(cachePath);
                var base64 = Convert.ToBase64String(bytes);
                _thumbnailCache[filePath] = base64;
                return base64;
            }
            catch { /* fall through to extract */ }
        }

        // Extract thumbnail via FFmpeg
        var thumbnail = await ExtractThumbnailAsync(filePath, cachePath);
        _thumbnailCache[filePath] = thumbnail;
        return thumbnail;
    }

    private async Task ScanFolderAsync(string folderPath)
    {
        try
        {
            var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            Debug.WriteLine($"[VideoLibrary] Found {files.Count} video files in {folderPath}");

            foreach (var file in files)
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    var item = new VideoLibraryItemDto
                    {
                        FilePath = file,
                        FileName = Path.GetFileName(file),
                        Title = Path.GetFileNameWithoutExtension(file),
                        FileSizeBytes = fileInfo.Length,
                        Folder = Path.GetDirectoryName(file) ?? string.Empty,
                        LastModified = fileInfo.LastWriteTimeUtc
                    };

                    _videoCache[file] = item;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[VideoLibrary] Error reading file info for {file}: {ex.Message}");
                }
            }

            // Probe metadata in background (non-blocking - first 500 files)
            _ = Task.Run(() => ProbeMetadataBatchAsync(files.Take(500).ToList()));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoLibrary] Error scanning folder {folderPath}: {ex.Message}");
        }
    }

    private async Task ProbeMetadataBatchAsync(List<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                var info = FFprobeService.GetMediaInfo(file);
                if (info != null && _videoCache.TryGetValue(file, out var item))
                {
                    item.DurationSeconds = info.Duration.TotalSeconds;
                    item.Width = info.Width;
                    item.Height = info.Height;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VideoLibrary] Error probing {Path.GetFileName(file)}: {ex.Message}");
            }

            // Small delay to avoid hammering FFprobe
            await Task.Delay(50);
        }

        Debug.WriteLine($"[VideoLibrary] Metadata probe complete for {files.Count} files");
    }

    private static async Task<string?> ExtractThumbnailAsync(string videoPath, string outputPath)
    {
        try
        {
            var ffmpegPath = FFmpegService.FindFfmpeg();
            if (ffmpegPath == null)
                return null;

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            // Extract frame at 10% of duration (or 5 seconds, whichever is smaller)
            var args = $"-i \"{videoPath}\" -ss 5 -vframes 1 -vf \"scale=320:-1\" -q:v 5 -y \"{outputPath}\"";

            var psi = new ProcessStartInfo(ffmpegPath, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            // Read stderr to prevent pipe buffer deadlock
            _ = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();

            var exited = await Task.Run(() => process.WaitForExit(10000));
            if (!exited)
            {
                try { process.Kill(); } catch { }
                return null;
            }

            if (File.Exists(outputPath) && new FileInfo(outputPath).Length > 0)
            {
                var bytes = await File.ReadAllBytesAsync(outputPath);
                return Convert.ToBase64String(bytes);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoLibrary] Error extracting thumbnail for {videoPath}: {ex.Message}");
        }

        return null;
    }

    private static string ComputeCacheKey(string filePath)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>
    /// Reads the Media Library folders from media_library_settings.json
    /// (shared with the Media Library tab).
    /// </summary>
    private static List<string> LoadMediaLibraryFolders()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "media_library_settings.json");

            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                
                if (doc.RootElement.TryGetProperty("LibraryFolders", out var foldersElement))
                {
                    var folders = JsonSerializer.Deserialize<List<string>>(foldersElement.GetRawText());
                    if (folders != null && folders.Count > 0)
                    {
                        Debug.WriteLine($"[VideoLibrary] Loaded {folders.Count} Media Library folder(s)");
                        return folders;
                    }
                }

                // Fallback: try PrimaryLibraryPath
                if (doc.RootElement.TryGetProperty("PrimaryLibraryPath", out var pathElement))
                {
                    var path = pathElement.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        Debug.WriteLine($"[VideoLibrary] Loaded 1 folder from PrimaryLibraryPath");
                        return new List<string> { path };
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VideoLibrary] Error loading Media Library folders: {ex.Message}");
        }

        return new List<string>();
    }
}
