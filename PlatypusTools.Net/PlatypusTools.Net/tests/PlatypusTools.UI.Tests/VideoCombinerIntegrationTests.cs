using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.Core.Services;
using System.IO;
using System;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class VideoCombinerIntegrationTests
    {
        [TestMethod]
        public async Task CombineAsync_WithFakeFfmpeg_ReportsProgressAndSucceeds()
        {
            string fakeDir = null;
            var cur = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(cur))
            {
                var cand = Path.GetFullPath(Path.Combine(cur, "..\\..\\..\\tests\\tools\\fake-ffmpeg"));
                if (Directory.Exists(cand)) { fakeDir = cand; break; }
                cur = Directory.GetParent(cur)?.FullName;
            }
            if (string.IsNullOrEmpty(fakeDir)) fakeDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tests\\tools\\fake-ffmpeg"));
            Assert.IsTrue(Directory.Exists(fakeDir), "fake-ffmpeg directory missing: " + fakeDir);

            var oldPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            Environment.SetEnvironmentVariable("PATH", fakeDir + Path.PathSeparator + oldPath);

            var tmp1 = Path.GetTempFileName();
            var tmp2 = Path.GetTempFileName();
            var outp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".mp4");

            try
            {
                var comb = new PlatypusTools.Core.Services.VideoCombinerService();
                var vm = new VideoCombinerViewModel(comb);
                vm.Files.Add(tmp1);
                vm.Files.Add(tmp2);
                vm.OutputPath = outp;

                await vm.CombineAsync();

                Assert.IsTrue(vm.ProgressPercent >= 0);
                StringAssert.Contains(vm.Log, "Exit:");
            }
            finally
            {
                Environment.SetEnvironmentVariable("PATH", oldPath);
                try { File.Delete(tmp1); } catch { }
                try { File.Delete(tmp2); } catch { }
                try { File.Delete(outp); } catch { }
            }
        }
    }
}