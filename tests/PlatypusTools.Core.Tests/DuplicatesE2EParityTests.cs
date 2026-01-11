using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Linq;
using System.Diagnostics;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class DuplicatesE2EParityTests
    {
        [TestMethod]
        public void DotNet_SelectNewest_Matches_PowerShell()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_dupe_e2e_parity");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var f1 = Path.Combine(tmp, "old.txt");
            var f2 = Path.Combine(tmp, "new.txt");
            File.WriteAllText(f1, "same");
            System.Threading.Thread.Sleep(1000);
            File.WriteAllText(f2, "same");

            // C#: find duplicates and choose newest per-group
            var groups = PlatypusTools.Core.Services.DuplicatesScanner.FindDuplicates(new[] { tmp }).ToList();
            Assert.IsTrue(groups.Count >= 1);
            var group = groups.First(g => g.Files.Contains(f1) && g.Files.Contains(f2));
            var chosen = group.Files.OrderByDescending(p => File.GetLastWriteTimeUtc(p)).First();

            // PowerShell: dot-source archive and perform grouping + newest-selection
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

            var ps = $". '{archived}'; $files = Get-ChildItem -Path '{tmp}' -File -Recurse | Select-Object -ExpandProperty FullName; $hashes = @{{}}; foreach($f in $files){{{{ $h = Get-FastFileHash -Path $f; if(-not $hashes.ContainsKey($h)){{{{ $hashes[$h] = @() }}}}; $hashes[$h] += $f }}}}; $selected = @(); foreach($h in $hashes.Keys){{{{ $group = $hashes[$h]; if($group.Count -gt 1){{{{ $sel = $group | ForEach-Object {{{{ Get-Item $_ }}}} | Sort-Object -Property LastWriteTimeUtc -Descending | Select-Object -First 1; $selected += $sel.FullName }}}}}}}}; $selected | ForEach-Object {{{{ Write-Output $_ }}}}";

            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            string outStr;
            using (var p = Process.Start(psi)) { outStr = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            var lines = outStr?.Split(new[] { '\r','\n' }, System.StringSplitOptions.RemoveEmptyEntries) ?? new string[0];

            // Find the selected file from PS that belongs to our group
            var psSelected = lines.FirstOrDefault(l => l.Trim().Equals(f1, System.StringComparison.OrdinalIgnoreCase) || l.Trim().Equals(f2, System.StringComparison.OrdinalIgnoreCase));
            Assert.IsNotNull(psSelected, "PowerShell did not report a selected file for the test group");

            // Compare the paths (normalize casing)
            Assert.AreEqual(Path.GetFullPath(chosen).TrimEnd('\\').ToLowerInvariant(), Path.GetFullPath(psSelected).TrimEnd('\\').ToLowerInvariant());

            Directory.Delete(tmp, true);
        }
        [TestMethod]
        public void DotNet_Stage_Commit_RemovesSelectedAndMatches_PowerShell()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_dupe_e2e_parity_stage");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);

            var f1 = Path.Combine(tmp, "old.txt");
            var f2 = Path.Combine(tmp, "new.txt");
            File.WriteAllText(f1, "same");
            System.Threading.Thread.Sleep(1000);
            File.WriteAllText(f2, "same");

            // PowerShell: select oldest
            var psSelect = $"$files = Get-ChildItem -Path '{tmp}' -File -Recurse | Select-Object -ExpandProperty FullName; $hashes = @{{}}; foreach($f in $files){{{{ $h = (Get-FileHash -Path $f -Algorithm SHA256).Hash; if(-not $hashes.ContainsKey($h)){{{{ $hashes[$h] = @() }}}}; $hashes[$h] += $f }}}}; $selected = @(); foreach($h in $hashes.Keys){{{{ $group = $hashes[$h]; if($group.Count -gt 1){{{{ $sel = $group | ForEach-Object {{{{ Get-Item $_ }}}} | Sort-Object -Property LastWriteTimeUtc -Ascending | Select-Object -First 1; $selected += $sel.FullName }}}}}}}}; $selected | ConvertTo-Json -Compress";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{psSelect}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            string outStr;
            using (var p = Process.Start(psi)) { outStr = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            var beforeSelected = System.Text.Json.JsonSerializer.Deserialize<string[]>(outStr) ?? new string[0];
            Assert.IsTrue(beforeSelected.Length >= 1);
            var toDelete = beforeSelected[0];

            // Stage + commit using the .NET flow
            var vm = new PlatypusTools.UI.ViewModels.DuplicatesViewModel();
            // Stage
            var staged = vm.StageFileToStaging(toDelete);
            Assert.IsTrue(!string.IsNullOrWhiteSpace(staged));
            var svm = new PlatypusTools.UI.ViewModels.StagingViewModel();
            svm.LoadStagedFiles();
            var entry = svm.StagedFiles.First(s => s.StagedPath == staged);
            entry.IsSelected = true;
            svm.CommitSelected();

            // After commit: the previously selected file should be gone; run PS selection again and expect no selection for this group
            using (var p2 = Process.Start(psi)) { outStr = p2.StandardOutput.ReadToEnd(); p2.WaitForExit(); }
            var afterSelected = System.Text.Json.JsonSerializer.Deserialize<string[]>(outStr) ?? new string[0];
            // The previously-deleted file should not appear in afterSelected; also groups should no longer report a duplicate selection
            Assert.IsFalse(afterSelected.Contains(toDelete, System.StringComparer.OrdinalIgnoreCase));

            Directory.Delete(tmp, true);
        }    }
}