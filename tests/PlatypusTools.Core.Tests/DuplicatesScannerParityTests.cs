using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class DuplicatesScannerParityTests
    {
        [TestMethod]
        public void DotNet_FindDuplicates_Matches_PowerShell_GetFastFileHash()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_dupe_parity");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            // Create duplicate files
            var a = Path.Combine(tmp, "a.txt");
            var b = Path.Combine(tmp, "b.txt");
            File.WriteAllText(a, "hello world");
            File.WriteAllText(b, "hello world");

            // C# groups
            var groups = PlatypusTools.Core.Services.DuplicatesScanner.FindDuplicates(new[] { tmp }).ToList();
            Assert.IsTrue(groups.Count == 1);
            Assert.AreEqual(2, groups[0].Files.Count);

            // PowerShell hashes grouped
            var archived = Path.Combine(Directory.GetCurrentDirectory(), "ArchivedScripts", "PlatypusTools.ps1");
            if (!File.Exists(archived))
            {
                var cur = Directory.GetCurrentDirectory();
                while (!string.IsNullOrEmpty(cur))
                {
                    var candidate = Path.Combine(cur, "ArchivedScripts", "PlatypusTools.ps1");
                    if (File.Exists(candidate)) { archived = candidate; break; }
                    var parent = Directory.GetParent(cur);
                    cur = parent?.FullName;
                }
            }
            Assert.IsTrue(File.Exists(archived), $"Archived PS not found: {archived}");

            var ps = $". '{archived}'; $h1 = Get-FastFileHash -Path '{a}'; $h2 = Get-FastFileHash -Path '{b}'; Write-Output $h1; Write-Output $h2";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            string outStr;
            using (var p = Process.Start(psi)) { outStr = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            var lines = outStr?.Split(new[] { '\r','\n' }, System.StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
            Assert.IsTrue(lines.Length >= 2);
            Assert.AreEqual(lines[0].Trim(), lines[1].Trim());

            Directory.Delete(tmp, true);
        }
    }
}