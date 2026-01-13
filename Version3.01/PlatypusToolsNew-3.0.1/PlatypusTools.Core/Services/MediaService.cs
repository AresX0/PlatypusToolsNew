using System;
using System.Diagnostics;
using System.IO;

namespace PlatypusTools.Core.Services
{
    public static class MediaService
    {
        public static string? ResolveToolPath(string toolName, string? toolsRoot = null)
        {
            if (!string.IsNullOrWhiteSpace(toolsRoot))
            {
                var candidate = Path.Combine(toolsRoot, toolName);
                if (File.Exists(candidate)) return candidate;
                candidate = candidate + ".exe";
                if (File.Exists(candidate)) return candidate;
            }

            // fallback: look on PATH
            try
            {
                var which = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
                if (which != null)
                {
                    foreach (var d in which)
                    {
                        var p = Path.Combine(d, toolName);
                        if (File.Exists(p)) return p;
                        if (File.Exists(p + ".exe")) return p + ".exe";
                    }
                }
            }
            catch { }
            return null;
        }

        public static bool IsToolAvailable(string toolName, string? toolsRoot = null)
        {
            return ResolveToolPath(toolName, toolsRoot) != null;
        }

        public static Process? StartTool(string toolExe, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(toolExe, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                return Process.Start(psi);
            }
            catch { return null; }
        }
    }
}