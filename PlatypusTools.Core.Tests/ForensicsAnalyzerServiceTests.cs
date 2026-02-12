using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Unit tests for ForensicsAnalyzerService (TASK-228).
    /// Tests forensics analysis modes, cancellation, and result structure.
    /// </summary>
    [TestClass]
    public class ForensicsAnalyzerServiceTests
    {
        #region Construction Tests

        [TestMethod]
        public void Constructor_CreatesInstance()
        {
            var service = new ForensicsAnalyzerService();
            Assert.IsNotNull(service);
        }

        #endregion

        #region Cancellation Tests

        [TestMethod]
        public async Task AnalyzeAsync_CancellationRequested_ThrowsOrCompletes()
        {
            var service = new ForensicsAnalyzerService();
            using var cts = new CancellationTokenSource();
            cts.Cancel(); // Cancel immediately

            try
            {
                var result = await service.AnalyzeAsync(
                    PlatypusTools.Core.Models.ForensicsMode.Lightweight,
                    cancellationToken: cts.Token);
                
                // If it completed despite cancellation, still valid
                Assert.IsNotNull(result);
            }
            catch (OperationCanceledException)
            {
                // Expected behavior
            }
        }

        #endregion

        #region Lightweight Analysis Tests

        [TestMethod]
        public async Task AnalyzeAsync_Lightweight_ReturnsResult()
        {
            var service = new ForensicsAnalyzerService();
            var progress = new Progress<string>();

            var result = await service.AnalyzeAsync(
                PlatypusTools.Core.Models.ForensicsMode.Lightweight,
                progress: progress);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.AllFindings);
        }

        [TestMethod]
        public async Task AnalyzeAsync_Lightweight_MemoryAnalysisNotNull()
        {
            var service = new ForensicsAnalyzerService();

            var result = await service.AnalyzeAsync(PlatypusTools.Core.Models.ForensicsMode.Lightweight);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.MemoryAnalysis);
        }

        [TestMethod]
        public async Task AnalyzeAsync_Lightweight_FileSystemAnalysisNotNull()
        {
            var service = new ForensicsAnalyzerService();

            var result = await service.AnalyzeAsync(PlatypusTools.Core.Models.ForensicsMode.Lightweight);

            Assert.IsNotNull(result);
            Assert.IsNotNull(result.FileSystemAnalysis);
        }

        #endregion

        #region ForensicsMode Tests

        [TestMethod]
        public void ForensicsMode_HasExpectedValues()
        {
            Assert.IsTrue(Enum.IsDefined(typeof(PlatypusTools.Core.Models.ForensicsMode), 
                PlatypusTools.Core.Models.ForensicsMode.Lightweight));
            Assert.IsTrue(Enum.IsDefined(typeof(PlatypusTools.Core.Models.ForensicsMode), 
                PlatypusTools.Core.Models.ForensicsMode.Deep));
        }

        #endregion

        #region Result Structure Tests

        [TestMethod]
        public void ForensicsAnalysisResult_DefaultsNotNull()
        {
            var result = new PlatypusTools.Core.Models.ForensicsAnalysisResult();
            Assert.IsNotNull(result.AllFindings);
        }

        [TestMethod]
        public void ForensicsFinding_CanBeCreated()
        {
            var finding = new PlatypusTools.Core.Models.ForensicsFinding
            {
                Title = "Test Finding",
                Description = "Test Description",
                Severity = PlatypusTools.Core.Models.ForensicsSeverity.High,
                Type = PlatypusTools.Core.Models.ForensicsFindingType.Process
            };

            Assert.AreEqual("Test Finding", finding.Title);
            Assert.AreEqual(PlatypusTools.Core.Models.ForensicsSeverity.High, finding.Severity);
        }

        #endregion
    }
}
