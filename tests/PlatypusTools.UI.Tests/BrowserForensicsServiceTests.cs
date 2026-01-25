using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services.Forensics;
using System;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    /// <summary>
    /// Unit tests for BrowserForensicsService.
    /// </summary>
    [TestClass]
    public class BrowserForensicsServiceTests
    {
        private BrowserForensicsService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new BrowserForensicsService();
        }

        [TestMethod]
        public void OperationName_ShouldReturnCorrectName()
        {
            Assert.AreEqual("Browser Forensics", _service.OperationName);
        }

        [TestMethod]
        public void DefaultOptions_ShouldExtractAllArtifacts()
        {
            Assert.IsTrue(_service.ExtractHistory);
            Assert.IsTrue(_service.ExtractCookies);
            Assert.IsTrue(_service.ExtractDownloads);
            Assert.IsTrue(_service.ExtractCredentials);
            Assert.IsTrue(_service.ExtractBookmarks);
        }

        [TestMethod]
        public void ExtractionOptions_CanBeModified()
        {
            _service.ExtractHistory = false;
            _service.ExtractCookies = false;

            Assert.IsFalse(_service.ExtractHistory);
            Assert.IsFalse(_service.ExtractCookies);
        }

        [TestMethod]
        public void DateFilter_CanBeSet()
        {
            _service.StartDate = DateTime.Now.AddDays(-30);
            _service.EndDate = DateTime.Now;

            Assert.IsNotNull(_service.StartDate);
            Assert.IsNotNull(_service.EndDate);
        }

        [TestMethod]
        public void BrowserHistoryEntry_ShouldStoreAllFields()
        {
            var entry = new BrowserHistoryEntry
            {
                Browser = "Chrome",
                Profile = "Default",
                Url = "https://example.com",
                Title = "Example",
                VisitTime = DateTime.Now,
                VisitCount = 5
            };

            Assert.AreEqual("Chrome", entry.Browser);
            Assert.AreEqual("https://example.com", entry.Url);
            Assert.AreEqual(5, entry.VisitCount);
        }

        [TestMethod]
        public void BrowserCookie_ShouldStoreSecurityFlags()
        {
            var cookie = new BrowserCookie
            {
                Host = ".example.com",
                Name = "session",
                IsSecure = true,
                IsHttpOnly = true,
                IsPersistent = false
            };

            Assert.IsTrue(cookie.IsSecure);
            Assert.IsTrue(cookie.IsHttpOnly);
            Assert.IsFalse(cookie.IsPersistent);
        }

        [TestMethod]
        public void BrowserDownload_ShouldTrackState()
        {
            var download = new BrowserDownload
            {
                Url = "http://example.com/file.zip",
                TargetPath = @"C:\Downloads\file.zip",
                TotalBytes = 1024 * 1024,
                State = DownloadState.Complete
            };

            Assert.AreEqual(DownloadState.Complete, download.State);
            Assert.AreEqual(1024 * 1024, download.TotalBytes);
        }

        [TestMethod]
        public void BrowserCredential_ShouldStoreMetadata()
        {
            var cred = new BrowserCredential
            {
                Url = "https://login.example.com",
                Username = "user@example.com",
                TimesUsed = 10,
                DateCreated = DateTime.Now.AddMonths(-6)
            };

            Assert.AreEqual("user@example.com", cred.Username);
            Assert.AreEqual(10, cred.TimesUsed);
        }

        [TestMethod]
        public void BrowserBookmark_ShouldStoreFolder()
        {
            var bookmark = new BrowserBookmark
            {
                Url = "https://github.com",
                Title = "GitHub",
                Folder = "Development",
                DateAdded = DateTime.Now
            };

            Assert.AreEqual("Development", bookmark.Folder);
        }

        [TestMethod]
        public void ForensicsResult_ShouldCalculateTotalArtifacts()
        {
            var result = new BrowserForensicsResult();
            result.History.Add(new BrowserHistoryEntry());
            result.History.Add(new BrowserHistoryEntry());
            result.Cookies.Add(new BrowserCookie());
            result.Downloads.Add(new BrowserDownload());

            Assert.AreEqual(4, result.TotalArtifacts);
        }

        [TestMethod]
        public void ExportHistoryToCsv_ShouldGenerateValidCsv()
        {
            var result = new BrowserForensicsResult();
            result.History.Add(new BrowserHistoryEntry
            {
                Browser = "Firefox",
                Profile = "default",
                Url = "https://test.com",
                Title = "Test Page",
                VisitTime = DateTime.Now,
                VisitCount = 3
            });

            var csv = _service.ExportHistoryToCsv(result);

            Assert.IsTrue(csv.Contains("Browser,Profile,URL,Title,VisitTime,VisitCount"));
            Assert.IsTrue(csv.Contains("Firefox"));
            Assert.IsTrue(csv.Contains("https://test.com"));
        }

        [TestMethod]
        public void DownloadState_ShouldHaveAllStates()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(DownloadState), DownloadState.InProgress));
            Assert.IsTrue(Enum.IsDefined(typeof(DownloadState), DownloadState.Complete));
            Assert.IsTrue(Enum.IsDefined(typeof(DownloadState), DownloadState.Cancelled));
            Assert.IsTrue(Enum.IsDefined(typeof(DownloadState), DownloadState.Interrupted));
        }
    }
}
