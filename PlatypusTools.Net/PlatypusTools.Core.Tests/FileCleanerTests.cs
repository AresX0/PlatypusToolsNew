using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class FileCleanerTests
    {
        [TestMethod]
        public void GetFiles_ReturnsCreatedFile()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_filecleaner_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var f = Path.Combine(tmp, "a.txt");
            File.WriteAllText(f, "x");

            var files = FileCleaner.GetFiles(tmp, new[] { "*.txt" }).ToList();
            Assert.IsTrue(files.Any(s => s.EndsWith("a.txt")));
        }

        [TestMethod]
        public void RemoveFiles_DryRun_DoesNotDelete()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_filecleaner_test2");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var f = Path.Combine(tmp, "b.txt");
            File.WriteAllText(f, "x");

            var removed = FileCleaner.RemoveFiles(new[] { f }, dryRun: true);
            Assert.IsTrue(File.Exists(f));
            Assert.IsTrue(removed.Contains(f));
        }

        [TestMethod]
        public void RemoveFiles_Backup_CreatesBackupAndDeletes()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_filecleaner_delete_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var f = Path.Combine(tmp, "c.txt");
            File.WriteAllText(f, "x");

            var backup = Path.Combine(tmp, "backup");
            var removed = FileCleaner.RemoveFiles(new[] { f }, dryRun: false, backupPath: backup, basePath: tmp);
            Assert.IsFalse(File.Exists(f));
            var backed = Path.Combine(backup, Path.GetFileName(f));
            Assert.IsTrue(File.Exists(backed));
            Assert.IsTrue(removed.Contains(f));
        }

        [TestMethod]
        public void RemoveFiles_Backup_PreservesRelativeStructure()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_filecleaner_relbackup_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var nested = Path.Combine(tmp, "sub", "sub2");
            Directory.CreateDirectory(nested);
            var f = Path.Combine(nested, "e.txt");
            File.WriteAllText(f, "x");

            var backup = Path.Combine(tmp, "backup");
            var removed = FileCleaner.RemoveFiles(new[] { f }, dryRun: false, backupPath: backup, basePath: tmp);
            Assert.IsFalse(File.Exists(f));
            var backed = Path.Combine(backup, "sub", "sub2", Path.GetFileName(f));
            Assert.IsTrue(File.Exists(backed));
            Assert.IsTrue(removed.Contains(f));
        }

        [TestMethod]
        public void RemoveFiles_BackupFailure_SkipsDelete()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_filecleaner_backupfail");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var f = Path.Combine(tmp, "d.txt");
            File.WriteAllText(f, "x");

            var backup = Path.Combine(tmp, "backup");
            Directory.CreateDirectory(backup);
            var dest = Path.Combine(backup, Path.GetFileName(f));
            File.WriteAllText(dest, "locked");
            File.SetAttributes(dest, FileAttributes.ReadOnly);

            var removed = FileCleaner.RemoveFiles(new[] { f }, dryRun: false, backupPath: backup, basePath: tmp);
            Assert.IsTrue(File.Exists(f)); // should not be deleted
            Assert.IsFalse(removed.Contains(f));

            // cleanup - remove read-only so directory can be deleted
            File.SetAttributes(dest, FileAttributes.Normal);
        }
    }
}