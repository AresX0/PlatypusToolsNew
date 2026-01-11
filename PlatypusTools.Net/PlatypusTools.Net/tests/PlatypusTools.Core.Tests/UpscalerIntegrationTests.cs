using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class UpscalerIntegrationTests
    {
        [TestMethod]
        public async Task RunAsync_WithFakeVideo2xInPath_SucceedsAndReportsProgress()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "pt_fake_video2x" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                // copy the fake script into the temp dir
                var src = Path.Combine(TestContext.CurrentContext?.TestRunDirectory ?? AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\tests\\tools\\fake-video2x\\video2x.bat");
                // fallback: use repo-relative path
                src = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\..\\tests\\tools\\fake-video2x\\video2x.bat"));
                Assert.IsTrue(File.Exists(src), "Fake video2x script not found at " + src);
                var dst = Path.Combine(tempDir, "video2x.bat");
                File.Copy(src, dst);

                var oldPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
                Environment.SetEnvironmentVariable("PATH", tempDir + Path.PathSeparator + oldPath);

                var svc = new UpscalerService();
                var input = Path.GetTempFileName();
                var output = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".mp4");

                var res = await svc.RunAsync(input, output, 2, null);
                Assert.IsNotNull(res);
                Assert.AreEqual(0, res.ExitCode);
                StringAssert.Contains(res.StdOut ?? string.Empty, "PROGRESS");

                // restore PATH
                Environment.SetEnvironmentVariable("PATH", oldPath);
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}