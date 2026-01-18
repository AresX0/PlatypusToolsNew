using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Archives.Zip;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Archives.Rar;
using SharpCompress.Archives.Tar;
using SharpCompress.Archives.GZip;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Readers;
using PlatypusTools.Core.Models.Archive;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for creating, browsing, and extracting archives.
    /// Supports ZIP, 7z, RAR, TAR, GZ, and other formats.
    /// </summary>
    public class ArchiveService
    {
        /// <summary>
        /// Gets the list of entries in an archive.
        /// </summary>
        public async Task<List<ArchiveEntry>> GetEntriesAsync(string archivePath, string? password = null)
        {
            return await Task.Run(() =>
            {
                var entries = new List<ArchiveEntry>();
                
                using var archive = OpenArchive(archivePath, password);
                if (archive == null) return entries;
                
                foreach (var entry in archive.Entries)
                {
                    entries.Add(new ArchiveEntry
                    {
                        Name = Path.GetFileName(entry.Key ?? ""),
                        Path = entry.Key ?? "",
                        Size = entry.Size,
                        CompressedSize = entry.CompressedSize,
                        LastModified = entry.LastModifiedTime ?? DateTime.MinValue,
                        IsDirectory = entry.IsDirectory,
                        IsEncrypted = entry.IsEncrypted
                    });
                }
                
                return entries.OrderBy(e => !e.IsDirectory).ThenBy(e => e.Path).ToList();
            });
        }
        
        /// <summary>
        /// Gets archive information without loading all entries.
        /// </summary>
        public ArchiveInfo GetArchiveInfo(string archivePath)
        {
            var info = new ArchiveInfo
            {
                FilePath = archivePath,
                FileName = Path.GetFileName(archivePath),
                FileSize = new FileInfo(archivePath).Length
            };
            
            using var archive = OpenArchive(archivePath, null);
            if (archive != null)
            {
                info.Format = DetectFormat(archivePath);
                info.EntryCount = archive.Entries.Count();
                info.TotalUncompressedSize = archive.Entries.Sum(e => e.Size);
                info.HasEncryptedEntries = archive.Entries.Any(e => e.IsEncrypted);
            }
            
            return info;
        }
        
        /// <summary>
        /// Extracts all or selected entries from an archive.
        /// </summary>
        public async Task ExtractAsync(
            string archivePath,
            ArchiveExtractOptions options,
            IProgress<ArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                using var archive = OpenArchive(archivePath, options.Password);
                if (archive == null)
                    throw new InvalidOperationException("Could not open archive");
                
                var entriesToExtract = archive.Entries
                    .Where(e => !e.IsDirectory)
                    .Where(e => options.SelectedEntries == null || options.SelectedEntries.Contains(e.Key))
                    .ToList();
                
                int totalFiles = entriesToExtract.Count;
                long totalBytes = entriesToExtract.Sum(e => e.Size);
                long bytesProcessed = 0;
                int fileIndex = 0;
                
                foreach (var entry in entriesToExtract)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var outputPath = options.PreserveFolderStructure
                        ? Path.Combine(options.OutputDirectory, entry.Key ?? "")
                        : Path.Combine(options.OutputDirectory, Path.GetFileName(entry.Key ?? ""));
                    
                    var outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir))
                        Directory.CreateDirectory(outputDir);
                    
                    if (File.Exists(outputPath) && !options.OverwriteExisting)
                    {
                        fileIndex++;
                        bytesProcessed += entry.Size;
                        continue;
                    }
                    
                    entry.WriteToFile(outputPath, new ExtractionOptions
                    {
                        ExtractFullPath = false,
                        Overwrite = options.OverwriteExisting
                    });
                    
                    fileIndex++;
                    bytesProcessed += entry.Size;
                    
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entry.Key ?? "",
                        CurrentFileIndex = fileIndex,
                        TotalFiles = totalFiles,
                        BytesProcessed = bytesProcessed,
                        TotalBytes = totalBytes
                    });
                }
            }, cancellationToken);
        }
        
        /// <summary>
        /// Creates a new archive from files and folders.
        /// </summary>
        public async Task CreateAsync(
            string outputPath,
            IEnumerable<string> sourcePaths,
            ArchiveCreateOptions options,
            IProgress<ArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                var files = new List<(string FullPath, string EntryPath)>();
                
                foreach (var source in sourcePaths)
                {
                    if (File.Exists(source))
                    {
                        files.Add((source, Path.GetFileName(source)));
                    }
                    else if (Directory.Exists(source))
                    {
                        var basePath = options.IncludeRootFolder ? Path.GetDirectoryName(source) ?? "" : source;
                        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                        {
                            var entryPath = GetRelativePath(basePath, file);
                            files.Add((file, entryPath));
                        }
                    }
                }
                
                int totalFiles = files.Count;
                long totalBytes = files.Sum(f => new FileInfo(f.FullPath).Length);
                long bytesProcessed = 0;
                int fileIndex = 0;
                
                using var stream = File.Create(outputPath);
                using var writer = CreateWriter(stream, options);
                
                foreach (var (fullPath, entryPath) in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    using var fileStream = File.OpenRead(fullPath);
                    writer.Write(entryPath, fileStream, new FileInfo(fullPath).LastWriteTime);
                    
                    fileIndex++;
                    bytesProcessed += new FileInfo(fullPath).Length;
                    
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entryPath,
                        CurrentFileIndex = fileIndex,
                        TotalFiles = totalFiles,
                        BytesProcessed = bytesProcessed,
                        TotalBytes = totalBytes
                    });
                }
            }, cancellationToken);
        }
        
        /// <summary>
        /// Adds files to an existing archive (ZIP only).
        /// </summary>
        public async Task AddToArchiveAsync(
            string archivePath,
            IEnumerable<string> filesToAdd,
            IProgress<ArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("Adding to archives is only supported for ZIP format");
            
            await Task.Run(() =>
            {
                using var archive = ZipArchive.Open(archivePath);
                
                var files = filesToAdd.ToList();
                int totalFiles = files.Count;
                int fileIndex = 0;
                
                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    archive.AddEntry(Path.GetFileName(file), file);
                    
                    fileIndex++;
                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = Path.GetFileName(file),
                        CurrentFileIndex = fileIndex,
                        TotalFiles = totalFiles
                    });
                }
                
                archive.SaveTo(archivePath, new SharpCompress.Writers.WriterOptions(CompressionType.Deflate));
            }, cancellationToken);
        }
        
        /// <summary>
        /// Tests archive integrity.
        /// </summary>
        public async Task<(bool IsValid, List<string> Errors)> TestArchiveAsync(string archivePath, string? password = null)
        {
            return await Task.Run(() =>
            {
                var errors = new List<string>();
                
                try
                {
                    using var archive = OpenArchive(archivePath, password);
                    if (archive == null)
                    {
                        errors.Add("Could not open archive");
                        return (false, errors);
                    }
                    
                    foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                    {
                        try
                        {
                            // Try to read the entry to verify integrity
                            using var stream = entry.OpenEntryStream();
                            var buffer = new byte[8192];
                            while (stream.Read(buffer, 0, buffer.Length) > 0) { }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{entry.Key}: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Archive error: {ex.Message}");
                }
                
                return (errors.Count == 0, errors);
            });
        }
        
        #region Private Methods
        
        private IArchive? OpenArchive(string path, string? password)
        {
            var options = new ReaderOptions { Password = password };
            
            try
            {
                return ArchiveFactory.Open(path, options);
            }
            catch
            {
                return null;
            }
        }
        
        private ArchiveFormat DetectFormat(string path)
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext switch
            {
                ".zip" => ArchiveFormat.Zip,
                ".7z" => ArchiveFormat.SevenZip,
                ".rar" => ArchiveFormat.Rar,
                ".tar" => ArchiveFormat.Tar,
                ".gz" or ".gzip" => ArchiveFormat.GZip,
                ".tgz" => ArchiveFormat.TarGz,
                ".tbz2" or ".tb2" => ArchiveFormat.TarBz2,
                _ => ArchiveFormat.Zip
            };
        }
        
        private IWriter CreateWriter(Stream stream, ArchiveCreateOptions options)
        {
            var compressionType = options.Level switch
            {
                CompressionLevel.None => CompressionType.None,
                CompressionLevel.Fastest => CompressionType.Deflate,
                CompressionLevel.Fast => CompressionType.Deflate,
                CompressionLevel.Normal => CompressionType.Deflate,
                CompressionLevel.Maximum => CompressionType.LZMA,
                CompressionLevel.Ultra => CompressionType.LZMA,
                _ => CompressionType.Deflate
            };
            
            var writerOptions = new WriterOptions(compressionType);
            
            return options.Format switch
            {
                ArchiveFormat.Zip => WriterFactory.Open(stream, ArchiveType.Zip, writerOptions),
                ArchiveFormat.Tar => WriterFactory.Open(stream, ArchiveType.Tar, writerOptions),
                ArchiveFormat.GZip => WriterFactory.Open(stream, ArchiveType.GZip, writerOptions),
                _ => WriterFactory.Open(stream, ArchiveType.Zip, writerOptions)
            };
        }
        
        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (string.IsNullOrEmpty(basePath))
                return fullPath;
            
            var baseUri = new Uri(basePath.EndsWith(Path.DirectorySeparatorChar.ToString()) 
                ? basePath 
                : basePath + Path.DirectorySeparatorChar);
            var fullUri = new Uri(fullPath);
            
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(fullUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }
        
        #endregion
    }
    
    /// <summary>
    /// Basic archive information.
    /// </summary>
    public class ArchiveInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public ArchiveFormat Format { get; set; }
        public int EntryCount { get; set; }
        public long TotalUncompressedSize { get; set; }
        public bool HasEncryptedEntries { get; set; }
        
        public string FileSizeDisplay => FormatFileSize(FileSize);
        public string UncompressedSizeDisplay => FormatFileSize(TotalUncompressedSize);
        
        private static string FormatFileSize(long bytes)
        {
            if (bytes == 0) return "0 B";
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
}
