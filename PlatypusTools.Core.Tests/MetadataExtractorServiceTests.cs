using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for MetadataExtractorService (TASK-227).
    /// Tests metadata extraction patterns, file type detection, and batch processing.
    /// </summary>
    [TestClass]
    public class MetadataExtractorServiceTests
    {
        #region IsAudioFile Tests

        [TestMethod]
        [DataRow(".mp3", true)]
        [DataRow(".wav", true)]
        [DataRow(".flac", true)]
        [DataRow(".ogg", true)]
        [DataRow(".m4a", true)]
        [DataRow(".wma", true)]
        [DataRow(".aac", true)]
        [DataRow(".opus", true)]
        [DataRow(".ape", true)]
        [DataRow(".txt", false)]
        [DataRow(".jpg", false)]
        [DataRow(".exe", false)]
        [DataRow(".pdf", false)]
        public void IsAudioFile_ValidatesFileExtensions(string extension, bool expected)
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), $"test_audio{extension}");

            // Act
            var result = MetadataExtractorService.IsAudioFile(tempFile);

            // Assert
            Assert.AreEqual(expected, result, $"Extension {extension} should be {(expected ? "" : "not ")}detected as audio");
        }

        [TestMethod]
        public void IsAudioFile_NullPath_ReturnsFalse()
        {
            Assert.IsFalse(MetadataExtractorService.IsAudioFile(null!));
        }

        [TestMethod]
        public void IsAudioFile_EmptyPath_ReturnsFalse()
        {
            Assert.IsFalse(MetadataExtractorService.IsAudioFile(string.Empty));
        }

        [TestMethod]
        public void IsAudioFile_CaseInsensitive()
        {
            var result1 = MetadataExtractorService.IsAudioFile("test.MP3");
            var result2 = MetadataExtractorService.IsAudioFile("test.Mp3");
            var result3 = MetadataExtractorService.IsAudioFile("test.mp3");

            Assert.IsTrue(result1);
            Assert.IsTrue(result2);
            Assert.IsTrue(result3);
        }

        #endregion

        #region ExtractMetadata Tests

        [TestMethod]
        public void ExtractMetadata_NonExistentFile_ReturnsNull()
        {
            var result = MetadataExtractorService.ExtractMetadata(@"C:\nonexistent\fake.mp3");
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ExtractMetadataAsync_NonExistentFile_ReturnsNull()
        {
            var result = await MetadataExtractorService.ExtractMetadataAsync(@"C:\nonexistent\fake.mp3");
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ExtractMetadataAsync_EmptyCollection_ReturnsEmptyList()
        {
            var result = await MetadataExtractorService.ExtractMetadataAsync(Array.Empty<string>());
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task ExtractMetadataAsync_BatchWithInvalidFiles_ReturnsPartialResults()
        {
            var files = new[] { @"C:\nonexistent\a.mp3", @"C:\nonexistent\b.mp3" };
            var result = await MetadataExtractorService.ExtractMetadataAsync(files);

            Assert.IsNotNull(result);
            // Non-existent files should be skipped or return null entries
            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region GetQuickInfo Tests

        [TestMethod]
        public void GetQuickInfo_NonExistentFile_ReturnsNull()
        {
            var result = MetadataExtractorService.GetQuickInfo(@"C:\nonexistent\fake.mp3");
            Assert.IsNull(result);
        }

        #endregion
    }
}
