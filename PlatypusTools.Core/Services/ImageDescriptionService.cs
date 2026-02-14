using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// IDEA-014: AI Image Description Service.
    /// Provides image description/alt-text generation using metadata analysis and
    /// basic image feature extraction. Designed to be extended with ONNX model
    /// support when Microsoft.ML.OnnxRuntime is added as a dependency.
    /// </summary>
    public class ImageDescriptionService
    {
        private static readonly Lazy<ImageDescriptionService> _instance = new(() => new ImageDescriptionService());
        public static ImageDescriptionService Instance => _instance.Value;

        /// <summary>
        /// Describes an image file using available metadata and heuristics.
        /// Returns structured image description with tags and alt-text.
        /// </summary>
        public async Task<ImageDescription> DescribeImageAsync(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("Image file not found", filePath);

            var description = new ImageDescription
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                FileSize = new FileInfo(filePath).Length,
                Format = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant(),
                AnalyzedAt = DateTime.UtcNow
            };

            // Extract metadata-based description
            await Task.Run(() =>
            {
                ExtractExifMetadata(filePath, description);
                AnalyzeFileCharacteristics(filePath, description);
                GenerateAltText(description);
            });

            return description;
        }

        /// <summary>
        /// Generates alt-text for multiple images in batch.
        /// </summary>
        public async Task<List<ImageDescription>> DescribeBatchAsync(IEnumerable<string> filePaths, 
            IProgress<(int current, int total, string file)>? progress = null)
        {
            var files = filePaths.ToList();
            var results = new List<ImageDescription>();

            for (int i = 0; i < files.Count; i++)
            {
                try
                {
                    var desc = await DescribeImageAsync(files[i]);
                    results.Add(desc);
                }
                catch (Exception ex)
                {
                    results.Add(new ImageDescription
                    {
                        FilePath = files[i],
                        FileName = Path.GetFileName(files[i]),
                        AltText = $"Error analyzing image: {ex.Message}",
                        Tags = { "error" }
                    });
                }
                progress?.Report((i + 1, files.Count, files[i]));
            }

            return results;
        }

        private void ExtractExifMetadata(string filePath, ImageDescription desc)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                var buffer = new byte[Math.Min(65536, stream.Length)];
                stream.Read(buffer, 0, buffer.Length);

                // Basic EXIF detection for JPEG (look for Exif header)
                if (buffer.Length > 12)
                {
                    // JPEG SOI marker
                    if (buffer[0] == 0xFF && buffer[1] == 0xD8)
                    {
                        desc.Tags.Add("photograph");
                        desc.ImageType = "JPEG Photo";

                        // Search for EXIF data markers
                        for (int i = 2; i < buffer.Length - 10; i++)
                        {
                            // Look for GPS IFD tag (0x8825) which indicates geotagged photo
                            if (buffer[i] == 0x88 && buffer[i + 1] == 0x25)
                            {
                                desc.Tags.Add("geotagged");
                                break;
                            }
                        }

                        // Check for camera make strings
                        var headerText = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(4096, buffer.Length));
                        if (headerText.Contains("Canon") || headerText.Contains("Nikon") || headerText.Contains("Sony") || 
                            headerText.Contains("Fujifilm") || headerText.Contains("Olympus") || headerText.Contains("Panasonic"))
                        {
                            desc.Tags.Add("camera-photo");
                        }
                        if (headerText.Contains("iPhone") || headerText.Contains("Samsung") || headerText.Contains("Pixel") || 
                            headerText.Contains("Huawei"))
                        {
                            desc.Tags.Add("mobile-photo");
                        }
                    }
                    // PNG signature
                    else if (buffer[0] == 0x89 && buffer[1] == 0x50 && buffer[2] == 0x4E && buffer[3] == 0x47)
                    {
                        desc.ImageType = "PNG Image";
                        // PNG with transparency likely a graphic/screenshot
                        // Check for tRNS or IDAT with alpha
                        var pngText = System.Text.Encoding.ASCII.GetString(buffer, 0, Math.Min(512, buffer.Length));
                        if (pngText.Contains("tRNS") || pngText.Contains("tEXt"))
                        {
                            desc.Tags.Add("transparent");
                        }
                    }
                    // BMP
                    else if (buffer[0] == 0x42 && buffer[1] == 0x4D)
                    {
                        desc.ImageType = "Bitmap Image";
                        desc.Tags.Add("bitmap");
                    }
                    // GIF
                    else if (buffer[0] == 0x47 && buffer[1] == 0x49 && buffer[2] == 0x46)
                    {
                        desc.ImageType = "GIF Image";
                        desc.Tags.Add("animated");
                    }
                    // WebP
                    else if (buffer.Length > 15 && buffer[8] == 0x57 && buffer[9] == 0x45 && buffer[10] == 0x42 && buffer[11] == 0x50)
                    {
                        desc.ImageType = "WebP Image";
                    }
                    // TIFF
                    else if ((buffer[0] == 0x49 && buffer[1] == 0x49) || (buffer[0] == 0x4D && buffer[1] == 0x4D))
                    {
                        desc.ImageType = "TIFF Image";
                        desc.Tags.Add("high-quality");
                    }
                }

                // Detect image dimensions from file header
                DetectDimensions(buffer, desc);
            }
            catch { /* Non-critical metadata extraction */ }
        }

        private void DetectDimensions(byte[] buffer, ImageDescription desc)
        {
            try
            {
                // JPEG: scan for SOF0 marker (0xFF 0xC0) to get dimensions
                if (buffer.Length > 2 && buffer[0] == 0xFF && buffer[1] == 0xD8)
                {
                    for (int i = 2; i < buffer.Length - 10; i++)
                    {
                        if (buffer[i] == 0xFF && (buffer[i + 1] == 0xC0 || buffer[i + 1] == 0xC2))
                        {
                            desc.Height = (buffer[i + 5] << 8) | buffer[i + 6];
                            desc.Width = (buffer[i + 7] << 8) | buffer[i + 8];
                            break;
                        }
                    }
                }
                // PNG: dimensions at bytes 16-23
                else if (buffer.Length > 24 && buffer[0] == 0x89 && buffer[1] == 0x50)
                {
                    desc.Width = (buffer[16] << 24) | (buffer[17] << 16) | (buffer[18] << 8) | buffer[19];
                    desc.Height = (buffer[20] << 24) | (buffer[21] << 16) | (buffer[22] << 8) | buffer[23];
                }
                // BMP: dimensions at bytes 18-25
                else if (buffer.Length > 26 && buffer[0] == 0x42 && buffer[1] == 0x4D)
                {
                    desc.Width = buffer[18] | (buffer[19] << 8) | (buffer[20] << 16) | (buffer[21] << 24);
                    desc.Height = buffer[22] | (buffer[23] << 8) | (buffer[24] << 16) | (buffer[25] << 24);
                }

                if (desc.Width > 0 && desc.Height > 0)
                {
                    double aspect = (double)desc.Width / desc.Height;
                    if (aspect > 1.7) desc.Tags.Add("panoramic");
                    else if (aspect > 1.3) desc.Tags.Add("landscape");
                    else if (aspect < 0.8) desc.Tags.Add("portrait");
                    else desc.Tags.Add("square");

                    if (desc.Width >= 3840 || desc.Height >= 2160) desc.Tags.Add("4K");
                    else if (desc.Width >= 1920 || desc.Height >= 1080) desc.Tags.Add("HD");
                    else if (desc.Width < 640 && desc.Height < 480) desc.Tags.Add("thumbnail");
                }
            }
            catch { }
        }

        private void AnalyzeFileCharacteristics(string filePath, ImageDescription desc)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath).ToLowerInvariant();

            // Detect common naming patterns
            if (fileName.Contains("screenshot") || fileName.Contains("screen_shot") || fileName.StartsWith("snip"))
            {
                desc.Tags.Add("screenshot");
                desc.ImageType ??= "Screenshot";
            }
            if (fileName.Contains("wallpaper") || fileName.Contains("background"))
            {
                desc.Tags.Add("wallpaper");
            }
            if (fileName.Contains("icon") || fileName.Contains("logo"))
            {
                desc.Tags.Add("icon");
                desc.ImageType ??= "Icon/Logo";
            }
            if (fileName.Contains("scan") || fileName.Contains("document"))
            {
                desc.Tags.Add("scanned-document");
                desc.ImageType ??= "Scanned Document";
            }
            if (fileName.Contains("meme") || fileName.Contains("reaction"))
            {
                desc.Tags.Add("meme");
            }
            if (fileName.Contains("album") || fileName.Contains("cover") || fileName.Contains("artwork"))
            {
                desc.Tags.Add("album-art");
                desc.ImageType ??= "Album Artwork";
            }

            // File size heuristics
            if (desc.FileSize > 10_000_000) desc.Tags.Add("high-resolution");
            else if (desc.FileSize < 50_000) desc.Tags.Add("compressed");
            
            // Date in filename
            if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"\d{4}[-_]\d{2}[-_]\d{2}"))
            {
                desc.Tags.Add("dated");
            }
        }

        private void GenerateAltText(ImageDescription desc)
        {
            var parts = new List<string>();
            
            // Image type
            if (!string.IsNullOrEmpty(desc.ImageType))
                parts.Add(desc.ImageType);
            else
                parts.Add($"{desc.Format} image");

            // Dimensions
            if (desc.Width > 0 && desc.Height > 0)
                parts.Add($"{desc.Width}×{desc.Height}");

            // Orientation
            if (desc.Tags.Contains("landscape")) parts.Add("landscape orientation");
            else if (desc.Tags.Contains("portrait")) parts.Add("portrait orientation");
            else if (desc.Tags.Contains("panoramic")) parts.Add("panoramic");

            // Quality
            if (desc.Tags.Contains("4K")) parts.Add("4K resolution");
            else if (desc.Tags.Contains("HD")) parts.Add("high definition");

            // Source
            if (desc.Tags.Contains("camera-photo")) parts.Add("from camera");
            else if (desc.Tags.Contains("mobile-photo")) parts.Add("from mobile device");
            else if (desc.Tags.Contains("screenshot")) parts.Add("screen capture");

            // Special types
            if (desc.Tags.Contains("album-art")) parts.Add("album artwork");
            if (desc.Tags.Contains("transparent")) parts.Add("with transparency");

            desc.AltText = string.Join(", ", parts);
        }
    }

    /// <summary>
    /// Structured image description with metadata tags and generated alt-text.
    /// </summary>
    public class ImageDescription
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string Format { get; set; } = string.Empty;
        public string? ImageType { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string AltText { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new();
        public DateTime AnalyzedAt { get; set; }

        /// <summary>
        /// Formatted file size string (e.g., "2.4 MB").
        /// </summary>
        public string FileSizeDisplay
        {
            get
            {
                if (FileSize >= 1_073_741_824) return $"{FileSize / 1_073_741_824.0:F1} GB";
                if (FileSize >= 1_048_576) return $"{FileSize / 1_048_576.0:F1} MB";
                if (FileSize >= 1024) return $"{FileSize / 1024.0:F1} KB";
                return $"{FileSize} B";
            }
        }

        /// <summary>
        /// Comma-separated tag string for display.
        /// </summary>
        public string TagsDisplay => string.Join(", ", Tags);

        /// <summary>
        /// Dimensions display string (e.g., "1920×1080").
        /// </summary>
        public string DimensionsDisplay => Width > 0 && Height > 0 ? $"{Width}×{Height}" : "Unknown";
    }
}
