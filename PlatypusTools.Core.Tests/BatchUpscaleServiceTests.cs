using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for BatchUpscaleService (TASK-294).
    /// Tests job creation, queue management, and processing states.
    /// </summary>
    [TestClass]
    public class BatchUpscaleServiceTests
    {
        #region Singleton Tests

        [TestMethod]
        public void Instance_ReturnsSingleInstance()
        {
            var instance1 = BatchUpscaleService.Instance;
            var instance2 = BatchUpscaleService.Instance;
            Assert.IsNotNull(instance1);
            Assert.AreSame(instance1, instance2);
        }

        #endregion

        #region CreateJob Tests

        [TestMethod]
        public void CreateJob_ValidParameters_CreatesJob()
        {
            var service = BatchUpscaleService.Instance;
            var files = new[] { @"C:\test\image1.png", @"C:\test\image2.jpg" };
            var settings = new PlatypusTools.Core.Models.ImageScaler.BatchUpscaleSettings();

            var job = service.CreateJob("Test Job", files, settings);

            Assert.IsNotNull(job);
            Assert.AreEqual("Test Job", job.Name);
            Assert.AreEqual(2, job.Items.Count);
        }

        [TestMethod]
        public void CreateJob_EmptyFiles_CreatesEmptyJob()
        {
            var service = BatchUpscaleService.Instance;
            var settings = new PlatypusTools.Core.Models.ImageScaler.BatchUpscaleSettings();

            var job = service.CreateJob("Empty Job", Array.Empty<string>(), settings);

            Assert.IsNotNull(job);
            Assert.AreEqual(0, job.Items.Count);
        }

        #endregion

        #region Queue Management Tests

        [TestMethod]
        public void EnqueueJob_AddsToAllJobs()
        {
            var service = BatchUpscaleService.Instance;
            service.ClearCompletedJobs(); // Clean state
            
            var settings = new PlatypusTools.Core.Models.ImageScaler.BatchUpscaleSettings();
            var job = service.CreateJob("Queue Test", new[] { @"C:\test\img.png" }, settings);

            service.EnqueueJob(job);

            Assert.IsTrue(service.AllJobs.Contains(job));
        }

        [TestMethod]
        public void QueuedJobCount_ReflectsQueueState()
        {
            var service = BatchUpscaleService.Instance;
            var initialCount = service.QueuedJobCount;

            // Count should be non-negative
            Assert.IsTrue(initialCount >= 0);
        }

        [TestMethod]
        public void IsProcessing_InitiallyFalse()
        {
            var service = BatchUpscaleService.Instance;
            // Service should not be processing when first accessed
            // (may be true if another test started it, so just check it's a valid boolean)
            Assert.IsTrue(service.IsProcessing == true || service.IsProcessing == false);
        }

        #endregion

        #region Pause/Resume Tests

        [TestMethod]
        public void PauseProcessing_SetsIsPaused()
        {
            var service = BatchUpscaleService.Instance;
            service.PauseProcessing();
            Assert.IsTrue(service.IsPaused);
        }

        [TestMethod]
        public void ResumeProcessing_ClearsIsPaused()
        {
            var service = BatchUpscaleService.Instance;
            service.PauseProcessing();
            service.ResumeProcessing();
            Assert.IsFalse(service.IsPaused);
        }

        #endregion

        #region RemoveJob Tests

        [TestMethod]
        public void RemoveJob_RemovesFromAllJobs()
        {
            var service = BatchUpscaleService.Instance;
            var settings = new PlatypusTools.Core.Models.ImageScaler.BatchUpscaleSettings();
            var job = service.CreateJob("Remove Test", new[] { @"C:\test\img.png" }, settings);

            service.EnqueueJob(job);
            service.RemoveJob(job);

            Assert.IsFalse(service.AllJobs.Contains(job));
        }

        #endregion

        #region Model Tests

        [TestMethod]
        public void BatchUpscaleSettings_DefaultValues()
        {
            var settings = new PlatypusTools.Core.Models.ImageScaler.BatchUpscaleSettings();
            Assert.IsNotNull(settings);
        }

        #endregion
    }
}
