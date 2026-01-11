using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    public class PowerShellResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
        public bool Success => ExitCode == 0;
    }

    public static class PowerShellRunner
    {
        private static string FindPowerShellExe()
        {
            // Prefer pwsh (PowerShell Core) if available, otherwise fall back to powershell.exe
            var pwsh = Environment.ExpandEnvironmentVariables("%ProgramFiles%\\PowerShell\\7\\pwsh.exe");
            if (System.IO.File.Exists(pwsh)) return pwsh;
            var pwshx86 = Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%\\PowerShell\\7\\pwsh.exe");
            if (System.IO.File.Exists(pwshx86)) return pwshx86;
            // fallback to system powershell
            var sys = Environment.ExpandEnvironmentVariables("%SystemRoot%\\system32\\WindowsPowerShell\\v1.0\\powershell.exe");
            if (System.IO.File.Exists(sys)) return sys;
            // last resort: just 'powershell'
            return "powershell";
        }

        public static async Task<PowerShellResult> RunScriptAsync(string command, int timeoutMs = 60000)
        {
            var exe = FindPowerShellExe();
            var psi = new ProcessStartInfo(exe, $"-NoProfile -NonInteractive -Command \"{command}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            try
            {
                using var p = Process.Start(psi);
                if (p == null) return new PowerShellResult { ExitCode = -1, StdErr = "Failed to start process" };
                var outTask = p.StandardOutput.ReadToEndAsync();
                var errTask = p.StandardError.ReadToEndAsync();
                var completed = await Task.WhenAll(outTask, errTask);
                if (!p.WaitForExit(timeoutMs))
                {
                    try { p.Kill(); } catch { }
                    return new PowerShellResult { ExitCode = -2, StdOut = completed[0], StdErr = completed[1] + "\nTimed out" };
                }
                return new PowerShellResult { ExitCode = p.ExitCode, StdOut = completed[0], StdErr = completed[1] };
            }
            catch (Exception ex)
            {
                return new PowerShellResult { ExitCode = -99, StdErr = ex.ToString() };
            }
        }
    }
}