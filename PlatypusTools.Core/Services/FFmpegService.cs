using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace PlatypusTools.Core.Services
{
    public class FFmpegResult
    {
        public int ExitCode { get; set; }
        public string StdOut { get; set; } = string.Empty;
        public string StdErr { get; set; } = string.Empty;
        public bool Success => ExitCode == 0;
    }

    public static class FFmpegService
    {
        public static string? FindFfmpeg(string? toolsFolder = null)
        {
            toolsFolder = toolsFolder ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(toolsFolder))
            {
                var candidate = Path.Combine(toolsFolder, "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
                
                // Also check subdirectory (MSI installs to Tools/ffmpeg/ffmpeg.exe)
                candidate = Path.Combine(toolsFolder, "ffmpeg", "ffmpeg.exe");
                if (File.Exists(candidate)) return candidate;
            }
            
            // Check common installation paths relative to app
            var appPath = AppDomain.CurrentDomain.BaseDirectory;
            var searchPaths = new[]
            {
                Path.Combine(appPath, "Tools", "ffmpeg.exe"),
                Path.Combine(appPath, "Tools", "ffmpeg", "ffmpeg.exe"),  // MSI installs here
                Path.Combine(appPath, "ffmpeg.exe"),
                Path.Combine(appPath, "ffmpeg", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "ffmpeg", "ffmpeg.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusTools", "Tools", "ffmpeg.exe"),
            };
            
            foreach (var path in searchPaths)
            {
                if (File.Exists(path)) return path;
            }

            // search PATH
            try
            {
                var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
                foreach (var p in paths)
                {
                    var candidate = Path.Combine(p, "ffmpeg.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
            catch { }

            return null;
        }

        public static async Task<FFmpegResult> RunAsync(string args, string? ffmpegPath = null, IProgress<string>? progress = null, CancellationToken cancellationToken = default, int timeoutMs = 600000)
        {
            ffmpegPath = ffmpegPath ?? FindFfmpeg();
            if (ffmpegPath == null) return new FFmpegResult { ExitCode = -1, StdErr = "ffmpeg not found" };
            var psi = new ProcessStartInfo(ffmpegPath, args)
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

            // Create a linked token that enforces both user cancellation and timeout
            using var timeoutCts = new CancellationTokenSource(timeoutMs);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                await p.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                return new FFmpegResult { ExitCode = -5, StdOut = stdout.ToString(), StdErr = stderr.ToString() + $"\nTimed out after {timeoutMs}ms" };
            }
            catch (OperationCanceledException)
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                return new FFmpegResult { ExitCode = -4, StdOut = stdout.ToString(), StdErr = stderr.ToString() + "\nCanceled" };
            }
            catch (Exception ex)
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                return new FFmpegResult { ExitCode = -99, StdErr = ex.ToString() };
            }

            return new FFmpegResult { ExitCode = p.ExitCode, StdOut = stdout.ToString(), StdErr = stderr.ToString() };
        }
    }
}