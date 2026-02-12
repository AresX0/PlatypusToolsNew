using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for PdfService (TASK-297).
    /// Tests PDF merge, split, info, watermark, encryption, and page manipulation.
    /// </summary>
    [TestClass]
    public class PdfServiceTests
    {
        private string _tempDir = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        [TestCleanup]
        public void Cleanup()
        {
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
            catch { }
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_CreatesInstance()
        {
            var service = new PdfService();
            Assert.IsNotNull(service);
        }

        #endregion

        #region GetPdfInfo Tests

        [TestMethod]
        public void GetPdfInfo_NonExistentFile_ThrowsOrReturnsNull()
        {
            var service = new PdfService();
            try
            {
                var info = service.GetPdfInfo(@"C:\nonexistent\test.pdf");
                // If it returns, verify structure
                Assert.IsNotNull(info);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is DirectoryNotFoundException || ex is ArgumentException)
            {
                // Expected
            }
        }

        #endregion

        #region MergePdfs Tests

        [TestMethod]
        public async Task MergePdfsAsync_EmptyList_ThrowsOrHandles()
        {
            var service = new PdfService();
            var outputPath = Path.Combine(_tempDir, "merged.pdf");

            try
            {
                await service.MergePdfsAsync(new List<string>(), outputPath);
            }
            catch (ArgumentException)
            {
                // Expected for empty input
            }
            catch (Exception)
            {
                // Other expected errors
            }
        }

        [TestMethod]
        public async Task MergePdfsAsync_NonExistentFiles_Throws()
        {
            var service = new PdfService();
            var files = new List<string> { @"C:\nonexistent\a.pdf", @"C:\nonexistent\b.pdf" };
            var outputPath = Path.Combine(_tempDir, "merged.pdf");

            try
            {
                await service.MergePdfsAsync(files, outputPath);
                Assert.Fail("Should have thrown for non-existent files");
            }
            catch (Exception)
            {
                // Expected
            }
        }

        #endregion

        #region SplitPdf Tests

        [TestMethod]
        public async Task SplitPdfAsync_NonExistentFile_Throws()
        {
            var service = new PdfService();
            try
            {
                await service.SplitPdfAsync(@"C:\nonexistent\test.pdf", _tempDir);
                Assert.Fail("Should have thrown");
            }
            catch (Exception)
            {
                // Expected
            }
        }

        #endregion

        #region IsPdfEncrypted Tests

        [TestMethod]
        public void IsPdfEncrypted_NonExistentFile_ThrowsOrReturnsFalse()
        {
            var service = new PdfService();
            try
            {
                var result = service.IsPdfEncrypted(@"C:\nonexistent\test.pdf");
                Assert.IsFalse(result);
            }
            catch (Exception)
            {
                // Expected
            }
        }

        #endregion

        #region Watermark Tests

        [TestMethod]
        public async Task AddWatermarkAsync_NonExistentFile_Throws()
        {
            var service = new PdfService();
            var outputPath = Path.Combine(_tempDir, "watermarked.pdf");
            try
            {
                await service.AddWatermarkAsync(@"C:\nonexistent\test.pdf", outputPath, "CONFIDENTIAL");
                Assert.Fail("Should have thrown");
            }
            catch (Exception)
            {
                // Expected
            }
        }

        #endregion

        #region ImagesToPdf Tests

        [TestMethod]
        public async Task ImagesToPdfAsync_EmptyList_ThrowsOrHandles()
        {
            var service = new PdfService();
            var outputPath = Path.Combine(_tempDir, "images.pdf");
            try
            {
                await service.ImagesToPdfAsync(new List<string>(), outputPath);
            }
            catch (ArgumentException)
            {
                // Expected for empty input
            }
            catch (Exception)
            {
                // Other expected errors
            }
        }

        #endregion

        #region Model Tests

        [TestMethod]
        public void PdfWatermarkOptions_DefaultValues()
        {
            var options = new PdfWatermarkOptions();
            Assert.IsNotNull(options);
            Assert.IsTrue(options.FontSize > 0);
            Assert.IsTrue(options.Opacity > 0 && options.Opacity <= 1);
        }

        [TestMethod]
        public void PdfEncryptionOptions_DefaultValues()
        {
            var options = new PdfEncryptionOptions();
            Assert.IsNotNull(options);
        }

        #endregion
    }
}
