using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PlatypusTools.Core.Models.Archive
{
    /// <summary>
    /// Supported archive formats.
    /// </summary>
    public enum ArchiveFormat
    {
        Zip,
        SevenZip,
        Rar,
        Tar,
        GZip,
        TarGz,
        TarBz2
    }
    
    /// <summary>
    /// Compression level for archive creation.
    /// </summary>
    public enum CompressionLevel
    {
        None,
        Fastest,
        Fast,
        Normal,
        Maximum,
        Ultra
    }
    
    /// <summary>
    /// Represents an entry within an archive.
    /// </summary>
    public class ArchiveEntry : BindableModel
    {
        private bool _isSelected;
        private string _name = string.Empty;
        private string _path = string.Empty;
        private long _size;
        private long _compressedSize;
        private DateTime _lastModified;
        private bool _isDirectory;
        private bool _isEncrypted;
        
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Full path within the archive.
        /// </summary>
        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// Uncompressed size in bytes.
        /// </summary>
        public long Size
        {
            get => _size;
            set { _size = value; OnPropertyChanged(); OnPropertyChanged(nameof(SizeDisplay)); }
        }
        
        public string SizeDisplay => FormatFileSize(Size);
        
        /// <summary>
        /// Compressed size in bytes.
        /// </summary>
        public long CompressedSize
        {
            get => _compressedSize;
            set { _compressedSize = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompressedSizeDisplay)); OnPropertyChanged(nameof(CompressionRatio)); }
        }
        
        public string CompressedSizeDisplay => FormatFileSize(CompressedSize);
        
        public string CompressionRatio => Size > 0 ? $"{(1.0 - (double)CompressedSize / Size) * 100:F1}%" : "-";
        
        public DateTime LastModified
        {
            get => _lastModified;
            set { _lastModified = value; OnPropertyChanged(); }
        }
        
        public bool IsDirectory
        {
            get => _isDirectory;
            set { _isDirectory = value; OnPropertyChanged(); }
        }
        
        public bool IsEncrypted
        {
            get => _isEncrypted;
            set { _isEncrypted = value; OnPropertyChanged(); }
        }
        
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }
        
        /// <summary>
        /// File extension for icon display.
        /// </summary>
        public string Extension => IsDirectory ? "folder" : System.IO.Path.GetExtension(Name)?.TrimStart('.') ?? "";
        
        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "-";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }
    }
    
    /// <summary>
    /// Archive operation progress information.
    /// </summary>
    public class ArchiveProgress
    {
        public string CurrentFile { get; set; } = string.Empty;
        public int CurrentFileIndex { get; set; }
        public int TotalFiles { get; set; }
        public long BytesProcessed { get; set; }
        public long TotalBytes { get; set; }
        public double PercentComplete => TotalBytes > 0 ? (double)BytesProcessed / TotalBytes * 100 : 0;
    }
    
    /// <summary>
    /// Settings for archive creation.
    /// </summary>
    public class ArchiveCreateOptions
    {
        public ArchiveFormat Format { get; set; } = ArchiveFormat.Zip;
        public CompressionLevel Level { get; set; } = CompressionLevel.Normal;
        public string? Password { get; set; }
        public bool IncludeRootFolder { get; set; } = true;
        public bool PreserveFolderStructure { get; set; } = true;
        
        /// <summary>
        /// Split archive into parts of this size (0 = no split).
        /// </summary>
        public long SplitSizeBytes { get; set; } = 0;
    }
    
    /// <summary>
    /// Settings for archive extraction.
    /// </summary>
    public class ArchiveExtractOptions
    {
        public string OutputDirectory { get; set; } = string.Empty;
        public string? Password { get; set; }
        public bool OverwriteExisting { get; set; } = false;
        public bool PreserveFolderStructure { get; set; } = true;
        
        /// <summary>
        /// If set, only extract entries matching these paths.
        /// </summary>
        public List<string>? SelectedEntries { get; set; }
    }
}
