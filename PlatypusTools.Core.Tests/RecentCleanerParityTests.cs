using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class RecentCleanerParityTests
    {
        [TestMethod]
        public void PowerShell_RemoveRecentShortcuts_MatchesDotnet()
        {
            var tmpTarget = Path.Combine(Path.GetTempPath(), "pt_recent_parity_target");
            if (Directory.Exists(tmpTarget)) Directory.Delete(tmpTarget, true);
            Directory.CreateDirectory(tmpTarget);
            var targetFile = Path.Combine(tmpTarget, "file.txt");
            File.WriteAllText(targetFile, "x");

            var recent = Path.Combine(Path.GetTempPath(), "pt_recent_parity_recent");
            if (Directory.Exists(recent)) Directory.Delete(recent, true);
            Directory.CreateDirectory(recent);

            // Create a real .lnk via PowerShell COM so both sides can resolve it
            var psCreate = $"$shell = New-Object -ComObject WScript.Shell; $s = $shell.CreateShortcut('{Path.Combine(recent, "t.lnk")}'); $s.TargetPath = '{targetFile}'; $s.Save();";
            var psiCreate = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{psCreate}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            using (var p = Process.Start(psiCreate)) { p.WaitForExit(); }

            // Dotnet result
            var dot = RecentCleaner.RemoveRecentShortcuts(new[] { tmpTarget }, dryRun: true, includeSubDirs: true, recentFolder: recent);

            // PowerShell result using archived script (safe source)
            var archived = Path.Combine(Directory.GetCurrentDirectory(), "ArchivedScripts", "PlatypusTools.ps1");
            if (!File.Exists(archived)) {
                // Fallback: walk up directories to repository root to find ArchivedScripts (handles test runner working dir differences)
                var cur = Directory.GetCurrentDirectory();
                while (!string.IsNullOrEmpty(cur)) {
                    var candidate = Path.Combine(cur, "ArchivedScripts", "PlatypusTools.ps1");
                    if (File.Exists(candidate)) { archived = candidate; break; }
                    var parent = Directory.GetParent(cur);
                    cur = parent?.FullName;
                }
            }
            Assert.IsTrue(File.Exists(archived), $"Archived PS script not found: {archived}");

            var ps = $". '{archived}'; $r = Remove-RecentShortcuts -targetDirs @('{tmpTarget}') -dryRun -includeSubDirs -recentFolder '{recent}'; $r | Select-Object Path,Target | ConvertTo-Json -Compress";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            string outStr;
            using (var p = Process.Start(psi)) { outStr = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            Assert.IsFalse(string.IsNullOrWhiteSpace(outStr));

            var doc = JsonDocument.Parse(outStr);
            var arr = doc.RootElement;
            var psPaths = arr.EnumerateArray().Select(el => el.GetProperty("Path").GetString()).ToList();

            var dotPaths = dot.Select(x => x.Path).ToList();

            CollectionAssert.AreEquivalent(psPaths, dotPaths);
        }
    }
}