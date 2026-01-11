using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Diagnostics;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class HiderParityTests
    {
        [TestMethod]
        public void PowerShell_SetHidden_MatchesDotNet()
        {
            var tmpDir = Path.Combine(Path.GetTempPath(), "pt_hider_parity");
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
            Directory.CreateDirectory(tmpDir);

            // C# side: set hidden and verify
            var csSet = PlatypusTools.Core.Services.HiderService.SetHidden(tmpDir, true);
            var csState = PlatypusTools.Core.Services.HiderService.GetHiddenState(tmpDir);

            // PowerShell side using archived script on a separate folder to avoid cross-interference
            var tmpDir2 = Path.Combine(Path.GetTempPath(), "pt_hider_parity_ps");
            if (Directory.Exists(tmpDir2)) Directory.Delete(tmpDir2, true);
            Directory.CreateDirectory(tmpDir2);

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
            Assert.IsTrue(File.Exists(archived), $"Archived PS script not found: {archived}");

            // Add a short sleep and request attributes too to avoid transient filesystem races
            var ps = $". '{archived}'; Set-Hidden -Path '{tmpDir2}'; Start-Sleep -Milliseconds 200; $s = Get-HiddenState -Path '{tmpDir2}'; Write-Output $s; Write-Output ((Get-Item -LiteralPath '{tmpDir2}').Attributes)";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            string outStr;
            using (var p = Process.Start(psi)) { outStr = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            var lines = outStr?.Split(new[] { '\r','\n' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
            var psState = false;
            if (lines.Length >= 1) { bool.TryParse(lines[0].Trim(), out psState); }
            var psAttrs = lines.Length >= 2 ? lines[1].Trim() : null;

            var csState2 = PlatypusTools.Core.Services.HiderService.GetHiddenState(tmpDir2);
            Assert.AreEqual(psState, csState2, $"Mismatch: PSattrs={psAttrs} PSState={psState} CSState={csState2}");

            // Cleanup
            PlatypusTools.Core.Services.HiderService.SetHidden(tmpDir, false);
            PlatypusTools.Core.Services.HiderService.SetHidden(tmpDir2, false);
            Directory.Delete(tmpDir, true);
            Directory.Delete(tmpDir2, true);        }
    }
}
