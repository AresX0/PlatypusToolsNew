using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services;

[SupportedOSPlatform("windows")]
public class ImageResizerService
{
    /// <summary>
    /// Resizes an image to fit within maximum dimensions while maintaining aspect ratio
    /// </summary>
    public async Task<ResizeResult> ResizeImageAsync(
        string inputPath,
        string outputPath,
        int maxWidth,
        int maxHeight,
        int quality = 90,
        ImageFormat? targetFormat = null,
        bool maintainAspectRatio = true,
        bool overwriteExisting = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                return ResizeResult.Failure($"Input file not found: {inputPath}");
            }

            if (File.Exists(outputPath) && !overwriteExisting)
            {
                return ResizeResult.Failure($"Output file already exists: {outputPath}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Load source image
            using var sourceImage = await Task.Run(() => Image.FromFile(inputPath), cancellationToken);
            
            // Calculate new dimensions
            var (newWidth, newHeight) = CalculateNewDimensions(
                sourceImage.Width,
                sourceImage.Height,
                maxWidth,
                maxHeight,
                maintainAspectRatio);

            cancellationToken.ThrowIfCancellationRequested();

            // Determine output format
            var outputFormat = targetFormat ?? GetImageFormat(inputPath);

            // Resize image
            using var resizedImage = await Task.Run(() => ResizeImage(sourceImage, newWidth, newHeight), cancellationToken);
            
            cancellationToken.ThrowIfCancellationRequested();

            // Save with quality settings for JPEG
            await Task.Run(() =>
            {
                if (outputFormat.Equals(ImageFormat.Jpeg))
                {
                    SaveJpegWithQuality(resizedImage, outputPath, quality);
                }
                else
                {
                    resizedImage.Save(outputPath, outputFormat);
                }
            }, cancellationToken);

            var fileInfo = new FileInfo(outputPath);
            return ResizeResult.Success(outputPath, newWidth, newHeight, fileInfo.Length);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ResizeResult.Failure($"Error resizing {Path.GetFileName(inputPath)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Batch resize multiple images
    /// </summary>
    public async Task<BatchResizeResult> BatchResizeAsync(
        IEnumerable<string> inputPaths,
        string outputFolder,
        int maxWidth,
        int maxHeight,
        int quality = 90,
        ImageFormat? targetFormat = null,
        string? targetExtension = null,
        bool maintainAspectRatio = true,
        bool overwriteExisting = false,
        IProgress<ResizeProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ResizeResult>();
        var inputList = inputPaths.ToList();
        int totalFiles = inputList.Count;
        int processedFiles = 0;

        foreach (var inputPath in inputList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            var extension = targetExtension ?? Path.GetExtension(inputPath);
            var outputPath = Path.Combine(outputFolder, $"{fileName}{extension}");

            progress?.Report(new ResizeProgress
            {
                Current = processedFiles,
                Total = totalFiles,
                CurrentFile = Path.GetFileName(inputPath),
                Message = $"Resizing {processedFiles + 1} of {totalFiles}..."
            });

            var result = await ResizeImageAsync(
                inputPath,
                outputPath,
                maxWidth,
                maxHeight,
                quality,
                targetFormat,
                maintainAspectRatio,
                overwriteExisting,
                cancellationToken);

            results.Add(result);
            processedFiles++;
        }

        progress?.Report(new ResizeProgress
        {
            Current = totalFiles,
            Total = totalFiles,
            CurrentFile = string.Empty,
            Message = $"Completed {results.Count(r => r.IsSuccess)} of {totalFiles} conversions"
        });

        return new BatchResizeResult
        {
            Results = results,
            TotalFiles = totalFiles,
            SuccessCount = results.Count(r => r.IsSuccess),
            FailureCount = results.Count(r => !r.IsSuccess)
        };
    }

    private static (int width, int height) CalculateNewDimensions(
        int originalWidth,
        int originalHeight,
        int maxWidth,
        int maxHeight,
        bool maintainAspectRatio)
    {
        if (!maintainAspectRatio)
        {
            return (maxWidth, maxHeight);
        }

        // If image is already smaller than max dimensions, don't upscale
        if (originalWidth <= maxWidth && originalHeight <= maxHeight)
        {
            return (originalWidth, originalHeight);
        }

        // Calculate scaling ratio
        double ratioX = (double)maxWidth / originalWidth;
        double ratioY = (double)maxHeight / originalHeight;
        double ratio = Math.Min(ratioX, ratioY);

        int newWidth = (int)(originalWidth * ratio);
        int newHeight = (int)(originalHeight * ratio);

        return (newWidth, newHeight);
    }

    private static Image ResizeImage(Image sourceImage, int width, int height)
    {
        var destRect = new Rectangle(0, 0, width, height);
        var destImage = new Bitmap(width, height);

        destImage.SetResolution(sourceImage.HorizontalResolution, sourceImage.VerticalResolution);

        using (var graphics = Graphics.FromImage(destImage))
        {
            graphics.CompositingMode = CompositingMode.SourceCopy;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (var wrapMode = new ImageAttributes())
            {
                wrapMode.SetWrapMode(WrapMode.TileFlipXY);
                graphics.DrawImage(sourceImage, destRect, 0, 0, sourceImage.Width, sourceImage.Height, GraphicsUnit.Pixel, wrapMode);
            }
        }

        return destImage;
    }

    private static void SaveJpegWithQuality(Image image, string path, int quality)
    {
        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);

        var jpegCodec = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

        image.Save(path, jpegCodec, encoderParameters);
    }

    private static ImageFormat GetImageFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".bmp" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".tif" or ".tiff" => ImageFormat.Tiff,
            _ => ImageFormat.Png
        };
    }
}

public class ResizeResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? OutputPath { get; init; }
    public int Width { get; init; }
    public int Height { get; init; }
    public long FileSize { get; init; }

    public static ResizeResult Success(string outputPath, int width, int height, long fileSize) => new()
    {
        IsSuccess = true,
        Message = "Resize successful",
        OutputPath = outputPath,
        Width = width,
        Height = height,
        FileSize = fileSize
    };

    public static ResizeResult Failure(string message) => new()
    {
        IsSuccess = false,
        Message = message
    };
}

public class BatchResizeResult
{
    public IEnumerable<ResizeResult> Results { get; init; } = Array.Empty<ResizeResult>();
    public int TotalFiles { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
}

public class ResizeProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
