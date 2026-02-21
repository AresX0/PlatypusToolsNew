using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for checking and downloading application updates from GitHub releases.
    /// </summary>
    public class UpdateService
    {
        private static UpdateService? _instance;
        public static UpdateService Instance => _instance ??= new UpdateService();

        private readonly HttpClient _apiClient;
        private readonly HttpClient _downloadClient;
        private readonly string _owner = "AresX0";
        private readonly string _repo = "PlatypusToolsNew";
        private readonly string _currentVersion;
        private readonly string _downloadPath;

        public UpdateService()
        {
            // Use Api client (15s timeout) for version checks, Download client (30min timeout) for MSI downloads
            _apiClient = HttpClientFactory.Api;
            _downloadClient = HttpClientFactory.Download;
            
            // Get current version from assembly
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            _currentVersion = assembly.GetName().Version?.ToString() ?? "0.0.0.0";
            
            _downloadPath = Path.Combine(Path.GetTempPath(), "PlatypusToolsUpdate");
        }

        /// <summary>
        /// Gets the current application version.
        /// </summary>
        public string CurrentVersion => _currentVersion;

        /// <summary>
        /// Event raised when an update is available.
        /// </summary>
        public event EventHandler<UpdateInfo>? UpdateAvailable;

        /// <summary>
        /// Event raised when update download progress changes.
        /// </summary>
        public event EventHandler<double>? DownloadProgress;

        /// <summary>
        /// Checks for updates from GitHub releases.
        /// </summary>
        /// <returns>Update info if available, null otherwise.</returns>
        public async Task<UpdateInfo?> CheckForUpdatesAsync(CancellationToken ct = default)
        {
            try
            {
                var url = $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest";
                var response = await _apiClient.GetStringAsync(url, ct);
                
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                var tagName = root.GetProperty("tag_name").GetString() ?? "";
                var version = tagName.TrimStart('v', 'V');
                
                // Compare versions
                if (!IsNewerVersion(version, _currentVersion))
                {
                    return null;
                }

                var updateInfo = new UpdateInfo
                {
                    Version = version,
                    TagName = tagName,
                    Name = root.GetProperty("name").GetString() ?? tagName,
                    Body = root.GetProperty("body").GetString() ?? "",
                    PublishedAt = root.GetProperty("published_at").GetDateTime(),
                    HtmlUrl = root.GetProperty("html_url").GetString() ?? ""
                };

                // Find installer asset
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            updateInfo.DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            updateInfo.FileName = name;
                            updateInfo.FileSize = asset.GetProperty("size").GetInt64();
                            break;
                        }
                    }
                }

                UpdateAvailable?.Invoke(this, updateInfo);
                return updateInfo;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error checking for updates: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Downloads the update file.
        /// </summary>
        public async Task<string?> DownloadUpdateAsync(UpdateInfo info, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                return null;
            }

            try
            {
                // Ensure download directory exists
                if (!Directory.Exists(_downloadPath))
                {
                    Directory.CreateDirectory(_downloadPath);
                }

                var filePath = Path.Combine(_downloadPath, info.FileName);

                using var response = await _downloadClient.GetAsync(info.DownloadUrl, 
                    HttpCompletionOption.ResponseHeadersRead, ct);
                
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? info.FileSize;
                var bytesRead = 0L;

                await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[81920]; // 80KB buffer
                int read;

                while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, read, ct);
                    bytesRead += read;

                    if (totalBytes > 0)
                    {
                        var progress = (bytesRead * 100.0) / totalBytes;
                        DownloadProgress?.Invoke(this, progress);
                    }
                }

                SimpleLogger.Info($"Update downloaded: {filePath}");
                return filePath;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error downloading update: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Launches the installer and optionally closes the application.
        /// </summary>
        public void LaunchInstaller(string installerPath, bool closeApp = true)
        {
            try
            {
                // Use msiexec.exe explicitly to avoid "No application is associated" errors
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/i \"{installerPath}\"",
                    UseShellExecute = true,
                    Verb = "runas" // Request admin elevation for MSI installation
                };

                System.Diagnostics.Process.Start(psi);

                if (closeApp)
                {
                    System.Windows.Application.Current?.Shutdown();
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error launching installer: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Opens the release page in the default browser.
        /// </summary>
        public void OpenReleasePage(UpdateInfo info)
        {
            if (!string.IsNullOrEmpty(info.HtmlUrl))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = info.HtmlUrl,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    SimpleLogger.Error($"Error opening release page: {ex.Message}");
                }
            }
        }

        private bool IsNewerVersion(string newVersion, string currentVersion)
        {
            try
            {
                // Remove any prefix (v, V) and suffix (-beta, -rc, etc.)
                var cleanNew = CleanVersionString(newVersion);
                var cleanCurrent = CleanVersionString(currentVersion);

                if (Version.TryParse(cleanNew, out var vNew) &&
                    Version.TryParse(cleanCurrent, out var vCurrent))
                {
                    return vNew > vCurrent;
                }

                // Fallback to string comparison
                return string.Compare(newVersion, currentVersion, StringComparison.OrdinalIgnoreCase) > 0;
            }
            catch
            {
                return false;
            }
        }

        private string CleanVersionString(string version)
        {
            version = version.TrimStart('v', 'V');
            
            // Remove suffix like -beta, -rc, etc.
            var dashIndex = version.IndexOf('-');
            if (dashIndex > 0)
            {
                version = version.Substring(0, dashIndex);
            }

            return version;
        }
    }

    /// <summary>
    /// Contains information about an available update.
    /// </summary>
    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string TagName { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Body { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public string HtmlUrl { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }

        public string FileSizeDisplay
        {
            get
            {
                if (FileSize >= 1073741824)
                    return $"{FileSize / 1073741824.0:F2} GB";
                if (FileSize >= 1048576)
                    return $"{FileSize / 1048576.0:F2} MB";
                if (FileSize >= 1024)
                    return $"{FileSize / 1024.0:F2} KB";
                return $"{FileSize} B";
            }
        }
    }
}
