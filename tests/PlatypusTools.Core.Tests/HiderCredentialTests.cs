using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class HiderCredentialTests
    {
        [TestMethod]
        public void SaveConfig_StoresCredentialRef()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_hider_cred.json");
            if (File.Exists(tmp)) File.Delete(tmp);

            var cfg = HiderService.GetDefaultConfig();
            var rec = new PlatypusTools.Core.Models.HiderRecord { FolderPath = Path.Combine(Path.GetTempPath(), "pt_hider_cred_test") };
            rec.PasswordRecord = HiderService.CreatePasswordRecord("s3cr3t");
            cfg.Folders.Add(rec);

            Assert.IsTrue(HiderService.SaveConfig(cfg, tmp));

            var raw = File.ReadAllText(tmp);
            // Ensure we saved a credential ref key instead of hash directly
            Assert.IsTrue(raw.Contains("EncryptedPasswordRef"));

            var loaded = HiderService.LoadConfig(tmp);
            Assert.IsNotNull(loaded);
            Assert.AreEqual(1, loaded.Folders.Count);
            Assert.IsNotNull(loaded.Folders[0].PasswordRecord);
            Assert.IsTrue(HiderService.TestPassword("s3cr3t", loaded.Folders[0].PasswordRecord));

            // Now simulate a legacy file containing EncryptedPassword and ensure LoadConfig migrates it
            var legacyBlob = HiderService.ExportLegacyBlob(cfg.Folders[0].PasswordRecord!);
            var json = File.ReadAllText(tmp);
            // Replace "EncryptedPasswordRef": "key" with "EncryptedPassword": "<blob>"
            json = json.Replace("EncryptedPasswordRef", "EncryptedPassword");
            json = json.Replace("EncryptedPassword": " + "" , "" );
            // Simplest approach: remove the EncryptedPasswordRef line and add EncryptedPassword with blob
            // We'll perform a targeted JSON edit for robustness
            var jdoc = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
            var list = jdoc["Folders"]!.AsArray();
            var obj = list[0].AsObject();
            obj.Remove("EncryptedPasswordRef");
            obj["EncryptedPassword"] = legacyBlob;
            File.WriteAllText(tmp, jdoc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            // Reload; migration should occur and SaveConfig should replace with EncryptedPasswordRef
            var loaded2 = HiderService.LoadConfig(tmp);
            Assert.IsNotNull(loaded2);
            Assert.AreEqual(1, loaded2.Folders.Count);
            Assert.IsNotNull(loaded2.Folders[0].PasswordRecord);
            Assert.IsTrue(HiderService.TestPassword("s3cr3t", loaded2.Folders[0].PasswordRecord));

            var final = File.ReadAllText(tmp);
            Assert.IsFalse(final.Contains("EncryptedPassword"));
            Assert.IsTrue(final.Contains("EncryptedPasswordRef"));
        }
    }
}