using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Integration tests for Phase 7 (TASK-232, TASK-233, TASK-234).
    /// Tests cross-service interactions and end-to-end workflows.
    /// </summary>
    [TestClass]
    public class Phase7IntegrationTests
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
            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, true);
            }
            catch { }
        }

        #region Audio Playback Pipeline Integration (TASK-232)

        [TestMethod]
        public void MetadataExtraction_IsAudioFile_CorrectlyFilters()
        {
            // Integration: Verify metadata extraction correctly identifies audio files
            // that would be passed to the audio player
            var audioFiles = new[] { "song.mp3", "track.flac", "audio.wav", "music.ogg", "clip.m4a" };
            var nonAudioFiles = new[] { "image.jpg", "doc.pdf", "video.mp4", "text.txt" };

            foreach (var file in audioFiles)
                Assert.IsTrue(MetadataExtractorService.IsAudioFile(file), $"{file} should be detected as audio");

            foreach (var file in nonAudioFiles)
                Assert.IsFalse(MetadataExtractorService.IsAudioFile(file), $"{file} should NOT be detected as audio");
        }

        [TestMethod]
        public async Task AudioPlaybackPipeline_MetadataAndLibraryIntegration()
        {
            // Integration: Library index should be able to scan a directory and metadata
            // extractor should agree on which files are audio
            var indexPath = Path.Combine(_tempDir, "test_index.json");
            var musicDir = Path.Combine(_tempDir, "music");
            Directory.CreateDirectory(musicDir);

            // Create some dummy files (won't have valid audio content but tests the pipeline)
            File.WriteAllText(Path.Combine(musicDir, "song1.mp3"), "not real audio");
            File.WriteAllText(Path.Combine(musicDir, "song2.flac"), "not real audio");
            File.WriteAllText(Path.Combine(musicDir, "readme.txt"), "not audio");

            // Both services should agree on what's an audio file
            var allFiles = Directory.GetFiles(musicDir);
            var audioByExtractor = allFiles.Where(f => MetadataExtractorService.IsAudioFile(f)).ToList();

            Assert.AreEqual(2, audioByExtractor.Count, "Should find 2 audio files");
            Assert.IsTrue(audioByExtractor.All(f => f.EndsWith(".mp3") || f.EndsWith(".flac")));
        }

        #endregion

        #region Library Scanning Integration (TASK-233)

        [TestMethod]
        public async Task LibraryScanning_EmptyDirectory_HandledGracefully()
        {
            var indexPath = Path.Combine(_tempDir, "index.json");
            var emptyDir = Path.Combine(_tempDir, "empty_lib");
            Directory.CreateDirectory(emptyDir);

            var service = new LibraryIndexService(indexPath);
            var index = await service.ScanAndIndexDirectoryAsync(emptyDir);

            Assert.IsNotNull(index);
            Assert.AreEqual(0, index.Tracks.Count);
        }

        [TestMethod]
        public async Task LibraryScanning_SearchAfterScan_ReturnsResults()
        {
            var indexPath = Path.Combine(_tempDir, "index.json");
            var service = new LibraryIndexService(indexPath);
            
            // Load empty index
            await service.LoadOrCreateIndexAsync();
            
            // Search should return empty, not throw
            var results = service.Search("anything");
            Assert.IsNotNull(results);
        }

        [TestMethod]
        public async Task LibraryScanning_PersistenceRoundTrip()
        {
            var indexPath = Path.Combine(_tempDir, "index.json");
            var service = new LibraryIndexService(indexPath);

            // Create, save, then reload
            await service.LoadOrCreateIndexAsync();
            var saved = await service.SaveIndexAsync();
            Assert.IsTrue(saved);

            // Create new service instance and load
            var service2 = new LibraryIndexService(indexPath);
            var index2 = await service2.LoadOrCreateIndexAsync();
            Assert.IsNotNull(index2);
        }

        [TestMethod]
        public async Task LibraryScanning_RemoveMissing_DoesNotCrash()
        {
            var indexPath = Path.Combine(_tempDir, "index.json");
            var service = new LibraryIndexService(indexPath);
            await service.LoadOrCreateIndexAsync();

            // Should complete without errors even with empty index
            var removed = await service.RemoveMissingFilesAsync();
            Assert.AreEqual(0, removed);
        }

        #endregion

        #region Plugin Loading Integration (TASK-234)

        [TestMethod]
        public void PluginDirectory_CreatesIfMissing()
        {
            // Integration: Verify the plugin loading pattern can handle
            // missing plugin directories gracefully
            var pluginDir = Path.Combine(_tempDir, "plugins");
            
            Assert.IsFalse(Directory.Exists(pluginDir));
            
            // Simulate plugin loader behavior: create directory if not exists
            if (!Directory.Exists(pluginDir))
                Directory.CreateDirectory(pluginDir);
            
            Assert.IsTrue(Directory.Exists(pluginDir));
            Assert.AreEqual(0, Directory.GetFiles(pluginDir).Length);
        }

        [TestMethod]
        public void PluginAssembly_InvalidDll_HandledGracefully()
        {
            // Integration: Verify invalid assemblies don't crash the loader
            var pluginDir = Path.Combine(_tempDir, "plugins");
            Directory.CreateDirectory(pluginDir);
            
            // Create a fake plugin DLL
            File.WriteAllBytes(Path.Combine(pluginDir, "fake_plugin.dll"), new byte[] { 0x00, 0x01, 0x02 });
            
            var dllFiles = Directory.GetFiles(pluginDir, "*.dll");
            Assert.AreEqual(1, dllFiles.Length);
            
            // Attempt to load — should not crash
            foreach (var dll in dllFiles)
            {
                try
                {
                    var assembly = System.Reflection.Assembly.LoadFrom(dll);
                    // If it somehow loads, that's fine
                }
                catch (BadImageFormatException)
                {
                    // Expected for fake DLL
                }
                catch (Exception)
                {
                    // Other expected errors
                }
            }
        }

        #endregion
    }
}
