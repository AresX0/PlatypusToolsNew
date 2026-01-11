using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Models;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class HiderEncryptionTests
    {
        [TestMethod]
        public void SaveLoad_EncryptsPasswordAndRestores()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_hider_secure.json");
            if (File.Exists(tmp)) File.Delete(tmp);

            var cfg = HiderService.GetDefaultConfig();
            var rec = new HiderRecord { FolderPath = "C:\\temp\\h_secure" };
            rec.PasswordRecord = HiderService.CreatePasswordRecord("s3cr3t");
            cfg.Folders.Add(rec);

            Assert.IsTrue(HiderService.SaveConfig(cfg, tmp));
            var raw = File.ReadAllText(tmp);
            // Ensure the raw json does not contain the clear-text hash field
            Assert.IsFalse(raw.Contains("\"Hash\""));

            var loaded = HiderService.LoadConfig(tmp);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Folders.Count);
            Assert.IsNotNull(loaded.Folders[0].PasswordRecord);
            Assert.IsTrue(HiderService.TestPassword("s3cr3t", loaded.Folders[0].PasswordRecord));
        }
    }
}