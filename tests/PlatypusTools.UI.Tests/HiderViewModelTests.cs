using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.UI.ViewModels;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Tests
{
    [TestClass]
    public class HiderViewModelTests
    {
        [TestMethod]
        public void AddAndRemove_RecordPersistsToConfig()
        {
            var tmpCfg = Path.Combine(Path.GetTempPath(), "pt_hider_ui_test.json");
            if (File.Exists(tmpCfg)) File.Delete(tmpCfg);

            var vm = new HiderViewModel(tmpCfg);

            vm.NewFolderPath = Path.Combine(Path.GetTempPath(), "pt_hider_ui_sample");
            vm.AddFolderCommand.Execute(null);

            Assert.AreEqual(1, vm.Records.Count);
            Assert.IsTrue(File.Exists(tmpCfg));

            // Remove
            var rec = vm.Records.First();
            vm.RemoveRecordCommand.Execute(rec);
            Assert.AreEqual(0, vm.Records.Count);

            var json = File.ReadAllText(tmpCfg);
            Assert.IsFalse(json.Contains("pt_hider_ui_sample"));
        }

        [TestMethod]
        public void SetHidden_UpdatesFilesystemAttributes()
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "pt_hider_ui_fs");
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);

            var tmpCfg = Path.Combine(Path.GetTempPath(), "pt_hider_ui_test2.json");
            if (File.Exists(tmpCfg)) File.Delete(tmpCfg);

            var vm = new HiderViewModel(tmpCfg);
            vm.NewFolderPath = tmpDir;
            vm.AddFolderCommand.Execute(null);

            var record = vm.Records.First();
            vm.SelectedRecord = record;
            vm.SetHiddenCommand.Execute(record);
            Assert.IsTrue(System.IO.File.GetAttributes(tmpDir).HasFlag(System.IO.FileAttributes.Hidden));

            vm.ClearHiddenCommand.Execute(record);
            Assert.IsFalse(System.IO.File.GetAttributes(tmpDir).HasFlag(System.IO.FileAttributes.Hidden));

            Directory.Delete(tmpDir, true);
        }

        [TestMethod]
        public async Task EditDialog_PersistsPasswordViaUI()
        {
            var tmpCfg = Path.Combine(Path.GetTempPath(), "pt_hider_ui_test3.json");
            if (File.Exists(tmpCfg)) File.Delete(tmpCfg);

            var vm = new HiderViewModel(tmpCfg);

            vm.NewFolderPath = Path.Combine(Path.GetTempPath(), "pt_hider_ui_sample2");
            vm.AddFolderCommand.Execute(null);
            var rec = vm.Records.First();

            // Simulate setting password via ViewModel directly (avoid UI dialog in test runner)
            var editVm = new PlatypusTools.UI.ViewModels.HiderEditViewModel(rec.Record);
            editVm.Password = "uiPass";
            // Apply password as if user clicked OK
            rec.Record.PasswordRecord = PlatypusTools.Core.Services.HiderService.CreatePasswordRecord(editVm.Password);

            // Save config and reload
            vm.SaveConfigCommand.Execute(null);
            var loaded = PlatypusTools.Core.Services.HiderService.LoadConfig(tmpCfg);
            Assert.IsNotNull(loaded);
            Assert.IsNotNull(loaded.Folders[0].PasswordRecord);
            Assert.IsTrue(PlatypusTools.Core.Services.HiderService.TestPassword("uiPass", loaded.Folders[0].PasswordRecord));

            // Now test duplicate delete logic (non-recycle for test)
            var tmpDup = Path.Combine(Path.GetTempPath(), "pt_dup_ui_test");
            if (Directory.Exists(tmpDup)) Directory.Delete(tmpDup, true);
            Directory.CreateDirectory(tmpDup);
            var f1 = Path.Combine(tmpDup, "d1.txt");
            var f2 = Path.Combine(tmpDup, "d2.txt");
            File.WriteAllText(f1, "dup"); File.WriteAllText(f2, "dup");

            var dvm = new PlatypusTools.UI.ViewModels.DuplicatesViewModel();
            dvm.FolderPath = tmpDup;
            dvm.UseRecycleBin = false; // ensure files are actually deleted for test
            await dvm.ScanForDuplicatesAsync();
            Assert.IsTrue(dvm.Groups.Count >= 1);
            var grp = dvm.Groups.First();
            // Select files for deletion
            foreach (var file in grp.Files) file.IsSelected = true;
            dvm.DryRun = true;
            dvm.DeleteSelectedCommand.Execute(null); // Dry run should not delete
            Assert.IsTrue(File.Exists(f1) && File.Exists(f2));

            dvm.DryRun = false;
            // Use the programmatic deletion method to avoid confirmation dialogs in test
            dvm.DeleteSelectedConfirmed();
            Assert.IsFalse(File.Exists(f1) || File.Exists(f2));

            Directory.Delete(tmpDup, true);
        }
    }
}