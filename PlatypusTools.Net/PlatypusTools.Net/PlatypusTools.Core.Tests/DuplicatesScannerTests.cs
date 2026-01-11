using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class DuplicatesScannerTests
    {
        [TestMethod]
        public void FindDuplicates_EmptyDirectory_ReturnsEmpty()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_dups_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var groups = DuplicatesScanner.FindDuplicates(new[] { tmp }).ToList();
            Assert.IsTrue(groups.Count == 0);
        }
    }
}