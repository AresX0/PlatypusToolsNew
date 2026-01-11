using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class FFmpegProgressParserTests
    {
        [TestMethod]
        public void TryParseOutTimeMs_ParsesOutTimeMs()
        {
            Assert.IsTrue(FFmpegProgressParser.TryParseOutTimeMs("out_time_ms=12345", out var ms));
            Assert.AreEqual(12345, ms);
        }

        [TestMethod]
        public void TryParseOutTime_ParsesOutTime()
        {
            Assert.IsTrue(FFmpegProgressParser.TryParseOutTimeMs("out_time=00:00:12.345", out var ms));
            Assert.AreEqual(12345, ms);
        }

        [TestMethod]
        public void TryParseOutTime_InvalidReturnsFalse()
        {
            Assert.IsFalse(FFmpegProgressParser.TryParseOutTimeMs("foo=bar", out var ms));
        }
    }
}