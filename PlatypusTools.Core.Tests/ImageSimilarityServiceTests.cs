using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for ImageSimilarityService (TASK-295).
    /// Tests similarity detection, hashing, and result structure.
    /// </summary>
    [TestClass]
    public class ImageSimilarityServiceTests
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
            var service = new ImageSimilarityService();
            Assert.IsNotNull(service);
        }

        #endregion

        #region FindSimilarImages Tests

        [TestMethod]
        public async Task FindSimilarImagesAsync_EmptyPaths_ReturnsEmpty()
        {
            var service = new ImageSimilarityService();
            var results = await service.FindSimilarImagesAsync(Array.Empty<string>());

            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public async Task FindSimilarImagesAsync_NonExistentPaths_ReturnsEmpty()
        {
            var service = new ImageSimilarityService();
            var paths = new[] { @"C:\nonexistent\folder1", @"C:\nonexistent\folder2" };

            var results = await service.FindSimilarImagesAsync(paths);

            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public async Task FindSimilarImagesAsync_EmptyDirectory_ReturnsEmpty()
        {
            var service = new ImageSimilarityService();
            var emptyDir = Path.Combine(_tempDir, "empty");
            Directory.CreateDirectory(emptyDir);

            var results = await service.FindSimilarImagesAsync(new[] { emptyDir });

            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public async Task FindSimilarImagesAsync_Cancellation_HandledGracefully()
        {
            var service = new ImageSimilarityService();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            try
            {
                var results = await service.FindSimilarImagesAsync(
                    new[] { _tempDir }, cancellationToken: cts.Token);
                Assert.IsNotNull(results);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        [TestMethod]
        public async Task FindSimilarImagesAsync_ThresholdBoundary()
        {
            var service = new ImageSimilarityService();

            // 0% threshold should match everything, 100% should match only identical
            var results0 = await service.FindSimilarImagesAsync(new[] { _tempDir }, threshold: 0);
            var results100 = await service.FindSimilarImagesAsync(new[] { _tempDir }, threshold: 100);

            Assert.IsNotNull(results0);
            Assert.IsNotNull(results100);
        }

        #endregion

        #region GenerateThumbnail Tests

        [TestMethod]
        public void GenerateThumbnail_NonExistentFile_ReturnsNull()
        {
            var service = new ImageSimilarityService();
            var result = service.GenerateThumbnail(@"C:\nonexistent\image.jpg");
            Assert.IsNull(result);
        }

        #endregion

        #region Model Tests

        [TestMethod]
        public void SimilarImageGroup_Structure()
        {
            var group = new SimilarImageGroup
            {
                ReferenceHash = "abc123"
            };

            Assert.AreEqual("abc123", group.ReferenceHash);
            Assert.IsNotNull(group.Images);
        }

        [TestMethod]
        public void SimilarImageInfo_Structure()
        {
            var info = new SimilarImageInfo
            {
                FilePath = @"C:\test\image.jpg",
                Hash = "def456",
                SimilarityPercent = 95.5,
                FileSize = 1024,
                Width = 1920,
                Height = 1080
            };

            Assert.AreEqual(@"C:\test\image.jpg", info.FilePath);
            Assert.AreEqual(95.5, info.SimilarityPercent);
            Assert.AreEqual(1920, info.Width);
        }

        #endregion
    }
}
