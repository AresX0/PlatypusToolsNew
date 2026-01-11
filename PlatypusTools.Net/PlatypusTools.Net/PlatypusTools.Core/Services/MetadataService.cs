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
                System.Diagnostics.Debug.WriteLine($"[MetadataService] ReadMetadata called for: {filePath}");
                
                // Try native reading first (faster and doesn't require ExifTool)
                if (NativeMetadataReader.IsSupported(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[MetadataService] Using native metadata reader");
                    metadata = NativeMetadataReader.ReadMetadata(filePath);
                    System.Diagnostics.Debug.WriteLine($"[MetadataService] Native reader returned {metadata.Count} entries");
                    
                    if (metadata.Count > 0)
                        return metadata;
                }
                
                // Fall back to ExifTool for unsupported formats or if native reading failed
                System.Diagnostics.Debug.WriteLine($"[MetadataService] Falling back to ExifTool");
                return await ReadMetadataWithExifTool(filePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MetadataService] ERROR: {ex.Message}");
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

        /// <summary>
        /// Writes metadata to a file. Tries native writing first (TagLib for audio/video),
        /// then falls back to ExifTool.
        /// </summary>
        public async Task<bool> WriteMetadata(string filePath, Dictionary<string, string> metadata)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[MetadataService] WriteMetadata called for: {filePath}");
                System.Diagnostics.Debug.WriteLine($"[MetadataService] Writing {metadata.Count} tags");

                // Try native writing with TagLib for audio/video
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                var audioVideoExtensions = new HashSet<string> { ".mp3", ".mp4", ".m4a", ".m4v", ".flac", ".ogg", ".wav", ".aac", ".wma", ".mkv", ".avi", ".mov" };
                
                if (audioVideoExtensions.Contains(extension))
                {
                    if (WriteWithTagLib(filePath, metadata))
                    {
                        System.Diagnostics.Debug.WriteLine($"[MetadataService] Successfully wrote with TagLib");
                        return true;
                    }
                }

                // Fall back to ExifTool
                return await WriteMetadataWithExifTool(filePath, metadata);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MetadataService] WriteMetadata error: {ex.Message}");
                return false;
            }
        }

        private bool WriteWithTagLib(string filePath, Dictionary<string, string> metadata)
        {
            try
            {
                using var file = TagLib.File.Create(filePath);
                var tag = file.Tag;

                foreach (var kvp in metadata)
                {
                    var key = kvp.Key.ToLowerInvariant().Replace(" ", "");
                    var value = kvp.Value;

                    switch (key)
                    {
                        case "title":
                            tag.Title = value;
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
                    }
                }

                file.Save();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MetadataService] TagLib write error: {ex.Message}");
                return false;
            }
        }

        private async Task<bool> WriteMetadataWithExifTool(string filePath, Dictionary<string, string> metadata)
        {
            var exifToolPath = GetExifToolPath();
            if (!File.Exists(exifToolPath))
                return false;

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
                Path.Combine(appPath, "Tools", "exiftool-13.45_64", "exiftool.exe"),
                Path.Combine(appPath, "Tools", "exiftool.exe"),
                Path.Combine(appPath, "exiftool-13.45_64", "exiftool.exe"),
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
