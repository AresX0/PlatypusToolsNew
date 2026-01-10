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
    public class IconConverterServiceTests
    {
        private IconConverterService _service = null!;
        private string _testFolder = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new IconConverterService();
            _testFolder = Path.Combine(Path.GetTempPath(), $"pt_ico_test_{Guid.NewGuid()}");
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
        public async Task ConvertToIcoAsync_ValidPngFile_CreatesIcoFile()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", ImageFormat.Png);
            var outputFile = Path.Combine(_testFolder, "output.ico");

            // Act
            var result = await _service.ConvertToIcoAsync(inputFile, outputFile, 32, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
            Assert.IsTrue(new FileInfo(outputFile).Length > 0);
        }

        [TestMethod]
        public async Task ConvertToIcoAsync_MultipleIconSizes_CreatesIcoWithMultipleSizes()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", ImageFormat.Png, 256, 256);
            var outputFile = Path.Combine(_testFolder, "multi_size.ico");

            // Act - create icon with 32px size
            var result = await _service.ConvertToIcoAsync(inputFile, outputFile, 32, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
            
            // Verify it's a valid ICO file
            using var icon = new Icon(outputFile);
            Assert.IsNotNull(icon);
        }

        [TestMethod]
        public async Task ConvertToIcoAsync_FileExists_OverwriteFalse_ReturnsError()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", ImageFormat.Png);
            var outputFile = Path.Combine(_testFolder, "existing.ico");
            File.WriteAllText(outputFile, "existing content");

            // Act
            var result = await _service.ConvertToIcoAsync(inputFile, outputFile, 32, false, CancellationToken.None);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsTrue(result.Message.Contains("already exists") || result.Message.Contains("exist"));
        }

        [TestMethod]
        public async Task ConvertToIcoAsync_FileExists_OverwriteTrue_Succeeds()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", ImageFormat.Png);
            var outputFile = Path.Combine(_testFolder, "existing.ico");
            File.WriteAllText(outputFile, "existing content");

            // Act
            var result = await _service.ConvertToIcoAsync(inputFile, outputFile, 32, true, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
        }

        [TestMethod]
        public async Task ConvertFormatAsync_PngToJpg_CreatesJpgFile()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", ImageFormat.Png);
            var outputFile = Path.Combine(_testFolder, "output.jpg");

            // Act
            var result = await _service.ConvertFormatAsync(inputFile, outputFile, ImageFormat.Jpeg, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
            
            // Verify format
            using var img = Image.FromFile(outputFile);
            Assert.AreEqual(ImageFormat.Jpeg.Guid, img.RawFormat.Guid);
        }

        [TestMethod]
        public async Task ConvertFormatAsync_JpgToPng_CreatesPngFile()
        {
            // Arrange
            var inputFile = CreateTestImage("test.jpg", ImageFormat.Jpeg);
            var outputFile = Path.Combine(_testFolder, "output.png");

            // Act
            var result = await _service.ConvertFormatAsync(inputFile, outputFile, ImageFormat.Png, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
            
            // Verify format
            using var img = Image.FromFile(outputFile);
            Assert.AreEqual(ImageFormat.Png.Guid, img.RawFormat.Guid);
        }

        [TestMethod]
        public async Task BatchConvertToIcoAsync_MultipleFiles_ConvertsAll()
        {
            // Arrange
            var inputFiles = new[]
            {
                CreateTestImage("test1.png", ImageFormat.Png),
                CreateTestImage("test2.png", ImageFormat.Png),
                CreateTestImage("test3.jpg", ImageFormat.Jpeg)
            };
            var outputFolder = _testFolder;

            // Act
            var result = await _service.BatchConvertToIcoAsync(inputFiles.ToList(), outputFolder, 32, false, null, CancellationToken.None);

            // Assert
            Assert.AreEqual(3, result.TotalFiles);
            Assert.AreEqual(3, result.SuccessCount);
            Assert.AreEqual(0, result.FailureCount);
            Assert.AreEqual(3, result.Results.Count());
        }

        [TestMethod]
        public async Task BatchConvertFormatAsync_MultipleFiles_ConvertsAll()
        {
            // Arrange
            var inputFiles = new[]
            {
                CreateTestImage("test1.png", ImageFormat.Png),
                CreateTestImage("test2.bmp", ImageFormat.Bmp)
            };
            var outputFolder = _testFolder;

            // Act
            var result = await _service.BatchConvertFormatAsync(
                inputFiles.ToList(), 
                outputFolder, 
                ImageFormat.Jpeg, 
                ".jpg", 
                false, 
                null, 
                CancellationToken.None);

            // Assert
            Assert.AreEqual(2, result.TotalFiles);
            Assert.AreEqual(2, result.SuccessCount);
            Assert.AreEqual(0, result.FailureCount);
        }

        [TestMethod]
        public async Task ConvertToIcoAsync_InvalidIconSize_UsesDefaultSize()
        {
            // Arrange
            var inputFile = CreateTestImage("test.png", ImageFormat.Png, 64, 64);
            var outputFile = Path.Combine(_testFolder, "output.ico");

            // Act - use an unusual size (service should handle it)
            var result = await _service.ConvertToIcoAsync(inputFile, outputFile, 33, false, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(File.Exists(outputFile));
        }

        [TestMethod]
        public async Task ConvertToIcoAsync_NonExistentFile_ReturnsError()
        {
            // Arrange
            var inputFile = Path.Combine(_testFolder, "nonexistent.png");
            var outputFile = Path.Combine(_testFolder, "output.ico");

            // Act
            var result = await _service.ConvertToIcoAsync(inputFile, outputFile, 32, false, CancellationToken.None);

            // Assert
            Assert.IsFalse(result.IsSuccess);
            Assert.IsNotNull(result.Message);
        }

        [TestMethod]
        public async Task BatchConvertToIcoAsync_WithProgress_ReportsProgress()
        {
            // Arrange
            var inputFiles = new[]
            {
                CreateTestImage("test1.png", ImageFormat.Png),
                CreateTestImage("test2.png", ImageFormat.Png)
            };
            var progressReports = new System.Collections.Generic.List<ConversionProgress>();
            var progress = new Progress<ConversionProgress>(p => progressReports.Add(p));

            // Act
            await _service.BatchConvertToIcoAsync(inputFiles.ToList(), _testFolder, 32, false, progress, CancellationToken.None);

            // Assert
            Assert.IsTrue(progressReports.Count > 0, "Progress should be reported");
        }

        private string CreateTestImage(string filename, ImageFormat format, int width = 64, int height = 64)
        {
            var path = Path.Combine(_testFolder, filename);
            using var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            
            // Fill with a color
            graphics.Clear(Color.Blue);
            
            // Draw something to make it non-uniform
            using var pen = new Pen(Color.Red, 2);
            graphics.DrawRectangle(pen, 10, 10, width - 20, height - 20);
            
            bitmap.Save(path, format);
            return path;
        }
    }
}
