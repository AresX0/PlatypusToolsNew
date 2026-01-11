using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using System.IO;
using System.Linq;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class DuplicatesE2ETests
    {
        [TestMethod]
        public void Scan_SelectOldest_Stage_Commit_DeletesSelected()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_duplicates_e2e_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var f1 = Path.Combine(tmp, "dupe1.txt");
            var f2 = Path.Combine(tmp, "dupe2.txt");

            File.WriteAllText(f1, "samecontent");
            File.WriteAllText(f2, "samecontent");

            // Make f1 older than f2
            File.SetLastWriteTimeUtc(f1, System.DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(f2, System.DateTime.UtcNow);

            var vm = new DuplicatesViewModel();
            vm.FolderPath = tmp;
            vm.ScanCommand.Execute(null);

            Assert.IsTrue(vm.Groups.Count == 1);
            var group = vm.Groups.First();
            Assert.AreEqual(2, group.Files.Count);

            // Select oldest (which should be f1)
            vm.SelectOldestCommand.Execute(null);
            var selected = group.Files.Where(f => f.IsSelected).ToList();
            Assert.AreEqual(1, selected.Count);
            Assert.AreEqual(f1, selected[0].Path);

            // Stage selected
            var stagedPath = vm.StageFileToStaging(selected[0].Path);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(stagedPath));
            Assert.IsTrue(File.Exists(stagedPath));
            Assert.IsTrue(File.Exists(stagedPath + ".meta"));
            Assert.AreEqual(selected[0].Path, File.ReadAllText(stagedPath + ".meta"));

            // Commit via StagingViewModel
            var svm = new StagingViewModel();
            svm.LoadStagedFiles();
            Assert.IsTrue(svm.StagedFiles.Any(s => s.StagedPath == stagedPath));
            var entry = svm.StagedFiles.First(s => s.StagedPath == stagedPath);
            entry.IsSelected = true;
            svm.CommitSelected();

            // Originals should be deleted, staged files removed
            Assert.IsFalse(File.Exists(f1));
            Assert.IsFalse(File.Exists(stagedPath));
            Assert.IsFalse(File.Exists(stagedPath + ".meta"));

            // Cleanup
            try { Directory.Delete(tmp, true); } catch { }
        }
    }
}