using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services.Forensics;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    /// <summary>
    /// Unit tests for IOCScannerService.
    /// </summary>
    [TestClass]
    public class IOCScannerServiceTests
    {
        private IOCScannerService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = new IOCScannerService();
            _service.ClearIOCs(); // Start fresh
        }

        [TestMethod]
        public void OperationName_ShouldReturnCorrectName()
        {
            Assert.AreEqual("IOC Scanner", _service.OperationName);
        }

        [TestMethod]
        public void AddIOC_ShouldAddToDatabase()
        {
            var ioc = new IOC
            {
                Type = IOCType.MD5,
                Value = "d41d8cd98f00b204e9800998ecf8427e",
                Description = "Empty file hash"
            };

            _service.AddIOC(ioc);

            Assert.AreEqual(1, _service.IOCs.Count);
            Assert.AreEqual(IOCType.MD5, _service.IOCs[0].Type);
        }

        [TestMethod]
        public void AddIOC_ShouldNotAddDuplicates()
        {
            var ioc1 = new IOC { Type = IOCType.IPv4, Value = "192.168.1.1" };
            var ioc2 = new IOC { Type = IOCType.IPv4, Value = "192.168.1.1" };

            _service.AddIOC(ioc1);
            _service.AddIOC(ioc2);

            Assert.AreEqual(1, _service.IOCs.Count);
        }

        [TestMethod]
        public void RemoveIOC_ShouldRemoveFromDatabase()
        {
            var ioc = new IOC { Type = IOCType.Domain, Value = "malware.com" };
            _service.AddIOC(ioc);

            _service.RemoveIOC(ioc.Id);

            Assert.AreEqual(0, _service.IOCs.Count);
        }

        [TestMethod]
        public void AddIOCs_ShouldAddMultiple()
        {
            var iocs = new[]
            {
                new IOC { Type = IOCType.SHA256, Value = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" },
                new IOC { Type = IOCType.IPv4, Value = "10.0.0.1" },
                new IOC { Type = IOCType.URL, Value = "http://evil.com/malware.exe" }
            };

            _service.AddIOCs(iocs);

            Assert.AreEqual(3, _service.IOCs.Count);
        }

        [TestMethod]
        public void ClearIOCs_ShouldRemoveAll()
        {
            _service.AddIOC(new IOC { Type = IOCType.Email, Value = "attacker@evil.com" });
            _service.AddIOC(new IOC { Type = IOCType.Domain, Value = "c2server.net" });

            _service.ClearIOCs();

            Assert.AreEqual(0, _service.IOCs.Count);
        }

        [TestMethod]
        public void AddFeed_ShouldAddToFeeds()
        {
            // Clear default feeds first by creating new service
            var feed = new IOCFeed
            {
                Name = "Test Feed",
                Url = "https://example.com/feed.txt",
                FeedType = IOCFeedType.PlainText
            };

            _service.AddFeed(feed);

            Assert.IsTrue(_service.Feeds.Any(f => f.Name == "Test Feed"));
        }

        [TestMethod]
        public async Task ScanDirectoryAsync_WithNonExistentPath_ShouldReturnError()
        {
            var result = await _service.ScanDirectoryAsync(@"C:\NonExistentPath12345");

            Assert.IsTrue(result.Errors.Count > 0);
            Assert.IsTrue(result.Errors[0].Contains("not found"));
        }

        [TestMethod]
        public void ExportResultsToCsv_ShouldGenerateValidCsv()
        {
            var result = new IOCScanResult();
            result.Matches.Add(new IOCMatch
            {
                IOC = new IOC { Type = IOCType.MD5, Value = "abc123", ThreatName = "Test Malware" },
                MatchLocation = @"C:\test.exe",
                Confidence = 0.95
            });

            var csv = _service.ExportResultsToCsv(result);

            Assert.IsTrue(csv.Contains("MD5"));
            Assert.IsTrue(csv.Contains("abc123"));
            Assert.IsTrue(csv.Contains("Test Malware"));
        }

        [TestMethod]
        public void IOCTypes_ShouldCoverCommonIndicators()
        {
            // Verify IOCType enum has expected values
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.MD5));
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.SHA1));
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.SHA256));
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.IPv4));
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.Domain));
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.URL));
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.Email));
            Assert.IsTrue(Enum.IsDefined(typeof(IOCType), IOCType.FileName));
        }
    }
}
