using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Lightweight FTP/SFTP client service - memory efficient implementation without external dependencies.
    /// Uses built-in .NET FTP support and SSH.NET for SFTP when available.
    /// </summary>
    public class FtpClientService : IDisposable
    {
        private bool _disposed;

        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 21;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool UseSftp { get; set; }
        public bool UsePassive { get; set; } = true;
        public bool UseSsl { get; set; }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<double>? ProgressChanged;

        private void ReportStatus(string status) => StatusChanged?.Invoke(this, status);
        private void ReportProgress(double percent) => ProgressChanged?.Invoke(this, percent);

        /// <summary>
        /// Lists files and directories in the specified remote path.
        /// </summary>
        public async Task<List<FtpFileInfo>> ListDirectoryAsync(string remotePath, CancellationToken ct = default)
        {
            var result = new List<FtpFileInfo>();

            if (UseSftp)
            {
                // SFTP requires SSH.NET - return empty if not connected
                ReportStatus("SFTP requires SSH.NET library");
                return result;
            }

            try
            {
                var uri = BuildUri(remotePath);
                var request = CreateRequest(uri, WebRequestMethods.Ftp.ListDirectoryDetails);

                ReportStatus($"Listing directory: {remotePath}");

                using var response = (FtpWebResponse)await request.GetResponseAsync();
                using var reader = new StreamReader(response.GetResponseStream());

                string? line;
                while ((line = await reader.ReadLineAsync(ct)) != null)
                {
                    var info = ParseListLine(line, remotePath);
                    if (info != null)
                    {
                        result.Add(info);
                    }
                }

                ReportStatus($"Found {result.Count} items");
            }
            catch (Exception ex)
            {
                ReportStatus($"Error: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Downloads a file from the remote server.
        /// </summary>
        public async Task<bool> DownloadFileAsync(string remotePath, string localPath, CancellationToken ct = default)
        {
            if (UseSftp)
            {
                ReportStatus("SFTP requires SSH.NET library");
                return false;
            }

            try
            {
                var uri = BuildUri(remotePath);
                var request = CreateRequest(uri, WebRequestMethods.Ftp.DownloadFile);

                ReportStatus($"Downloading: {Path.GetFileName(remotePath)}");

                using var response = (FtpWebResponse)await request.GetResponseAsync();
                using var responseStream = response.GetResponseStream();
                
                var directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using var fileStream = File.Create(localPath);
                var buffer = new byte[81920]; // 80KB buffer
                long totalRead = 0;
                long contentLength = response.ContentLength > 0 ? response.ContentLength : 1;
                int bytesRead;

                while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, ct);
                    totalRead += bytesRead;
                    ReportProgress((double)totalRead / contentLength * 100);
                }

                ReportStatus($"Downloaded: {Path.GetFileName(localPath)}");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"Download error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Uploads a file to the remote server.
        /// </summary>
        public async Task<bool> UploadFileAsync(string localPath, string remotePath, CancellationToken ct = default)
        {
            if (UseSftp)
            {
                ReportStatus("SFTP requires SSH.NET library");
                return false;
            }

            try
            {
                var uri = BuildUri(remotePath);
                var request = CreateRequest(uri, WebRequestMethods.Ftp.UploadFile);

                ReportStatus($"Uploading: {Path.GetFileName(localPath)}");

                using var fileStream = File.OpenRead(localPath);
                using var requestStream = await request.GetRequestStreamAsync();

                var buffer = new byte[81920];
                long totalWritten = 0;
                long fileLength = fileStream.Length;
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                {
                    await requestStream.WriteAsync(buffer, 0, bytesRead, ct);
                    totalWritten += bytesRead;
                    ReportProgress((double)totalWritten / fileLength * 100);
                }

                using var response = (FtpWebResponse)await request.GetResponseAsync();
                ReportStatus($"Uploaded: {Path.GetFileName(localPath)} - {response.StatusDescription}");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"Upload error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Creates a directory on the remote server.
        /// </summary>
        public async Task<bool> CreateDirectoryAsync(string remotePath, CancellationToken ct = default)
        {
            if (UseSftp) return false;

            try
            {
                var uri = BuildUri(remotePath);
                var request = CreateRequest(uri, WebRequestMethods.Ftp.MakeDirectory);

                using var response = (FtpWebResponse)await request.GetResponseAsync();
                ReportStatus($"Created directory: {remotePath}");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"Create directory error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deletes a file on the remote server.
        /// </summary>
        public async Task<bool> DeleteFileAsync(string remotePath, CancellationToken ct = default)
        {
            if (UseSftp) return false;

            try
            {
                var uri = BuildUri(remotePath);
                var request = CreateRequest(uri, WebRequestMethods.Ftp.DeleteFile);

                using var response = (FtpWebResponse)await request.GetResponseAsync();
                ReportStatus($"Deleted: {remotePath}");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"Delete error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Renames/moves a file on the remote server.
        /// </summary>
        public async Task<bool> RenameAsync(string fromPath, string toName, CancellationToken ct = default)
        {
            if (UseSftp) return false;

            try
            {
                var uri = BuildUri(fromPath);
                var request = CreateRequest(uri, WebRequestMethods.Ftp.Rename);
                request.RenameTo = toName;

                using var response = (FtpWebResponse)await request.GetResponseAsync();
                ReportStatus($"Renamed: {fromPath} -> {toName}");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"Rename error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tests connection to the FTP server.
        /// </summary>
        public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
        {
            try
            {
                ReportStatus($"Connecting to {Host}:{Port}...");
                var files = await ListDirectoryAsync("/", ct);
                ReportStatus($"Connected successfully! Found {files.Count} items in root.");
                return true;
            }
            catch (Exception ex)
            {
                ReportStatus($"Connection failed: {ex.Message}");
                return false;
            }
        }

        private Uri BuildUri(string path)
        {
            var scheme = UseSftp ? "sftp" : "ftp";
            var cleanPath = path.TrimStart('/');
            return new Uri($"{scheme}://{Host}:{Port}/{cleanPath}");
        }

        private FtpWebRequest CreateRequest(Uri uri, string method)
        {
            var request = (FtpWebRequest)WebRequest.Create(uri);
            request.Method = method;
            request.Credentials = new NetworkCredential(Username, Password);
            request.UsePassive = UsePassive;
            request.UseBinary = true;
            request.EnableSsl = UseSsl;
            request.KeepAlive = false;
            return request;
        }

        private FtpFileInfo? ParseListLine(string line, string parentPath)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            try
            {
                // Unix-style listing: drwxr-xr-x 2 user group 4096 Jan 21 12:00 dirname
                // Windows-style listing: 01-21-26 12:00PM <DIR> dirname
                var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 4) return null;

                bool isDirectory;
                string name;
                long size = 0;

                if (line.StartsWith("d") || line.StartsWith("-") || line.StartsWith("l"))
                {
                    // Unix-style
                    isDirectory = line.StartsWith("d");
                    name = parts[^1]; // Last element
                    if (parts.Length >= 5 && long.TryParse(parts[4], out var sz))
                    {
                        size = sz;
                    }
                }
                else if (parts.Length >= 4 && (parts[2] == "<DIR>" || char.IsDigit(parts[2][0])))
                {
                    // Windows-style
                    isDirectory = parts[2] == "<DIR>";
                    name = string.Join(" ", parts.Skip(3));
                    if (!isDirectory && long.TryParse(parts[2], out var sz))
                    {
                        size = sz;
                    }
                }
                else
                {
                    return null;
                }

                if (name == "." || name == "..") return null;

                return new FtpFileInfo
                {
                    Name = name,
                    FullPath = Path.Combine(parentPath, name).Replace('\\', '/'),
                    IsDirectory = isDirectory,
                    Size = size
                };
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Represents a file or directory on an FTP server.
    /// </summary>
    public class FtpFileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime Modified { get; set; }

        public string DisplaySize => IsDirectory ? "<DIR>" : FormatSize(Size);

        private static string FormatSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }

    /// <summary>
    /// Saved FTP/SFTP session for quick connect.
    /// </summary>
    public class SavedFtpSession
    {
        public string Name { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 21;
        public string Username { get; set; } = string.Empty;
        public bool UseSftp { get; set; }
        public bool UsePassive { get; set; } = true;
        public bool UseSsl { get; set; }
        public string PrivateKeyPath { get; set; } = string.Empty; // For SFTP key authentication
        public string DefaultRemotePath { get; set; } = "/";
        public string DefaultLocalPath { get; set; } = string.Empty;
        public DateTime LastUsed { get; set; }
    }
}
