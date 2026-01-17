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
        /// Supports splitting into multiple parts if SplitSizeBytes is set.
        /// </summary>
        public async Task CreateAsync(
            string outputPath,
            IEnumerable<string> sourcePaths,
            ArchiveCreateOptions options,
            IProgress<ArchiveProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // If split is enabled, use split archive creation
            if (options.SplitSizeBytes > 0)
            {
                await CreateSplitArchiveAsync(outputPath, sourcePaths, options, progress, cancellationToken);
                return;
            }
            
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
        /// Creates split archives by distributing files across multiple archive parts.
        /// </summary>
        private async Task CreateSplitArchiveAsync(
            string outputPath,
            IEnumerable<string> sourcePaths,
            ArchiveCreateOptions options,
            IProgress<ArchiveProgress>? progress,
            CancellationToken cancellationToken)
        {
            await Task.Run(() =>
            {
                var files = new List<(string FullPath, string EntryPath, long Size)>();
                
                foreach (var source in sourcePaths)
                {
                    if (File.Exists(source))
                    {
                        files.Add((source, Path.GetFileName(source), new FileInfo(source).Length));
                    }
                    else if (Directory.Exists(source))
                    {
                        var basePath = options.IncludeRootFolder ? Path.GetDirectoryName(source) ?? "" : source;
                        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                        {
                            var entryPath = GetRelativePath(basePath, file);
                            files.Add((file, entryPath, new FileInfo(file).Length));
                        }
                    }
                }
                
                int totalFiles = files.Count;
                long totalBytes = files.Sum(f => f.Size);
                long bytesProcessed = 0;
                int fileIndex = 0;
                
                // Split files into parts based on size
                var parts = new List<List<(string FullPath, string EntryPath, long Size)>>();
                var currentPart = new List<(string FullPath, string EntryPath, long Size)>();
                long currentPartSize = 0;
                
                foreach (var file in files)
                {
                    // If adding this file exceeds the limit and we have files, start a new part
                    if (currentPartSize + file.Size > options.SplitSizeBytes && currentPart.Count > 0)
                    {
                        parts.Add(currentPart);
                        currentPart = new List<(string FullPath, string EntryPath, long Size)>();
                        currentPartSize = 0;
                    }
                    
                    currentPart.Add(file);
                    currentPartSize += file.Size;
                }
                
                // Add the last part
                if (currentPart.Count > 0)
                {
                    parts.Add(currentPart);
                }
                
                // Create each part
                var ext = Path.GetExtension(outputPath);
                var baseName = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? "",
                    Path.GetFileNameWithoutExtension(outputPath));
                
                for (int partNum = 0; partNum < parts.Count; partNum++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var partPath = parts.Count > 1 
                        ? $"{baseName}.part{partNum + 1:D3}{ext}" 
                        : outputPath;
                    
                    using var stream = File.Create(partPath);
                    using var writer = CreateWriter(stream, options);
                    
                    foreach (var (fullPath, entryPath, size) in parts[partNum])
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        using var fileStream = File.OpenRead(fullPath);
                        writer.Write(entryPath, fileStream, new FileInfo(fullPath).LastWriteTime);
                        
                        fileIndex++;
                        bytesProcessed += size;
                        
                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = $"Part {partNum + 1}/{parts.Count}: {entryPath}",
                            CurrentFileIndex = fileIndex,
                            TotalFiles = totalFiles,
                            BytesProcessed = bytesProcessed,
                            TotalBytes = totalBytes
                        });
                    }
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
