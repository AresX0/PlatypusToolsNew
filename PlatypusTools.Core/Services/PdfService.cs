using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Drawing;

namespace PlatypusTools.Core.Services;

/// <summary>
/// Service for PDF manipulation operations including merge, split, extract, compress, rotate, and convert.
/// </summary>
public class PdfService
{
    /// <summary>
    /// Progress event for tracking PDF operations.
    /// </summary>
    public event EventHandler<PdfProgressEventArgs>? ProgressChanged;

    /// <summary>
    /// Merges multiple PDF files into a single PDF document.
    /// </summary>
    /// <param name="inputFiles">List of PDF file paths to merge.</param>
    /// <param name="outputPath">Output path for the merged PDF.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task MergePdfsAsync(IList<string> inputFiles, string outputPath, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var outputDocument = new PdfDocument();
            int totalPages = 0;
            int processedPages = 0;

            // First pass: count total pages
            foreach (var file in inputFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var inputDocument = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                totalPages += inputDocument.PageCount;
            }

            // Second pass: merge pages
            foreach (var file in inputFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var inputDocument = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                
                for (int i = 0; i < inputDocument.PageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    outputDocument.AddPage(inputDocument.Pages[i]);
                    processedPages++;
                    
                    ReportProgress(processedPages, totalPages, $"Merging page {processedPages} of {totalPages}");
                }
            }

            outputDocument.Save(outputPath);
            ReportProgress(totalPages, totalPages, "Merge complete");
        }, cancellationToken);
    }

    /// <summary>
    /// Splits a PDF file into individual pages or ranges.
    /// </summary>
    /// <param name="inputPath">Input PDF file path.</param>
    /// <param name="outputDirectory">Output directory for split PDFs.</param>
    /// <param name="pageRanges">Optional page ranges (e.g., "1-3,5,7-10"). If null, splits into individual pages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of output file paths.</returns>
    public async Task<List<string>> SplitPdfAsync(string inputPath, string outputDirectory, string? pageRanges = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var outputFiles = new List<string>();
            using var inputDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            
            Directory.CreateDirectory(outputDirectory);
            string baseName = Path.GetFileNameWithoutExtension(inputPath);

            if (string.IsNullOrWhiteSpace(pageRanges))
            {
                // Split into individual pages
                for (int i = 0; i < inputDocument.PageCount; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    using var outputDocument = new PdfDocument();
                    outputDocument.AddPage(inputDocument.Pages[i]);
                    
                    string outputPath = Path.Combine(outputDirectory, $"{baseName}_page{i + 1}.pdf");
                    outputDocument.Save(outputPath);
                    outputFiles.Add(outputPath);
                    
                    ReportProgress(i + 1, inputDocument.PageCount, $"Extracting page {i + 1} of {inputDocument.PageCount}");
                }
            }
            else
            {
                // Split by ranges
                var ranges = ParsePageRanges(pageRanges, inputDocument.PageCount);
                int rangeIndex = 1;
                
                foreach (var range in ranges)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    using var outputDocument = new PdfDocument();
                    for (int i = range.Start; i <= range.End; i++)
                    {
                        outputDocument.AddPage(inputDocument.Pages[i - 1]); // Convert to 0-based
                    }
                    
                    string outputPath = Path.Combine(outputDirectory, $"{baseName}_pages{range.Start}-{range.End}.pdf");
                    outputDocument.Save(outputPath);
                    outputFiles.Add(outputPath);
                    
                    ReportProgress(rangeIndex++, ranges.Count, $"Creating range {range.Start}-{range.End}");
                }
            }

            return outputFiles;
        }, cancellationToken);
    }

    /// <summary>
    /// Extracts specific pages from a PDF file.
    /// </summary>
    /// <param name="inputPath">Input PDF file path.</param>
    /// <param name="outputPath">Output PDF file path.</param>
    /// <param name="pageNumbers">List of page numbers to extract (1-based).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ExtractPagesAsync(string inputPath, string outputPath, IList<int> pageNumbers, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var inputDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();
            
            int processed = 0;
            foreach (var pageNum in pageNumbers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (pageNum >= 1 && pageNum <= inputDocument.PageCount)
                {
                    outputDocument.AddPage(inputDocument.Pages[pageNum - 1]);
                }
                processed++;
                ReportProgress(processed, pageNumbers.Count, $"Extracting page {pageNum}");
            }
            
            outputDocument.Save(outputPath);
        }, cancellationToken);
    }

    /// <summary>
    /// Rotates pages in a PDF file.
    /// </summary>
    /// <param name="inputPath">Input PDF file path.</param>
    /// <param name="outputPath">Output PDF file path.</param>
    /// <param name="rotationDegrees">Rotation in degrees (90, 180, 270).</param>
    /// <param name="pageNumbers">Page numbers to rotate (1-based). If null, rotates all pages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RotatePagesAsync(string inputPath, string outputPath, int rotationDegrees, IList<int>? pageNumbers = null, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
            
            var pagesToRotate = pageNumbers ?? Enumerable.Range(1, document.PageCount).ToList();
            int processed = 0;
            
            foreach (var pageNum in pagesToRotate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (pageNum >= 1 && pageNum <= document.PageCount)
                {
                    var page = document.Pages[pageNum - 1];
                    page.Rotate = (page.Rotate + rotationDegrees) % 360;
                }
                processed++;
                ReportProgress(processed, pagesToRotate.Count, $"Rotating page {pageNum}");
            }
            
            document.Save(outputPath);
        }, cancellationToken);
    }

    /// <summary>
    /// Gets information about a PDF file.
    /// </summary>
    /// <param name="filePath">PDF file path.</param>
    /// <returns>PDF information.</returns>
    public PdfInfo GetPdfInfo(string filePath)
    {
        using var document = PdfReader.Open(filePath, PdfDocumentOpenMode.ReadOnly);
        
        return new PdfInfo
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            PageCount = document.PageCount,
            Title = document.Info.Title ?? string.Empty,
            Author = document.Info.Author ?? string.Empty,
            Subject = document.Info.Subject ?? string.Empty,
            Creator = document.Info.Creator ?? string.Empty,
            CreationDate = document.Info.CreationDate,
            ModificationDate = document.Info.ModificationDate,
            FileSize = new FileInfo(filePath).Length
        };
    }

    /// <summary>
    /// Adds a text watermark to a PDF file.
    /// </summary>
    /// <param name="inputPath">Input PDF file path.</param>
    /// <param name="outputPath">Output PDF file path.</param>
    /// <param name="watermarkText">Watermark text.</param>
    /// <param name="options">Watermark options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task AddWatermarkAsync(string inputPath, string outputPath, string watermarkText, PdfWatermarkOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new PdfWatermarkOptions();
        
        await Task.Run(() =>
        {
            using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);
            
            for (int i = 0; i < document.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var page = document.Pages[i];
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);
                
                var font = new XFont("Arial", options.FontSize);
                var brush = new XSolidBrush(XColor.FromArgb((int)(options.Opacity * 255), 128, 128, 128));
                
                var size = gfx.MeasureString(watermarkText, font);
                double x = (page.Width.Point - size.Width) / 2;
                double y = (page.Height.Point + size.Height) / 2;
                
                // Save state and apply rotation
                var state = gfx.Save();
                gfx.TranslateTransform(page.Width.Point / 2, page.Height.Point / 2);
                gfx.RotateTransform(options.Rotation);
                gfx.TranslateTransform(-page.Width.Point / 2, -page.Height.Point / 2);
                
                gfx.DrawString(watermarkText, font, brush, x, y);
                gfx.Restore(state);
                
                ReportProgress(i + 1, document.PageCount, $"Adding watermark to page {i + 1}");
            }
            
            document.Save(outputPath);
        }, cancellationToken);
    }

    /// <summary>
    /// Converts images to a PDF file.
    /// </summary>
    /// <param name="imagePaths">List of image file paths.</param>
    /// <param name="outputPath">Output PDF file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ImagesToPdfAsync(IList<string> imagePaths, string outputPath, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var document = new PdfDocument();
            int processed = 0;
            
            foreach (var imagePath in imagePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                var page = document.AddPage();
                using var gfx = XGraphics.FromPdfPage(page);
                
                try
                {
                    using var image = XImage.FromFile(imagePath);
                    
                    // Adjust page size to image aspect ratio
                    double ratio = image.PixelWidth / (double)image.PixelHeight;
                    if (ratio > 1)
                    {
                        page.Width = XUnit.FromPoint(792); // Letter width
                        page.Height = XUnit.FromPoint(792 / ratio);
                    }
                    else
                    {
                        page.Height = XUnit.FromPoint(792);
                        page.Width = XUnit.FromPoint(792 * ratio);
                    }
                    
                    gfx.DrawImage(image, 0, 0, page.Width, page.Height);
                }
                catch (Exception ex)
                {
                    // Skip problematic images but log
                    System.Diagnostics.Debug.WriteLine($"Failed to add image {imagePath}: {ex.Message}");
                }
                
                processed++;
                ReportProgress(processed, imagePaths.Count, $"Converting image {processed} of {imagePaths.Count}");
            }
            
            document.Save(outputPath);
        }, cancellationToken);
    }

    /// <summary>
    /// Deletes specific pages from a PDF file.
    /// </summary>
    /// <param name="inputPath">Input PDF file path.</param>
    /// <param name="outputPath">Output PDF file path.</param>
    /// <param name="pageNumbers">Page numbers to delete (1-based).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeletePagesAsync(string inputPath, string outputPath, IList<int> pageNumbers, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var inputDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();
            
            var pagesToDelete = new HashSet<int>(pageNumbers);
            int processed = 0;
            
            for (int i = 0; i < inputDocument.PageCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (!pagesToDelete.Contains(i + 1))
                {
                    outputDocument.AddPage(inputDocument.Pages[i]);
                }
                
                processed++;
                ReportProgress(processed, inputDocument.PageCount, $"Processing page {processed}");
            }
            
            outputDocument.Save(outputPath);
        }, cancellationToken);
    }

    /// <summary>
    /// Reorders pages in a PDF file.
    /// </summary>
    /// <param name="inputPath">Input PDF file path.</param>
    /// <param name="outputPath">Output PDF file path.</param>
    /// <param name="newOrder">New page order (1-based indices).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ReorderPagesAsync(string inputPath, string outputPath, IList<int> newOrder, CancellationToken cancellationToken = default)
    {
        await Task.Run(() =>
        {
            using var inputDocument = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
            using var outputDocument = new PdfDocument();
            
            int processed = 0;
            foreach (var pageNum in newOrder)
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (pageNum >= 1 && pageNum <= inputDocument.PageCount)
                {
                    outputDocument.AddPage(inputDocument.Pages[pageNum - 1]);
                }
                
                processed++;
                ReportProgress(processed, newOrder.Count, $"Reordering page {processed}");
            }
            
            outputDocument.Save(outputPath);
        }, cancellationToken);
    }

    private List<PageRange> ParsePageRanges(string ranges, int maxPages)
    {
        var result = new List<PageRange>();
        var parts = ranges.Split(',', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.Contains('-'))
            {
                var rangeParts = trimmed.Split('-');
                if (rangeParts.Length == 2 &&
                    int.TryParse(rangeParts[0], out int start) &&
                    int.TryParse(rangeParts[1], out int end))
                {
                    start = Math.Max(1, Math.Min(start, maxPages));
                    end = Math.Max(1, Math.Min(end, maxPages));
                    result.Add(new PageRange(start, end));
                }
            }
            else if (int.TryParse(trimmed, out int page))
            {
                page = Math.Max(1, Math.Min(page, maxPages));
                result.Add(new PageRange(page, page));
            }
        }
        
        return result;
    }

    private void ReportProgress(int current, int total, string message)
    {
        double percentage = total > 0 ? (double)current / total * 100 : 0;
        ProgressChanged?.Invoke(this, new PdfProgressEventArgs(current, total, percentage, message));
    }

    private record PageRange(int Start, int End);
}

/// <summary>
/// Progress event arguments for PDF operations.
/// </summary>
public class PdfProgressEventArgs : EventArgs
{
    public int CurrentItem { get; }
    public int TotalItems { get; }
    public double ProgressPercentage { get; }
    public string Message { get; }

    public PdfProgressEventArgs(int currentItem, int totalItems, double percentage, string message)
    {
        CurrentItem = currentItem;
        TotalItems = totalItems;
        ProgressPercentage = percentage;
        Message = message;
    }
}

/// <summary>
/// Information about a PDF file.
/// </summary>
public class PdfInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public int PageCount { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Creator { get; set; } = string.Empty;
    public DateTime CreationDate { get; set; }
    public DateTime ModificationDate { get; set; }
    public long FileSize { get; set; }
}

/// <summary>
/// Options for PDF watermark.
/// </summary>
public class PdfWatermarkOptions
{
    public double FontSize { get; set; } = 48;
    public double Opacity { get; set; } = 0.3;
    public double Rotation { get; set; } = -45;
}
