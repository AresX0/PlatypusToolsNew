using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class FileCleanerMappingTests
    {
        [TestMethod]
        public void ComputeBackupMapping_PreservesRelativePaths()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_filecleaner_relmap_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var nested = Path.Combine(tmp, "sub", "sub2");
            Directory.CreateDirectory(nested);
            var f = Path.Combine(nested, "e.txt");
            File.WriteAllText(f, "x");

            var backup = Path.Combine(tmp, "backup");
            var mapping = FileCleaner.ComputeBackupMapping(new[] { f }, backup, tmp);
            Assert.IsTrue(mapping.ContainsKey(f));
            var dest = mapping[f];
            var expected = Path.Combine(backup, "sub", "sub2", Path.GetFileName(f));
            Assert.AreEqual(expected, dest);
        }
    }
}