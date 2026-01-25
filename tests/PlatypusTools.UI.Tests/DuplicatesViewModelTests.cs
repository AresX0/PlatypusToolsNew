using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class DuplicatesViewModelTests
    {
        [TestMethod]
        public async Task SelectNewest_SelectsCorrectFile()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_dup_select_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var f1 = Path.Combine(tmp, "old.txt");
            var f2 = Path.Combine(tmp, "new.txt");
            File.WriteAllText(f1, "a");
            System.Threading.Thread.Sleep(1000);
            File.WriteAllText(f2, "a");

            var vm = new DuplicatesViewModel();
            vm.FolderPath = tmp;
            await vm.ScanForDuplicatesAsync();
            Assert.IsTrue(vm.Groups.Count >= 1);
            var grp = vm.Groups.First(g => g.Files.Any(f => f.Path == f1) && g.Files.Any(f => f.Path == f2));
            Assert.AreEqual(2, grp.Files.Count, $"Group files: {string.Join(',', grp.Files.Select(f => Path.GetFileName(f.Path)))}");
            vm.SelectNewestCommand.Execute(null);
            var selected = grp.Files.Where(f => f.IsSelected).ToList();
            if (selected.Count != 1)
            {
                Assert.Fail($"Selected count: {selected.Count}; selected: {string.Join(',', selected.Select(s => Path.GetFileName(s.Path)))}; group: {string.Join(',', grp.Files.Select(f => Path.GetFileName(f.Path)))}");
            }
            Assert.AreEqual("new.txt", Path.GetFileName(selected[0].Path));

            Directory.Delete(tmp, true);
        }

        [TestMethod]
        public void StageFileToStaging_CopiesFile()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_dup_stage_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var f1 = Path.Combine(tmp, "file1.txt");
            File.WriteAllText(f1, "content");

            var vm = new DuplicatesViewModel();
            var dest = vm.StageFileToStaging(f1);
            Assert.IsNotNull(dest);
            Assert.IsTrue(File.Exists(dest));
            Assert.AreEqual("content", File.ReadAllText(dest));

            // Ensure metadata was written
            Assert.IsTrue(File.Exists(dest + ".meta"));
            Assert.AreEqual(f1, File.ReadAllText(dest + ".meta"));

            // Ensure metadata was written
            Assert.IsTrue(File.Exists(dest + ".meta"));
            Assert.AreEqual(f1, File.ReadAllText(dest + ".meta"));

            // Cleanup
            try { Directory.Delete(Path.GetDirectoryName(dest) ?? string.Empty, true); } catch { }
            Directory.Delete(tmp, true);
        }

        [TestMethod]
        public void StagingViewModel_LoadsStagedEntry()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_staging_vm_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var f1 = Path.Combine(tmp, "file1.txt");
            File.WriteAllText(f1, "content");

            var vm = new DuplicatesViewModel();
            var dest = vm.StageFileToStaging(f1);
            var svm = new StagingViewModel();
            svm.LoadStagedFiles();
            Assert.IsTrue(svm.StagedFiles.Any(sf => sf.StagedPath == dest && sf.OriginalPath == f1));

            // Cleanup
            try { Directory.Delete(Path.GetDirectoryName(dest) ?? string.Empty, true); } catch { }
            Directory.Delete(tmp, true);
        }

        [TestMethod]
        public void StagingViewModel_RestoreSelected_RestoresFile()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_staging_restore_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var origDir = Path.Combine(tmp, "orig"); Directory.CreateDirectory(origDir);
            var f1 = Path.Combine(origDir, "file1.txt");
            File.WriteAllText(f1, "original");

            var vm = new DuplicatesViewModel();
            var staged = vm.StageFileToStaging(f1);

            // simulate user removing original to test restore behavior
            File.Delete(f1);

            var svm = new StagingViewModel();
            svm.LoadStagedFiles();
            var entry = svm.StagedFiles.First(sf => sf.StagedPath == staged);
            entry.IsSelected = true;
            svm.RestoreSelected();

            Assert.IsTrue(File.Exists(f1) || File.Exists(Path.Combine(origDir, Path.GetFileNameWithoutExtension(f1) + " (1)" + Path.GetExtension(f1))));

            // Cleanup
            try { Directory.Delete(Path.GetDirectoryName(staged) ?? string.Empty, true); } catch { }
            Directory.Delete(tmp, true);
        }

        [TestMethod]
        public void StagingViewModel_CommitSelected_DeletesOriginalAndStaged()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_staging_commit_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var origDir = Path.Combine(tmp, "orig"); Directory.CreateDirectory(origDir);
            var f1 = Path.Combine(origDir, "file1.txt");
            File.WriteAllText(f1, "original");

            var vm = new DuplicatesViewModel();
            var staged = vm.StageFileToStaging(f1);

            var svm = new StagingViewModel();
            svm.LoadStagedFiles();
            var entry = svm.StagedFiles.First(sf => sf.StagedPath == staged);
            entry.IsSelected = true;
            svm.CommitSelected();

            Assert.IsFalse(File.Exists(f1));
            Assert.IsFalse(File.Exists(staged));

            // Cleanup
            try { Directory.Delete(Path.GetDirectoryName(staged) ?? string.Empty, true); } catch { }
            Directory.Delete(tmp, true);
        }
    }
}