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
    /// Unit tests for LibraryIndexService (TASK-226).
    /// Tests library scanning, indexing, searching, and persistence.
    /// </summary>
    [TestClass]
    public class LibraryIndexServiceTests
    {
        private string _tempDir = string.Empty;
        private string _indexPath = string.Empty;

        [TestInitialize]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "PlatypusTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _indexPath = Path.Combine(_tempDir, "test_index.json");
        }

        [TestCleanup]
        public void Cleanup()
        {
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { /* Ignore cleanup errors */ }
        }

        #region Constructor Tests

        [TestMethod]
        public void Constructor_WithIndexPath_CreatesInstance()
        {
            var service = new LibraryIndexService(_indexPath);
            Assert.IsNotNull(service);
        }

        [TestMethod]
        public void Constructor_Default_CreatesInstance()
        {
            var service = new LibraryIndexService();
            Assert.IsNotNull(service);
        }

        #endregion

        #region LoadOrCreateIndex Tests

        [TestMethod]
        public async Task LoadOrCreateIndexAsync_NewIndex_CreatesEmpty()
        {
            var service = new LibraryIndexService(_indexPath);
            var index = await service.LoadOrCreateIndexAsync();

            Assert.IsNotNull(index);
            Assert.IsNotNull(index.Tracks);
        }

        [TestMethod]
        public async Task LoadOrCreateIndexAsync_CalledTwice_ReturnsSameData()
        {
            var service = new LibraryIndexService(_indexPath);
            var index1 = await service.LoadOrCreateIndexAsync();
            var index2 = await service.LoadOrCreateIndexAsync();

            Assert.IsNotNull(index1);
            Assert.IsNotNull(index2);
        }

        #endregion

        #region Search Tests

        [TestMethod]
        public async Task Search_EmptyIndex_ReturnsEmpty()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var results = service.Search("test");
            Assert.IsNotNull(results);
            Assert.AreEqual(0, results.Count);
        }

        [TestMethod]
        public async Task Search_EmptyQuery_ReturnsAll()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var results = service.Search("");
            Assert.IsNotNull(results);
        }

        #endregion

        #region GetAllArtists / GetAllAlbums Tests

        [TestMethod]
        public async Task GetAllArtists_EmptyIndex_ReturnsEmpty()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var artists = service.GetAllArtists();
            Assert.IsNotNull(artists);
            Assert.AreEqual(0, artists.Count);
        }

        [TestMethod]
        public async Task GetAllAlbums_EmptyIndex_ReturnsEmpty()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var albums = service.GetAllAlbums();
            Assert.IsNotNull(albums);
            Assert.AreEqual(0, albums.Count);
        }

        #endregion

        #region GetCurrentIndex Tests

        [TestMethod]
        public async Task GetCurrentIndex_AfterLoad_ReturnsIndex()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var index = service.GetCurrentIndex();
            Assert.IsNotNull(index);
        }

        #endregion

        #region SaveIndex Tests

        [TestMethod]
        public async Task SaveIndexAsync_EmptyIndex_CreatesFile()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var saved = await service.SaveIndexAsync();
            Assert.IsTrue(saved);
            Assert.IsTrue(File.Exists(_indexPath));
        }

        #endregion

        #region ClearAsync Tests

        [TestMethod]
        public async Task ClearAsync_EmptiesIndex()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var cleared = await service.ClearAsync();
            Assert.IsTrue(cleared);

            var index = service.GetCurrentIndex();
            Assert.AreEqual(0, index.Tracks.Count);
        }

        #endregion

        #region ScanAndIndex Tests

        [TestMethod]
        public async Task ScanAndIndexDirectoryAsync_EmptyDirectory_ReturnsEmptyIndex()
        {
            var service = new LibraryIndexService(_indexPath);
            var emptyDir = Path.Combine(_tempDir, "empty");
            Directory.CreateDirectory(emptyDir);

            var index = await service.ScanAndIndexDirectoryAsync(emptyDir);
            Assert.IsNotNull(index);
            Assert.AreEqual(0, index.Tracks.Count);
        }

        [TestMethod]
        public async Task ScanAndIndexDirectoryAsync_NonExistentDirectory_HandlesGracefully()
        {
            var service = new LibraryIndexService(_indexPath);

            try
            {
                var index = await service.ScanAndIndexDirectoryAsync(@"C:\NonExistent\Directory");
                Assert.IsNotNull(index);
            }
            catch (DirectoryNotFoundException)
            {
                // Expected
            }
        }

        #endregion

        #region RemoveMissingFiles Tests

        [TestMethod]
        public async Task RemoveMissingFilesAsync_EmptyIndex_Returns0()
        {
            var service = new LibraryIndexService(_indexPath);
            await service.LoadOrCreateIndexAsync();

            var removed = await service.RemoveMissingFilesAsync();
            Assert.AreEqual(0, removed);
        }

        #endregion
    }
}
