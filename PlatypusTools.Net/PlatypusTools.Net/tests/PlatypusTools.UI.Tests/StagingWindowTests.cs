using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.UI.Views;
using System.IO;
using System.Linq;
using System.Threading;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class StagingWindowTests
    {
        [TestMethod]
        public void StagingWindow_RestoreSelected_RestoresFile_UI()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_staging_win_restore_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var origDir = Path.Combine(tmp, "orig"); Directory.CreateDirectory(origDir);
            var f1 = Path.Combine(origDir, "file1.txt");
            File.WriteAllText(f1, "original");

            var vm = new PlatypusTools.UI.ViewModels.DuplicatesViewModel();
            var staged = vm.StageFileToStaging(f1);

            var thread = new Thread(() =>
            {
                var svm = new StagingViewModel();
                svm.LoadStagedFiles();
                var wnd = new StagingWindow { DataContext = svm };
                wnd.Loaded += (s, e) =>
                {
                    var entry = svm.StagedFiles.First(sf => sf.StagedPath == staged);
                    entry.IsSelected = true;
                    // Execute restore via ViewModel command
                    svm.RestoreSelected();
                    wnd.DialogResult = true;
                    wnd.Close();
                };
                wnd.ShowDialog();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);

            // Assert restored
            Assert.IsTrue(File.Exists(f1) || File.Exists(Path.Combine(origDir, Path.GetFileNameWithoutExtension(f1) + " (1)" + Path.GetExtension(f1))));

            // Cleanup
            try { Directory.Delete(Path.GetDirectoryName(staged) ?? string.Empty, true); } catch { }
            Directory.Delete(tmp, true);
        }

        [TestMethod]
        public void StagingWindow_RemoveSelected_RemovesStaged_UI()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_staging_win_remove_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var f1 = Path.Combine(tmp, "file1.txt");
            File.WriteAllText(f1, "content");

            var vm = new PlatypusTools.UI.ViewModels.DuplicatesViewModel();
            var staged = vm.StageFileToStaging(f1);

            var thread = new Thread(() =>
            {
                var svm = new StagingViewModel();
                svm.LoadStagedFiles();
                var wnd = new StagingWindow { DataContext = svm };
                wnd.Loaded += (s, e) =>
                {
                    var entry = svm.StagedFiles.First(sf => sf.StagedPath == staged);
                    entry.IsSelected = true;
                    svm.RemoveSelected();
                    wnd.DialogResult = true;
                    wnd.Close();
                };
                wnd.ShowDialog();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);

            Assert.IsFalse(File.Exists(staged));

            // Cleanup
            try { Directory.Delete(Path.GetDirectoryName(staged) ?? string.Empty, true); } catch { }
            Directory.Delete(tmp, true);
        }

        [TestMethod]
        public void StagingWindow_CommitSelected_DeletesOriginalAndStaged_UI()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_staging_win_commit_test");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var origDir = Path.Combine(tmp, "orig"); Directory.CreateDirectory(origDir);
            var f1 = Path.Combine(origDir, "file1.txt");
            File.WriteAllText(f1, "original");

            var vm = new PlatypusTools.UI.ViewModels.DuplicatesViewModel();
            var staged = vm.StageFileToStaging(f1);

            var thread = new Thread(() =>
            {
                var svm = new StagingViewModel();
                svm.LoadStagedFiles();
                var wnd = new StagingWindow { DataContext = svm };
                wnd.Loaded += (s, e) =>
                {
                    var entry = svm.StagedFiles.First(sf => sf.StagedPath == staged);
                    entry.IsSelected = true;
                    svm.CommitSelected();
                    wnd.DialogResult = true;
                    wnd.Close();
                };
                wnd.ShowDialog();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join(5000);

            Assert.IsFalse(File.Exists(f1));
            Assert.IsFalse(File.Exists(staged));

            // Cleanup
            try { Directory.Delete(Path.GetDirectoryName(staged) ?? string.Empty, true); } catch { }
            Directory.Delete(tmp, true);
        }
    }
}