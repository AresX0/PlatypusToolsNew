using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    public class UpscalerService
    {
        public string? FindVideo2x(string? toolsFolder = null)
        {
            toolsFolder = toolsFolder ?? string.Empty;
            // check explicit tool folder first
            if (!string.IsNullOrWhiteSpace(toolsFolder))
            {
                var candidate = Path.Combine(toolsFolder, "video2x.exe");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(toolsFolder, "video2x.py");
                if (File.Exists(candidate)) return candidate;
                // check generic base + PATHEXT
                var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.PY";
                foreach (var ext in pathext.Split(';'))
                {
                    var cand = Path.Combine(toolsFolder, "video2x" + ext.Trim());
                    if (File.Exists(cand)) return cand;
                }
            }

            try
            {
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
                var pathext = Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.PY";
                var exts = pathext.Split(';');
                foreach (var p in paths)
                {
                    // direct known names
                    var a = Path.Combine(p, "video2x.exe");
                    if (File.Exists(a)) return a;
                    var b = Path.Combine(p, "video2x.py");
                    if (File.Exists(b)) return b;
                    // generic base + PATHEXT
                    foreach (var ext in exts)
                    {
                        var cand = Path.Combine(p, "video2x" + ext.Trim());
                        if (File.Exists(cand)) return cand;
                    }
                    // check bare file name as well
                    var bare = Path.Combine(p, "video2x");
                    if (File.Exists(bare)) return bare;
                }
            }
            catch { }

            return null;
        }

        public virtual async Task<FFmpegResult> RunAsync(string inputPath, string outputPath, int scale = 2, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
        {
            var exe = FindVideo2x();
            if (exe == null) return new FFmpegResult { ExitCode = -1, StdErr = "video2x not found" };

            // Basic best-effort CLI invocation; video2x CLI may differ across versions so we use reasonable defaults.
            string fileName = Path.GetFileName(exe).ToLowerInvariant();
            string args;
            string runner;

            // If the tool is a python script, run with python; if it's a batch file, run via cmd /c
            if (fileName.EndsWith(".py"))
            {
                runner = "python";
                args = $"\"{exe}\" -i \"{inputPath}\" -o \"{outputPath}\" -s {scale}";
            }
            else if (fileName.EndsWith(".bat") || fileName.EndsWith(".cmd"))
            {
                runner = "cmd";
                args = $"/c \"\"{exe}\" -i \"{inputPath}\" -o \"{outputPath}\" -s {scale}\"";
            }
            else
            {
                runner = exe;
                args = $"-i \"{inputPath}\" -o \"{outputPath}\" -s {scale}";
            }

            var psi = new ProcessStartInfo(runner, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            using var p = new Process() { StartInfo = psi, EnableRaisingEvents = true };
            p.OutputDataReceived += (_, e) => { if (e.Data != null) { stdout.AppendLine(e.Data); progress?.Report(e.Data); } };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderr.AppendLine(e.Data); progress?.Report(e.Data); } };

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
                return new FFmpegResult { ExitCode = -4, StdOut = stdout.ToString(), StdErr = stderr.ToString() + "\nCanceled" };
            }
            catch (Exception ex)
            {
                try { if (!p.HasExited) p.Kill(); } catch { }
                return new FFmpegResult { ExitCode = -99, StdErr = ex.ToString() };
            }

            return new FFmpegResult { ExitCode = p.ExitCode, StdOut = stdout.ToString(), StdErr = stderr.ToString() };
        }
    }
}