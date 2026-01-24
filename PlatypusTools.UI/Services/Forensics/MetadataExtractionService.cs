using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.UI.Utilities;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Metadata extracted from a document.
    /// </summary>
    public class ExtractedMetadata
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        
        // Common metadata fields
        public string? Author { get; set; }
        public string? Title { get; set; }
        public string? Creator { get; set; }
        public string? Producer { get; set; }
        public string? Company { get; set; }
        public string? Software { get; set; }
        public string? Subject { get; set; }
        public string? Keywords { get; set; }
        public string? Description { get; set; }
        public string? Copyright { get; set; }
        public string? LastModifiedBy { get; set; }
        
        // GPS/Location data
        public string? GPSLatitude { get; set; }
        public string? GPSLongitude { get; set; }
        public string? GPSPosition { get; set; }
        
        // Camera/EXIF
        public string? CameraMake { get; set; }
        public string? CameraModel { get; set; }
        public string? ExposureTime { get; set; }
        public string? FNumber { get; set; }
        public string? ISO { get; set; }
        public string? FocalLength { get; set; }
        
        // All metadata fields from ExifTool
        public Dictionary<string, string> AllMetadata { get; set; } = new();
        
        public int MetadataCount => AllMetadata.Count;
        public bool HasGPS => !string.IsNullOrEmpty(GPSLatitude) || !string.IsNullOrEmpty(GPSLongitude);
    }

    /// <summary>
    /// Metadata extraction summary.
    /// </summary>
    public class MetadataExtractionSummary
    {
        public int FilesProcessed { get; set; }
        public int SuccessfulExtractions { get; set; }
        public int TotalMetadataFields { get; set; }
        public List<string> UniqueAuthors { get; } = new();
        public List<string> UniqueCompanies { get; } = new();
        public List<string> UniqueSoftware { get; } = new();
        public int FilesWithGPS { get; set; }
        public List<string> CameraModels { get; } = new();
    }

    /// <summary>
    /// Service for extracting metadata from documents using ExifTool.
    /// OSINT/FOCA-style metadata gathering for intelligence purposes.
    /// </summary>
    public class MetadataExtractionService : ForensicOperationBase
    {
        private static readonly HttpClient _httpClient = new();
        
        private static readonly string[] _supportedExtensions = 
        {
            ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt",
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", 
            ".webp", ".heic", ".raw", ".cr2", ".nef", ".arw"
        };

        public override string OperationName => "Metadata Extraction";

        /// <summary>
        /// Path to ExifTool executable.
        /// </summary>
        public string? ExifToolPath { get; set; }

        /// <summary>
        /// Extracts metadata from all supported files in a folder.
        /// </summary>
        public async Task<(List<ExtractedMetadata> metadata, MetadataExtractionSummary summary)> ExtractFromFolderAsync(
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            var results = new List<ExtractedMetadata>();
            var summary = new MetadataExtractionSummary();

            await ExecuteWithHandlingAsync(async (token) =>
            {
                if (!Directory.Exists(folderPath))
                {
                    LogError($"Folder not found: {folderPath}");
                    return;
                }

                LogHeader("Document Metadata Extraction");
                Log($"Folder: {folderPath}");
                Log($"ExifTool: {(File.Exists(ExifToolPath) ? "Available" : "Not found - using basic extraction")}");
                Log("");

                // Get files using async enumeration
                var files = await AsyncFileEnumerator.GetFilesAsync(
                    folderPath, _supportedExtensions, cancellationToken: token);

                Log($"Found {files.Count} files to analyze");
                Log("");

                int processed = 0;
                foreach (var file in files)
                {
                    token.ThrowIfCancellationRequested();

                    StatusMessage = $"Extracting: {Path.GetFileName(file)}";

                    var metadata = await ExtractFromFileAsync(file, token);
                    if (metadata != null)
                    {
                        results.Add(metadata);
                        summary.SuccessfulExtractions++;
                        summary.TotalMetadataFields += metadata.MetadataCount;

                        // Track unique values
                        if (!string.IsNullOrEmpty(metadata.Author) && 
                            !summary.UniqueAuthors.Contains(metadata.Author))
                            summary.UniqueAuthors.Add(metadata.Author);
                        
                        if (!string.IsNullOrEmpty(metadata.Company) && 
                            !summary.UniqueCompanies.Contains(metadata.Company))
                            summary.UniqueCompanies.Add(metadata.Company);
                        
                        if (!string.IsNullOrEmpty(metadata.Software) && 
                            !summary.UniqueSoftware.Contains(metadata.Software))
                            summary.UniqueSoftware.Add(metadata.Software);
                        
                        if (metadata.HasGPS)
                            summary.FilesWithGPS++;

                        if (!string.IsNullOrEmpty(metadata.CameraModel))
                        {
                            var camera = $"{metadata.CameraMake} {metadata.CameraModel}".Trim();
                            if (!summary.CameraModels.Contains(camera))
                                summary.CameraModels.Add(camera);
                        }
                    }

                    processed++;
                    summary.FilesProcessed = processed;
                    UpdateProgress(processed, files.Count);
                }

                // Log summary
                Log("");
                LogHeader("Extraction Summary");
                Log($"Files processed: {summary.FilesProcessed}");
                Log($"Successful extractions: {summary.SuccessfulExtractions}");
                Log($"Total metadata fields: {summary.TotalMetadataFields}");
                Log("");

                if (summary.UniqueAuthors.Any())
                    Log($"Authors ({summary.UniqueAuthors.Count}): {string.Join(", ", summary.UniqueAuthors.Take(15))}");
                if (summary.UniqueCompanies.Any())
                    Log($"Companies ({summary.UniqueCompanies.Count}): {string.Join(", ", summary.UniqueCompanies.Take(15))}");
                if (summary.UniqueSoftware.Any())
                    Log($"Software ({summary.UniqueSoftware.Count}): {string.Join(", ", summary.UniqueSoftware.Take(15))}");

                Log("");
                if (summary.FilesWithGPS > 0)
                {
                    LogWarning($"⚠ Found {summary.FilesWithGPS} files with GPS coordinates!");
                    foreach (var gpsFile in results.Where(r => r.HasGPS).Take(10))
                    {
                        var coords = !string.IsNullOrEmpty(gpsFile.GPSPosition) 
                            ? gpsFile.GPSPosition 
                            : $"{gpsFile.GPSLatitude}, {gpsFile.GPSLongitude}";
                        Log($"  • {gpsFile.FileName}: {coords}");
                    }
                }
                else
                {
                    Log("No GPS coordinates found in scanned files");
                }

                if (summary.CameraModels.Any())
                {
                    Log("");
                    Log($"Camera Models ({summary.CameraModels.Count}): {string.Join(", ", summary.CameraModels.Take(10))}");
                }

                Log("");
                LogSuccess("Metadata extraction complete");
            });

            return (results, summary);
        }

        /// <summary>
        /// Extracts metadata from a single file.
        /// </summary>
        public async Task<ExtractedMetadata?> ExtractFromFileAsync(string filePath, CancellationToken token = default)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var metadata = new ExtractedMetadata
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    Created = fileInfo.CreationTime,
                    Modified = fileInfo.LastWriteTime
                };

                if (!string.IsNullOrEmpty(ExifToolPath) && File.Exists(ExifToolPath))
                {
                    await ExtractWithExifToolAsync(filePath, metadata, token);
                }
                else
                {
                    // Fallback to basic extraction for Office documents
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext is ".docx" or ".xlsx" or ".pptx")
                    {
                        await ExtractFromOfficeXmlAsync(filePath, metadata);
                    }
                }

                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private async Task ExtractWithExifToolAsync(string filePath, ExtractedMetadata metadata, CancellationToken token)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = ExifToolPath!,
                    Arguments = $"-j -a -G \"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = psi };
                process.Start();

                var output = await process.StandardOutput.ReadToEndAsync(token);
                await process.WaitForExitAsync(token);

                if (string.IsNullOrEmpty(output)) return;

                using var doc = JsonDocument.Parse(output);
                var root = doc.RootElement[0];

                foreach (var property in root.EnumerateObject())
                {
                    var key = property.Name;
                    var value = property.Value.ValueKind == JsonValueKind.String
                        ? property.Value.GetString() ?? ""
                        : property.Value.ToString();

                    if (string.IsNullOrEmpty(value) || value == "0" || value == "Unknown")
                        continue;

                    metadata.AllMetadata[key] = value;

                    // Map to specific properties
                    var keyLower = key.ToLowerInvariant();
                    MapMetadataProperty(metadata, keyLower, value);
                }
            }
            catch
            {
                // Ignore extraction errors
            }
        }

        private void MapMetadataProperty(ExtractedMetadata metadata, string keyLower, string value)
        {
            // Author/Creator
            if (keyLower.Contains("author") && string.IsNullOrEmpty(metadata.Author))
                metadata.Author = value;
            if (keyLower.Contains("creator") && !keyLower.Contains("creatortool") && string.IsNullOrEmpty(metadata.Creator))
                metadata.Creator = value;
            if (keyLower.Contains("producer") && string.IsNullOrEmpty(metadata.Producer))
                metadata.Producer = value;
            if (keyLower.Contains("company") && string.IsNullOrEmpty(metadata.Company))
                metadata.Company = value;
            if ((keyLower == "title" || keyLower.EndsWith(":title")) && string.IsNullOrEmpty(metadata.Title))
                metadata.Title = value;
            if (keyLower.Contains("software") && string.IsNullOrEmpty(metadata.Software))
                metadata.Software = value;
            if (keyLower.Contains("lastmodifiedby") && string.IsNullOrEmpty(metadata.LastModifiedBy))
                metadata.LastModifiedBy = value;

            // GPS
            if (keyLower.Contains("gpslatitude") && !keyLower.Contains("ref") && string.IsNullOrEmpty(metadata.GPSLatitude))
                metadata.GPSLatitude = value;
            if (keyLower.Contains("gpslongitude") && !keyLower.Contains("ref") && string.IsNullOrEmpty(metadata.GPSLongitude))
                metadata.GPSLongitude = value;
            if (keyLower.Contains("gpsposition") && string.IsNullOrEmpty(metadata.GPSPosition))
                metadata.GPSPosition = value;

            // Camera
            if ((keyLower == "model" || keyLower.EndsWith(":model")) && string.IsNullOrEmpty(metadata.CameraModel))
                metadata.CameraModel = value;
            if ((keyLower == "make" || keyLower.EndsWith(":make")) && string.IsNullOrEmpty(metadata.CameraMake))
                metadata.CameraMake = value;
            if (keyLower.Contains("exposuretime") && string.IsNullOrEmpty(metadata.ExposureTime))
                metadata.ExposureTime = value;
            if (keyLower.Contains("fnumber") && string.IsNullOrEmpty(metadata.FNumber))
                metadata.FNumber = value;
            if ((keyLower == "iso" || keyLower.Contains(":iso")) && string.IsNullOrEmpty(metadata.ISO))
                metadata.ISO = value;
            if (keyLower.Contains("focallength") && string.IsNullOrEmpty(metadata.FocalLength))
                metadata.FocalLength = value;
        }

        private async Task ExtractFromOfficeXmlAsync(string filePath, ExtractedMetadata metadata)
        {
            try
            {
                using var archive = ZipFile.OpenRead(filePath);
                var coreEntry = archive.GetEntry("docProps/core.xml");
                if (coreEntry != null)
                {
                    using var stream = coreEntry.Open();
                    using var reader = new StreamReader(stream);
                    var xml = await reader.ReadToEndAsync();

                    metadata.Author = ExtractXmlValue(xml, "dc:creator");
                    metadata.Title = ExtractXmlValue(xml, "dc:title");
                    metadata.LastModifiedBy = ExtractXmlValue(xml, "cp:lastModifiedBy");
                    metadata.Subject = ExtractXmlValue(xml, "dc:subject");
                    metadata.Keywords = ExtractXmlValue(xml, "cp:keywords");
                }
            }
            catch
            {
                // Ignore
            }
        }

        private string? ExtractXmlValue(string xml, string tag)
        {
            var startTag = $"<{tag}>";
            var endTag = $"</{tag}>";
            var start = xml.IndexOf(startTag);
            var end = xml.IndexOf(endTag);
            if (start >= 0 && end > start)
                return xml.Substring(start + startTag.Length, end - start - startTag.Length);
            return null;
        }

        /// <summary>
        /// Downloads ExifTool.
        /// </summary>
        public async Task<string?> DownloadExifToolAsync(CancellationToken cancellationToken = default)
        {
            LogHeader("Downloading ExifTool");

            try
            {
                var toolsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                Directory.CreateDirectory(toolsDir);

                const string downloadUrl = "https://exiftool.org/exiftool-12.76.zip";
                var zipPath = Path.Combine(toolsDir, "exiftool.zip");

                Log($"Downloading from: {downloadUrl}");

                var bytes = await _httpClient.GetByteArrayAsync(downloadUrl, cancellationToken);
                await File.WriteAllBytesAsync(zipPath, bytes, cancellationToken);

                // Extract
                var extractPath = Path.Combine(toolsDir, "exiftool_temp");
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);

                ZipFile.ExtractToDirectory(zipPath, extractPath);

                // Find and rename the executable
                var exeFile = Directory.GetFiles(extractPath, "*.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (exeFile != null)
                {
                    var destPath = Path.Combine(toolsDir, "exiftool.exe");
                    if (File.Exists(destPath))
                        File.Delete(destPath);
                    File.Move(exeFile, destPath);
                    ExifToolPath = destPath;

                    // Cleanup
                    Directory.Delete(extractPath, true);
                    File.Delete(zipPath);

                    LogSuccess($"ExifTool installed to: {destPath}");
                    return destPath;
                }

                LogError("Could not find ExifTool executable in archive");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"Download failed: {ex.Message}");
                return null;
            }
        }
    }
}
