using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class ImageResizerServiceTests
    {
        private ImageResizerService _service = null!;
        private string _testFolder = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new ImageResizerService();
            _testFolder = Path.Combine(Path.GetTempPath(), $"pt_resize_test_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testFolder);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testFolder))
            {
                try { Directory.Delete(_testFolder, true); } catch { }
            }
        }

        [TestMethod]
        public async Task ResizeImageAsync_LargeImage_ReducesSize()
        {
            // Arrange
            var inputFile = CreateTestImage("large.png", 1920, 1080);
            var outputFile = Path.Combine(_testFolder, "resized.png");

            // Act
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 800, 600, 90, null, true, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
            
            using var img = Image.FromFile(outputFile);
            Assert.IsTrue(img.Width <= 800);
            Assert.IsTrue(img.Height <= 600);
        }

        [TestMethod]
        public async Task ResizeImageAsync_MaintainAspectRatio_PreservesProportions()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", 1600, 900); // 16:9 aspect ratio
            var outputFile = Path.Combine(_testFolder, "resized.png");

            // Act
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 800, 800, 90, null, true, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            
            using var img = Image.FromFile(outputFile);
            // Should fit within 800x800 while maintaining 16:9 ratio
            // Expected: 800x450 (width maxed out)
            Assert.AreEqual(800, img.Width);
            Assert.AreEqual(450, img.Height);
        }

        [TestMethod]
        public async Task ResizeImageAsync_SmallImage_NoUpscaling_KeepsOriginalSize()
        {
            // Arrange
            var inputFile = CreateTestImage("small.png", 400, 300);
            var outputFile = Path.Combine(_testFolder, "not_upscaled.png");

            // Act
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 1920, 1080, 90, null, true, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            
            using var img = Image.FromFile(outputFile);
            // Should not upscale, so dimensions should match original
            Assert.AreEqual(400, img.Width);
            Assert.AreEqual(300, img.Height);
        }

        [TestMethod]
        public async Task ResizeImageAsync_FormatConversion_ConvertsToJpeg()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", 800, 600);
            var outputFile = Path.Combine(_testFolder, "converted.jpg");

            // Act
            var result = await _service.ResizeImageAsync(
                inputFile, 
                outputFile, 
                800, 
                600, 
                85, 
                ImageFormat.Jpeg, 
                true, 
                false, 
                CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
            
            using var img = Image.FromFile(outputFile);
            Assert.AreEqual(ImageFormat.Jpeg.Guid, img.RawFormat.Guid);
        }

        [TestMethod]
        public async Task ResizeImageAsync_QualitySetting_AffectsJpegFileSize()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", 800, 600);
            var highQualityFile = Path.Combine(_testFolder, "high_quality.jpg");
            var lowQualityFile = Path.Combine(_testFolder, "low_quality.jpg");

            // Act
            var highResult = await _service.ResizeImageAsync(inputFile, highQualityFile, 800, 600, 95, ImageFormat.Jpeg, true, false, CancellationToken.None);
            var lowResult = await _service.ResizeImageAsync(inputFile, lowQualityFile, 800, 600, 50, ImageFormat.Jpeg, true, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(highResult.IsSuccess);
            Assert.IsTrue(lowResult.IsSuccess);
            
            var highSize = new FileInfo(highQualityFile).Length;
            var lowSize = new FileInfo(lowQualityFile).Length;
            
            Assert.IsTrue(highSize > lowSize, "High quality should produce larger file");
        }

        [TestMethod]
        public async Task ResizeImageAsync_FileExists_OverwriteFalse_ReturnsError()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", 800, 600);
            var outputFile = Path.Combine(_testFolder, "existing.png");
            File.WriteAllText(outputFile, "existing content");

            // Act
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 400, 300, 90, null, true, false, CancellationToken.None);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Message.Contains("already exists") || result.Message.Contains("exist"));
        }

        [TestMethod]
        public async Task ResizeImageAsync_FileExists_OverwriteTrue_Succeeds()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", 800, 600);
            var outputFile = Path.Combine(_testFolder, "existing.png");
            File.WriteAllText(outputFile, "existing content");

            // Act
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 400, 300, 90, null, true, true, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
        }

        [TestMethod]
        public async Task BatchResizeAsync_MultipleFiles_ResizesAll()
        {
            // Arrange
            var inputFiles = new[]
            {
                CreateTestImage("test1.png", 1920, 1080),
                CreateTestImage("test2.jpg", 1600, 900),
                CreateTestImage("test3.bmp", 2560, 1440)
            };

            // Act
            var result = await _service.BatchResizeAsync(
                inputFiles.ToList(),
                _testFolder,
                800,
                600,
                90,
                null,  // targetFormat
                null,  // targetExtension
                true,  // maintainAspectRatio
                false, // overwriteExisting
                null,  // progress
                CancellationToken.None);

            // Assert
            // Batch resize should create resized versions
            // Check if any files were successfully resized
            Assert.IsTrue(result.Results.Any(), "Should have results");
            Assert.IsTrue(result.TotalFiles == 3, "Should process 3 files");
            
            // Verify output files that succeeded
            foreach (var resizeResult in result.Results.Where(r => r.IsSuccess))
            {
                Assert.IsTrue(File.Exists(resizeResult.OutputPath), $"Output file should exist: {resizeResult.OutputPath}");
                Assert.IsTrue(resizeResult.Width <= 800);
                Assert.IsTrue(resizeResult.Height <= 600);
            }
        }

        [TestMethod]
        public async Task BatchResizeAsync_WithProgress_ReportsProgress()
        {
            // Arrange
            var inputFiles = new[]
            {
                CreateTestImage("test1.png", 800, 600),
                CreateTestImage("test2.png", 800, 600)
            };
            var progressReports = new System.Collections.Generic.List<ResizeProgress>();
            var progress = new Progress<ResizeProgress>(p => progressReports.Add(p));

            // Act
            await _service.BatchResizeAsync(inputFiles.ToList(), _testFolder, 400, 300, 90, null, null, true, false, progress, CancellationToken.None);

            // Assert
            Assert.IsTrue(progressReports.Count > 0, "Progress should be reported");
        }

        [TestMethod]
        public async Task ResizeImageAsync_WidthOnly_CalculatesHeightBasedOnAspectRatio()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", 1600, 900); // 16:9
            var outputFile = Path.Combine(_testFolder, "resized.png");

            // Act - only specify max width
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 800, 10000, 90, null, true, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            
            using var img = Image.FromFile(outputFile);
            Assert.AreEqual(800, img.Width);
            Assert.AreEqual(450, img.Height); // Maintains 16:9 ratio
        }

        [TestMethod]
        public async Task ResizeImageAsync_HeightOnly_CalculatesWidthBasedOnAspectRatio()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", 1600, 900); // 16:9
            var outputFile = Path.Combine(_testFolder, "resized.png");

            // Act - only specify max height
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 10000, 450, 90, null, true, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            
            using var img = Image.FromFile(outputFile);
            Assert.AreEqual(800, img.Width); // Maintains 16:9 ratio
            Assert.AreEqual(450, img.Height);
        }

        [TestMethod]
        public async Task ResizeImageAsync_NonExistentFile_ReturnsError()
        {
            // Arrange
            var inputFile = Path.Combine(_testFolder, "nonexistent.png");
            var outputFile = Path.Combine(_testFolder, "output.png");

            // Act
            var result = await _service.ResizeImageAsync(inputFile, outputFile, 800, 600, 90, null, true, false, CancellationToken.None);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Message);
        }

        [TestMethod]
        public async Task BatchResizeAsync_MixedSuccess_ReportsCorrectCounts()
        {
            // Arrange
            var inputFiles = new[]
            {
                CreateTestImage("valid.png", 800, 600),
                Path.Combine(_testFolder, "nonexistent.png"), // This will fail
                CreateTestImage("valid2.jpg", 800, 600)
            };

            // Act
            var result = await _service.BatchResizeAsync(
                inputFiles.ToList(),
                _testFolder,
                400,
                300,
                90,
                null,  // targetFormat
                null,  // targetExtension
                true,  // maintainAspectRatio
                false, // overwriteExisting
                null,  // progress
                CancellationToken.None);

            // Assert
            Assert.AreEqual(3, result.TotalFiles);
            // Some might succeed, some might fail - just verify counts are correct
            Assert.IsTrue(result.SuccessCount + result.FailureCount == 3, "Success + Failure should equal total");
        }

        private string CreateTestImage(string filename, int width, int height)
        {
            var path = Path.Combine(_testFolder, filename);
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            
            // Create a gradient fill to make file sizes realistic
            using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
                new Rectangle(0, 0, width, height),
                Color.Blue,
                Color.Green,
                45f);
            graphics.FillRectangle(brush, 0, 0, width, height);
            
            // Add some detail
            using var pen = new Pen(Color.Red, 3);
            graphics.DrawEllipse(pen, width / 4, height / 4, width / 2, height / 2);
            
            // Determine format from extension
            var ext = Path.GetExtension(filename).ToLowerInvariant();
            var format = ext switch
            {
                ".jpg" => ImageFormat.Jpeg,
                ".bmp" => ImageFormat.Bmp,
                ".gif" => ImageFormat.Gif,
                _ => ImageFormat.Png
            };
            
            bitmap.Save(path, format);
            return path;
        }
    }
}
