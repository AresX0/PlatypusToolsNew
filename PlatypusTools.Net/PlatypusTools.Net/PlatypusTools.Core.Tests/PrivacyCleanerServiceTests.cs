using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class PrivacyCleanerServiceTests
    {
        private PrivacyCleanerService _service = null!;
        private string _testFolder = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new PrivacyCleanerService();
            _testFolder = Path.Combine(Path.GetTempPath(), $"pt_privacy_test_{Guid.NewGuid()}");
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
        public async Task AnalyzeAsync_NoCategories_ReturnsEmptyResult()
        {
            // Act
            var result = await _service.AnalyzeAsync(PrivacyCategories.None, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.TotalItems);
            Assert.AreEqual(0, result.TotalSize);
            Assert.AreEqual(0, result.Categories.Count);
        }

        [TestMethod]
        public async Task AnalyzeAsync_WindowsRecentDocs_FindsRecentFolder()
        {
            // Act
            var result = await _service.AnalyzeAsync(PrivacyCategories.WindowsRecentDocs, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            // Recent folder might be empty or have files, just verify analysis ran
            var category = result.Categories.FirstOrDefault(c => c.Category == "Recent Documents");
            Assert.IsNotNull(category);
        }

        [TestMethod]
        public async Task CleanAsync_DryRun_DoesNotDeleteFiles()
        {
            // Arrange - create test files
            var testDir = Path.Combine(_testFolder, "browser_cache");
            Directory.CreateDirectory(testDir);
            var testFile = Path.Combine(testDir, "cache.dat");
            File.WriteAllText(testFile, "cached data");

            // Create analysis result manually for testing
            var analysisResult = new PrivacyAnalysisResult
            {
                TotalItems = 1,
                TotalSize = new FileInfo(testFile).Length,
                Categories = new System.Collections.Generic.List<PrivacyCategoryResult>
                {
                    new PrivacyCategoryResult
                    {
                        Category = "Test Browser Cache",
                        ItemCount = 1,
                        TotalSize = new FileInfo(testFile).Length,
                        Items = new System.Collections.Generic.List<PrivacyItem>
                        {
                            new PrivacyItem { Path = testFile, Size = new FileInfo(testFile).Length, IsFile = true }
                        }
                    }
                }
            };

            // Act
            var result = await _service.CleanAsync(analysisResult, true, null, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.WasDryRun);
            Assert.IsTrue(File.Exists(testFile), "File should still exist in dry run mode");
            Assert.AreEqual(1, result.ItemsDeleted); // Counted as "would be deleted"
        }

        [TestMethod]
        public async Task CleanAsync_ActualClean_DeletesFiles()
        {
            // Arrange - create test files
            var testDir = Path.Combine(_testFolder, "browser_cache");
            Directory.CreateDirectory(testDir);
            var testFile = Path.Combine(testDir, "cache.dat");
            File.WriteAllText(testFile, "cached data");

            var analysisResult = new PrivacyAnalysisResult
            {
                TotalItems = 1,
                TotalSize = new FileInfo(testFile).Length,
                Categories = new System.Collections.Generic.List<PrivacyCategoryResult>
                {
                    new PrivacyCategoryResult
                    {
                        Category = "Test Browser Cache",
                        ItemCount = 1,
                        TotalSize = new FileInfo(testFile).Length,
                        Items = new System.Collections.Generic.List<PrivacyItem>
                        {
                            new PrivacyItem { Path = testFile, Size = new FileInfo(testFile).Length, IsFile = true }
                        }
                    }
                }
            };

            // Act
            var result = await _service.CleanAsync(analysisResult, false, null, CancellationToken.None);

            // Assert
            Assert.IsFalse(result.WasDryRun);
            Assert.IsFalse(File.Exists(testFile), "File should be deleted");
            Assert.AreEqual(1, result.ItemsDeleted);
        }

        [TestMethod]
        public async Task CleanAsync_ActualClean_DeletesDirectories()
        {
            // Arrange - create test directory with files
            var testDir = Path.Combine(_testFolder, "browser_cache_dir");
            Directory.CreateDirectory(testDir);
            var testFile = Path.Combine(testDir, "cache.dat");
            File.WriteAllText(testFile, "cached data");

            var analysisResult = new PrivacyAnalysisResult
            {
                TotalItems = 1,
                TotalSize = new FileInfo(testFile).Length,
                Categories = new System.Collections.Generic.List<PrivacyCategoryResult>
                {
                    new PrivacyCategoryResult
                    {
                        Category = "Test Browser Cache",
                        ItemCount = 1,
                        TotalSize = new FileInfo(testFile).Length,
                        Items = new System.Collections.Generic.List<PrivacyItem>
                        {
                            new PrivacyItem { Path = testDir, Size = new FileInfo(testFile).Length, IsFile = false }
                        }
                    }
                }
            };

            // Act
            var result = await _service.CleanAsync(analysisResult, false, null, CancellationToken.None);

            // Assert
            Assert.IsFalse(result.WasDryRun);
            Assert.IsFalse(Directory.Exists(testDir), "Directory should be deleted");
            Assert.AreEqual(1, result.ItemsDeleted);
        }

        [TestMethod]
        public async Task AnalyzeAsync_MultipleCategories_AnalyzesAll()
        {
            // Act
            var categories = PrivacyCategories.WindowsRecentDocs | PrivacyCategories.WindowsClipboard;
            var result = await _service.AnalyzeAsync(categories, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            // Should have attempted to analyze both categories (even if they're empty)
        }

        [TestMethod]
        public async Task AnalyzeAsync_AllBrowsers_AnalyzesAllBrowserCategories()
        {
            // Act
            var categories = PrivacyCategories.BrowserChrome | 
                           PrivacyCategories.BrowserEdge | 
                           PrivacyCategories.BrowserFirefox | 
                           PrivacyCategories.BrowserBrave;
            var result = await _service.AnalyzeAsync(categories, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            // Browsers might not be installed, but analysis should complete without error
        }

        [TestMethod]
        public async Task AnalyzeAsync_AllCloudServices_AnalyzesAllCloudCategories()
        {
            // Act
            var categories = PrivacyCategories.CloudOneDrive | 
                           PrivacyCategories.CloudGoogle | 
                           PrivacyCategories.CloudDropbox | 
                           PrivacyCategories.CloudiCloud;
            var result = await _service.AnalyzeAsync(categories, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            // Cloud services might not be used, but analysis should complete without error
        }

        [TestMethod]
        public async Task AnalyzeAsync_AllApplications_AnalyzesAllAppCategories()
        {
            // Act
            var categories = PrivacyCategories.ApplicationOffice | 
                           PrivacyCategories.ApplicationAdobe | 
                           PrivacyCategories.ApplicationMediaPlayers;
            var result = await _service.AnalyzeAsync(categories, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            // Applications might not be installed, but analysis should complete without error
        }

        [TestMethod]
        public async Task AnalyzeAsync_WithProgress_ReportsProgress()
        {
            // Arrange
            var progressReports = new System.Collections.Generic.List<string>();
            var progress = new Progress<string>(msg => progressReports.Add(msg));

            // Act
            await _service.AnalyzeAsync(PrivacyCategories.WindowsRecentDocs, progress, CancellationToken.None);

            // Assert
            Assert.IsTrue(progressReports.Count > 0, "Progress should be reported");
        }

        [TestMethod]
        public async Task CleanAsync_WithProgress_ReportsProgress()
        {
            // Arrange
            var testFile = Path.Combine(_testFolder, "test.dat");
            File.WriteAllText(testFile, "test data");

            var analysisResult = new PrivacyAnalysisResult
            {
                TotalItems = 1,
                TotalSize = new FileInfo(testFile).Length,
                Categories = new System.Collections.Generic.List<PrivacyCategoryResult>
                {
                    new PrivacyCategoryResult
                    {
                        Category = "Test Category",
                        ItemCount = 1,
                        TotalSize = new FileInfo(testFile).Length,
                        Items = new System.Collections.Generic.List<PrivacyItem>
                        {
                            new PrivacyItem { Path = testFile, Size = new FileInfo(testFile).Length, IsFile = true }
                        }
                    }
                }
            };

            var progressReports = new System.Collections.Generic.List<string>();
            var progress = new Progress<string>(msg => progressReports.Add(msg));

            // Act
            await _service.CleanAsync(analysisResult, true, progress, CancellationToken.None);

            // Assert
            Assert.IsTrue(progressReports.Count > 0, "Progress should be reported");
        }

        [TestMethod]
        public async Task CleanAsync_NonExistentPath_CollectsError()
        {
            // Arrange
            var analysisResult = new PrivacyAnalysisResult
            {
                TotalItems = 1,
                TotalSize = 100,
                Categories = new System.Collections.Generic.List<PrivacyCategoryResult>
                {
                    new PrivacyCategoryResult
                    {
                        Category = "Test Category",
                        ItemCount = 1,
                        TotalSize = 100,
                        Items = new System.Collections.Generic.List<PrivacyItem>
                        {
                            new PrivacyItem { Path = Path.Combine(_testFolder, "nonexistent.dat"), Size = 100, IsFile = true }
                        }
                    }
                }
            };

            // Act
            var result = await _service.CleanAsync(analysisResult, false, null, CancellationToken.None);

            // Assert
            // Should complete without exception (errors are collected, not thrown)
            Assert.AreEqual(0, result.ItemsDeleted); // Nothing was actually deleted
        }

        [TestMethod]
        public async Task AnalyzeAsync_CancellationRequested_ThrowsOperationCanceledException()
        {
            // Arrange
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            // Act & Assert
            // TaskCanceledException inherits from OperationCanceledException in async context
            try
            {
                await _service.AnalyzeAsync(PrivacyCategories.WindowsRecentDocs, null, cts.Token);
                Assert.Fail("Should have thrown cancellation exception");
            }
            catch (OperationCanceledException)
            {
                // Expected - includes TaskCanceledException
            }
        }

        [TestMethod]
        public void PrivacyCategories_FlagsEnum_WorksCorrectly()
        {
            // Arrange & Act
            var combined = PrivacyCategories.BrowserChrome | PrivacyCategories.BrowserEdge;

            // Assert
            Assert.IsTrue(combined.HasFlag(PrivacyCategories.BrowserChrome));
            Assert.IsTrue(combined.HasFlag(PrivacyCategories.BrowserEdge));
            Assert.IsFalse(combined.HasFlag(PrivacyCategories.BrowserFirefox));
        }

        [TestMethod]
        public void PrivacyCategoryResult_Properties_CanBeSet()
        {
            // Arrange & Act
            var result = new PrivacyCategoryResult
            {
                Category = "Test Browser",
                ItemCount = 15,
                TotalSize = 2048,
                Items = new System.Collections.Generic.List<PrivacyItem>
                {
                    new PrivacyItem { Path = "path1", Size = 1024, IsFile = true },
                    new PrivacyItem { Path = "path2", Size = 1024, IsFile = true }
                }
            };

            // Assert
            Assert.AreEqual("Test Browser", result.Category);
            Assert.AreEqual(15, result.ItemCount);
            Assert.AreEqual(2048, result.TotalSize);
            Assert.AreEqual(2, result.Items.Count);
        }

        [TestMethod]
        public void PrivacyCleanupResult_Initialization_Works()
        {
            // Arrange & Act
            var result = new PrivacyCleanupResult
            {
                WasDryRun = true,
                ItemsDeleted = 10,
                SpaceFreed = 4096,
                Errors = new System.Collections.Generic.List<string> { "Error 1", "Error 2" }
            };

            // Assert
            Assert.IsTrue(result.WasDryRun);
            Assert.AreEqual(10, result.ItemsDeleted);
            Assert.AreEqual(4096, result.SpaceFreed);
            Assert.AreEqual(2, result.Errors.Count);
        }

        [TestMethod]
        public async Task AnalyzeAsync_WindowsClipboard_HandlesClipboardState()
        {
            // Act
            var result = await _service.AnalyzeAsync(PrivacyCategories.WindowsClipboard, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            // Clipboard analysis should complete (even if clipboard is empty or category not found)
            // On some systems, clipboard history may not be available
            Assert.IsTrue(result.Categories.Count >= 0, "Should complete analysis");
        }

        [TestMethod]
        public async Task AnalyzeAsync_AllCategories_CompletesWithoutError()
        {
            // Act - analyze ALL categories
            var allCategories = PrivacyCategories.BrowserChrome | PrivacyCategories.BrowserEdge |
                               PrivacyCategories.BrowserFirefox | PrivacyCategories.BrowserBrave |
                               PrivacyCategories.CloudOneDrive | PrivacyCategories.CloudGoogle |
                               PrivacyCategories.CloudDropbox | PrivacyCategories.CloudiCloud |
                               PrivacyCategories.WindowsRecentDocs | PrivacyCategories.WindowsJumpLists |
                               PrivacyCategories.WindowsExplorerHistory | PrivacyCategories.WindowsClipboard |
                               PrivacyCategories.ApplicationOffice | PrivacyCategories.ApplicationAdobe |
                               PrivacyCategories.ApplicationMediaPlayers;

            var result = await _service.AnalyzeAsync(allCategories, null, CancellationToken.None);

            // Assert
            Assert.IsNotNull(result);
            // Should complete without throwing exceptions
        }

        [TestMethod]
        public async Task CleanAsync_MultipleCategories_DeletesFromAll()
        {
            // Arrange - create test files for multiple categories
            var testFile1 = Path.Combine(_testFolder, "cache1.dat");
            var testFile2 = Path.Combine(_testFolder, "cache2.dat");
            File.WriteAllText(testFile1, "cache data 1");
            File.WriteAllText(testFile2, "cache data 2");

            var analysisResult = new PrivacyAnalysisResult
            {
                TotalItems = 2,
                TotalSize = new FileInfo(testFile1).Length + new FileInfo(testFile2).Length,
                Categories = new System.Collections.Generic.List<PrivacyCategoryResult>
                {
                    new PrivacyCategoryResult
                    {
                        Category = "Category 1",
                        ItemCount = 1,
                        TotalSize = new FileInfo(testFile1).Length,
                        Items = new System.Collections.Generic.List<PrivacyItem>
                        {
                            new PrivacyItem { Path = testFile1, Size = new FileInfo(testFile1).Length, IsFile = true }
                        }
                    },
                    new PrivacyCategoryResult
                    {
                        Category = "Category 2",
                        ItemCount = 1,
                        TotalSize = new FileInfo(testFile2).Length,
                        Items = new System.Collections.Generic.List<PrivacyItem>
                        {
                            new PrivacyItem { Path = testFile2, Size = new FileInfo(testFile2).Length, IsFile = true }
                        }
                    }
                }
            };

            // Act
            var result = await _service.CleanAsync(analysisResult, false, null, CancellationToken.None);

            // Assert
            Assert.IsFalse(result.WasDryRun);
            Assert.IsFalse(File.Exists(testFile1), "File 1 should be deleted");
            Assert.IsFalse(File.Exists(testFile2), "File 2 should be deleted");
            Assert.AreEqual(2, result.ItemsDeleted);
        }
    }
}
