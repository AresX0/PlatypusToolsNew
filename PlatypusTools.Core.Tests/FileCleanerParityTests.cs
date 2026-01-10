using Microsoft.VisualStudio.TestTools.UnitTesting;
using PlatypusTools.Core.Services;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Linq;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class FileCleanerParityTests
    {
        [TestMethod]
        public void PowerShell_ComputeBackupMapping_MatchesDotnet()
        {
            var tmp = Path.Combine(Path.GetTempPath(), "pt_fc_parity");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var nested = Path.Combine(tmp, "a", "b"); Directory.CreateDirectory(nested);
            var f = Path.Combine(nested, "x.txt"); File.WriteAllText(f, "x");
            var backup = Path.Combine(tmp, "backup"); Directory.CreateDirectory(backup);

            // Dotnet mapping
            var dot = FileCleaner.ComputeBackupMapping(new[] { f }, backup, tmp);

            // PowerShell mapping: run a snippet that computes same rule and outputs JSON
            var psScript = $"$f = '{f}'; $base = '{tmp}'; $backup = '{backup}'; $map = @{{}}; $full = (Resolve-Path $f).ProviderPath; $b = (Resolve-Path $base).ProviderPath; if ($full.StartsWith($b)) {{ $rel = $full.Substring($b.Length).TrimStart('\\','/'); $dest = Join-Path $backup $rel }} else {{ $dest = Join-Path $backup (Split-Path $f -Leaf) }}; $obj = @{{ ($f) = $dest }}; $obj | ConvertTo-Json -Compress";

            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{psScript}\"")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            var outStr = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            Assert.IsFalse(string.IsNullOrWhiteSpace(outStr));

            var jsonDoc = JsonDocument.Parse(outStr);
            var psDest = jsonDoc.RootElement.EnumerateObject().First().Value.GetString();

            var expected = dot[f];
            Assert.AreEqual(expected, psDest);
        }
    }
}