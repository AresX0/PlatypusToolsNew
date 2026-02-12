using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services;
using System;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Tests for <see cref="UpdateService"/> - application update checking and downloading service.
    /// </summary>
    [TestClass]
    public class UpdateServiceTests
    {
        #region UpdateInfo Tests

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_ReturnsBytes_WhenLessThan1KB()
        {
            var info = new UpdateInfo { FileSize = 500 };
            Assert.AreEqual("500 B", info.FileSizeDisplay);
        }

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_ReturnsKB_WhenLessThan1MB()
        {
            var info = new UpdateInfo { FileSize = 2048 };
            Assert.AreEqual("2.00 KB", info.FileSizeDisplay);
        }

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_ReturnsMB_WhenLessThan1GB()
        {
            var info = new UpdateInfo { FileSize = 5 * 1024 * 1024 }; // 5MB
            Assert.AreEqual("5.00 MB", info.FileSizeDisplay);
        }

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_ReturnsGB_WhenGreaterThan1GB()
        {
            var info = new UpdateInfo { FileSize = 2L * 1024 * 1024 * 1024 }; // 2GB
            Assert.AreEqual("2.00 GB", info.FileSizeDisplay);
        }

        [TestMethod]
        public void UpdateInfo_DefaultValues_AreEmpty()
        {
            var info = new UpdateInfo();
            
            Assert.AreEqual(string.Empty, info.Version);
            Assert.AreEqual(string.Empty, info.TagName);
            Assert.AreEqual(string.Empty, info.Name);
            Assert.AreEqual(string.Empty, info.Body);
            Assert.AreEqual(string.Empty, info.HtmlUrl);
            Assert.AreEqual(string.Empty, info.DownloadUrl);
            Assert.AreEqual(string.Empty, info.FileName);
            Assert.AreEqual(0, info.FileSize);
        }

        [TestMethod]
        public void UpdateInfo_Properties_CanBeSet()
        {
            var now = DateTime.Now;
            var info = new UpdateInfo
            {
                Version = "3.4.0.10",
                TagName = "v3.4.0.10",
                Name = "Release v3.4.0.10",
                Body = "Bug fixes and improvements",
                PublishedAt = now,
                HtmlUrl = "https://github.com/test/releases/v3.4.0.10",
                DownloadUrl = "https://github.com/test/releases/download/v3.4.0.10/test.msi",
                FileName = "test.msi",
                FileSize = 1024 * 1024 * 100 // 100MB
            };

            Assert.AreEqual("3.4.0.10", info.Version);
            Assert.AreEqual("v3.4.0.10", info.TagName);
            Assert.AreEqual("Release v3.4.0.10", info.Name);
            Assert.AreEqual("Bug fixes and improvements", info.Body);
            Assert.AreEqual(now, info.PublishedAt);
            Assert.AreEqual("https://github.com/test/releases/v3.4.0.10", info.HtmlUrl);
            Assert.AreEqual("https://github.com/test/releases/download/v3.4.0.10/test.msi", info.DownloadUrl);
            Assert.AreEqual("test.msi", info.FileName);
            Assert.AreEqual(104857600, info.FileSize);
        }

        #endregion

        #region UpdateService Instance Tests

        [TestMethod]
        public void Instance_ReturnsSameInstance()
        {
            var instance1 = UpdateService.Instance;
            var instance2 = UpdateService.Instance;
            
            Assert.AreSame(instance1, instance2);
        }

        [TestMethod]
        public void Instance_IsNotNull()
        {
            Assert.IsNotNull(UpdateService.Instance);
        }

        [TestMethod]
        public void CurrentVersion_IsNotNullOrEmpty()
        {
            var service = UpdateService.Instance;
            Assert.IsFalse(string.IsNullOrEmpty(service.CurrentVersion));
        }

        #endregion

        #region Edge Cases

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_HandlesZero()
        {
            var info = new UpdateInfo { FileSize = 0 };
            Assert.AreEqual("0 B", info.FileSizeDisplay);
        }

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_HandlesExactKB()
        {
            var info = new UpdateInfo { FileSize = 1024 };
            Assert.AreEqual("1.00 KB", info.FileSizeDisplay);
        }

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_HandlesExactMB()
        {
            var info = new UpdateInfo { FileSize = 1024 * 1024 };
            Assert.AreEqual("1.00 MB", info.FileSizeDisplay);
        }

        [TestMethod]
        public void UpdateInfo_FileSizeDisplay_HandlesExactGB()
        {
            var info = new UpdateInfo { FileSize = 1024L * 1024 * 1024 };
            Assert.AreEqual("1.00 GB", info.FileSizeDisplay);
        }

        #endregion
    }
}
