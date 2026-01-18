using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services;

/// <summary>
/// Service for adding watermarks to images and videos.
/// </summary>
public class WatermarkService
{
    /// <summary>
    /// Progress event for tracking watermark operations.
    /// </summary>
    public event EventHandler<WatermarkProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Adds a text watermark to an image.
    /// </summary>
    /// <param name="inputPath">Input image path.</param>
    /// <param name="outputPath">Output image path.</param>
    /// <param name="options">Watermark options.</param>
    public void AddTextWatermark(string inputPath, string outputPath, TextWatermarkOptions options)
    {
        using var image = Image.FromFile(inputPath);
        using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        
        // Draw original image
        graphics.DrawImage(image, 0, 0, image.Width, image.Height);
        
        // Configure text rendering
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        
        using var font = new Font(options.FontFamily, options.FontSize, options.FontStyle);
        var textSize = graphics.MeasureString(options.Text, font);
        
        // Calculate position
        var position = CalculatePosition(image.Width, image.Height, textSize.Width, textSize.Height, options.Position, options.OffsetX, options.OffsetY);
        
        // Create brush with opacity
        using var brush = new SolidBrush(Color.FromArgb((int)(options.Opacity * 255), options.TextColor));
        
        // Apply rotation if needed
        if (options.Rotation != 0)
        {
            graphics.TranslateTransform(position.X + textSize.Width / 2, position.Y + textSize.Height / 2);
            graphics.RotateTransform(options.Rotation);
            graphics.TranslateTransform(-(position.X + textSize.Width / 2), -(position.Y + textSize.Height / 2));
        }
        
        // Draw text
        graphics.DrawString(options.Text, font, brush, position);
        
        // Save with appropriate format
        SaveImage(bitmap, outputPath, GetImageFormat(inputPath));
    }

    /// <summary>
    /// Adds an image watermark to an image.
    /// </summary>
    /// <param name="inputPath">Input image path.</param>
    /// <param name="outputPath">Output image path.</param>
    /// <param name="watermarkPath">Watermark image path (supports transparency).</param>
    /// <param name="options">Watermark options.</param>
    public void AddImageWatermark(string inputPath, string outputPath, string watermarkPath, ImageWatermarkOptions options)
    {
        using var image = Image.FromFile(inputPath);
        using var watermark = Image.FromFile(watermarkPath);
        using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        
        // Draw original image
        graphics.DrawImage(image, 0, 0, image.Width, image.Height);
        
        // Calculate watermark size
        int wmWidth = (int)(watermark.Width * options.Scale);
        int wmHeight = (int)(watermark.Height * options.Scale);
        
        // Calculate position
        var position = CalculatePosition(image.Width, image.Height, wmWidth, wmHeight, options.Position, options.OffsetX, options.OffsetY);
        
        // Create color matrix for opacity
        var colorMatrix = new ColorMatrix
        {
            Matrix33 = (float)options.Opacity
        };
        
        using var imageAttributes = new ImageAttributes();
        imageAttributes.SetColorMatrix(colorMatrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        
        // Draw watermark with opacity
        graphics.DrawImage(watermark, 
            new Rectangle((int)position.X, (int)position.Y, wmWidth, wmHeight),
            0, 0, watermark.Width, watermark.Height,
            GraphicsUnit.Pixel, imageAttributes);
        
        // Save with appropriate format
        SaveImage(bitmap, outputPath, GetImageFormat(inputPath));
    }

    /// <summary>
    /// Adds a tiled watermark pattern to an image.
    /// </summary>
    /// <param name="inputPath">Input image path.</param>
    /// <param name="outputPath">Output image path.</param>
    /// <param name="options">Watermark options.</param>
    public void AddTiledWatermark(string inputPath, string outputPath, TextWatermarkOptions options)
    {
        using var image = Image.FromFile(inputPath);
        using var bitmap = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        
        // Draw original image
        graphics.DrawImage(image, 0, 0, image.Width, image.Height);
        
        // Configure text rendering
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        
        using var font = new Font(options.FontFamily, options.FontSize, options.FontStyle);
        using var brush = new SolidBrush(Color.FromArgb((int)(options.Opacity * 255), options.TextColor));
        
        var textSize = graphics.MeasureString(options.Text, font);
        
        // Tile the watermark across the image
        int spacingX = (int)(textSize.Width * 1.5);
        int spacingY = (int)(textSize.Height * 2);
        
        for (int y = -spacingY; y < image.Height + spacingY; y += spacingY)
        {
            for (int x = -spacingX; x < image.Width + spacingX; x += spacingX)
            {
                var state = graphics.Save();
                graphics.TranslateTransform(x + textSize.Width / 2, y + textSize.Height / 2);
                graphics.RotateTransform(options.Rotation);
                graphics.TranslateTransform(-(textSize.Width / 2), -(textSize.Height / 2));
                graphics.DrawString(options.Text, font, brush, 0, 0);
                graphics.Restore(state);
            }
        }
        
        SaveImage(bitmap, outputPath, GetImageFormat(inputPath));
    }

    /// <summary>
    /// Batch processes multiple images with a text watermark.
    /// </summary>
    /// <param name="inputFiles">List of input image paths.</param>
    /// <param name="outputDirectory">Output directory.</param>
    /// <param name="options">Watermark options.</param>
    /// <param name="nameSuffix">Suffix to add to output filenames.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of output file paths.</returns>
    public async Task<List<string>> BatchAddTextWatermarkAsync(
        IList<string> inputFiles, 
        string outputDirectory, 
        TextWatermarkOptions options,
        string nameSuffix = "_watermarked",
        CancellationToken cancellationToken = default)
    {
        var outputFiles = new List<string>();
        Directory.CreateDirectory(outputDirectory);
        
        int processed = 0;
        int total = inputFiles.Count;
        
        await Task.Run(() =>
        {
            foreach (var inputFile in inputFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(inputFile);
                    string extension = Path.GetExtension(inputFile);
                    string outputPath = Path.Combine(outputDirectory, $"{baseName}{nameSuffix}{extension}");
                    
                    if (options.Position == WatermarkPosition.Tiled)
                    {
                        AddTiledWatermark(inputFile, outputPath, options);
                    }
                    else
                    {
                        AddTextWatermark(inputFile, outputPath, options);
                    }
                    
                    outputFiles.Add(outputPath);
                    processed++;
                    ReportProgress(processed, total, $"Processed {Path.GetFileName(inputFile)}");
                }
                catch (Exception ex)
                {
                    processed++;
                    ReportProgress(processed, total, $"Failed: {Path.GetFileName(inputFile)} - {ex.Message}");
                }
            }
        }, cancellationToken);
        
        return outputFiles;
    }

    /// <summary>
    /// Batch processes multiple images with an image watermark.
    /// </summary>
    public async Task<List<string>> BatchAddImageWatermarkAsync(
        IList<string> inputFiles,
        string outputDirectory,
        string watermarkPath,
        ImageWatermarkOptions options,
        string nameSuffix = "_watermarked",
        CancellationToken cancellationToken = default)
    {
        var outputFiles = new List<string>();
        Directory.CreateDirectory(outputDirectory);
        
        int processed = 0;
        int total = inputFiles.Count;
        
        await Task.Run(() =>
        {
            foreach (var inputFile in inputFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                try
                {
                    string baseName = Path.GetFileNameWithoutExtension(inputFile);
                    string extension = Path.GetExtension(inputFile);
                    string outputPath = Path.Combine(outputDirectory, $"{baseName}{nameSuffix}{extension}");
                    
                    AddImageWatermark(inputFile, outputPath, watermarkPath, options);
                    
                    outputFiles.Add(outputPath);
                    processed++;
                    ReportProgress(processed, total, $"Processed {Path.GetFileName(inputFile)}");
                }
                catch (Exception ex)
                {
                    processed++;
                    ReportProgress(processed, total, $"Failed: {Path.GetFileName(inputFile)} - {ex.Message}");
                }
            }
        }, cancellationToken);
        
        return outputFiles;
    }

    private PointF CalculatePosition(int imageWidth, int imageHeight, float wmWidth, float wmHeight, WatermarkPosition position, int offsetX, int offsetY)
    {
        float x = 0, y = 0;
        int margin = 20;
        
        switch (position)
        {
            case WatermarkPosition.TopLeft:
                x = margin + offsetX;
                y = margin + offsetY;
                break;
            case WatermarkPosition.TopCenter:
                x = (imageWidth - wmWidth) / 2 + offsetX;
                y = margin + offsetY;
                break;
            case WatermarkPosition.TopRight:
                x = imageWidth - wmWidth - margin + offsetX;
                y = margin + offsetY;
                break;
            case WatermarkPosition.MiddleLeft:
                x = margin + offsetX;
                y = (imageHeight - wmHeight) / 2 + offsetY;
                break;
            case WatermarkPosition.Center:
                x = (imageWidth - wmWidth) / 2 + offsetX;
                y = (imageHeight - wmHeight) / 2 + offsetY;
                break;
            case WatermarkPosition.MiddleRight:
                x = imageWidth - wmWidth - margin + offsetX;
                y = (imageHeight - wmHeight) / 2 + offsetY;
                break;
            case WatermarkPosition.BottomLeft:
                x = margin + offsetX;
                y = imageHeight - wmHeight - margin + offsetY;
                break;
            case WatermarkPosition.BottomCenter:
                x = (imageWidth - wmWidth) / 2 + offsetX;
                y = imageHeight - wmHeight - margin + offsetY;
                break;
            case WatermarkPosition.BottomRight:
                x = imageWidth - wmWidth - margin + offsetX;
                y = imageHeight - wmHeight - margin + offsetY;
                break;
        }
        
        return new PointF(x, y);
    }

    private void SaveImage(Bitmap bitmap, string outputPath, ImageFormat format)
    {
        // Ensure directory exists
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        
        if (format.Equals(ImageFormat.Jpeg))
        {
            // Use high quality JPEG encoding
            var encoder = GetEncoder(ImageFormat.Jpeg);
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
            bitmap.Save(outputPath, encoder, encoderParams);
        }
        else
        {
            bitmap.Save(outputPath, format);
        }
    }

    private ImageCodecInfo GetEncoder(ImageFormat format)
    {
        var codecs = ImageCodecInfo.GetImageEncoders();
        return codecs.First(c => c.FormatID == format.Guid);
    }

    private ImageFormat GetImageFormat(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => ImageFormat.Jpeg,
            ".png" => ImageFormat.Png,
            ".bmp" => ImageFormat.Bmp,
            ".gif" => ImageFormat.Gif,
            ".tiff" or ".tif" => ImageFormat.Tiff,
            _ => ImageFormat.Png
        };
    }

    private void ReportProgress(int current, int total, string message)
    {
        double percentage = total > 0 ? (double)current / total * 100 : 0;
        ProgressChanged?.Invoke(this, new WatermarkProgressEventArgs(current, total, percentage, message));
    }
}

/// <summary>
/// Text watermark options.
/// </summary>
public class TextWatermarkOptions
{
    public string Text { get; set; } = "Watermark";
    public string FontFamily { get; set; } = "Arial";
    public float FontSize { get; set; } = 24;
    public FontStyle FontStyle { get; set; } = FontStyle.Bold;
    public Color TextColor { get; set; } = Color.White;
    public double Opacity { get; set; } = 0.5;
    public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;
    public float Rotation { get; set; } = 0;
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 0;
}

/// <summary>
/// Image watermark options.
/// </summary>
public class ImageWatermarkOptions
{
    public double Opacity { get; set; } = 0.5;
    public double Scale { get; set; } = 1.0;
    public WatermarkPosition Position { get; set; } = WatermarkPosition.BottomRight;
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 0;
}

/// <summary>
/// Watermark position options.
/// </summary>
public enum WatermarkPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    Tiled
}

/// <summary>
/// Progress event arguments for watermark operations.
/// </summary>
public class WatermarkProgressEventArgs : EventArgs
{
    public int CurrentItem { get; }
    public int TotalItems { get; }
    public double ProgressPercentage { get; }
    public string Message { get; }

    public WatermarkProgressEventArgs(int currentItem, int totalItems, double percentage, string message)
    {
        CurrentItem = currentItem;
        TotalItems = totalItems;
        ProgressPercentage = percentage;
        Message = message;
    }
}
