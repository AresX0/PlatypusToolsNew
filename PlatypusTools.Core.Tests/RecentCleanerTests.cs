using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class RecentCleanerTests
    {
        [TestMethod]
        public void RemoveRecentShortcuts_DryRun_NoThrow()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_recent_test");
            Directory.CreateDirectory(tmp);
            var created = Path.Combine(tmp, "dummy.txt");
            File.WriteAllText(created, "x");

            var res = RecentCleaner.RemoveRecentShortcuts(new[] { Path.GetTempPath() }, dryRun: true);
            Assert.IsNotNull(res);
        }

        [TestMethod]
        public void RemoveRecentShortcuts_WithRecentFolderParameter_NoThrow()
        {
            var recentFolder = Path.Combine(Path.GetTempPath(), "pt_recent_folder");
            if (Directory.Exists(recentFolder)) Directory.Delete(recentFolder, true);
            Directory.CreateDirectory(recentFolder);

            var res = RecentCleaner.RemoveRecentShortcuts(new[] { Path.GetTempPath() }, dryRun: true, recentFolder: recentFolder);
            Assert.IsNotNull(res);
        }
    }
}