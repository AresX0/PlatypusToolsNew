using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class VideoCombinerTests
    {
        [TestMethod]
        public async Task CombineAsync_FFmpegMissing_ReturnsNotFoundResult()
        {
            var combiner = new VideoCombinerService();
            var tmp1 = Path.GetTempFileName();
            var tmp2 = Path.GetTempFileName();
            var outp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mp4");
            try
            {
                var res = await combiner.CombineAsync(new[] { tmp1, tmp2 }, outp);
                Assert.IsNotNull(res);
                Assert.AreEqual(-1, res.ExitCode);
                StringAssert.Contains(res.StdErr ?? string.Empty, "ffmpeg not found");
            }
            finally
            {
                try { File.Delete(tmp1); } catch { }
                try { File.Delete(tmp2); } catch { }
                try { File.Delete(outp); } catch { }
            }
        }
    }
}