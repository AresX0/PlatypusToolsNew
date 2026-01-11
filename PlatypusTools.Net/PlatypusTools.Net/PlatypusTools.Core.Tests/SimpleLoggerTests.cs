using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class SimpleLoggerTests
    {
        [TestMethod]
        public void Logger_WritesToFile()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_logger_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var log = Path.Combine(tmp, "log.txt");

            SimpleLogger.LogFile = log;
            SimpleLogger.MinLevel = LogLevel.Info;
            SimpleLogger.Info("Test entry");

            Assert.IsTrue(File.Exists(log), "Log file should exist");
            var content = File.ReadAllText(log);
            Assert.IsTrue(content.Contains("Test entry"), "Log file should contain the logged message");
        }
    }
}