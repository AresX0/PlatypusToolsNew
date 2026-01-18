using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for generating file previews including images, text, and metadata.
    /// </summary>
    public class FilePreviewService
    {
        private static FilePreviewService? _instance;
        public static FilePreviewService Instance => _instance ??= new FilePreviewService();

        private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".webp", ".ico" };
        private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v" };
        private static readonly string[] AudioExtensions = { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a" };
        private static readonly string[] TextExtensions = { ".txt", ".md", ".json", ".xml", ".csv", ".log", ".ini", ".cfg", ".yaml", ".yml" };
        private static readonly string[] CodeExtensions = { ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".html", ".css", ".sql", ".ps1", ".sh" };

        /// <summary>
        /// Gets the preview type for a file.
        /// </summary>
        public FilePreviewType GetPreviewType(string filePath)
        {
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            
            if (Array.Exists(ImageExtensions, e => e == ext)) return FilePreviewType.Image;
            if (Array.Exists(VideoExtensions, e => e == ext)) return FilePreviewType.Video;
            if (Array.Exists(AudioExtensions, e => e == ext)) return FilePreviewType.Audio;
            if (Array.Exists(TextExtensions, e => e == ext)) return FilePreviewType.Text;
            if (Array.Exists(CodeExtensions, e => e == ext)) return FilePreviewType.Code;
            
            return FilePreviewType.Unknown;
        }

        /// <summary>
        /// Gets an image preview for a file.
        /// </summary>
        public async Task<BitmapSource?> GetImagePreviewAsync(string filePath, int maxWidth = 800, CancellationToken ct = default)
        {
            if (!File.Exists(filePath)) return null;
            
            var type = GetPreviewType(filePath);
            if (type != FilePreviewType.Image) return null;

            return await Task.Run(() =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();
                    
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(filePath);
                    bitmap.DecodePixelWidth = maxWidth;
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }, ct);
        }

        /// <summary>
        /// Gets a text preview for a file.
        /// </summary>
        public async Task<string?> GetTextPreviewAsync(string filePath, int maxLines = 100, CancellationToken ct = default)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                var lines = new StringBuilder();
                int lineCount = 0;
                
                using var reader = new StreamReader(filePath, Encoding.UTF8, true);
                while (!reader.EndOfStream && lineCount < maxLines)
                {
                    ct.ThrowIfCancellationRequested();
                    var line = await reader.ReadLineAsync(ct);
                    lines.AppendLine(line);
                    lineCount++;
                }
                
                if (!reader.EndOfStream)
                {
                    lines.AppendLine($"\n... ({maxLines} lines shown, more content exists)");
                }
                
                return lines.ToString();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets file metadata preview.
        /// </summary>
        public FileMetadataPreview GetMetadataPreview(string filePath)
        {
            var preview = new FileMetadataPreview { FilePath = filePath };
            
            if (!File.Exists(filePath)) return preview;

            try
            {
                var info = new FileInfo(filePath);
                preview.FileName = info.Name;
                preview.Extension = info.Extension;
                preview.Size = info.Length;
                preview.SizeFormatted = FormatFileSize(info.Length);
                preview.Created = info.CreationTime;
                preview.Modified = info.LastWriteTime;
                preview.Accessed = info.LastAccessTime;
                preview.IsReadOnly = info.IsReadOnly;
                preview.Attributes = info.Attributes.ToString();
                preview.Directory = info.DirectoryName;
                preview.PreviewType = GetPreviewType(filePath);
            }
            catch (Exception ex)
            {
                preview.Error = ex.Message;
            }

            return preview;
        }

        /// <summary>
        /// Gets a hex dump preview for binary files.
        /// </summary>
        public async Task<string?> GetHexPreviewAsync(string filePath, int maxBytes = 256, CancellationToken ct = default)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                var buffer = new byte[maxBytes];
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var bytesRead = await stream.ReadAsync(buffer, ct);
                
                var sb = new StringBuilder();
                for (int i = 0; i < bytesRead; i += 16)
                {
                    sb.Append($"{i:X8}  ");
                    
                    // Hex
                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < bytesRead)
                            sb.Append($"{buffer[i + j]:X2} ");
                        else
                            sb.Append("   ");
                        if (j == 7) sb.Append(' ');
                    }
                    
                    sb.Append(' ');
                    
                    // ASCII
                    for (int j = 0; j < 16 && i + j < bytesRead; j++)
                    {
                        var c = (char)buffer[i + j];
                        sb.Append(char.IsControl(c) ? '.' : c);
                    }
                    
                    sb.AppendLine();
                }
                
                return sb.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string FormatFileSize(long bytes)
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

    public class FileMetadataPreview
    {
        public string FilePath { get; set; } = "";
        public string? FileName { get; set; }
        public string? Extension { get; set; }
        public long Size { get; set; }
        public string? SizeFormatted { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public DateTime Accessed { get; set; }
        public bool IsReadOnly { get; set; }
        public string? Attributes { get; set; }
        public string? Directory { get; set; }
        public FilePreviewType PreviewType { get; set; }
        public string? Error { get; set; }
    }

    public enum FilePreviewType
    {
        Unknown,
        Image,
        Video,
        Audio,
        Text,
        Code
    }
}
