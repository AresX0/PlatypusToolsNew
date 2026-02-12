using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for ArchiveService (TASK-298).
    /// Tests archive creation, extraction, info, and testing.
    /// </summary>
    [TestClass]
    public class ArchiveServiceTests
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
            var service = new ArchiveService();
            Assert.IsNotNull(service);
        }

        #endregion

        #region GetArchiveInfo Tests

        [TestMethod]
        public void GetArchiveInfo_NonExistentFile_ThrowsOrReturnsDefault()
        {
            var service = new ArchiveService();
            try
            {
                var info = service.GetArchiveInfo(@"C:\nonexistent\archive.zip");
                Assert.IsNotNull(info);
            }
            catch (Exception ex) when (ex is FileNotFoundException || ex is ArgumentException)
            {
                // Expected
            }
        }

        #endregion

        #region CreateAsync Tests

        [TestMethod]
        public async Task CreateAsync_WithFiles_CreatesArchive()
        {
            var service = new ArchiveService();

            // Create some test files
            var file1 = Path.Combine(_tempDir, "test1.txt");
            var file2 = Path.Combine(_tempDir, "test2.txt");
            File.WriteAllText(file1, "Hello World 1");
            File.WriteAllText(file2, "Hello World 2");

            var outputPath = Path.Combine(_tempDir, "output.zip");
            var options = new PlatypusTools.Core.Models.Archive.ArchiveCreateOptions();

            await service.CreateAsync(outputPath, new[] { file1, file2 }, options);

            Assert.IsTrue(File.Exists(outputPath), "Archive should be created");
            Assert.IsTrue(new FileInfo(outputPath).Length > 0, "Archive should not be empty");
        }

        [TestMethod]
        public async Task CreateAsync_EmptyFileList_ThrowsOrCreatesEmpty()
        {
            var service = new ArchiveService();
            var outputPath = Path.Combine(_tempDir, "empty.zip");
            var options = new PlatypusTools.Core.Models.Archive.ArchiveCreateOptions();

            try
            {
                await service.CreateAsync(outputPath, Array.Empty<string>(), options);
                // If it succeeds, archive may exist
            }
            catch (Exception)
            {
                // Expected for empty input
            }
        }

        #endregion

        #region ExtractAsync Tests

        [TestMethod]
        public async Task ExtractAsync_CreatedArchive_ExtractsAll()
        {
            var service = new ArchiveService();

            // Create a zip first
            var file1 = Path.Combine(_tempDir, "extract_test.txt");
            File.WriteAllText(file1, "Extract me!");
            var zipPath = Path.Combine(_tempDir, "extract_test.zip");
            var createOptions = new PlatypusTools.Core.Models.Archive.ArchiveCreateOptions();
            await service.CreateAsync(zipPath, new[] { file1 }, createOptions);

            // Extract
            var extractDir = Path.Combine(_tempDir, "extracted");
            Directory.CreateDirectory(extractDir);
            var extractOptions = new PlatypusTools.Core.Models.Archive.ArchiveExtractOptions
            {
                OutputDirectory = extractDir,
                OverwriteExisting = true
            };
            await service.ExtractAsync(zipPath, extractOptions);

            var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
            Assert.IsTrue(extractedFiles.Length > 0, "Should have extracted files");
        }

        #endregion

        #region GetEntries Tests

        [TestMethod]
        public async Task GetEntriesAsync_ValidZip_ReturnsList()
        {
            var service = new ArchiveService();

            // Create a zip
            var file1 = Path.Combine(_tempDir, "entry_test.txt");
            File.WriteAllText(file1, "Entry content");
            var zipPath = Path.Combine(_tempDir, "entries_test.zip");
            var createOptions = new PlatypusTools.Core.Models.Archive.ArchiveCreateOptions();
            await service.CreateAsync(zipPath, new[] { file1 }, createOptions);

            // Get entries
            var entries = await service.GetEntriesAsync(zipPath);
            Assert.IsNotNull(entries);
            Assert.IsTrue(entries.Count > 0);
        }

        [TestMethod]
        public async Task GetEntriesAsync_NonExistentFile_ThrowsOrReturnsEmpty()
        {
            var service = new ArchiveService();
            try
            {
                var entries = await service.GetEntriesAsync(@"C:\nonexistent\archive.zip");
                Assert.IsNotNull(entries);
            }
            catch (Exception)
            {
                // Expected
            }
        }

        #endregion

        #region TestArchive Tests

        [TestMethod]
        public async Task TestArchiveAsync_ValidArchive_ReturnsValid()
        {
            var service = new ArchiveService();

            // Create a valid zip
            var file1 = Path.Combine(_tempDir, "valid_test.txt");
            File.WriteAllText(file1, "Valid content");
            var zipPath = Path.Combine(_tempDir, "valid_test.zip");
            var createOptions = new PlatypusTools.Core.Models.Archive.ArchiveCreateOptions();
            await service.CreateAsync(zipPath, new[] { file1 }, createOptions);

            var (isValid, errors) = await service.TestArchiveAsync(zipPath);
            Assert.IsTrue(isValid, "Archive should be valid");
            Assert.AreEqual(0, errors.Count, "Should have no errors");
        }

        [TestMethod]
        public async Task TestArchiveAsync_InvalidFile_ReturnsInvalid()
        {
            var service = new ArchiveService();
            var fakePath = Path.Combine(_tempDir, "fake.zip");
            File.WriteAllBytes(fakePath, new byte[] { 0x00, 0x01, 0x02, 0x03 });

            var (isValid, errors) = await service.TestArchiveAsync(fakePath);
            Assert.IsFalse(isValid, "Fake archive should be invalid");
        }

        #endregion
    }
}
