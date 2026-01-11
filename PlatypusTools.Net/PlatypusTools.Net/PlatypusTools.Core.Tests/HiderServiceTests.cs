using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class HiderServiceTests
    {
        [TestMethod]
        public void SaveAndLoadConfig_Works()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_hider_cfg.json");
            if (File.Exists(tmp)) File.Delete(tmp);

            var cfg = HiderService.GetDefaultConfig();
            cfg.AutoHideEnabled = true;
            cfg.AutoHideMinutes = 7;
            cfg.Folders.Add(new HiderRecord { FolderPath = "C:\\temp\\h1" });

            Assert.IsTrue(HiderService.SaveConfig(cfg, tmp));
            var loaded = HiderService.LoadConfig(tmp);
            Assert.IsNotNull(loaded);
            Assert.IsTrue(loaded.AutoHideEnabled);
            Assert.AreEqual(7, loaded.AutoHideMinutes);
            Assert.AreEqual(1, loaded.Folders.Count);
        }

        [TestMethod]
        public void SetAndGetHidden_StateMatchesFilesystem()
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "pt_hider_test");
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);

            Assert.IsFalse(HiderService.GetHiddenState(tmpDir));
            Assert.IsTrue(HiderService.SetHidden(tmpDir, true));
            Assert.IsTrue(HiderService.GetHiddenState(tmpDir));
            Assert.IsTrue(HiderService.SetHidden(tmpDir, false));
            Assert.IsFalse(HiderService.GetHiddenState(tmpDir));

            Directory.Delete(tmpDir, true);
        }

        [TestMethod]
        public void PasswordRecord_CreateAndVerify()
        {
            var rec = HiderService.CreatePasswordRecord("s3cr3t");
            Assert.IsNotNull(rec);
            Assert.IsTrue(HiderService.TestPassword("s3cr3t", rec));
            Assert.IsFalse(HiderService.TestPassword("wrong", rec));
        }
    }
}
