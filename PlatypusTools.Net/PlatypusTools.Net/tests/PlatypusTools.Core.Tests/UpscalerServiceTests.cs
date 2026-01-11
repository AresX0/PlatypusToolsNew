using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class UpscalerServiceTests
    {
        [TestMethod]
        public async Task RunAsync_MissingVideo2x_ReturnsNotFound()
        {
            var svc = new UpscalerService();
            var tmp = Path.GetTempFileName();
            var outp = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mp4");
            var res = await svc.RunAsync(tmp, outp, 2);
            Assert.IsNotNull(res);
            Assert.AreEqual(-1, res.ExitCode);
            StringAssert.Contains(res.StdErr ?? string.Empty, "video2x not found");
        }
    }
}