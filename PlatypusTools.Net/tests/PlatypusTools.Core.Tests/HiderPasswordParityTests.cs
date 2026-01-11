using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;
using System.IO;

namespace PlatypusTools.Core.Tests
{
    [TestClass]
    public class HiderPasswordParityTests
    {
        [TestMethod]
        public void PasswordRecord_CreatedByDotNet_IsAcceptedByPowerShell()
        {
            var rec = PlatypusTools.Core.Services.HiderService.CreatePasswordRecord("s3cr3t");
            Assert.IsNotNull(rec);

            // Invoke archived PS Test-Password
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

            var psRec = $"@{{Salt='{rec.Salt}'; Hash='{rec.Hash}'; Iterations={rec.Iterations}}}";
            var ps = $". '{archived}'; $pwd = ConvertTo-SecureString 's3cr3t' -AsPlainText -Force; $r = {psRec}; $ok = Test-Password -Password $pwd -PasswordRecord $r; Write-Output $ok";
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -NonInteractive -Command \"{ps}\"") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            string outStr;
            using (var p = Process.Start(psi)) { outStr = p.StandardOutput.ReadToEnd(); p.WaitForExit(); }
            var lines = outStr?.Split(new[] { '\r','\n' }, System.StringSplitOptions.RemoveEmptyEntries) ?? new string[0];
            var psOk = false;
            if (lines.Length > 0) bool.TryParse(lines[0].Trim(), out psOk);

            Assert.IsTrue(psOk, "PowerShell failed to validate the password using .NET-created PasswordRecord");
        }
    }
}