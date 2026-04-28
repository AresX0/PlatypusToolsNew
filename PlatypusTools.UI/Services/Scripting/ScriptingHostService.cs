using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Scripting
{
    /// <summary>
    /// Phase 2.1 — minimal one-shot script runner. Executes a single PowerShell or Python
    /// command via the OS interpreter and returns combined stdout+stderr. No long-lived
    /// host yet (deferred). Read-only — does NOT expose internal services.
    /// </summary>
    public sealed class ScriptingHostService
    {
        private static readonly Lazy<ScriptingHostService> _instance = new(() => new ScriptingHostService());
        public static ScriptingHostService Instance => _instance.Value;

        public async Task<ScriptResult> RunPowerShellAsync(string script, CancellationToken ct = default)
        {
            return await RunAsync(GetPowerShellExe(), $"-NoProfile -NonInteractive -Command -", script, ct).ConfigureAwait(false);
        }

        public async Task<ScriptResult> RunPythonAsync(string script, CancellationToken ct = default)
        {
            var py = ResolvePython();
            if (py == null) return new ScriptResult(false, "", "Python interpreter not found. Install python or place python.exe in Tools/python.", -1);
            return await RunAsync(py, "-", script, ct).ConfigureAwait(false);
        }

        public bool IsPowerShellAvailable() => !string.IsNullOrEmpty(GetPowerShellExe());
        public bool IsPythonAvailable() => ResolvePython() != null;

        private static string GetPowerShellExe()
        {
            // Prefer pwsh (PowerShell 7+), fall back to Windows PowerShell.
            try
            {
                var pwsh = FindOnPath("pwsh.exe");
                if (!string.IsNullOrEmpty(pwsh)) return pwsh;
            }
            catch { }
            var sysRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows";
            var winps = Path.Combine(sysRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            return File.Exists(winps) ? winps : "";
        }

        private static string? ResolvePython()
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "Tools", "python", "python.exe");
            if (File.Exists(bundled)) return bundled;
            var onPath = FindOnPath("python.exe");
            if (!string.IsNullOrEmpty(onPath)) return onPath;
            onPath = FindOnPath("python3.exe");
            return string.IsNullOrEmpty(onPath) ? null : onPath;
        }

        private static string? FindOnPath(string exe)
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var p in path.Split(Path.PathSeparator))
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(p)) continue;
                    var full = Path.Combine(p, exe);
                    if (File.Exists(full)) return full;
                }
                catch { }
            }
            return null;
        }

        private static async Task<ScriptResult> RunAsync(string exe, string args, string stdin, CancellationToken ct)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                using var proc = Process.Start(psi);
                if (proc == null) return new ScriptResult(false, "", "Failed to start interpreter.", -1);

                await proc.StandardInput.WriteLineAsync(stdin).ConfigureAwait(false);
                proc.StandardInput.Close();

                var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                var stdout = await stdoutTask.ConfigureAwait(false);
                var stderr = await stderrTask.ConfigureAwait(false);
                return new ScriptResult(proc.ExitCode == 0, stdout, stderr, proc.ExitCode);
            }
            catch (OperationCanceledException) { return new ScriptResult(false, "", "Cancelled.", -1); }
            catch (Exception ex) { return new ScriptResult(false, "", ex.Message, -1); }
        }
    }

    public sealed record ScriptResult(bool Success, string StdOut, string StdErr, int ExitCode);
}
