using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Library configuration stored as JSON.
    /// </summary>
    public class MediaLibraryConfig
    {
        public string PrimaryLibraryPath { get; set; } = string.Empty;
        public List<string> WatchFolders { get; set; } = new();
        public DateTime LastUpdated { get; set; } = DateTime.Now;
        public List<MediaLibraryEntry> Entries { get; set; } = new();
    }

    /// <summary>
    /// An entry in the media library.
    /// </summary>
    public class MediaLibraryEntry
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public long Size { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime DateModified { get; set; }
        public string? Sha256Hash { get; set; }
        public string? SourcePath { get; set; }
        public bool IsInLibrary { get; set; }
    }

    /// <summary>
    /// Progress information for media scanning/copying.
    /// </summary>
    public class MediaScanProgress
    {
        public string CurrentFile { get; set; } = string.Empty;
        public int FilesScanned { get; set; }
        public int TotalFiles { get; set; }
        public int FilesCopied { get; set; }
        public long BytesCopied { get; set; }
        public string StatusMessage { get; set; } = string.Empty;
        public double PercentComplete => TotalFiles > 0 ? (double)FilesScanned / TotalFiles * 100 : 0;
    }

    public class MediaLibraryService
    {
        private const string LibraryConfigFileName = "media_library.json";
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg"
        };

        private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".flac", ".aac", ".m4a", ".wma", ".ogg", ".opus"
        };

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tiff"
        };

        private MediaLibraryConfig? _config;
        private string? _configPath;

        /// <summary>
        /// Get the library configuration file path.
        /// </summary>
        public string GetConfigPath(string libraryPath)
        {
            return Path.Combine(libraryPath, LibraryConfigFileName);
        }

        /// <summary>
        /// Load or create library configuration.
        /// </summary>
        public async Task<MediaLibraryConfig> LoadConfigAsync(string libraryPath)
        {
            _configPath = GetConfigPath(libraryPath);

            if (File.Exists(_configPath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(_configPath);
                    _config = JsonSerializer.Deserialize<MediaLibraryConfig>(json, JsonOptions) ?? new MediaLibraryConfig();
                }
                catch
                {
                    _config = new MediaLibraryConfig();
                }
            }
            else
            {
                _config = new MediaLibraryConfig();
            }

            _config.PrimaryLibraryPath = libraryPath;
            return _config;
        }

        /// <summary>
        /// Save library configuration.
        /// </summary>
        public async Task SaveConfigAsync()
        {
            if (_config == null || string.IsNullOrEmpty(_configPath))
                return;

            _config.LastUpdated = DateTime.Now;
            var json = JsonSerializer.Serialize(_config, JsonOptions);
            await File.WriteAllTextAsync(_configPath, json);
        }

        /// <summary>
        /// Set the primary library path.
        /// </summary>
        public async Task SetPrimaryLibraryPathAsync(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            await LoadConfigAsync(path);
            await SaveConfigAsync();
        }

        /// <summary>
        /// Scan a folder for media files.
        /// </summary>
        public async Task<List<MediaItem>> ScanFolderForMediaAsync(
            string folderPath,
            IProgress<MediaScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var items = new List<MediaItem>();

            await Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(folderPath, "*.*", SearchOption.AllDirectories);
                    var mediaFiles = files.Where(f => IsMediaFile(f)).ToList();

                    for (int i = 0; i < mediaFiles.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var file = mediaFiles[i];
                        try
                        {
                            var ext = Path.GetExtension(file);
                            var fileInfo = new FileInfo(file);

                            MediaType type = GetMediaType(ext);

                            var item = new MediaItem
                            {
                                FilePath = file,
                                FileName = fileInfo.Name,
                                Type = type,
                                Size = fileInfo.Length,
                                DateAdded = fileInfo.CreationTime,
                                DateModified = fileInfo.LastWriteTime
                            };

                            items.Add(item);

                            progress?.Report(new MediaScanProgress
                            {
                                CurrentFile = file,
                                FilesScanned = i + 1,
                                TotalFiles = mediaFiles.Count,
                                StatusMessage = $"Scanning: {Path.GetFileName(file)}"
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }, cancellationToken);

            return items;
        }

        /// <summary>
        /// Scan multiple drives/folders for media and prepare for import.
        /// </summary>
        public async Task<List<MediaItem>> ScanDrivesForMediaAsync(
            IEnumerable<string> paths,
            IProgress<MediaScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var allItems = new List<MediaItem>();

            foreach (var path in paths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new MediaScanProgress
                {
                    StatusMessage = $"Scanning: {path}"
                });

                var items = await ScanFolderForMediaAsync(path, progress, cancellationToken);
                allItems.AddRange(items);
            }

            return allItems;
        }

        /// <summary>
        /// Copy media files to the library.
        /// </summary>
        public async Task<int> CopyToLibraryAsync(
            IEnumerable<MediaItem> items,
            string libraryPath,
            bool organizeByType = true,
            IProgress<MediaScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_config == null)
            {
                await LoadConfigAsync(libraryPath);
            }

            var itemsList = items.ToList();
            int copied = 0;
            long bytesCopied = 0;

            for (int i = 0; i < itemsList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var item = itemsList[i];
                try
                {
                    // Determine destination folder
                    string destFolder = libraryPath;
                    if (organizeByType)
                    {
                        destFolder = item.Type switch
                        {
                            MediaType.Video => Path.Combine(libraryPath, "Videos"),
                            MediaType.Audio => Path.Combine(libraryPath, "Audio"),
                            MediaType.Image => Path.Combine(libraryPath, "Images"),
                            _ => Path.Combine(libraryPath, "Other")
                        };
                    }

                    if (!Directory.Exists(destFolder))
                    {
                        Directory.CreateDirectory(destFolder);
                    }

                    var destPath = Path.Combine(destFolder, item.FileName);

                    // Handle duplicates
                    if (File.Exists(destPath))
                    {
                        var baseName = Path.GetFileNameWithoutExtension(item.FileName);
                        var ext = Path.GetExtension(item.FileName);
                        int counter = 1;
                        while (File.Exists(destPath))
                        {
                            destPath = Path.Combine(destFolder, $"{baseName}_{counter++}{ext}");
                        }
                    }

                    // Copy file
                    await Task.Run(() => File.Copy(item.FilePath, destPath, false), cancellationToken);

                    // Add to library entries
                    _config?.Entries.Add(new MediaLibraryEntry
                    {
                        FilePath = destPath,
                        FileName = Path.GetFileName(destPath),
                        Type = item.Type,
                        Size = item.Size,
                        DateAdded = DateTime.Now,
                        DateModified = item.DateModified,
                        SourcePath = item.FilePath,
                        IsInLibrary = true
                    });

                    copied++;
                    bytesCopied += item.Size;

                    progress?.Report(new MediaScanProgress
                    {
                        CurrentFile = item.FileName,
                        FilesScanned = i + 1,
                        TotalFiles = itemsList.Count,
                        FilesCopied = copied,
                        BytesCopied = bytesCopied,
                        StatusMessage = $"Copied: {item.FileName}"
                    });
                }
                catch (Exception ex)
                {
                    progress?.Report(new MediaScanProgress
                    {
                        StatusMessage = $"Error copying {item.FileName}: {ex.Message}"
                    });
                }
            }

            // Save updated config
            await SaveConfigAsync();

            return copied;
        }

        /// <summary>
        /// Get all library entries.
        /// </summary>
        public List<MediaLibraryEntry> GetLibraryEntries()
        {
            return _config?.Entries ?? new List<MediaLibraryEntry>();
        }

        /// <summary>
        /// Refresh library by scanning the primary path.
        /// </summary>
        public async Task RefreshLibraryAsync(
            IProgress<MediaScanProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (_config == null || string.IsNullOrEmpty(_config.PrimaryLibraryPath))
                return;

            var items = await ScanFolderForMediaAsync(_config.PrimaryLibraryPath, progress, cancellationToken);

            _config.Entries.Clear();
            foreach (var item in items)
            {
                _config.Entries.Add(new MediaLibraryEntry
                {
                    FilePath = item.FilePath,
                    FileName = item.FileName,
                    Type = item.Type,
                    Size = item.Size,
                    DateAdded = item.DateAdded,
                    DateModified = item.DateModified,
                    IsInLibrary = true
                });
            }

            await SaveConfigAsync();
        }

        /// <summary>
        /// Check if a file is a media file.
        /// </summary>
        public static bool IsMediaFile(string path)
        {
            var ext = Path.GetExtension(path);
            return VideoExtensions.Contains(ext) || 
                   AudioExtensions.Contains(ext) || 
                   ImageExtensions.Contains(ext);
        }

        /// <summary>
        /// Get media type from extension.
        /// </summary>
        public static MediaType GetMediaType(string extension)
        {
            if (VideoExtensions.Contains(extension)) return MediaType.Video;
            if (AudioExtensions.Contains(extension)) return MediaType.Audio;
            if (ImageExtensions.Contains(extension)) return MediaType.Image;
            return MediaType.Unknown;
        }

        public async Task<List<MediaItem>> ScanDirectoryAsync(
            string path,
            bool includeSubdirectories = true,
            Action<int, int, string>? onProgress = null,
            Action<List<MediaItem>>? onBatchProcessed = null,
            CancellationToken cancellationToken = default)
        {
            const int BatchSize = 300;
            var items = new List<MediaItem>();

            await Task.Run(async () =>
            {
                try
                {
                    var totalFiles = 0;
                    var processedFiles = 0;
                    var batchItems = new List<MediaItem>();

                    onProgress?.Invoke(0, 0, "Discovering files...");

                    // Use robust enumeration for deep directories
                    foreach (var file in EnumerateMediaFilesRobust(path, includeSubdirectories))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        totalFiles++;

                        try
                        {
                            var ext = Path.GetExtension(file);
                            var fileInfo = new FileInfo(file);

                            MediaType type = MediaType.Unknown;
                            if (VideoExtensions.Contains(ext)) type = MediaType.Video;
                            else if (AudioExtensions.Contains(ext)) type = MediaType.Audio;
                            else if (ImageExtensions.Contains(ext)) type = MediaType.Image;
                            else continue;

                            var item = new MediaItem
                            {
                                FilePath = file,
                                FileName = fileInfo.Name,
                                Type = type,
                                Size = fileInfo.Length,
                                DateAdded = fileInfo.CreationTime,
                                DateModified = fileInfo.LastWriteTime
                            };

                            items.Add(item);
                            batchItems.Add(item);
                            processedFiles++;

                            // Report progress and update UI in batches
                            if (processedFiles % BatchSize == 0)
                            {
                                onProgress?.Invoke(processedFiles, totalFiles, $"Scanned {processedFiles} files...");
                                onBatchProcessed?.Invoke(new List<MediaItem>(batchItems));
                                batchItems.Clear();
                                await Task.Delay(1); // Yield to UI
                            }
                        }
                        catch { }
                    }

                    // Process remaining batch
                    if (batchItems.Count > 0)
                    {
                        onBatchProcessed?.Invoke(batchItems);
                    }

                    onProgress?.Invoke(processedFiles, processedFiles, $"Complete: {processedFiles} media files");
                }
                catch { }
            }, cancellationToken);

            return items;
        }

        /// <summary>
        /// Enumerate media files robustly, handling access denied and deep directories.
        /// </summary>
        private IEnumerable<string> EnumerateMediaFilesRobust(string directory, bool recursive)
        {
            var allExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var ext in VideoExtensions) allExtensions.Add(ext);
            foreach (var ext in AudioExtensions) allExtensions.Add(ext);
            foreach (var ext in ImageExtensions) allExtensions.Add(ext);

            var dirsToProcess = new Queue<string>();
            dirsToProcess.Enqueue(directory);

            while (dirsToProcess.Count > 0)
            {
                var currentDir = dirsToProcess.Dequeue();

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(currentDir);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (PathTooLongException)
                {
                    continue;
                }
                catch
                {
                    continue;
                }

                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file);
                    if (allExtensions.Contains(ext))
                        yield return file;
                }

                if (recursive)
                {
                    IEnumerable<string> subdirs;
                    try
                    {
                        subdirs = Directory.EnumerateDirectories(currentDir);
                    }
                    catch
                    {
                        continue;
                    }

                    foreach (var subdir in subdirs)
                    {
                        dirsToProcess.Enqueue(subdir);
                    }
                }
            }
        }

        public async Task<MediaMetadata?> GetMetadataAsync(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext))
            {
                return await GetFFprobeMetadataAsync(filePath);
            }
            else if (ImageExtensions.Contains(ext))
            {
                return GetImageMetadata(filePath);
            }

            return null;
        }

        private async Task<MediaMetadata?> GetFFprobeMetadataAsync(string filePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    Arguments = $"-v quiet -print_format json -show_format -show_streams \"{filePath}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                var json = JsonDocument.Parse(output);
                var format = json.RootElement.GetProperty("format");

                var metadata = new MediaMetadata
                {
                    Duration = format.TryGetProperty("duration", out var dur) ? TimeSpan.FromSeconds(double.Parse(dur.GetString() ?? "0")) : TimeSpan.Zero,
                    Bitrate = format.TryGetProperty("bit_rate", out var br) ? long.Parse(br.GetString() ?? "0") : 0,
                    Format = format.TryGetProperty("format_name", out var fmt) ? fmt.GetString() : null
                };

                if (json.RootElement.TryGetProperty("streams", out var streams))
                {
                    foreach (var stream in streams.EnumerateArray())
                    {
                        if (stream.TryGetProperty("codec_type", out var codecType))
                        {
                            var type = codecType.GetString();
                            if (type == "video")
                            {
                                metadata.Width = stream.TryGetProperty("width", out var w) ? w.GetInt32() : 0;
                                metadata.Height = stream.TryGetProperty("height", out var h) ? h.GetInt32() : 0;
                                metadata.VideoCodec = stream.TryGetProperty("codec_name", out var vc) ? vc.GetString() : null;
                                metadata.FrameRate = stream.TryGetProperty("r_frame_rate", out var fr) ? fr.GetString() : null;
                            }
                            else if (type == "audio")
                            {
                                metadata.AudioCodec = stream.TryGetProperty("codec_name", out var ac) ? ac.GetString() : null;
                                metadata.SampleRate = stream.TryGetProperty("sample_rate", out var sr) ? int.Parse(sr.GetString() ?? "0") : 0;
                                metadata.Channels = stream.TryGetProperty("channels", out var ch) ? ch.GetInt32() : 0;
                            }
                        }
                    }
                }

                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private MediaMetadata? GetImageMetadata(string filePath)
        {
            try
            {
                using var img = System.Drawing.Image.FromFile(filePath);
                return new MediaMetadata
                {
                    Width = img.Width,
                    Height = img.Height,
                    Format = img.RawFormat.ToString()
                };
            }
            catch
            {
                return null;
            }
        }

        public List<MediaItem> FilterByType(List<MediaItem> items, MediaType type)
        {
            return items.Where(i => i.Type == type).ToList();
        }

        public List<MediaItem> SortBySize(List<MediaItem> items, bool descending = true)
        {
            return descending ? items.OrderByDescending(i => i.Size).ToList() : items.OrderBy(i => i.Size).ToList();
        }

        public List<MediaItem> SortByDate(List<MediaItem> items, bool descending = true)
        {
            return descending ? items.OrderByDescending(i => i.DateModified).ToList() : items.OrderBy(i => i.DateModified).ToList();
        }
    }

    public class MediaItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public MediaType Type { get; set; }
        public long Size { get; set; }
        public DateTime DateAdded { get; set; }
        public DateTime DateModified { get; set; }
        public MediaMetadata? Metadata { get; set; }
    }

    public class MediaMetadata
    {
        public TimeSpan Duration { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long Bitrate { get; set; }
        public string? Format { get; set; }
        public string? VideoCodec { get; set; }
        public string? AudioCodec { get; set; }
        public string? FrameRate { get; set; }
        public int SampleRate { get; set; }
        public int Channels { get; set; }
    }

    public enum MediaType
    {
        Unknown,
        Video,
        Audio,
        Image
    }
}
