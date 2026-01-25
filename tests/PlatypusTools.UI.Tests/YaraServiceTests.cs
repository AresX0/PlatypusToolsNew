using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services.Forensics;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    /// <summary>
    /// Unit tests for YaraService.
    /// </summary>
    [TestClass]
    public class YaraServiceTests
    {
        private YaraService _service = null!;

        [TestInitialize]
        public void Setup()
        {
            _service = ForensicsServiceFactory.CreateYaraService();
        }

        [TestMethod]
        public void OperationName_ShouldReturnCorrectName()
        {
            Assert.AreEqual("YARA Scan", _service.OperationName);
        }

        [TestMethod]
        public void YaraPath_ShouldBeConfigurable()
        {
            _service.YaraPath = @"C:\Tools\yara64.exe";
            Assert.AreEqual(@"C:\Tools\yara64.exe", _service.YaraPath);
        }

        [TestMethod]
        public void RulesDirectory_ShouldBeConfigurable()
        {
            _service.RulesDirectory = @"C:\Rules";
            Assert.AreEqual(@"C:\Rules", _service.RulesDirectory);
        }

        [TestMethod]
        public void TargetPath_ShouldBeConfigurable()
        {
            _service.TargetPath = @"C:\Malware";
            Assert.AreEqual(@"C:\Malware", _service.TargetPath);
        }

        [TestMethod]
        public void YaraMatch_ShouldStoreRuleInfo()
        {
            var match = new YaraMatch
            {
                RuleName = "Ransomware_Indicator",
                FilePath = @"C:\suspicious.exe",
                Metadata = "Author: DFIR"
            };

            Assert.AreEqual("Ransomware_Indicator", match.RuleName);
            Assert.AreEqual(@"C:\suspicious.exe", match.FilePath);
        }

        [TestMethod]
        public async Task ScanAsync_WithNoTarget_ShouldReturnEmptyResult()
        {
            _service.TargetPath = string.Empty;
            
            var result = await _service.ScanAsync();

            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Matches.Count);
        }
    }

    /// <summary>
    /// Unit tests for ForensicsServiceFactory.
    /// </summary>
    [TestClass]
    public class ForensicsServiceFactoryTests
    {
        [TestMethod]
        public void CreateVolatilityService_ShouldReturnConfiguredService()
        {
            using var service = ForensicsServiceFactory.CreateVolatilityService();

            Assert.IsNotNull(service);
            Assert.AreEqual("Volatility Analysis", service.OperationName);
        }

        [TestMethod]
        public void CreateKapeService_ShouldReturnConfiguredService()
        {
            using var service = ForensicsServiceFactory.CreateKapeService();

            Assert.IsNotNull(service);
            Assert.AreEqual("KAPE Collection", service.OperationName);
        }

        [TestMethod]
        public void CreatePlasoService_ShouldReturnConfiguredService()
        {
            using var service = ForensicsServiceFactory.CreatePlasoService();

            Assert.IsNotNull(service);
            Assert.AreEqual("Plaso Timeline", service.OperationName);
        }

        [TestMethod]
        public void CreateYaraService_ShouldReturnConfiguredService()
        {
            using var service = ForensicsServiceFactory.CreateYaraService();

            Assert.IsNotNull(service);
            Assert.AreEqual("YARA Scan", service.OperationName);
        }

        [TestMethod]
        public void CreateTaskSchedulerService_ShouldReturnService()
        {
            using var service = ForensicsServiceFactory.CreateTaskSchedulerService();

            Assert.IsNotNull(service);
            Assert.AreEqual("Task Scheduler", service.OperationName);
        }

        [TestMethod]
        public void CreateBrowserForensicsService_ShouldReturnService()
        {
            using var service = ForensicsServiceFactory.CreateBrowserForensicsService();

            Assert.IsNotNull(service);
            Assert.AreEqual("Browser Forensics", service.OperationName);
        }

        [TestMethod]
        public void CreateIOCScannerService_ShouldReturnService()
        {
            using var service = ForensicsServiceFactory.CreateIOCScannerService();

            Assert.IsNotNull(service);
            Assert.AreEqual("IOC Scanner", service.OperationName);
        }

        [TestMethod]
        public void CreateRegistryDiffService_ShouldReturnService()
        {
            using var service = ForensicsServiceFactory.CreateRegistryDiffService();

            Assert.IsNotNull(service);
            Assert.AreEqual("Registry Diff Tool", service.OperationName);
        }

        [TestMethod]
        public void CreatePcapParserService_ShouldReturnService()
        {
            using var service = ForensicsServiceFactory.CreatePcapParserService();

            Assert.IsNotNull(service);
            Assert.AreEqual("PCAP Parser", service.OperationName);
        }

        [TestMethod]
        public void DefaultOutputPath_ShouldBeValid()
        {
            var path = ForensicsServiceFactory.DefaultOutputPath;

            Assert.IsFalse(string.IsNullOrEmpty(path));
            Assert.IsTrue(path.Contains("DFIR"));
        }

        [TestMethod]
        public void DefaultToolsPath_ShouldBeValid()
        {
            var path = ForensicsServiceFactory.DefaultToolsPath;

            Assert.IsFalse(string.IsNullOrEmpty(path));
            Assert.IsTrue(path.Contains("Tools"));
        }

        [TestMethod]
        public void GetToolPath_ShouldReturnFullPath()
        {
            var path = ForensicsServiceFactory.GetToolPath("yara");

            Assert.IsTrue(path.Contains("yara"));
            Assert.IsTrue(path.Contains("Tools"));
        }

        [TestMethod]
        public void GetOutputPath_ShouldReturnFullPath()
        {
            var path = ForensicsServiceFactory.GetOutputPath("Volatility");

            Assert.IsTrue(path.Contains("Volatility"));
            Assert.IsTrue(path.Contains("DFIR"));
        }
    }
}
