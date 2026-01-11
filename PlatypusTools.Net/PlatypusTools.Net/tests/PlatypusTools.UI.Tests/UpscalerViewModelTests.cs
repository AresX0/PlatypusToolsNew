using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.Core.Services;
using System.Threading;
using System.Collections.Generic;
using System.Linq;
using System;
namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class UpscalerViewModelTests
    {
        class FakeUpscaler : UpscalerService
        {
            public override async Task<FFmpegResult> RunAsync(string inputPath, string outputPath, int scale = 2, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
            {
                progress?.Report("start");
                await Task.Delay(10, cancellationToken);
                progress?.Report("done");
                return new FFmpegResult { ExitCode = 0, StdOut = "ok", StdErr = "" };
            }
        }

        [TestMethod]
        public async Task UpscaleAsync_ReportsLogsAndTogglesIsRunning()
        {
            var vm = new UpscalerViewModel(new FakeUpscaler());
            vm.Files.Add("a.mp4");
            vm.OutputFolder = System.IO.Path.GetTempPath();

            await vm.UpscaleAsync();

            Assert.IsFalse(vm.IsRunning);
            StringAssert.Contains(vm.Log, "start");
            StringAssert.Contains(vm.Log, "done");
            StringAssert.Contains(vm.Log, "Result: ExitCode=0");
        }
    }
}