using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class FFprobeServiceTests
    {
        [TestMethod]
        public async System.Threading.Tasks.Task GetDurationSecondsAsync_NoFfprobe_ReturnsMinusOne()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                var d = await FFprobeService.GetDurationSecondsAsync(tmp, ffprobePath: null);
                Assert.AreEqual(-1, d);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }

        [TestMethod]
        public async System.Threading.Tasks.Task GetDurationSecondsAsync_FakeFfprobe_ReturnsValue()
        {
            var tmp = Path.GetTempFileName();
            try
            {
                // use the fake ffprobe shim in tests/tools/fake-ffmpeg
                var repoTmp = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\tests\\tools\\fake-ffmpeg"));
                var ffprobe = Path.Combine(repoTmp, "ffprobe.bat");
                Assert.IsTrue(File.Exists(ffprobe), "fake ffprobe not found: " + ffprobe);
                var d = await FFprobeService.GetDurationSecondsAsync(tmp, ffprobePath: ffprobe);
                Assert.IsTrue(d > 0);
            }
            finally { try { File.Delete(tmp); } catch { } }
        }
    }
}