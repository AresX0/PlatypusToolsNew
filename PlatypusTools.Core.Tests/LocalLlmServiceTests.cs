using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.Services.AI;
using PlatypusTools.UI.Services.Security;

namespace PlatypusTools.Core.Tests
{
    /// <summary>
    /// Tests around the privacy-critical loopback guard. These exist because
    /// a regression here could leak prompts to the public internet despite
    /// the "Local-only mode" toggle being on.
    /// </summary>
    [TestClass]
    public class LocalLlmServiceTests
    {
        [DataTestMethod]
        [DataRow("localhost", true)]
        [DataRow("LOCALHOST", true)]
        [DataRow("127.0.0.1", true)]
        [DataRow("127.10.20.30", true)]
        [DataRow("::1", true)]
        [DataRow("[::1]", true)]
        [DataRow("0.0.0.0", true)]
        [DataRow("api.anthropic.com", false)]
        [DataRow("8.8.8.8", false)]
        [DataRow("192.168.1.1", false)]
        [DataRow("10.0.0.1", false)]
        [DataRow("", false)]
        public void IsLoopback_ClassifiesHosts(string host, bool expected)
        {
            Assert.AreEqual(expected, LocalLlmService.IsLoopback(host));
        }

        [TestMethod]
        public void DataProtection_RoundTripsPlaintext()
        {
            var original = "sk-test-12345-AB+CD/ef==";
            var encrypted = DataProtectionHelper.Protect(original);
            // On a Windows runner DPAPI is available; on others Protect returns
            // the original unchanged. Either way Unprotect must round-trip.
            var decrypted = DataProtectionHelper.Unprotect(encrypted);
            Assert.AreEqual(original, decrypted);
        }

        [TestMethod]
        public void DataProtection_PassesThroughEmpty()
        {
            Assert.AreEqual(string.Empty, DataProtectionHelper.Protect(""));
            Assert.AreEqual(string.Empty, DataProtectionHelper.Protect(null));
            Assert.AreEqual(string.Empty, DataProtectionHelper.Unprotect(""));
            Assert.AreEqual(string.Empty, DataProtectionHelper.Unprotect(null));
        }
    }
}
