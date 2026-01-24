using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Service for YARA rules-based malware scanning.
    /// Supports custom rules and community rulesets (YARA-Forge, Awesome-YARA).
    /// </summary>
    public class YaraService : ForensicOperationBase
    {
        private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };

        public override string OperationName => "YARA Scan";

        #region Configuration

        /// <summary>
        /// Gets or sets the path to yara executable (yara.exe or yara64.exe).
        /// </summary>
        public string YaraPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the directory containing YARA rule files (.yar).
        /// </summary>
        public string RulesDirectory { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target path to scan (file or directory).
        /// </summary>
        public string TargetPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets whether to scan recursively.
        /// </summary>
        public bool Recursive { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include module data.
        /// </summary>
        public bool IncludeModules { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to show metadata for matching rules.
        /// </summary>
        public bool ShowMetadata { get; set; } = true;

        /// <summary>
        /// Gets or sets file extensions to scan. Empty means all files.
        /// </summary>
        public List<string> FileExtensions { get; set; } = new() { ".exe", ".dll", ".ps1", ".vbs", ".js", ".doc", ".docx", ".pdf" };

        #endregion

        #region Well-Known Rule Sources

        public static readonly Dictionary<string, string> RuleSources = new()
        {
            { "YARA-Forge Core", "https://github.com/YARAHQ/yara-forge/releases/latest/download/yara-forge-rules-core.zip" },
            { "YARA-Forge Full", "https://github.com/YARAHQ/yara-forge/releases/latest/download/yara-forge-rules-full.zip" },
            { "Awesome YARA", "https://github.com/InQuest/awesome-yara/archive/refs/heads/master.zip" },
            { "Signature Base", "https://github.com/Neo23x0/signature-base/archive/refs/heads/master.zip" },
            { "YARA Rules Community", "https://github.com/Yara-Rules/rules/archive/refs/heads/master.zip" }
        };

        #endregion

        #region Scanning

        /// <summary>
        /// Runs YARA scan on the target path.
        /// </summary>
        public async Task<YaraResult> ScanAsync(CancellationToken cancellationToken = default)
        {
            var result = new YaraResult();

            if (string.IsNullOrWhiteSpace(TargetPath))
            {
                ReportError("Please select a target path to scan");
                return result;
            }

            if (!File.Exists(TargetPath) && !Directory.Exists(TargetPath))
            {
                ReportError($"Target path does not exist: {TargetPath}");
                return result;
            }

            var yaraExe = FindYaraExecutable();
            if (yaraExe == null)
            {
                ReportError("YARA executable not found. Please download and configure YARA.");
                ReportYaraHelp();
                return result;
            }

            var rulesFiles = GetRulesFiles();
            if (rulesFiles.Count == 0)
            {
                ReportError("No YARA rule files found. Please download rules or configure rules directory.");
                return result;
            }

            await ExecuteWithHandlingAsync(async () =>
            {
                ReportProgress("========================================");
                ReportProgress("YARA Malware Scan Started");
                ReportProgress("========================================");
                ReportProgress($"YARA Executable: {yaraExe}");
                ReportProgress($"Rules Directory: {RulesDirectory}");
                ReportProgress($"Rule Files: {rulesFiles.Count}");
                ReportProgress($"Target: {TargetPath}");
                ReportProgress($"Recursive: {Recursive}");
                ReportProgress("");

                var processedRules = 0;
                var totalMatches = 0;

                foreach (var ruleFile in rulesFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var ruleName = Path.GetFileName(ruleFile);
                    ReportProgress($"Scanning with: {ruleName}", (processedRules * 100.0) / rulesFiles.Count);

                    var matches = await ScanWithRuleFileAsync(yaraExe, ruleFile, cancellationToken);

                    foreach (var match in matches)
                    {
                        result.Matches.Add(match);
                        totalMatches++;
                        ReportProgress($"  🔴 MATCH: {match.RuleName} in {match.FilePath}");
                    }

                    processedRules++;
                }

                result.Success = true;
                result.RulesScanned = rulesFiles.Count;
                result.Message = totalMatches > 0
                    ? $"⚠ Scan complete: {totalMatches} matches found!"
                    : "✓ Scan complete: No threats detected";

                ReportProgress("");
                ReportProgress("========================================");
                ReportProgress("Scan Summary");
                ReportProgress("========================================");
                ReportProgress($"Rules scanned: {rulesFiles.Count}");
                ReportProgress($"Matches found: {totalMatches}");

                if (totalMatches > 0)
                {
                    ReportProgress("");
                    ReportProgress("Matched Rules:");
                    foreach (var group in result.Matches.GroupBy(m => m.RuleName))
                    {
                        ReportProgress($"  • {group.Key}: {group.Count()} files");
                    }
                }

            }, cancellationToken);

            return result;
        }

        private async Task<List<YaraMatch>> ScanWithRuleFileAsync(
            string yaraExe, string ruleFile, CancellationToken cancellationToken)
        {
            var matches = new List<YaraMatch>();

            try
            {
                var args = BuildArguments(ruleFile);

                var startInfo = new ProcessStartInfo
                {
                    FileName = yaraExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                var output = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        output.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken);

                // Parse YARA output: RULE_NAME FILE_PATH
                var lines = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var match = ParseYaraOutput(line.Trim(), ruleFile);
                    if (match != null)
                        matches.Add(match);
                }
            }
            catch (Exception ex)
            {
                ReportProgress($"  ⚠ Error with {Path.GetFileName(ruleFile)}: {ex.Message}");
            }

            return matches;
        }

        private string BuildArguments(string ruleFile)
        {
            var args = new StringBuilder();

            if (Recursive && Directory.Exists(TargetPath))
                args.Append("-r ");

            if (ShowMetadata)
                args.Append("-m ");

            if (IncludeModules)
                args.Append("-w ");

            args.Append($"\"{ruleFile}\" \"{TargetPath}\"");

            return args.ToString();
        }

        private YaraMatch? ParseYaraOutput(string line, string ruleFile)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            // Standard YARA output format: RULE_NAME FILE_PATH
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                return new YaraMatch
                {
                    RuleName = parts[0],
                    FilePath = parts[1],
                    RuleFile = ruleFile,
                    Timestamp = DateTime.Now
                };
            }

            return null;
        }

        private string? FindYaraExecutable()
        {
            if (!string.IsNullOrEmpty(YaraPath) && File.Exists(YaraPath))
                return YaraPath;

            // Check common locations
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var possiblePaths = new[]
            {
                Path.Combine(programFiles, "YARA", "yara64.exe"),
                Path.Combine(programFiles, "YARA", "yara.exe"),
                Path.Combine(programFiles, "yara", "yara64.exe"),
                Path.Combine(programFiles, "yara", "yara.exe"),
                @"C:\Tools\yara\yara64.exe",
                @"C:\Tools\yara\yara.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path)) return path;
            }

            // Try system PATH
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "yara",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });
                process?.WaitForExit(5000);
                var path = process?.StandardOutput.ReadLine();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch { }

            return null;
        }

        private List<string> GetRulesFiles()
        {
            var files = new List<string>();

            if (!string.IsNullOrEmpty(RulesDirectory) && Directory.Exists(RulesDirectory))
            {
                files.AddRange(Directory.GetFiles(RulesDirectory, "*.yar", SearchOption.AllDirectories));
                files.AddRange(Directory.GetFiles(RulesDirectory, "*.yara", SearchOption.AllDirectories));
            }

            return files;
        }

        #endregion

        #region Rule Download

        /// <summary>
        /// Downloads YARA rules from a known source.
        /// </summary>
        public async Task<bool> DownloadRulesAsync(string sourceName, string targetDirectory, CancellationToken cancellationToken = default)
        {
            if (!RuleSources.TryGetValue(sourceName, out var url))
            {
                ReportError($"Unknown rule source: {sourceName}");
                return false;
            }

            ReportProgress($"Downloading {sourceName}...");
            ReportProgress($"URL: {url}");

            try
            {
                Directory.CreateDirectory(targetDirectory);

                var zipPath = Path.Combine(targetDirectory, $"{sourceName.Replace(" ", "_")}.zip");

                // Download
                using var response = await _httpClient.GetAsync(url, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(zipPath, FileMode.Create);
                await response.Content.CopyToAsync(fs, cancellationToken);

                ReportProgress($"Downloaded: {zipPath}");

                // Extract
                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDirectory, true);
                ReportProgress($"Extracted to: {targetDirectory}");

                // Clean up zip
                File.Delete(zipPath);

                RulesDirectory = targetDirectory;
                ReportProgress($"✓ Rules downloaded successfully!");

                return true;
            }
            catch (Exception ex)
            {
                ReportProgress($"✗ Download failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads the YARA executable.
        /// </summary>
        public async Task<bool> DownloadYaraAsync(string targetDirectory, CancellationToken cancellationToken = default)
        {
            const string yaraUrl = "https://github.com/VirusTotal/yara/releases/latest/download/yara-master-2326-win64.zip";

            ReportProgress("Downloading YARA...");
            ReportProgress($"URL: {yaraUrl}");

            try
            {
                Directory.CreateDirectory(targetDirectory);

                var zipPath = Path.Combine(targetDirectory, "yara.zip");

                using var response = await _httpClient.GetAsync(yaraUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var fs = new FileStream(zipPath, FileMode.Create);
                await response.Content.CopyToAsync(fs, cancellationToken);

                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, targetDirectory, true);
                File.Delete(zipPath);

                var yaraExe = Directory.GetFiles(targetDirectory, "yara*.exe", SearchOption.AllDirectories).FirstOrDefault();
                if (yaraExe != null)
                {
                    YaraPath = yaraExe;
                    ReportProgress($"✓ YARA installed: {yaraExe}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                ReportProgress($"✗ Download failed: {ex.Message}");
            }

            return false;
        }

        private void ReportYaraHelp()
        {
            ReportProgress("========================================");
            ReportProgress("YARA - Pattern Matching Tool");
            ReportProgress("========================================");
            ReportProgress("");
            ReportProgress("⚠ YARA executable not found.");
            ReportProgress("");
            ReportProgress("YARA enables:");
            ReportProgress("  • Pattern-based malware detection");
            ReportProgress("  • Custom signature matching");
            ReportProgress("  • File classification");
            ReportProgress("");
            ReportProgress("Download: https://github.com/VirusTotal/yara/releases");
            ReportProgress("");
            ReportProgress("Community Rules:");
            ReportProgress("  • YARA-Forge: https://yarahq.github.io/");
            ReportProgress("  • Signature Base: https://github.com/Neo23x0/signature-base");
            ReportProgress("  • Awesome YARA: https://github.com/InQuest/awesome-yara");
        }

        #endregion

        #region Rule Validation

        /// <summary>
        /// Validates YARA rules for syntax errors.
        /// </summary>
        public async Task<bool> ValidateRulesAsync(CancellationToken cancellationToken = default)
        {
            var yaraExe = FindYaraExecutable();
            if (yaraExe == null)
            {
                ReportError("YARA executable not found");
                return false;
            }

            var rulesFiles = GetRulesFiles();
            if (rulesFiles.Count == 0)
            {
                ReportError("No rule files found");
                return false;
            }

            ReportProgress($"Validating {rulesFiles.Count} rule files...");
            var validCount = 0;
            var errorCount = 0;

            foreach (var ruleFile in rulesFiles)
            {
                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = yaraExe,
                        Arguments = $"-c \"{ruleFile}\"", // Compile only (validate)
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = new Process { StartInfo = startInfo };
                    var error = new StringBuilder();
                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            error.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginErrorReadLine();
                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                    {
                        validCount++;
                    }
                    else
                    {
                        ReportProgress($"  ✗ {Path.GetFileName(ruleFile)}: {error}");
                        errorCount++;
                    }
                }
                catch (Exception ex)
                {
                    ReportProgress($"  ✗ {Path.GetFileName(ruleFile)}: {ex.Message}");
                    errorCount++;
                }
            }

            ReportProgress($"Validation complete: {validCount} valid, {errorCount} errors");
            return errorCount == 0;
        }

        #endregion
    }

    #region Result Models

    public class YaraResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int RulesScanned { get; set; }
        public List<YaraMatch> Matches { get; } = new();
    }

    public class YaraMatch
    {
        public string RuleName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string RuleFile { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Metadata { get; set; } = string.Empty;
    }

    #endregion
}
