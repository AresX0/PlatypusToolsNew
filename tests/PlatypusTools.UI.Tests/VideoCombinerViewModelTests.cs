using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Models;
using System.Collections.Generic;
using System.Linq;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class VideoCombinerViewModelTests
    {
        class FakeCombiner : VideoCombinerService
        {
            public override async Task<FFmpegResult> CombineAsync(IEnumerable<string> inputFiles, string outputFile, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            {
                progress?.Report("progress=test-start");
                await Task.Delay(20, cancellationToken);
                progress?.Report("progress=test-end");
                return new FFmpegResult { ExitCode = 0, StdOut = "ok", StdErr = "" };
            }
        }

        [TestMethod]
        public async Task CombineAsync_ReportsProgressAndTogglesIsRunning()
        {
            var comb = new FakeCombiner();
            var vm = new VideoCombinerViewModel(comb);
            vm.Files.Add("a");
            vm.OutputPath = "out.mp4";

            var task = vm.CombineAsync();

            // should be running immediately
            Assert.IsTrue(vm.IsRunning);

            await task;
            
            // Allow Progress<T> callbacks to complete (they use SynchronizationContext.Post)
            await Task.Delay(100);

            Assert.IsFalse(vm.IsRunning);
            StringAssert.Contains(vm.Log, "progress=test-start");
            StringAssert.Contains(vm.Log, "progress=test-end");
            StringAssert.Contains(vm.Log, "Exit: 0");
        }
    }
}