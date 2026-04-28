using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Security
{
    public class YaraMatch
    {
        public string RuleName { get; set; } = "";
        public string Path { get; set; } = "";
        public string Tags { get; set; } = "";
    }

    // Shells out to yara.exe (registered in DependencyCheckerService.CheckYaraAsync()).
    public class YaraScannerService
    {
        public static IEnumerable<string> CandidatePaths()
        {
            yield return "yara";
            yield return "yara64";
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "yara64.exe");
            yield return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "yara.exe");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "yara", "yara64.exe");
        }

        public static string? ResolveYara()
        {
            foreach (var p in CandidatePaths())
            {
                try
                {
                    var psi = new ProcessStartInfo(p, "--version")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit(2000);
                        if (proc.ExitCode == 0) return p;
                    }
                }
                catch { }
            }
            return null;
        }

        public async Task<List<YaraMatch>> ScanAsync(string rulesFile, string targetPath, bool recursive, CancellationToken ct = default)
        {
            var matches = new List<YaraMatch>();
            var yara = ResolveYara() ?? throw new InvalidOperationException("yara.exe not found. Install YARA from https://github.com/VirusTotal/yara/releases");
            if (!File.Exists(rulesFile)) throw new FileNotFoundException("Rules file not found.", rulesFile);
            if (!File.Exists(targetPath) && !Directory.Exists(targetPath)) throw new FileNotFoundException("Target not found.", targetPath);

            var args = recursive ? $"-r -g \"{rulesFile}\" \"{targetPath}\"" : $"-g \"{rulesFile}\" \"{targetPath}\"";
            var psi = new ProcessStartInfo(yara, args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start yara.");
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
            {
                // Format: rulename [tags] path
                var first = line.IndexOf(' ');
                if (first <= 0) continue;
                var rule = line.Substring(0, first);
                var rest = line.Substring(first + 1).Trim();
                string tags = "";
                if (rest.StartsWith("["))
                {
                    var end = rest.IndexOf(']');
                    if (end > 0)
                    {
                        tags = rest.Substring(1, end - 1);
                        rest = rest.Substring(end + 1).Trim();
                    }
                }
                matches.Add(new YaraMatch { RuleName = rule, Tags = tags, Path = rest });
            }
            await proc.WaitForExitAsync(ct);
            return matches;
        }
    }
}
