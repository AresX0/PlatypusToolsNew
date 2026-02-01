using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public interface IMetadataService
    {
        Task<Dictionary<string, string>> ReadMetadata(string filePath);
        Task<bool> WriteMetadata(string filePath, Dictionary<string, string> metadata);
        Task<bool> ClearMetadata(string filePath, string[] tags);
        Task<List<string>> GetAvailableTags(string filePath);
        string GetExifToolPath();
        bool IsExifToolAvailable();
        void SetCustomExifToolPath(string path);
        string? GetCustomExifToolPath();
    }

    internal static class ServiceLogger
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "Logs", "service_debug.log");

        public static void Log(string message)
        {
            try
            {
                var dir = Path.GetDirectoryName(LogPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
                
                var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
                System.Diagnostics.Debug.WriteLine(message);
            }
            catch { }
        }
    }

    public class MetadataServiceEnhanced : IMetadataService
    {
        /// <summary>
        /// Reads metadata from a file. Uses native .NET libraries first, then falls back to ExifTool.
        /// </summary>
        public async Task<Dictionary<string, string>> ReadMetadata(string filePath)
        {
            var metadata = new Dictionary<string, string>();
            
            try
            {
                ServiceLogger.Log($"[MetadataService] ReadMetadata called for: {filePath}");
                
                // Try native reading first (faster and doesn't require ExifTool)
                if (NativeMetadataReader.IsSupported(filePath))
                {
                    ServiceLogger.Log($"[MetadataService] Using native metadata reader");
                    metadata = NativeMetadataReader.ReadMetadata(filePath);
                    ServiceLogger.Log($"[MetadataService] Native reader returned {metadata.Count} entries");
                    
                    if (metadata.Count > 0)
                        return metadata;
                }
                
                // Fall back to ExifTool for unsupported formats or if native reading failed
                ServiceLogger.Log($"[MetadataService] Falling back to ExifTool");
                return await ReadMetadataWithExifTool(filePath);
            }
            catch (Exception ex)
            {
                ServiceLogger.Log($"[MetadataService] ERROR: {ex.Message}");
            }

            return metadata;
        }

        private async Task<Dictionary<string, string>> ReadMetadataWithExifTool(string filePath)
        {
            var metadata = new Dictionary<string, string>();
            
            var exifToolPath = GetExifToolPath();
            if (!File.Exists(exifToolPath))
                return metadata;

            var startInfo = new ProcessStartInfo
            {
                FileName = exifToolPath,
                Arguments = $"-a -G1 -s \"{filePath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exifToolPath)
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            // Send Enter key to close exiftool(-k).exe if it's waiting
            try { await process.StandardInput.WriteLineAsync(); } catch { }
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var errorOutput = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cts.Token);

                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = line.Substring(0, colonIndex).Trim();
                        var value = line.Substring(colonIndex + 1).Trim();
                        
                        if (key.StartsWith("["))
                        {
                            var endBracket = key.IndexOf(']');
                            if (endBracket > 0 && endBracket < key.Length - 1)
                                key = key.Substring(endBracket + 1).Trim();
                        }
                        
                        metadata[key] = value;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(); } catch { }
            }

            return metadata;
        }


        // Read-only tags that cannot be written
        private static readonly HashSet<string> ReadOnlyTags = new(StringComparer.OrdinalIgnoreCase)
        {
            "AudioChannels", "AudioCodec", "AudioSampleRate", "AudioBitrate", "BitsPerSample",
            "Directory", "Duration", "FileCreateDate", "FileModifyDate", "FileName",
            "FileSize", "FileType", "FileAccessDate", "FileInodeChangeDate", "FileModifiedDate",
            "ImageHeight", "ImageSize", "ImageWidth", "VideoCodec", "BitRate", "Bitrate",
            "MIMEType", "SourceFile", "ExifToolVersion", "Warning", "Error",
            "FramesPerSecond", "Height", "Width", "SamplesPerSecond", "StreamCount",
            "DetectedFileTypeLongName", "DetectedFileTypeName", "DetectedMIMEType",
            "ExpectedFileNameExtension", "Version", "TrackId", "Matrix", "Modified",
            "MinorVersion", "PosterTime", "PreferredRate", "PreferredVolume",
            "PreviewDuration", "PreviewTime", "Rotation", "SelectionDuration", "SelectionTime",
            "Volume", "NextTrackId", "Created"
        };

        // Normalize tag key by removing group prefixes like [AVI], [File], etc.
        private static string NormalizeTagKey(string key)
        {
            if (key.StartsWith("["))
            {
                var endBracket = key.IndexOf(']');
                if (endBracket > 0 && endBracket < key.Length - 1)
                    key = key.Substring(endBracket + 1).Trim();
            }
            return key.Replace(":", "").Replace(" ", "");
        }

        private static bool IsReadOnlyTag(string key) => ReadOnlyTags.Contains(NormalizeTagKey(key));


        /// <summary>
        /// Writes metadata to a file. Tries native writing first (TagLib for audio/video),
        /// then falls back to ExifTool.
        /// </summary>
        public async Task<bool> WriteMetadata(string filePath, Dictionary<string, string> metadata)
        {
            try
            {
                // Filter out read-only tags
                var writableTags = metadata
                    .Where(kvp => !IsReadOnlyTag(kvp.Key))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

                ServiceLogger.Log($"[MetadataService] WriteMetadata called for: {filePath}");
                ServiceLogger.Log($"[MetadataService] Writing {writableTags.Count} tags (filtered from {metadata.Count})");

                if (writableTags.Count == 0)
                {
                    ServiceLogger.Log($"[MetadataService] No writable tags to save");
                    return true; // Nothing to write is success
                }

                // Try native writing with TagLib for audio/video
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var tagLibExtensions = new HashSet<string> { ".mp3", ".mp4", ".m4a", ".m4v", ".flac", ".ogg", ".wav", ".aac", ".wma" };
                
                // Note: TagLib doesn't fully support MKV/AVI writing, skip to ExifTool for those
                if (tagLibExtensions.Contains(extension))
                {
                    if (WriteWithTagLib(filePath, writableTags))
                    {
                        ServiceLogger.Log($"[MetadataService] Successfully wrote with TagLib");
                        return true;
                    }
                }

                // Fall back to ExifTool (but note MKV is not supported by ExifTool for writing either)
                var exifToolUnsupported = new HashSet<string> { ".mkv", ".webm" };
                if (exifToolUnsupported.Contains(extension))
                {
                    ServiceLogger.Log($"[MetadataService] ExifTool does not support writing to {extension} files");
                    return false;
                }

                return await WriteMetadataWithExifTool(filePath, writableTags);
            }
            catch (Exception ex)
            {
                ServiceLogger.Log($"[MetadataService] WriteMetadata error: {ex.Message}");
                return false;
            }
        }

        private bool WriteWithTagLib(string filePath, Dictionary<string, string> metadata)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 500;
            
            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    ServiceLogger.Log($"[MetadataService] WriteWithTagLib called for: {filePath} (attempt {attempt}/{maxRetries})");
                    ServiceLogger.Log($"[MetadataService] Metadata keys: {string.Join(", ", metadata.Keys)}");
                    
                    // Force garbage collection to release any lingering file handles
                    if (attempt > 1)
                    {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        Thread.Sleep(retryDelayMs);
                    }
                    
                    using var file = TagLib.File.Create(filePath);
                var tag = file.Tag;
                var tagsWritten = 0;

                foreach (var kvp in metadata)
                {
                    var key = kvp.Key.ToLowerInvariant().Replace(" ", "").Replace(":", "");
                    var value = kvp.Value;
                    ServiceLogger.Log($"[MetadataService] Processing tag: {kvp.Key} -> normalized: {key} = {value}");

                    switch (key)
                    {
                        case "title":
                            tag.Title = value;
                            tagsWritten++;
                            break;
                        case "artist":
                        case "performers":
                            tag.Performers = value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim()).ToArray();
                            break;
                        case "album":
                            tag.Album = value;
                            break;
                        case "year":
                            if (uint.TryParse(value, out var year))
                                tag.Year = year;
                            break;
                        case "track":
                        case "tracknumber":
                            if (uint.TryParse(value, out var track))
                                tag.Track = track;
                            break;
                        case "genre":
                        case "genres":
                            tag.Genres = value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim()).ToArray();
                            break;
                        case "comment":
                        case "comments":
                            tag.Comment = value;
                            break;
                        case "copyright":
                            tag.Copyright = value;
                            break;
                        case "composer":
                        case "composers":
                            tag.Composers = value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim()).ToArray();
                            break;
                        case "albumartist":
                        case "albumartists":
                            tag.AlbumArtists = value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                                .Select(s => s.Trim()).ToArray();
                            break;
                        case "description":
                            tag.Description = value;
                            break;
                        case "conductor":
                            tag.Conductor = value;
                            break;
                        case "disc":
                        case "discnumber":
                            if (uint.TryParse(value, out var disc))
                                tag.Disc = disc;
                            break;
                        case "xmpownername":
                        case "ownername":
                        case "owner":
                            // XMP:OwnerName - store in comment or custom field
                            tag.Comment = string.IsNullOrEmpty(tag.Comment) ? $"Owner: {value}" : tag.Comment;
                            tagsWritten++;
                            break;
                        default:
                            ServiceLogger.Log($"[MetadataService] Unhandled tag: {key}");
                            break;
                    }
                }

                ServiceLogger.Log($"[MetadataService] Tags written: {tagsWritten}, calling Save()");
                    file.Save();
                    ServiceLogger.Log($"[MetadataService] TagLib Save() completed successfully");
                    return true;
                }
                catch (IOException ioEx) when (attempt < maxRetries)
                {
                    ServiceLogger.Log($"[MetadataService] TagLib IO error (attempt {attempt}): {ioEx.Message} - will retry");
                    continue;
                }
                catch (Exception ex)
                {
                    ServiceLogger.Log($"[MetadataService] TagLib write error: {ex.Message}");
                    ServiceLogger.Log($"[MetadataService] TagLib exception: {ex}");
                    if (attempt == maxRetries)
                        return false;
                }
            }
            return false;
        }

        private async Task<bool> WriteMetadataWithExifTool(string filePath, Dictionary<string, string> metadata)
        {
            ServiceLogger.Log($"[MetadataService] WriteMetadataWithExifTool called for: {filePath}");
            
            var exifToolPath = GetExifToolPath();
            ServiceLogger.Log($"[MetadataService] ExifTool path: {exifToolPath}");
            
            if (!File.Exists(exifToolPath))
            {
                ServiceLogger.Log($"[MetadataService] ExifTool not found at: {exifToolPath}");
                return false;
            }

            var args = new List<string>();
            foreach (var kvp in metadata)
            {
                if (!string.IsNullOrEmpty(kvp.Value))
                {
                    var escapedValue = kvp.Value.Replace("\"", "\\\"");
                    args.Add($"-{kvp.Key}=\"{escapedValue}\"");
                }
            }
            args.Add("-overwrite_original");
            args.Add($"\"{filePath}\"");

            var commandLine = string.Join(" ", args);
            ServiceLogger.Log($"[MetadataService] ExifTool command: {exifToolPath} {commandLine}");

            var startInfo = new ProcessStartInfo
            {
                FileName = exifToolPath,
                Arguments = commandLine,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exifToolPath)
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();
            
            try { await process.StandardInput.WriteLineAsync(); } catch { }
            
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync(cts.Token);
                
                ServiceLogger.Log($"[MetadataService] ExifTool exit code: {process.ExitCode}");
                ServiceLogger.Log($"[MetadataService] ExifTool stdout: {stdout}");
                if (!string.IsNullOrEmpty(stderr))
                    ServiceLogger.Log($"[MetadataService] ExifTool stderr: {stderr}");
                
                return process.ExitCode == 0;
            }
            catch (OperationCanceledException)
            {
                ServiceLogger.Log($"[MetadataService] ExifTool timed out after 30 seconds");
                try { process.Kill(); } catch { }
                return false;
            }
        }

        public async Task<bool> ClearMetadata(string filePath, string[] tags)
        {
            try
            {
                var exifToolPath = GetExifToolPath();
                if (!File.Exists(exifToolPath))
                    return false;

                var args = new List<string>();
                foreach (var tag in tags)
                {
                    args.Add($"-{tag}=");
                }
                args.Add($"-overwrite_original");
                args.Add($"\"{filePath}\"");

                var startInfo = new ProcessStartInfo
                {
                    FileName = exifToolPath,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    RedirectStandardInput = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exifToolPath)
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                
                // Send Enter key in case exiftool(-k).exe is being used
                try { await process.StandardInput.WriteLineAsync(); } catch { }
                
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await process.StandardOutput.ReadToEndAsync();
                    await process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync(cts.Token);
                    return process.ExitCode == 0;
                }
                catch (OperationCanceledException)
                {
                    try { process.Kill(); } catch { }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        public async Task<List<string>> GetAvailableTags(string filePath)
        {
            var tags = new List<string>();

            try
            {
                var metadata = await ReadMetadata(filePath);
                tags.AddRange(metadata.Keys);
            }
            catch { }

            return tags;
        }

        public string GetExifToolPath()
        {
            // Check for custom path in configuration first
            var customPath = GetCustomExifToolPath();
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                return customPath;

            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            
            // Build list of all possible paths - most specific first
            var allPaths = new List<string>
            {
                // Installed location (MSI installs Tools folder next to exe)
                Path.Combine(appPath, "Tools", "exiftool", "exiftool.exe"),  // MSI installs here
                Path.Combine(appPath, "Tools", "exiftool-13.45_64", "exiftool.exe"),
                Path.Combine(appPath, "Tools", "exiftool.exe"),
                Path.Combine(appPath, "Tools", "exiftool_files", "exiftool.exe"),
                Path.Combine(appPath, "exiftool-13.45_64", "exiftool.exe"),
                // Program Files locations (system-installed)
                @"C:\Program Files\ExifTool\exiftool.exe",
                @"C:\Program Files (x86)\ExifTool\exiftool.exe",
                Path.Combine(appPath, "exiftool.exe"),
                
                // Program Files locations
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "exiftool-13.45_64", "exiftool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "exiftool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ExifTool", "exiftool.exe"),
                
                // Dev environment paths (relative to project)
                Path.Combine(appPath, "..", "..", "..", "..", "PlatypusUtils", "Tools", "exiftool-13.45_64", "exiftool.exe"),
                Path.Combine(appPath, "..", "..", "..", "..", "PlatypusUtils", "Tools", "exiftool.exe"),
                
                // Common user install locations
                @"C:\exiftool\exiftool.exe",
                @"C:\Program Files\exiftool\exiftool.exe"
            };

            foreach (var path in allPaths)
            {
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }

            // Search recursively in Program Files directories
            try
            {
                var programFilesDirs = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                };

                foreach (var programFilesDir in programFilesDirs)
                {
                    if (string.IsNullOrEmpty(programFilesDir) || !Directory.Exists(programFilesDir))
                        continue;

                    var exiftoolPath = SearchForExifTool(programFilesDir);
                    if (!string.IsNullOrEmpty(exiftoolPath))
                        return exiftoolPath;
                }
            }
            catch { }

            return "exiftool";
        }

        private string? SearchForExifTool(string directory)
        {
            try
            {
                // Search in current directory first
                var exeFiles = Directory.GetFiles(directory, "exiftool.exe", SearchOption.TopDirectoryOnly);
                if (exeFiles.Length > 0)
                    return exeFiles[0];

                // Search in subdirectories (up to 3 levels deep to avoid performance issues)
                var subdirs = Directory.GetDirectories(directory);
                foreach (var subdir in subdirs)
                {
                    try
                    {
                        // Check immediate subdirectory
                        exeFiles = Directory.GetFiles(subdir, "exiftool.exe", SearchOption.TopDirectoryOnly);
                        if (exeFiles.Length > 0)
                            return exeFiles[0];

                        // Check one level deeper
                        var subsubdirs = Directory.GetDirectories(subdir);
                        foreach (var subsubdir in subsubdirs)
                        {
                            try
                            {
                                exeFiles = Directory.GetFiles(subsubdir, "exiftool.exe", SearchOption.TopDirectoryOnly);
                                if (exeFiles.Length > 0)
                                    return exeFiles[0];
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }

        public void SetCustomExifToolPath(string path)
        {
            try
            {
                var configPath = GetConfigFilePath();
                var dir = Path.GetDirectoryName(configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(configPath, path);
            }
            catch { }
        }

        public string? GetCustomExifToolPath()
        {
            try
            {
                var configPath = GetConfigFilePath();
                if (File.Exists(configPath))
                    return File.ReadAllText(configPath).Trim();
            }
            catch { }

            return null;
        }

        private string GetConfigFilePath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "PlatypusTools", "exiftool_path.txt");
        }

        public bool IsExifToolAvailable()
        {
            try
            {
                var exifToolPath = GetExifToolPath();
                if (exifToolPath == "exiftool")
                {
                    var process = Process.Start(new ProcessStartInfo
                    {
                        FileName = "exiftool",
                        Arguments = "-ver",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    return process != null;
                }
                return File.Exists(exifToolPath);
            }
            catch
            {
                return false;
            }
        }
    }

    public static class MetadataService
    {
        public static IDictionary<string, object?> GetMetadata(string filePath, string? exiftoolPath = null)
        {
            var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(filePath)) return result;

            var exif = MediaService.ResolveToolPath("exiftool", exiftoolPath);
            if (!string.IsNullOrEmpty(exif))
            {
                try
                {
                    using var proc = MediaService.StartTool(exif, $"-j \"{filePath}\"");
                    if (proc == null) return result;
                    var outJson = proc.StandardOutput.ReadToEnd();
                    proc.WaitForExit(3000);
                    if (!string.IsNullOrWhiteSpace(outJson))
                    {
                        var arr = JsonSerializer.Deserialize<List<JsonElement>>(outJson);
                        if (arr != null && arr.Count > 0)
                        {
                            foreach (var prop in arr[0].EnumerateObject()) result[prop.Name] = prop.Value.ToString();
                        }
                    }
                }
                catch { }
                return result;
            }

            // Fallback: basic file info
            try
            {
                var fi = new FileInfo(filePath);
                result["Name"] = fi.Name;
                result["Length"] = fi.Length;
                result["Created"] = fi.CreationTimeUtc;
                result["Modified"] = fi.LastWriteTimeUtc;
            }
            catch { }
            return result;
        }
    }
}





