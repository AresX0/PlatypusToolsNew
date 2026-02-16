using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public class DependencyCheckerService
    {
        public async Task<DependencyCheckResult> CheckAllDependenciesAsync()
        {
            var result = new DependencyCheckResult();

            result.FFmpegInstalled = await CheckFFmpegAsync();
            result.ExifToolInstalled = await CheckExifToolAsync();
            result.YtDlpInstalled = await CheckYtDlpAsync();
            result.WebView2Installed = CheckWebView2();

            result.AllDependenciesMet = result.FFmpegInstalled && result.ExifToolInstalled && result.YtDlpInstalled && result.WebView2Installed;

            return result;
        }

        private async Task<bool> CheckFFmpegAsync()
        {
            try
            {
                // Check common installation paths
                var paths = new[]
                {
                    "ffmpeg",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "ffmpeg.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "ffmpeg", "bin", "ffmpeg.exe")
                };

                foreach (var path in paths)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "-version",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(psi);
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                            if (process.ExitCode == 0)
                            {
                                return true;
                            }
                        }
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> CheckExifToolAsync()
        {
            try
            {
                var paths = new[]
                {
                    "exiftool",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool", "exiftool.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool_files", "exiftool.exe"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool-13.45_64", "exiftool.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "exiftool.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "exiftool", "exiftool.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "exiftool-13.45_64", "exiftool.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "exiftool", "exiftool.exe")
                };

                foreach (var path in paths)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "-ver",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(psi);
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                            if (process.ExitCode == 0)
                            {
                                return true;
                            }
                        }
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        private bool CheckWebView2()
        {
            try
            {
                // Check if WebView2 Runtime is installed
                var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                if (regKey != null)
                {
                    regKey.Close();
                    return true;
                }

                regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}");
                if (regKey != null)
                {
                    regKey.Close();
                    return true;
                }

                return false;
            }
            catch
            {
                // Assume it's installed if we can't check
                return true;
            }
        }

        private async Task<bool> CheckYtDlpAsync()
        {
            try
            {
                var paths = new[]
                {
                    "yt-dlp",
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "yt-dlp.exe")
                };

                foreach (var path in paths)
                {
                    try
                    {
                        var psi = new ProcessStartInfo
                        {
                            FileName = path,
                            Arguments = "--version",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };

                        using var process = Process.Start(psi);
                        if (process != null)
                        {
                            await process.WaitForExitAsync();
                            if (process.ExitCode == 0)
                            {
                                return true;
                            }
                        }
                    }
                    catch { }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        public string GetFFmpegDownloadUrl() => "https://ffmpeg.org/download.html";
        public string GetExifToolDownloadUrl() => "https://exiftool.org/";
        public string GetYtDlpDownloadUrl() => "https://github.com/yt-dlp/yt-dlp/releases";
        public string GetWebView2DownloadUrl() => "https://developer.microsoft.com/en-us/microsoft-edge/webview2/";
    }

    public class DependencyCheckResult
    {
        public bool FFmpegInstalled { get; set; }
        public bool ExifToolInstalled { get; set; }
        public bool YtDlpInstalled { get; set; }
        public bool WebView2Installed { get; set; }
        public bool AllDependenciesMet { get; set; }

        public string GetMissingDependenciesMessage()
        {
            var missing = new System.Collections.Generic.List<string>();
            if (!FFmpegInstalled) missing.Add("FFmpeg (for video operations)");
            if (!ExifToolInstalled) missing.Add("ExifTool (for metadata editing)");
            if (!YtDlpInstalled) missing.Add("yt-dlp (for streaming audio)");
            if (!WebView2Installed) missing.Add("WebView2 Runtime (for help system)");

            if (missing.Count == 0)
                return "All dependencies are installed.";

            return $"Missing dependencies:\n• {string.Join("\n• ", missing)}";
        }
    }
}
