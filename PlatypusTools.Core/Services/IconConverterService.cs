using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services;

[SupportedOSPlatform("windows")]
public class IconConverterService
{
    /// <summary>
    /// Converts an image file to ICO format with specified icon size
    /// </summary>
    public async Task<ConversionResult> ConvertToIcoAsync(
        string inputPath,
        string outputPath,
        int iconSize = 256,
        bool overwriteExisting = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                return ConversionResult.Failure($"Input file not found: {inputPath}");
            }

            if (File.Exists(outputPath) && !overwriteExisting)
            {
                return ConversionResult.Failure($"Output file already exists: {outputPath}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Load source image
            using var sourceImage = await Task.Run(() => Image.FromFile(inputPath), cancellationToken);
            
            // Resize to target icon size maintaining aspect ratio
            using var resizedImage = ResizeImage(sourceImage, iconSize, iconSize);
            
            cancellationToken.ThrowIfCancellationRequested();

            // Save as ICO
            await Task.Run(() =>
            {
                using var stream = new FileStream(outputPath, FileMode.Create);
                using var icon = Icon.FromHandle(((Bitmap)resizedImage).GetHicon());
                icon.Save(stream);
            }, cancellationToken);

            return ConversionResult.Success(outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ConversionResult.Failure($"Error converting {Path.GetFileName(inputPath)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts an image file from one format to another
    /// </summary>
    public async Task<ConversionResult> ConvertFormatAsync(
        string inputPath,
        string outputPath,
        ImageFormat targetFormat,
        bool overwriteExisting = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!File.Exists(inputPath))
            {
                return ConversionResult.Failure($"Input file not found: {inputPath}");
            }

            if (File.Exists(outputPath) && !overwriteExisting)
            {
                return ConversionResult.Failure($"Output file already exists: {outputPath}");
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Load and convert
            using var image = await Task.Run(() => Image.FromFile(inputPath), cancellationToken);
            
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Run(() => image.Save(outputPath, targetFormat), cancellationToken);

            return ConversionResult.Success(outputPath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ConversionResult.Failure($"Error converting {Path.GetFileName(inputPath)}: {ex.Message}");
        }
    }

    /// <summary>
    /// Batch convert multiple images to ICO format
    /// </summary>
    public async Task<BatchConversionResult> BatchConvertToIcoAsync(
        IEnumerable<string> inputPaths,
        string outputFolder,
        int iconSize = 256,
        bool overwriteExisting = false,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ConversionResult>();
        var inputList = inputPaths.ToList();
        int totalFiles = inputList.Count;
        int processedFiles = 0;

        foreach (var inputPath in inputList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            var outputPath = Path.Combine(outputFolder, $"{fileName}.ico");

            progress?.Report(new ConversionProgress
            {
                Current = processedFiles,
                Total = totalFiles,
                CurrentFile = Path.GetFileName(inputPath),
                Message = $"Converting {processedFiles + 1} of {totalFiles}..."
            });

            var result = await ConvertToIcoAsync(inputPath, outputPath, iconSize, overwriteExisting, cancellationToken);
            results.Add(result);

            processedFiles++;
        }

        progress?.Report(new ConversionProgress
        {
            Current = totalFiles,
            Total = totalFiles,
            CurrentFile = string.Empty,
            Message = $"Completed {results.Count(r => r.IsSuccess)} of {totalFiles} conversions"
        });

        return new BatchConversionResult
        {
            Results = results,
            TotalFiles = totalFiles,
            SuccessCount = results.Count(r => r.IsSuccess),
            FailureCount = results.Count(r => !r.IsSuccess)
        };
    }

    /// <summary>
    /// Batch convert multiple images to a different format
    /// </summary>
    public async Task<BatchConversionResult> BatchConvertFormatAsync(
        IEnumerable<string> inputPaths,
        string outputFolder,
        ImageFormat targetFormat,
        string targetExtension,
        bool overwriteExisting = false,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ConversionResult>();
        var inputList = inputPaths.ToList();
        int totalFiles = inputList.Count;
        int processedFiles = 0;

        foreach (var inputPath in inputList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileName = Path.GetFileNameWithoutExtension(inputPath);
            var outputPath = Path.Combine(outputFolder, $"{fileName}.{targetExtension}");

            progress?.Report(new ConversionProgress
            {
                Current = processedFiles,
                Total = totalFiles,
                CurrentFile = Path.GetFileName(inputPath),
                Message = $"Converting {processedFiles + 1} of {totalFiles}..."
            });

            var result = await ConvertFormatAsync(inputPath, outputPath, targetFormat, overwriteExisting, cancellationToken);
            results.Add(result);

            processedFiles++;
        }

        progress?.Report(new ConversionProgress
        {
            Current = totalFiles,
            Total = totalFiles,
            CurrentFile = string.Empty,
            Message = $"Completed {results.Count(r => r.IsSuccess)} of {totalFiles} conversions"
        });

        return new BatchConversionResult
        {
            Results = results,
            TotalFiles = totalFiles,
            SuccessCount = results.Count(r => r.IsSuccess),
            FailureCount = results.Count(r => !r.IsSuccess)
        };
    }

    private static Image ResizeImage(Image image, int maxWidth, int maxHeight)
    {
        // Calculate new dimensions maintaining aspect ratio
        int newWidth = image.Width;
        int newHeight = image.Height;

        double ratioX = (double)maxWidth / image.Width;
        double ratioY = (double)maxHeight / image.Height;
        double ratio = Math.Min(ratioX, ratioY);

        newWidth = (int)(image.Width * ratio);
        newHeight = (int)(image.Height * ratio);

        var newImage = new Bitmap(newWidth, newHeight);
        using (var graphics = Graphics.FromImage(newImage))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(image, 0, 0, newWidth, newHeight);
        }

        return newImage;
    }
}

public class ConversionResult
{
    public bool IsSuccess { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? OutputPath { get; init; }

    public static ConversionResult Success(string outputPath) => new()
    {
        IsSuccess = true,
        Message = "Conversion successful",
        OutputPath = outputPath
    };

    public static ConversionResult Failure(string message) => new()
    {
        IsSuccess = false,
        Message = message
    };
}

public class BatchConversionResult
{
    public IEnumerable<ConversionResult> Results { get; init; } = Array.Empty<ConversionResult>();
    public int TotalFiles { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
}

public class ConversionProgress
{
    public int Current { get; init; }
    public int Total { get; init; }
    public string CurrentFile { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
}
