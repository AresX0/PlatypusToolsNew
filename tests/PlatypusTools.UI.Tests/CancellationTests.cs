using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class CancellationTests
    {
        [TestMethod]
        public async Task VideoCombiner_Cancel_StopsCombining()
        {
            // Arrange
            var combiner = new VideoCombinerService();
            var vm = new MockVideoCombinerViewModel(combiner);
            vm.Files.Add("test1.mp4");
            vm.Files.Add("test2.mp4");
            vm.OutputPath = "output.mp4";

            // Act
            var task = vm.CombineAsync();
            await Task.Delay(50); // Let it start
            vm.CancelCommand.Execute(null);
            await task;

            // Assert
            Assert.IsTrue(vm.Log.Contains("Cancellation requested") || vm.Log.Contains("Operation cancelled"), "Should log cancellation");
            Assert.IsFalse(vm.IsRunning, "Should not be running after cancellation");
        }

        [TestMethod]
        public async Task Upscaler_Cancel_StopsUpscaling()
        {
            // Arrange
            var service = new MockUpscalerService();
            var vm = new UpscalerViewModel(service);
            vm.Files.Add("test.mp4");
            vm.SelectedScale = 2;

            // Act
            var task = vm.UpscaleAsync();
            await Task.Delay(50); // Let it start
            vm.CancelCommand.Execute(null);
            await task;

            // Assert
            Assert.IsTrue(vm.Log.Contains("Cancellation requested") || vm.Log.Contains("Cancelled"), "Should log cancellation");
            Assert.IsFalse(vm.IsRunning, "Should not be running after cancellation");
        }

        [TestMethod]
        public async Task ImageConverter_Cancel_StopsConverting()
        {
            // Arrange
            async Task<bool> slowConverter(string src, string dst, int? mw, int? mh, long q, SvgConversionMode mode)
            {
                await Task.Delay(200);
                return true;
            }
            var vm = new ImageConverterViewModel(slowConverter);
            vm.Files.Add("test1.jpg");
            vm.Files.Add("test2.jpg");
            vm.Files.Add("test3.jpg");
            vm.OutputFolder = "output";

            // Act
            var task = vm.ConvertAsync();
            await Task.Delay(50); // Let it start
            vm.CancelCommand.Execute(null);
            await task;

            // Assert
            Assert.IsTrue(vm.Log.Contains("Cancellation requested") || vm.Log.Contains("cancelled"), "Should log cancellation");
            Assert.IsFalse(vm.IsRunning, "Should not be running after cancellation");
        }

        [TestMethod]
        public void AllViewModels_CancelCommand_EnabledOnlyWhenRunning()
        {
            // Arrange & Act
            var combinerVm = new MockVideoCombinerViewModel(new VideoCombinerService());
            var upscalerVm = new UpscalerViewModel(new MockUpscalerService());
            var imageVm = new ImageConverterViewModel();

            // Assert - initial state
            Assert.IsFalse(combinerVm.CancelCommand.CanExecute(null), "VideoCombiner cancel should be disabled when not running");
            Assert.IsFalse(upscalerVm.CancelCommand.CanExecute(null), "Upscaler cancel should be disabled when not running");
            Assert.IsFalse(imageVm.CancelCommand.CanExecute(null), "ImageConverter cancel should be disabled when not running");

            // Simulate running
            combinerVm.IsRunning = true;
            upscalerVm.IsRunning = true;
            imageVm.IsRunning = true;

            // Assert - running state
            Assert.IsTrue(combinerVm.CancelCommand.CanExecute(null), "VideoCombiner cancel should be enabled when running");
            Assert.IsTrue(upscalerVm.CancelCommand.CanExecute(null), "Upscaler cancel should be enabled when running");
            Assert.IsTrue(imageVm.CancelCommand.CanExecute(null), "ImageConverter cancel should be enabled when running");
        }

        // Mock VideoCombinerViewModel that doesn't require real ffmpeg
        private class MockVideoCombinerViewModel : VideoCombinerViewModel
        {
            public MockVideoCombinerViewModel(VideoCombinerService combiner) : base(combiner) { }

            public override async Task CombineAsync()
            {
                IsRunning = true;
                Log = string.Empty;
                using var cts = new CancellationTokenSource();
                
                // Simulate slow operation
                var cancelTask = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(5000, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        Log += "Operation cancelled.\n";
                    }
                });

                // Watch for cancel command
                var checkTask = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested && IsRunning)
                    {
                        await Task.Delay(10);
                        if (Log.Contains("Cancellation requested"))
                        {
                            cts.Cancel();
                            break;
                        }
                    }
                });

                await Task.WhenAny(cancelTask, checkTask);
                cts.Cancel();
                IsRunning = false;
            }
        }

        // Mock UpscalerService that doesn't require real video2x
        private class MockUpscalerService : UpscalerService
        {
            public override async Task<FFmpegResult> RunAsync(string src, string dst, int scale, IProgress<string>? progress, CancellationToken ct)
            {
                progress?.Report($"Upscaling {src} at scale {scale}x");
                await Task.Delay(5000, ct); // Simulate slow processing
                progress?.Report("Done");
                return new FFmpegResult { ExitCode = 0, StdOut = "success", StdErr = "" };
            }
        }
    }
}
