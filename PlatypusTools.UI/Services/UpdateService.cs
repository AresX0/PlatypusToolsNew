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

        // Valid MSI magic bytes: D0 CF 11 E0 (OLE Compound File)
        private static readonly byte[] MsiMagicBytes = { 0xD0, 0xCF, 0x11, 0xE0 };
        // Valid EXE magic bytes: MZ
        private static readonly byte[] ExeMagicBytes = { 0x4D, 0x5A };

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

                // Find installer asset - prefer MSI over standalone EXE
                if (root.TryGetProperty("assets", out var assets))
                {
                    // First pass: look for MSI installer
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                        {
                            updateInfo.DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            updateInfo.FileName = name;
                            updateInfo.FileSize = asset.GetProperty("size").GetInt64();
                            break;
                        }
                    }

                    // Fallback: if no MSI found, look for EXE
                    if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
                    {
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.GetProperty("name").GetString() ?? "";
                            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            {
                                updateInfo.DownloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                                updateInfo.FileName = name;
                                updateInfo.FileSize = asset.GetProperty("size").GetInt64();
                                break;
                            }
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
        /// Downloads the update file with validation.
        /// Uses a temp file strategy: downloads to .downloading file, validates, then renames.
        /// </summary>
        public async Task<string?> DownloadUpdateAsync(UpdateInfo info, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(info.DownloadUrl))
            {
                SimpleLogger.Error("Download URL is empty — cannot download update.");
                return null;
            }

            try
            {
                // Ensure download directory exists and clean up stale .downloading files
                if (!Directory.Exists(_downloadPath))
                {
                    Directory.CreateDirectory(_downloadPath);
                }
                CleanupStaleDownloads();

                var finalPath = Path.Combine(_downloadPath, info.FileName);
                var tempPath = finalPath + ".downloading";

                // Delete any previous partial download
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }

                // Delete any previous completed download of the same file
                if (File.Exists(finalPath))
                {
                    File.Delete(finalPath);
                }

                SimpleLogger.Info($"Downloading update from: {info.DownloadUrl}");
                SimpleLogger.Info($"Expected file size: {info.FileSize:N0} bytes");

                using var response = await _downloadClient.GetAsync(info.DownloadUrl, 
                    HttpCompletionOption.ResponseHeadersRead, ct);
                
                response.EnsureSuccessStatusCode();

                // Validate content type - GitHub should return application/octet-stream
                var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    SimpleLogger.Error($"Server returned HTML instead of binary data (Content-Type: {contentType}). Download URL may be invalid.");
                    return null;
                }

                var totalBytes = response.Content.Headers.ContentLength ?? info.FileSize;
                var bytesRead = 0L;

                // Download to temp file first
                await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                await using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 
                    bufferSize: 262144)) // 256KB file buffer for better write performance
                {
                    var buffer = new byte[262144]; // 256KB read buffer (larger = fewer syscalls for 300MB+ files)
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

                    // Flush to ensure all bytes are written to disk
                    await fileStream.FlushAsync(ct);
                }

                SimpleLogger.Info($"Download complete: {bytesRead:N0} bytes written to temp file");

                // === POST-DOWNLOAD VALIDATION ===

                // 1. Verify file exists and has content
                if (!File.Exists(tempPath))
                {
                    SimpleLogger.Error("Downloaded temp file does not exist after download.");
                    return null;
                }

                var actualSize = new FileInfo(tempPath).Length;
                SimpleLogger.Info($"Temp file size on disk: {actualSize:N0} bytes");

                // 2. Verify file size matches expected (with 1% tolerance for content-encoding differences)
                if (info.FileSize > 0)
                {
                    var sizeDifference = Math.Abs(actualSize - info.FileSize);
                    var toleranceBytes = (long)(info.FileSize * 0.01); // 1% tolerance
                    if (sizeDifference > toleranceBytes)
                    {
                        SimpleLogger.Error($"Downloaded file size mismatch! Expected: {info.FileSize:N0}, Got: {actualSize:N0}, Difference: {sizeDifference:N0} bytes");
                        TryDeleteFile(tempPath);
                        return null;
                    }
                }

                // 3. Verify file has zero size check
                if (actualSize == 0)
                {
                    SimpleLogger.Error("Downloaded file is empty (0 bytes).");
                    TryDeleteFile(tempPath);
                    return null;
                }

                // 4. Validate file magic bytes (MSI or EXE header)
                var validationError = ValidateFileHeader(tempPath, info.FileName);
                if (validationError != null)
                {
                    SimpleLogger.Error(validationError);
                    TryDeleteFile(tempPath);
                    return null;
                }

                // === VALIDATION PASSED — Rename temp file to final name ===
                File.Move(tempPath, finalPath, overwrite: true);
                SimpleLogger.Info($"Update validated and saved: {finalPath} ({actualSize:N0} bytes)");
                return finalPath;
            }
            catch (OperationCanceledException)
            {
                SimpleLogger.Info("Update download was cancelled by user.");
                return null;
            }
            catch (HttpRequestException ex)
            {
                SimpleLogger.Error($"Network error downloading update: {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                SimpleLogger.Error($"File I/O error downloading update: {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error downloading update: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Validates the file header bytes match the expected file type.
        /// Returns null if valid, or an error message string if invalid.
        /// </summary>
        private static string? ValidateFileHeader(string filePath, string fileName)
        {
            try
            {
                var headerBytes = new byte[4];
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bytesRead = fs.Read(headerBytes, 0, 4);
                
                if (bytesRead < 4)
                {
                    return $"File too small to validate header (only {bytesRead} bytes read).";
                }

                if (fileName.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    // MSI files must start with OLE Compound File magic: D0 CF 11 E0
                    if (headerBytes[0] != MsiMagicBytes[0] || headerBytes[1] != MsiMagicBytes[1] ||
                        headerBytes[2] != MsiMagicBytes[2] || headerBytes[3] != MsiMagicBytes[3])
                    {
                        var actualHeader = BitConverter.ToString(headerBytes);
                        return $"Downloaded MSI has invalid header: {actualHeader} (expected D0-CF-11-E0). File may be corrupt or an HTML error page.";
                    }
                }
                else if (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    // EXE files must start with MZ
                    if (headerBytes[0] != ExeMagicBytes[0] || headerBytes[1] != ExeMagicBytes[1])
                    {
                        var actualHeader = BitConverter.ToString(headerBytes);
                        return $"Downloaded EXE has invalid header: {actualHeader} (expected 4D-5A / 'MZ'). File may be corrupt or an HTML error page.";
                    }
                }

                return null; // Valid
            }
            catch (Exception ex)
            {
                return $"Error validating file header: {ex.Message}";
            }
        }

        /// <summary>
        /// Removes stale .downloading temp files from the update directory.
        /// </summary>
        private void CleanupStaleDownloads()
        {
            try
            {
                foreach (var file in Directory.GetFiles(_downloadPath, "*.downloading"))
                {
                    TryDeleteFile(file);
                }
            }
            catch
            {
                // Non-critical — ignore cleanup errors
            }
        }

        /// <summary>
        /// Safely attempts to delete a file, ignoring errors.
        /// </summary>
        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore — temp file cleanup is best-effort
            }
        }

        /// <summary>
        /// Launches the installer and optionally closes the application.
        /// Validates the installer file before launching.
        /// </summary>
        public void LaunchInstaller(string installerPath, bool closeApp = true)
        {
            try
            {
                // Pre-install validation: ensure the file exists
                if (!File.Exists(installerPath))
                {
                    throw new FileNotFoundException($"Installer file not found: {installerPath}");
                }

                // Pre-install validation: ensure the file is not empty
                var fileSize = new FileInfo(installerPath).Length;
                if (fileSize == 0)
                {
                    throw new InvalidOperationException("Installer file is empty (0 bytes). Please re-download the update.");
                }

                // Pre-install validation: check file header
                var headerError = ValidateFileHeader(installerPath, Path.GetFileName(installerPath));
                if (headerError != null)
                {
                    SimpleLogger.Error($"Pre-install validation failed: {headerError}");
                    throw new InvalidOperationException($"Installer file appears corrupt: {headerError}\nPlease re-download the update.");
                }

                SimpleLogger.Info($"Launching installer: {installerPath} ({fileSize:N0} bytes)");

                System.Diagnostics.ProcessStartInfo psi;

                if (installerPath.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                {
                    // Copy MSI to a shared location accessible by the elevated Windows Installer service.
                    // Error 2503 occurs when msiexec (running as SYSTEM) can't access files in the user's %TEMP%.
                    var sharedDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                        "PlatypusTools", "Updates");
                    Directory.CreateDirectory(sharedDir);
                    var sharedMsiPath = Path.Combine(sharedDir, Path.GetFileName(installerPath));
                    
                    try
                    {
                        File.Copy(installerPath, sharedMsiPath, overwrite: true);
                        SimpleLogger.Info($"Copied MSI to shared location: {sharedMsiPath}");
                    }
                    catch (Exception copyEx)
                    {
                        SimpleLogger.Error($"Failed to copy MSI to shared location, using original path: {copyEx.Message}");
                        sharedMsiPath = installerPath; // Fallback to original path
                    }

                    // Launch the MSI via msiexec.exe with elevation.
                    // The MSI has been copied to a shared ProgramData location to avoid
                    // error 2503 where msiexec (SYSTEM) can't access user %TEMP%.
                    psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "msiexec.exe",
                        Arguments = $"/i \"{sharedMsiPath}\"",
                        UseShellExecute = true,
                        Verb = "runas" // Request admin elevation via UAC
                    };
                }
                else
                {
                    // Launch EXE directly
                    psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = installerPath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                }

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
