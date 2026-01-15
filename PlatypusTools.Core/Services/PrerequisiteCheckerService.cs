using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for checking availability of external dependencies and tools required by PlatypusTools
    /// </summary>
    public interface IPrerequisiteCheckerService
    {
        /// <summary>
        /// Check if a specific tool is available in the system
        /// </summary>
        Task<bool> IsToolAvailableAsync(string toolName);

        /// <summary>
        /// Get all missing prerequisites
        /// </summary>
        Task<List<PrerequisiteInfo>> GetMissingPrerequisitesAsync();

        /// <summary>
        /// Get details about a specific prerequisite
        /// </summary>
        PrerequisiteInfo GetPrerequisiteInfo(string toolName);

        /// <summary>
        /// Get version of an installed tool
        /// </summary>
        Task<string> GetToolVersionAsync(string toolName);
    }

    /// <summary>
    /// Information about a system prerequisite
    /// </summary>
    public class PrerequisiteInfo
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Description { get; set; }
        public string ExecutableName { get; set; }
        public string DownloadUrl { get; set; }
        public string DownloadUrlWindows { get; set; }
        public string DownloadUrlMac { get; set; }
        public string DownloadUrlLinux { get; set; }
        public string Instructions { get; set; }
        public bool IsRequired { get; set; }
        public string VersionCheckArgument { get; set; }

        public PrerequisiteInfo()
        {
            VersionCheckArgument = "--version";
        }
    }

    public class PrerequisiteCheckerService : IPrerequisiteCheckerService
    {
        private static readonly Dictionary<string, PrerequisiteInfo> Prerequisites = new()
        {
            ["ffmpeg"] = new PrerequisiteInfo
            {
                Name = "ffmpeg",
                DisplayName = "FFmpeg",
                Description = "Required for video conversion, audio processing, and format detection",
                ExecutableName = "ffmpeg.exe",
                DownloadUrl = "https://ffmpeg.org/download.html",
                DownloadUrlWindows = "https://www.gyan.dev/ffmpeg/builds/",
                DownloadUrlMac = "https://brew.sh/ (brew install ffmpeg)",
                DownloadUrlLinux = "https://ffmpeg.org/download.html",
                Instructions = "Download FFmpeg and add it to your system PATH",
                IsRequired = true,
                VersionCheckArgument = "-version"
            },
            ["exiftool"] = new PrerequisiteInfo
            {
                Name = "exiftool",
                DisplayName = "ExifTool",
                Description = "Required for reading and writing metadata tags in media files",
                ExecutableName = "exiftool.exe",
                DownloadUrl = "https://exiftool.org/",
                DownloadUrlWindows = "https://exiftool.org/index.html#download",
                DownloadUrlMac = "https://brew.sh/ (brew install exiftool)",
                DownloadUrlLinux = "Install via package manager (apt install libimage-exiftool-perl)",
                Instructions = "Download ExifTool and add it to your system PATH",
                IsRequired = false,
                VersionCheckArgument = "-version"
            },
            ["fpcalc"] = new PrerequisiteInfo
            {
                Name = "fpcalc",
                DisplayName = "fpcalc (AcoustID)",
                Description = "Required for acoustic fingerprinting and music recognition",
                ExecutableName = "fpcalc.exe",
                DownloadUrl = "https://acoustid.org/fingerprinter",
                DownloadUrlWindows = "https://acoustid.org/fingerprinter",
                DownloadUrlMac = "https://brew.sh/ (brew install chromaprint)",
                DownloadUrlLinux = "Install via package manager (apt install libchromaprint-tools)",
                Instructions = "Download fpcalc and add it to your system PATH",
                IsRequired = false,
                VersionCheckArgument = "-version"
            }
        };

        public async Task<bool> IsToolAvailableAsync(string toolName)
        {
            if (!Prerequisites.ContainsKey(toolName.ToLower()))
                return false;

            var info = Prerequisites[toolName.ToLower()];
            return await CheckToolExistsAsync(info.ExecutableName);
        }

        public async Task<List<PrerequisiteInfo>> GetMissingPrerequisitesAsync()
        {
            var missing = new List<PrerequisiteInfo>();

            foreach (var (key, prereq) in Prerequisites)
            {
                bool isAvailable = await CheckToolExistsAsync(prereq.ExecutableName);
                if (!isAvailable)
                {
                    missing.Add(prereq);
                }
            }

            return missing;
        }

        public PrerequisiteInfo GetPrerequisiteInfo(string toolName)
        {
            return Prerequisites.TryGetValue(toolName.ToLower(), out var info) ? info : null;
        }

        public async Task<string> GetToolVersionAsync(string toolName)
        {
            if (!Prerequisites.ContainsKey(toolName.ToLower()))
                return null;

            var info = Prerequisites[toolName.ToLower()];
            return await GetToolVersionAsync(info.ExecutableName, info.VersionCheckArgument);
        }

        private static async Task<bool> CheckToolExistsAsync(string executableName)
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Check in PATH
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c where {executableName}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(processInfo))
                    {
                        process?.WaitForExit();
                        return process?.ExitCode == 0;
                    }
                }
                catch
                {
                    return false;
                }
            });
        }

        private static async Task<string> GetToolVersionAsync(string executableName, string versionArg)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var processInfo = new ProcessStartInfo
                    {
                        FileName = executableName,
                        Arguments = versionArg,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using (var process = Process.Start(processInfo))
                    {
                        if (process == null) return null;
                        process.WaitForExit(5000);

                        string output = process.StandardOutput.ReadToEnd();
                        string error = process.StandardError.ReadToEnd();
                        return string.IsNullOrWhiteSpace(output) ? error : output;
                    }
                }
                catch
                {
                    return null;
                }
            });
        }
    }
}
