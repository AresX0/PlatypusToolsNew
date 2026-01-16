using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Represents a group of visually similar images.
    /// </summary>
    public class SimilarImageGroup
    {
        public string ReferenceHash { get; set; } = string.Empty;
        public List<SimilarImageInfo> Images { get; set; } = new();
    }

    /// <summary>
    /// Information about a similar image.
    /// </summary>
    public class SimilarImageInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string Hash { get; set; } = string.Empty;
        public double SimilarityPercent { get; set; }
        public long FileSize { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Resolution => $"{Width}x{Height}";
        public Bitmap? Thumbnail { get; set; }
    }

    /// <summary>
    /// Progress information for similarity scanning.
    /// </summary>
    public class SimilarityScanProgress
    {
        public int TotalFiles { get; set; }
        public int ProcessedFiles { get; set; }
        public int SimilarGroupsFound { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public double ProgressPercent => TotalFiles > 0 ? (ProcessedFiles * 100.0 / TotalFiles) : 0;
    }

    /// <summary>
    /// Service for finding visually similar images using perceptual hashing algorithms.
    /// </summary>
    public class ImageSimilarityService
    {
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".tif" };
        
        public event EventHandler<SimilarityScanProgress>? ProgressChanged;

        /// <summary>
        /// Finds groups of visually similar images in the specified paths.
        /// </summary>
        /// <param name="paths">Folders or files to scan</param>
        /// <param name="threshold">Similarity threshold (0-100, default 90 for 90% similar)</param>
        /// <param name="recurse">Whether to search subdirectories</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Groups of similar images</returns>
        public async Task<List<SimilarImageGroup>> FindSimilarImagesAsync(
            IEnumerable<string> paths, 
            int threshold = 90, 
            bool recurse = true,
            CancellationToken cancellationToken = default)
        {
            var progress = new SimilarityScanProgress();
            
            // Collect all image files
            var imageFiles = CollectImageFiles(paths, recurse).ToList();
            progress.TotalFiles = imageFiles.Count;
            
            if (imageFiles.Count < 2)
                return new List<SimilarImageGroup>();

            // Calculate hashes for all images
            var imageHashes = new Dictionary<string, (string pHash, string dHash, int width, int height, long size)>();
            
            foreach (var file in imageFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                progress.CurrentFile = Path.GetFileName(file);
                progress.ProcessedFiles++;
                ProgressChanged?.Invoke(this, progress);
                
                try
                {
                    var (pHash, dHash, width, height) = await Task.Run(() => ComputeImageHashes(file), cancellationToken);
                    var size = new FileInfo(file).Length;
                    imageHashes[file] = (pHash, dHash, width, height, size);
                }
                catch
                {
                    // Skip files that can't be processed
                }
            }

            // Find similar images
            var groups = new List<SimilarImageGroup>();
            var processedFiles = new HashSet<string>();
            var hashList = imageHashes.ToList();

            for (int i = 0; i < hashList.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var file1 = hashList[i].Key;
                if (processedFiles.Contains(file1)) continue;

                var group = new SimilarImageGroup { ReferenceHash = hashList[i].Value.pHash };
                var file1Info = hashList[i].Value;
                
                group.Images.Add(new SimilarImageInfo
                {
                    FilePath = file1,
                    Hash = file1Info.pHash,
                    SimilarityPercent = 100,
                    Width = file1Info.width,
                    Height = file1Info.height,
                    FileSize = file1Info.size
                });

                for (int j = i + 1; j < hashList.Count; j++)
                {
                    var file2 = hashList[j].Key;
                    if (processedFiles.Contains(file2)) continue;

                    var file2Info = hashList[j].Value;
                    
                    // Compare using both pHash and dHash for better accuracy
                    var pHashSimilarity = CalculateHashSimilarity(file1Info.pHash, file2Info.pHash);
                    var dHashSimilarity = CalculateHashSimilarity(file1Info.dHash, file2Info.dHash);
                    
                    // Use average of both hashes
                    var avgSimilarity = (pHashSimilarity + dHashSimilarity) / 2;

                    if (avgSimilarity >= threshold)
                    {
                        group.Images.Add(new SimilarImageInfo
                        {
                            FilePath = file2,
                            Hash = file2Info.pHash,
                            SimilarityPercent = avgSimilarity,
                            Width = file2Info.width,
                            Height = file2Info.height,
                            FileSize = file2Info.size
                        });
                        processedFiles.Add(file2);
                    }
                }

                if (group.Images.Count > 1)
                {
                    // Sort by similarity (highest first), then by file size (largest first)
                    group.Images = group.Images
                        .OrderByDescending(x => x.SimilarityPercent)
                        .ThenByDescending(x => x.FileSize)
                        .ToList();
                    
                    groups.Add(group);
                    progress.SimilarGroupsFound = groups.Count;
                    ProgressChanged?.Invoke(this, progress);
                }
                
                processedFiles.Add(file1);
            }

            return groups;
        }

        /// <summary>
        /// Computes perceptual hash (pHash) and difference hash (dHash) for an image.
        /// </summary>
        private (string pHash, string dHash, int width, int height) ComputeImageHashes(string filePath)
        {
            using var original = Image.FromFile(filePath);
            int width = original.Width;
            int height = original.Height;
            
            using var bitmap = new Bitmap(original);
            
            var pHash = ComputePerceptualHash(bitmap);
            var dHash = ComputeDifferenceHash(bitmap);
            
            return (pHash, dHash, width, height);
        }

        /// <summary>
        /// Computes perceptual hash (pHash) using DCT-based algorithm.
        /// Resizes to 32x32, converts to grayscale, applies DCT, takes top-left 8x8.
        /// </summary>
        private string ComputePerceptualHash(Bitmap bitmap)
        {
            const int hashSize = 8;
            const int sampleSize = 32;

            // Resize to 32x32
            using var resized = ResizeImage(bitmap, sampleSize, sampleSize);
            
            // Convert to grayscale values
            var grayValues = new double[sampleSize, sampleSize];
            for (int y = 0; y < sampleSize; y++)
            {
                for (int x = 0; x < sampleSize; x++)
                {
                    var pixel = resized.GetPixel(x, y);
                    grayValues[x, y] = 0.299 * pixel.R + 0.587 * pixel.G + 0.114 * pixel.B;
                }
            }

            // Apply DCT
            var dctValues = ApplyDCT(grayValues, sampleSize);

            // Take top-left 8x8 (excluding DC component)
            var dctLowFreq = new double[hashSize * hashSize];
            int idx = 0;
            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    if (x == 0 && y == 0) continue; // Skip DC
                    dctLowFreq[idx++] = dctValues[x, y];
                }
            }

            // Calculate median
            var sorted = dctLowFreq.Take(idx).OrderBy(x => x).ToArray();
            var median = sorted[sorted.Length / 2];

            // Generate hash
            ulong hash = 0;
            idx = 0;
            for (int y = 0; y < hashSize; y++)
            {
                for (int x = 0; x < hashSize; x++)
                {
                    if (x == 0 && y == 0) continue;
                    if (dctValues[x, y] > median)
                        hash |= (1UL << idx);
                    idx++;
                }
            }

            return hash.ToString("X16");
        }

        /// <summary>
        /// Computes difference hash (dHash) - compares adjacent pixels.
        /// </summary>
        private string ComputeDifferenceHash(Bitmap bitmap)
        {
            const int hashWidth = 9;
            const int hashHeight = 8;

            // Resize to 9x8
            using var resized = ResizeImage(bitmap, hashWidth, hashHeight);

            ulong hash = 0;
            int bit = 0;

            for (int y = 0; y < hashHeight; y++)
            {
                for (int x = 0; x < hashWidth - 1; x++)
                {
                    var pixel1 = resized.GetPixel(x, y);
                    var pixel2 = resized.GetPixel(x + 1, y);
                    
                    var gray1 = 0.299 * pixel1.R + 0.587 * pixel1.G + 0.114 * pixel1.B;
                    var gray2 = 0.299 * pixel2.R + 0.587 * pixel2.G + 0.114 * pixel2.B;

                    if (gray1 > gray2)
                        hash |= (1UL << bit);
                    bit++;
                }
            }

            return hash.ToString("X16");
        }

        /// <summary>
        /// Applies 2D Discrete Cosine Transform.
        /// </summary>
        private double[,] ApplyDCT(double[,] input, int size)
        {
            var output = new double[size, size];
            
            for (int u = 0; u < size; u++)
            {
                for (int v = 0; v < size; v++)
                {
                    double sum = 0;
                    for (int x = 0; x < size; x++)
                    {
                        for (int y = 0; y < size; y++)
                        {
                            sum += input[x, y] * 
                                   Math.Cos((2 * x + 1) * u * Math.PI / (2 * size)) *
                                   Math.Cos((2 * y + 1) * v * Math.PI / (2 * size));
                        }
                    }
                    
                    double cu = u == 0 ? 1 / Math.Sqrt(2) : 1;
                    double cv = v == 0 ? 1 / Math.Sqrt(2) : 1;
                    output[u, v] = 0.25 * cu * cv * sum;
                }
            }
            
            return output;
        }

        /// <summary>
        /// Calculates similarity between two hashes (0-100).
        /// </summary>
        private double CalculateHashSimilarity(string hash1, string hash2)
        {
            if (string.IsNullOrEmpty(hash1) || string.IsNullOrEmpty(hash2))
                return 0;

            try
            {
                var val1 = Convert.ToUInt64(hash1, 16);
                var val2 = Convert.ToUInt64(hash2, 16);
                
                // Count differing bits (Hamming distance)
                var xor = val1 ^ val2;
                int differentBits = 0;
                
                while (xor != 0)
                {
                    differentBits += (int)(xor & 1);
                    xor >>= 1;
                }
                
                // Convert to similarity percentage (64 bits total)
                return 100.0 * (64 - differentBits) / 64;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Resizes an image to the specified dimensions.
        /// </summary>
        private Bitmap ResizeImage(Bitmap source, int width, int height)
        {
            var dest = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(dest);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(source, 0, 0, width, height);
            return dest;
        }

        /// <summary>
        /// Generates a thumbnail for an image.
        /// </summary>
        public Bitmap? GenerateThumbnail(string filePath, int maxSize = 150)
        {
            try
            {
                using var original = Image.FromFile(filePath);
                
                double ratio = Math.Min((double)maxSize / original.Width, (double)maxSize / original.Height);
                int newWidth = (int)(original.Width * ratio);
                int newHeight = (int)(original.Height * ratio);
                
                var thumbnail = new Bitmap(newWidth, newHeight);
                using var graphics = Graphics.FromImage(thumbnail);
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                graphics.DrawImage(original, 0, 0, newWidth, newHeight);
                
                return thumbnail;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Collects all image files from the specified paths.
        /// </summary>
        private IEnumerable<string> CollectImageFiles(IEnumerable<string> paths, bool recurse)
        {
            var searchOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            
            foreach (var path in paths)
            {
                if (Directory.Exists(path))
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", searchOption))
                    {
                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (SupportedExtensions.Contains(ext))
                            yield return file;
                    }
                }
                else if (File.Exists(path))
                {
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (SupportedExtensions.Contains(ext))
                        yield return path;
                }
            }
        }
    }
}
