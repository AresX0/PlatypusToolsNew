using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class AudioLibraryTests
    {
        private string _testDirectory;

        [TestInitialize]
        public void Setup()
        {
            _testDirectory = Path.Combine(Path.GetTempPath(), $"AudioLibraryTests_{Guid.NewGuid()}");
            Directory.CreateDirectory(_testDirectory);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testDirectory))
                Directory.Delete(_testDirectory, recursive: true);
        }

        [TestMethod]
        public void PathCanonicalizer_NormalizesPath()
        {
            var path1 = @"C:\Music\Artist\Album\Song.mp3";
            var path2 = @"c:\music\artist\album\song.mp3";
            
            var canonical1 = PathCanonicalizer.Canonicalize(path1);
            var canonical2 = PathCanonicalizer.Canonicalize(path2);
            
            Assert.AreEqual(canonical1, canonical2, "Paths should be normalized to same value");
        }

        [TestMethod]
        public void PathCanonicalizer_DeduplicationKey()
        {
            var path1 = @"C:\Music\Song.mp3";
            var path2 = @"c:\music\song.mp3";
            
            var key1 = PathCanonicalizer.GetDeduplicationKey(path1);
            var key2 = PathCanonicalizer.GetDeduplicationKey(path2);
            
            Assert.AreEqual(key1, key2, "Deduplication keys should match");
        }

        [TestMethod]
        public void PathCanonicalizer_PathsEqual()
        {
            var path1 = @"C:\Music\Song.mp3";
            var path2 = @"c:\music\song.mp3";
            
            Assert.IsTrue(PathCanonicalizer.PathsEqual(path1, path2), "Paths should be equal");
        }

        [TestMethod]
        public async Task AtomicFileWriter_WritesFileAtomically()
        {
            var filePath = Path.Combine(_testDirectory, "test.json");
            var content = "{\"test\": \"data\"}";
            
            var result = await AtomicFileWriter.WriteTextAtomicAsync(filePath, content);
            
            Assert.IsTrue(result, "Write should succeed");
            Assert.IsTrue(File.Exists(filePath), "File should exist");
            
            var readContent = File.ReadAllText(filePath);
            Assert.AreEqual(content, readContent, "Content should match");
        }

        [TestMethod]
        public async Task AtomicFileWriter_CreatesBackup()
        {
            var filePath = Path.Combine(_testDirectory, "test.json");
            var originalContent = "original";
            var newContent = "updated";
            
            // Write original
            await AtomicFileWriter.WriteTextAtomicAsync(filePath, originalContent, keepBackup: false);
            
            // Update with backup
            var result = await AtomicFileWriter.WriteTextAtomicAsync(filePath, newContent, keepBackup: true);
            
            Assert.IsTrue(result, "Update should succeed");
            var backupPath = filePath + ".backup";
            Assert.IsTrue(File.Exists(backupPath), "Backup should exist");
            
            var backupContent = File.ReadAllText(backupPath);
            Assert.AreEqual(originalContent, backupContent, "Backup should contain original content");
        }

        [TestMethod]
        public async Task AtomicFileWriter_BackupExists()
        {
            var filePath = Path.Combine(_testDirectory, "test.json");
            var content1 = "original";
            var content2 = "updated";
            
            // Write initial file
            await AtomicFileWriter.WriteTextAtomicAsync(filePath, content1, keepBackup: false);
            
            // Write again with backup
            await AtomicFileWriter.WriteTextAtomicAsync(filePath, content2, keepBackup: true);
            
            // Check backup exists
            var backupExists = AtomicFileWriter.BackupExists(filePath);
            
            Assert.IsTrue(backupExists, "Backup should exist");
            
            var backupPath = AtomicFileWriter.GetBackupPath(filePath);
            Assert.IsTrue(File.Exists(backupPath), "Backup file should exist at expected path");
            
            var backupContent = File.ReadAllText(backupPath);
            Assert.AreEqual(content1, backupContent, "Backup should contain original content");
        }

        [TestMethod]
        public void Track_DisplayPropertiesFallback()
        {
            var track = new Track
            {
                FilePath = "/music/unknown.mp3"
            };
            
            Assert.AreEqual("unknown", track.DisplayTitle, "DisplayTitle should fallback to filename");
            Assert.AreEqual("Unknown Artist", track.DisplayArtist, "DisplayArtist should be default");
            Assert.AreEqual("Unknown Album", track.DisplayAlbum, "DisplayAlbum should be default");
        }

        [TestMethod]
        public void Track_DurationFormatting()
        {
            var track = new Track
            {
                DurationMs = 125000 // 2:05
            };
            
            Assert.AreEqual("2:05", track.DurationFormatted, "Duration should be formatted as MM:SS");
        }

        [TestMethod]
        public void LibraryIndex_BuildsIndices()
        {
            var index = new LibraryIndex();
            index.Tracks.Add(new Track { Title = "Song1", Artist = "Artist1", FilePath = "/path1" });
            index.Tracks.Add(new Track { Title = "Song2", Artist = "Artist1", FilePath = "/path2" });
            index.Tracks.Add(new Track { Title = "Song3", Artist = "Artist2", FilePath = "/path3" });
            
            index.RebuildIndices();
            
            var artist1Tracks = index.GetTracksByArtist("Artist1");
            Assert.AreEqual(2, artist1Tracks.Count, "Artist1 should have 2 tracks");
            
            var artist2Tracks = index.GetTracksByArtist("Artist2");
            Assert.AreEqual(1, artist2Tracks.Count, "Artist2 should have 1 track");
        }

        [TestMethod]
        public void LibraryIndex_SearchByTitle()
        {
            var index = new LibraryIndex();
            index.Tracks.Add(new Track { Title = "Hello World", FilePath = "/path1" });
            index.Tracks.Add(new Track { Title = "Hello Friend", FilePath = "/path2" });
            index.Tracks.Add(new Track { Title = "Goodbye", FilePath = "/path3" });
            
            var results = index.SearchByTitle("Hello");
            
            Assert.AreEqual(2, results.Count, "Should find 2 tracks with 'Hello'");
        }

        [TestMethod]
        public void LibraryIndex_FindByPath()
        {
            var index = new LibraryIndex();
            var track1 = new Track { Title = "Song1", FilePath = "/music/song1.mp3" };
            var track2 = new Track { Title = "Song2", FilePath = "/music/song2.mp3" };
            index.Tracks.Add(track1);
            index.Tracks.Add(track2);
            
            var found = index.FindTrackByPath("/music/song1.mp3");
            
            Assert.IsNotNull(found, "Track should be found");
            Assert.AreEqual("Song1", found.Title, "Correct track should be returned");
        }

        [TestMethod]
        public async Task LibraryIndexService_CreatesIndex()
        {
            var indexPath = Path.Combine(_testDirectory, "index.json");
            var service = new LibraryIndexService(indexPath);
            
            var index = await service.LoadOrCreateIndexAsync();
            
            Assert.IsNotNull(index, "Index should be created");
            Assert.AreEqual("1.0.0", index.Version, "Version should be set");
        }

        [TestMethod]
        public async Task LibraryIndexService_PersistsIndex()
        {
            var indexPath = Path.Combine(_testDirectory, "index.json");
            var service = new LibraryIndexService(indexPath);
            
            var index = await service.LoadOrCreateIndexAsync();
            index.Tracks.Add(new Track 
            { 
                Title = "Test Track",
                FilePath = "/test/track.mp3",
                DurationMs = 180000
            });
            
            var saved = await service.SaveIndexAsync();
            
            Assert.IsTrue(saved, "Save should succeed");
            Assert.IsTrue(File.Exists(indexPath), "Index file should exist");
            
            // Load in new service instance
            var service2 = new LibraryIndexService(indexPath);
            var loadedIndex = await service2.LoadOrCreateIndexAsync();
            
            Assert.AreEqual(1, loadedIndex.Tracks.Count, "Loaded index should have 1 track");
            Assert.AreEqual("Test Track", loadedIndex.Tracks[0].Title, "Track should be persisted");
        }

        [TestMethod]
        public async Task MetadataExtractorService_IsAudioFile()
        {
            Assert.IsTrue(MetadataExtractorService.IsAudioFile("song.mp3"));
            Assert.IsTrue(MetadataExtractorService.IsAudioFile("music.flac"));
            Assert.IsFalse(MetadataExtractorService.IsAudioFile("document.pdf"));
            Assert.IsFalse(MetadataExtractorService.IsAudioFile("image.jpg"));
        }

        [TestMethod]
        public void MetadataExtractorService_HandlesNonexistentFile()
        {
            var track = MetadataExtractorService.ExtractMetadata("/nonexistent/file.mp3");
            
            Assert.IsNull(track, "Should return null for nonexistent file");
        }
    }
}
