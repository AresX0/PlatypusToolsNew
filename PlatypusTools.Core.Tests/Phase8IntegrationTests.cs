using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Services.Forensics;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Integration tests for Phase 8 (TASK-301, TASK-302, TASK-303, TASK-304).
    /// Tests cross-service interactions for video, batch processing, audio, and playback.
    /// </summary>
    [TestClass]
    public class Phase8IntegrationTests
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

        #region Video Editor Timeline Integration (TASK-301)

        [TestMethod]
        public void TimelineModels_CreationAndRelationship()
        {
            // Verify UI timeline models can be created and linked
            var track = new PlatypusTools.Core.Models.Video.TimelineTrack
            {
                Name = "V1",
                Type = PlatypusTools.Core.Models.Video.TrackType.Video,
                IsVisible = true,
                Opacity = 0.8,
                BlendMode = "Screen"
            };

            var clip = new PlatypusTools.Core.Models.Video.TimelineClip
            {
                Name = "Test Clip",
                SourcePath = @"C:\test\video.mp4",
                StartPosition = TimeSpan.Zero,
                Duration = TimeSpan.FromSeconds(10),
                Speed = 1.5
            };

            track.Clips.Add(clip);

            Assert.AreEqual(1, track.Clips.Count);
            Assert.AreEqual("Test Clip", track.Clips[0].Name);
            Assert.AreEqual(0.8, track.Opacity);
            Assert.AreEqual("Screen", track.BlendMode);
        }

        [TestMethod]
        public void TimelineFilters_AttachToClips()
        {
            var clip = new PlatypusTools.Core.Models.Video.TimelineClip
            {
                Name = "Filter Test Clip",
                Duration = TimeSpan.FromSeconds(5)
            };

            var filter = new PlatypusTools.Core.Models.Video.Filter
            {
                Name = "blur",
                DisplayName = "Gaussian Blur",
                FFmpegFilterName = "boxblur",
                Category = PlatypusTools.Core.Models.Video.FilterCategory.ColorCorrection,
                IsEnabled = true
            };
            filter.Parameters.Add(new PlatypusTools.Core.Models.Video.FilterParameter
            {
                Name = "luma_radius",
                DisplayName = "Radius",
                Type = PlatypusTools.Core.Models.Video.FilterParameterType.Integer,
                Value = 5
            });

            clip.Filters.Add(filter);

            Assert.AreEqual(1, clip.Filters.Count);
            Assert.AreEqual("boxblur=luma_radius=5", clip.Filters[0].BuildFFmpegFilter());
        }

        [TestMethod]
        public void TextElement_PresetCreation()
        {
            var title = PlatypusTools.Core.Models.Video.TextPresets.CreateTitle();
            Assert.IsNotNull(title);
            Assert.AreEqual("Title", title.PresetName);
            Assert.IsNotNull(title.InAnimation);
            Assert.AreEqual(PlatypusTools.Core.Models.Video.TextAnimationType.FadeIn, title.InAnimation.Type);

            var lowerThird = PlatypusTools.Core.Models.Video.TextPresets.CreateLowerThird();
            Assert.AreEqual("Lower Third", lowerThird.PresetName);
            Assert.IsNotNull(lowerThird.BackgroundColor);
        }

        [TestMethod]
        public void ExportSettings_PresetsValid()
        {
            var hd = PlatypusTools.Core.Services.Video.ExportSettings.HD1080p;
            Assert.AreEqual(1920, hd.Width);
            Assert.AreEqual(1080, hd.Height);

            var yt = PlatypusTools.Core.Services.Video.ExportSettings.YouTube1080p;
            Assert.AreEqual(18, yt.Crf);
            Assert.AreEqual("slow", yt.Preset);
        }

        #endregion

        #region Batch Processing Integration (TASK-302)

        [TestMethod]
        public void BatchUpscale_JobLifecycle()
        {
            var service = BatchUpscaleService.Instance;
            var settings = new PlatypusTools.Core.Models.ImageScaler.BatchUpscaleSettings();

            // Create, enqueue, remove
            var job = service.CreateJob("Lifecycle Test", new[] { @"C:\test\img.png" }, settings);
            service.EnqueueJob(job);
            Assert.IsTrue(service.AllJobs.Contains(job));

            service.RemoveJob(job);
            Assert.IsFalse(service.AllJobs.Contains(job));
        }

        [TestMethod]
        public async Task ArchiveRoundTrip_CreateExtractVerify()
        {
            var service = new ArchiveService();

            // Create files
            var sourceDir = Path.Combine(_tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "file1.txt"), "Content 1");
            File.WriteAllText(Path.Combine(sourceDir, "file2.txt"), "Content 2");

            var files = Directory.GetFiles(sourceDir);
            var zipPath = Path.Combine(_tempDir, "roundtrip.zip");

            // Create archive
            var createOptions = new PlatypusTools.Core.Models.Archive.ArchiveCreateOptions();
            await service.CreateAsync(zipPath, files, createOptions);
            Assert.IsTrue(File.Exists(zipPath));

            // Extract archive
            var extractDir = Path.Combine(_tempDir, "extracted");
            Directory.CreateDirectory(extractDir);
            var extractOptions = new PlatypusTools.Core.Models.Archive.ArchiveExtractOptions
            {
                OutputDirectory = extractDir,
                OverwriteExisting = true
            };
            await service.ExtractAsync(zipPath, extractOptions);

            // Verify
            var extractedFiles = Directory.GetFiles(extractDir, "*.*", SearchOption.AllDirectories);
            Assert.IsTrue(extractedFiles.Length >= 2, "Should have extracted at least 2 files");
        }

        #endregion

        #region Audio Conversion Integration (TASK-303)

        [TestMethod]
        public void AudioFileDetection_IntegrationWithMetadata()
        {
            // Verify audio file detection works consistently across services
            var extensions = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a", ".wma", ".aac" };

            foreach (var ext in extensions)
            {
                var result = MetadataExtractorService.IsAudioFile($"test{ext}");
                Assert.IsTrue(result, $"Should detect {ext} as audio");
            }
        }

        [TestMethod]
        public async Task MetadataExtraction_BatchProcessing()
        {
            // Verify batch metadata extraction doesn't crash with invalid files
            var files = new[] { @"C:\nonexistent\a.mp3", @"C:\nonexistent\b.flac" };
            var results = await MetadataExtractorService.ExtractMetadataAsync(files);

            Assert.IsNotNull(results);
            // Non-existent files should result in empty list
        }

        #endregion

        #region Forensics Pipeline Integration Tests

        [TestMethod]
        public void ForensicsResult_AggregateFindings()
        {
            var result = new PlatypusTools.Core.Models.ForensicsAnalysisResult();

            // Verify the result can aggregate findings from different sources
            result.AllFindings.Add(new PlatypusTools.Core.Models.ForensicsFinding
            {
                Title = "Suspicious Process",
                Type = PlatypusTools.Core.Models.ForensicsFindingType.Process,
                Severity = PlatypusTools.Core.Models.ForensicsSeverity.High
            });

            result.AllFindings.Add(new PlatypusTools.Core.Models.ForensicsFinding
            {
                Title = "Anomalous File",
                Type = PlatypusTools.Core.Models.ForensicsFindingType.FileSystem,
                Severity = PlatypusTools.Core.Models.ForensicsSeverity.Medium
            });

            Assert.AreEqual(2, result.AllFindings.Count);
            Assert.AreEqual(1, result.AllFindings.Count(f => f.Severity == PlatypusTools.Core.Models.ForensicsSeverity.High));
        }

        [TestMethod]
        public void PcapModels_NetworkAnalysis()
        {
            var result = new PcapParserService.PcapAnalysisResult();

            result.Packets.Add(new PcapParserService.NetworkPacket
            {
                SourceIP = System.Net.IPAddress.Parse("192.168.1.1"),
                DestinationIP = System.Net.IPAddress.Parse("10.0.0.1"),
                Protocol = PcapParserService.ProtocolType.TCP,
                CapturedLength = 1500
            });

            result.DnsQueries.Add(new PcapParserService.DnsQuery
            {
                QueryName = "example.com",
                QueryType = "A"
            });

            Assert.AreEqual(1, result.Packets.Count);
            Assert.AreEqual(1, result.DnsQueries.Count);
        }

        #endregion
    }
}
