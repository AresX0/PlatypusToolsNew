using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class PowerShellRunnerTests
    {
        [TestMethod]
        public async Task RunScriptAsync_CapturesOutput()
        {
            var res = await PowerShellRunner.RunScriptAsync("Write-Output 'hello-pt'", timeoutMs: 5000);
            Assert.IsTrue(res.Success || res.ExitCode >= 0);
            Assert.IsTrue(res.StdOut.Contains("hello-pt"));
        }
    }
}