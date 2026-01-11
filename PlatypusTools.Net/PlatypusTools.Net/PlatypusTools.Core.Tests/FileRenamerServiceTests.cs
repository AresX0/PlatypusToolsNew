using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class FileRenamerServiceTests
    {
        private FileRenamerService _service = null!;
        private string _testFolder = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new FileRenamerService();
            _testFolder = Path.Combine(Path.GetTempPath(), "FileRenamerTests_" + Guid.NewGuid().ToString("N"));
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
        public void ScanFolder_FindsAllFiles()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_testFolder, "test1.txt"), "test");
            File.WriteAllText(Path.Combine(_testFolder, "test2.mp4"), "test");

            // Act
            var ops = _service.ScanFolder(_testFolder, false, FileTypeFilter.All);

            // Assert
            Assert.AreEqual(2, ops.Count);
        }

        [TestMethod]
        public void ScanFolder_FiltersVideoFiles()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_testFolder, "test1.txt"), "test");
            File.WriteAllText(Path.Combine(_testFolder, "test2.mp4"), "test");
            File.WriteAllText(Path.Combine(_testFolder, "test3.mkv"), "test");

            // Act
            var ops = _service.ScanFolder(_testFolder, false, FileTypeFilter.Video);

            // Assert
            Assert.AreEqual(2, ops.Count);
            Assert.IsTrue(ops.All(o => o.OriginalFileName.EndsWith(".mp4") || o.OriginalFileName.EndsWith(".mkv")));
        }

        [TestMethod]
        public void ApplyPrefixRules_AddsPrefix()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "test.txt", ProposedFileName = "test.txt", Directory = _testFolder }
            };

            // Act
            _service.ApplyPrefixRules(ops, "PREFIX-", null, null, false, false, false);

            // Assert
            Assert.AreEqual("PREFIX-test.txt", ops[0].ProposedFileName);
        }

        [TestMethod]
        public void ApplyPrefixRules_ReplacesOldPrefix()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "OLD-test.txt", ProposedFileName = "OLD-test.txt", Directory = _testFolder }
            };

            // Act
            _service.ApplyPrefixRules(ops, "NEW-", "OLD-", null, false, false, false);

            // Assert
            Assert.AreEqual("NEW-test.txt", ops[0].ProposedFileName);
        }

        [TestMethod]
        public void ApplyPrefixRules_IgnoresFilesWithIgnorePrefix()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "IGNORE-test.txt", ProposedFileName = "IGNORE-test.txt", Directory = _testFolder },
                new RenameOperation { OriginalFileName = "test2.txt", ProposedFileName = "test2.txt", Directory = _testFolder }
            };

            // Act
            _service.ApplyPrefixRules(ops, "PREFIX-", null, "IGNORE-", false, false, false);

            // Assert
            Assert.AreEqual("IGNORE-test.txt", ops[0].ProposedFileName, "Should not change ignored file");
            Assert.AreEqual("PREFIX-test2.txt", ops[1].ProposedFileName, "Should add prefix to non-ignored file");
        }

        [TestMethod]
        public void ApplySeasonEpisodeNumbering_RenumbersAlphabetically()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "PREFIX-C-file.mp4", ProposedFileName = "PREFIX-C-file.mp4", Directory = _testFolder },
                new RenameOperation { OriginalFileName = "PREFIX-A-file.mp4", ProposedFileName = "PREFIX-A-file.mp4", Directory = _testFolder },
                new RenameOperation { OriginalFileName = "PREFIX-B-file.mp4", ProposedFileName = "PREFIX-B-file.mp4", Directory = _testFolder }
            };

            // Act
            _service.ApplySeasonEpisodeNumbering(ops, null, 2, 1, 4, true, false);

            // Assert
            Assert.IsTrue(ops[0].ProposedFileName.Contains("E0003"), "C should be episode 3");
            Assert.IsTrue(ops[1].ProposedFileName.Contains("E0001"), "A should be episode 1");
            Assert.IsTrue(ops[2].ProposedFileName.Contains("E0002"), "B should be episode 2");
        }

        [TestMethod]
        public void ApplySeasonEpisodeNumbering_IncludesSeasonFormat()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "PREFIX-file.mp4", ProposedFileName = "PREFIX-file.mp4", Directory = _testFolder }
            };

            // Act
            _service.ApplySeasonEpisodeNumbering(ops, 1, 2, 1, 3, true, true);

            // Assert
            Assert.IsTrue(ops[0].ProposedFileName.Contains("S01E001"), "Should have S01E001 format");
        }

        [TestMethod]
        public void ApplyCleaningRules_RemovesCommonTokens()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "movie-1080p-4k.mp4", ProposedFileName = "movie-1080p-4k.mp4", Directory = _testFolder }
            };

            // Act
            _service.ApplyCleaningRules(ops, true, false, false, false, null);

            // Assert
            Assert.IsFalse(ops[0].ProposedFileName.Contains("1080p"));
            Assert.IsFalse(ops[0].ProposedFileName.Contains("4k"));
        }

        [TestMethod]
        public void ApplyCleaningRules_RemovesCustomTokens()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "movie-CUSTOM-test.mp4", ProposedFileName = "movie-CUSTOM-test.mp4", Directory = _testFolder }
            };

            // Act
            _service.ApplyCleaningRules(ops, false, false, false, false, new[] { "CUSTOM" });

            // Assert
            Assert.IsFalse(ops[0].ProposedFileName.Contains("CUSTOM"));
        }

        [TestMethod]
        public void ApplyNormalization_SpacesToDashes()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "my test file.mp4", ProposedFileName = "my test file.mp4", Directory = _testFolder }
            };

            // Act
            _service.ApplyNormalization(ops, NormalizationPreset.SpacesToDashes);

            // Assert
            Assert.AreEqual("my-test-file.mp4", ops[0].ProposedFileName);
        }

        [TestMethod]
        public void ApplyNormalization_UnderscoresToDashes()
        {
            // Arrange
            var ops = new List<RenameOperation>
            {
                new RenameOperation { OriginalFileName = "my_test_file.mp4", ProposedFileName = "my_test_file.mp4", Directory = _testFolder }
            };

            // Act
            _service.ApplyNormalization(ops, NormalizationPreset.UnderscoresToDashes);

            // Assert
            Assert.AreEqual("my-test-file.mp4", ops[0].ProposedFileName);
        }

        [TestMethod]
        public void ApplyChanges_RenamesSelectedFiles()
        {
            // Arrange
            var file1 = Path.Combine(_testFolder, "test1.txt");
            var file2 = Path.Combine(_testFolder, "test2.txt");
            File.WriteAllText(file1, "test");
            File.WriteAllText(file2, "test");

            var ops = new List<RenameOperation>
            {
                new RenameOperation 
                { 
                    OriginalPath = file1, 
                    ProposedPath = Path.Combine(_testFolder, "renamed1.txt"),
                    OriginalFileName = "test1.txt",
                    ProposedFileName = "renamed1.txt",
                    Directory = _testFolder,
                    IsSelected = true
                },
                new RenameOperation 
                { 
                    OriginalPath = file2, 
                    ProposedPath = Path.Combine(_testFolder, "renamed2.txt"),
                    OriginalFileName = "test2.txt",
                    ProposedFileName = "renamed2.txt",
                    Directory = _testFolder,
                    IsSelected = false
                }
            };

            // Act
            var backup = _service.ApplyChanges(ops);

            // Assert
            Assert.IsTrue(File.Exists(Path.Combine(_testFolder, "renamed1.txt")), "Selected file should be renamed");
            Assert.IsFalse(File.Exists(file1), "Original file should not exist");
            Assert.IsTrue(File.Exists(file2), "Unselected file should not be renamed");
            Assert.AreEqual(1, backup.Count, "Backup should contain one operation");
            Assert.AreEqual(RenameStatus.Success, ops[0].Status);
        }

        [TestMethod]
        public void UndoChanges_RestoresOriginalFiles()
        {
            // Arrange
            var file1 = Path.Combine(_testFolder, "test1.txt");
            File.WriteAllText(file1, "test");

            var ops = new List<RenameOperation>
            {
                new RenameOperation 
                { 
                    OriginalPath = file1, 
                    ProposedPath = Path.Combine(_testFolder, "renamed1.txt"),
                    OriginalFileName = "test1.txt",
                    ProposedFileName = "renamed1.txt",
                    Directory = _testFolder,
                    IsSelected = true
                }
            };

            var backup = _service.ApplyChanges(ops);

            // Act
            _service.UndoChanges(backup);

            // Assert
            Assert.IsTrue(File.Exists(file1), "Original file should be restored");
            Assert.IsFalse(File.Exists(Path.Combine(_testFolder, "renamed1.txt")), "Renamed file should not exist");
        }

        [TestMethod]
        public void CompleteWorkflow_PrefixAndEpisodeAndCleaning()
        {
            // Arrange
            File.WriteAllText(Path.Combine(_testFolder, "C-movie.mp4"), "test");
            File.WriteAllText(Path.Combine(_testFolder, "A-movie-1080p.mp4"), "test");
            File.WriteAllText(Path.Combine(_testFolder, "B-movie 720p.mp4"), "test");

            // Act
            var ops = _service.ScanFolder(_testFolder, false, FileTypeFilter.Video);
            _service.ApplyPrefixRules(ops, "SHOW-", null, null, false, false, false);
            _service.ApplySeasonEpisodeNumbering(ops, 1, 2, 1, 3, true, true);
            _service.ApplyCleaningRules(ops, true, false, false, false, null);
            _service.ApplyNormalization(ops, NormalizationPreset.SpacesToDashes);

            // Assert
            Assert.AreEqual(3, ops.Count);
            // All should have SHOW- prefix, S01E### format, no quality tokens, dashes instead of spaces
            foreach (var op in ops)
            {
                Assert.IsTrue(op.ProposedFileName.StartsWith("SHOW-"));
                Assert.IsTrue(op.ProposedFileName.Contains("S01E"));
                Assert.IsFalse(op.ProposedFileName.Contains("1080p"));
                Assert.IsFalse(op.ProposedFileName.Contains("720p"));
                Assert.IsFalse(op.ProposedFileName.Contains(" "));
            }
        }
    }
}
