using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
        public async Task<Dictionary<string, string>> ReadMetadata(string filePath)
        {
            var metadata = new Dictionary<string, string>();
            
            try
            {
                var exifToolPath = GetExifToolPath();
                if (!File.Exists(exifToolPath))
                    return metadata;

                var startInfo = new ProcessStartInfo
                {
                    FileName = exifToolPath,
                    Arguments = $"-a -G1 -s \"{filePath}\"",  // -a = all tags, -G1 = group names, -s = short output
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                var output = await process.StandardOutput.ReadToEndAsync();
                var errorOutput = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Parse output: each line is "[Group] TagName: Value"
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var colonIndex = line.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        var key = line.Substring(0, colonIndex).Trim();
                        var value = line.Substring(colonIndex + 1).Trim();
                        
                        // Remove [Group] prefix if present
                        if (key.StartsWith("["))
                        {
                            var endBracket = key.IndexOf(']');
                            if (endBracket > 0 && endBracket < key.Length - 1)
                            {
                                key = key.Substring(endBracket + 1).Trim();
                            }
                        }
                        
                        metadata[key] = value;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log error but return empty dictionary
                Console.WriteLine($"Error reading metadata: {ex.Message}");
            }

            return metadata;
        }

        public async Task<bool> WriteMetadata(string filePath, Dictionary<string, string> metadata)
        {
            try
            {
                var exifToolPath = GetExifToolPath();
                if (!File.Exists(exifToolPath))
                    return false;

                // Build arguments
                var args = new List<string>();
                foreach (var kvp in metadata)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                        args.Add($"-{kvp.Key}=\"{kvp.Value}\"");
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
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                await process.WaitForExitAsync();

                return process.ExitCode == 0;
            }
            catch
            {
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
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                process.Start();
                await process.WaitForExitAsync();

                return process.ExitCode == 0;
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

            // Check Tools folder relative to application first
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var toolsPaths = new[]
            {
                Path.Combine(appPath, "..", "..", "..", "..", "PlatypusUtils", "Tools", "exiftool.exe"),
                Path.Combine(appPath, "..", "..", "..", "..", "LocalArchive", "PlatypusUtils", "Tools", "exiftool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "exiftool.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "exiftool-13.43_64", "exiftool.exe")
            };

            foreach (var path in toolsPaths)
            {
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }

            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exiftool", "exiftool.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "exiftool.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool-13.43_64", "exiftool.exe"),
                @"C:\Program Files\PlatypusTools\Tools\exiftool.exe",
                @"C:\Program Files\PlatypusTools\Tools\exiftool-13.43_64\exiftool.exe",
                @"C:\Program Files\ExifTool\exiftool.exe",
                @"C:\exiftool\exiftool.exe",
                @"C:\Program Files\exiftool\exiftool.exe",
                "exiftool"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
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