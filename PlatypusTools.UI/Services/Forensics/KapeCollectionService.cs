using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Service for KAPE (Kroll Artifact Parser and Extractor) artifact collection.
    /// Supports both KAPE.exe execution and manual file collection fallback.
    /// </summary>
    public class KapeCollectionService : ForensicOperationBase
    {
        public override string OperationName => "KAPE Collection";

        #region Configuration

        /// <summary>
        /// Gets or sets the path to KAPE.exe.
        /// </summary>
        public string KapePath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the target path to collect artifacts from.
        /// </summary>
        public string TargetPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output path for collected artifacts.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        // Target toggles
        public bool CollectPrefetch { get; set; } = true;
        public bool CollectAmcache { get; set; } = true;
        public bool CollectEventLogs { get; set; } = true;
        public bool CollectMft { get; set; } = false;
        public bool CollectRegistry { get; set; } = true;
        public bool CollectSrum { get; set; } = false;
        public bool CollectShellBags { get; set; } = false;
        public bool CollectJumpLists { get; set; } = true;
        public bool CollectLnkFiles { get; set; } = true;
        public bool CollectShimcache { get; set; } = false;
        public bool CollectRecycleBin { get; set; } = true;
        public bool CollectUsnJrnl { get; set; } = false;
        public bool CollectBrowserData { get; set; } = false;

        #endregion

        #region Collection

        /// <summary>
        /// Runs KAPE artifact collection.
        /// </summary>
        public async Task<KapeResult> CollectAsync(CancellationToken cancellationToken = default)
        {
            var result = new KapeResult();

            if (string.IsNullOrWhiteSpace(TargetPath) || !Directory.Exists(TargetPath))
            {
                ReportError("Please select a valid target path");
                return result;
            }

            await ExecuteWithHandlingAsync(async () =>
            {
                Directory.CreateDirectory(OutputPath);

                ReportProgress("========================================");
                ReportProgress("KAPE Artifact Collection Started");
                ReportProgress("========================================");
                ReportProgress($"Target Path: {TargetPath}");
                ReportProgress($"Output Path: {OutputPath}");
                ReportProgress($"KAPE Path: {(string.IsNullOrEmpty(KapePath) || !File.Exists(KapePath) ? "(not set - using manual collection)" : KapePath)}");
                ReportProgress("");

                if (!string.IsNullOrEmpty(KapePath) && File.Exists(KapePath))
                {
                    await RunKapeExeCollectionAsync(result, cancellationToken);
                }
                else
                {
                    await RunManualCollectionAsync(result, cancellationToken);
                }

            }, cancellationToken);

            return result;
        }

        private async Task RunKapeExeCollectionAsync(KapeResult result, CancellationToken cancellationToken)
        {
            ReportProgress("[MODE] Using KAPE.exe for artifact collection");
            ReportProgress("       KAPE uses raw disk access to bypass file locks");
            ReportProgress("");

            var targetsList = BuildTargetsList();
            var targetsArg = string.Join(",", targetsList.Distinct());
            var targetDrive = Path.GetPathRoot(TargetPath)?.TrimEnd('\\') ?? "C:";

            ReportProgress($"[TARGETS] Selected: {targetsArg}");
            ReportProgress("");

            var kapeArgs = $"--tsource {targetDrive} --tdest \"{OutputPath}\" --target \"{targetsArg}\" --tflush --debug";

            ReportProgress($"[COMMAND] {KapePath}");
            ReportProgress($"[ARGS] {kapeArgs}");
            ReportProgress("");
            ReportProgress("--- KAPE Output ---");

            var startInfo = new ProcessStartInfo
            {
                FileName = KapePath,
                Arguments = kapeArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(KapePath) ?? ""
            };

            using var process = new Process { StartInfo = startInfo };

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    ReportProgress(e.Data);
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    ReportProgress($"[STDERR] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(cancellationToken);

            ReportProgress("");
            ReportProgress("--- End KAPE Output ---");
            ReportProgress("");
            ReportProgress($"[EXIT CODE] {process.ExitCode}");

            await ScanOutputAsync(result);

            result.Success = process.ExitCode == 0;
            result.Message = result.Success
                ? $"KAPE collection complete: {result.Artifacts.Count} artifacts"
                : $"KAPE exited with code {process.ExitCode}";

            if (!result.Success)
            {
                ReportProgress("");
                ReportProgress("Common issues:");
                ReportProgress("  • Run as Administrator for raw disk access");
                ReportProgress("  • Ensure target drive is accessible");
                ReportProgress("  • Check KAPE targets exist in Targets folder");
            }
        }

        private async Task ScanOutputAsync(KapeResult result)
        {
            await Task.Run(() =>
            {
                if (!Directory.Exists(OutputPath)) return;

                var files = Directory.GetFiles(OutputPath, "*", SearchOption.AllDirectories);
                var totalSize = files.Sum(f => new FileInfo(f).Length);

                ReportProgress("");
                ReportProgress($"[OUTPUT SCAN] Found {files.Length} files ({FormatBytes(totalSize)})");

                var folders = files
                    .Select(f => Path.GetDirectoryName(f))
                    .Where(d => d != null)
                    .Distinct()
                    .Select(d => new
                    {
                        Path = d!,
                        Files = Directory.GetFiles(d!, "*", SearchOption.TopDirectoryOnly).Length
                    })
                    .OrderBy(x => x.Path);

                foreach (var folder in folders)
                {
                    var relativePath = Path.GetRelativePath(OutputPath, folder.Path);
                    ReportProgress($"  📁 {relativePath}: {folder.Files} files");

                    result.Artifacts.Add(new KapeArtifact
                    {
                        TargetName = relativePath,
                        SourcePath = TargetPath,
                        OutputPath = folder.Path,
                        FileCount = folder.Files,
                        Timestamp = DateTime.Now
                    });
                }
            });
        }

        private async Task RunManualCollectionAsync(KapeResult result, CancellationToken cancellationToken)
        {
            ReportProgress("[MODE] Manual file collection (KAPE.exe not available)");
            ReportProgress("");
            ReportProgress("⚠ WARNING: Manual collection CANNOT access locked system files!");
            ReportProgress("           The following files require KAPE or raw disk tools:");
            ReportProgress("           • $MFT (Master File Table)");
            ReportProgress("           • Registry hives (SYSTEM, SAM, SECURITY, SOFTWARE)");
            ReportProgress("           • $UsnJrnl (Change journal)");
            ReportProgress("           • Some event logs currently in use");
            ReportProgress("");
            ReportProgress("💡 TIP: Download KAPE from https://www.kroll.com/kape");
            ReportProgress("");

            var targets = GetManualTargets();
            var enabledTargets = targets.Where(t => t.enabled).ToList();
            var completed = 0;
            var successCount = 0;
            var failCount = 0;

            foreach (var (_, name, relativePath) in enabledTargets)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ReportProgress($"Collecting {name}...", (completed * 100.0) / enabledTargets.Count);

                var sourcePath = Path.Combine(TargetPath, relativePath);
                var destPath = Path.Combine(OutputPath, name);

                var (success, error) = await CollectTargetAsync(sourcePath, destPath, name);

                if (success)
                {
                    successCount++;
                    if (Directory.Exists(destPath) && Directory.GetFiles(destPath, "*", SearchOption.AllDirectories).Length > 0)
                    {
                        var fileCount = Directory.GetFiles(destPath, "*", SearchOption.AllDirectories).Length;
                        result.Artifacts.Add(new KapeArtifact
                        {
                            TargetName = name,
                            SourcePath = relativePath,
                            OutputPath = destPath,
                            FileCount = fileCount,
                            Timestamp = DateTime.Now
                        });
                    }
                }
                else
                {
                    failCount++;
                    result.Errors.Add($"{name}: {error}");
                }

                completed++;
            }

            ReportProgress("");
            ReportProgress("========================================");
            ReportProgress("Collection Summary");
            ReportProgress("========================================");
            ReportProgress($"Successful: {successCount}");
            ReportProgress($"Failed: {failCount}");
            ReportProgress($"Output: {OutputPath}");

            result.Success = successCount > 0;
            result.Message = $"Collection complete: {result.Artifacts.Count} artifacts ({failCount} failed)";
        }

        private async Task<(bool success, string error)> CollectTargetAsync(string sourcePath, string destPath, string name)
        {
            try
            {
                if (File.Exists(sourcePath))
                {
                    Directory.CreateDirectory(destPath);
                    await Task.Run(() => File.Copy(sourcePath, Path.Combine(destPath, Path.GetFileName(sourcePath)), true));
                    ReportProgress($"✓ Collected: {name}");
                    return (true, string.Empty);
                }
                else if (Directory.Exists(sourcePath))
                {
                    var fileCount = await CopyDirectoryAsync(sourcePath, destPath);
                    if (fileCount > 0)
                    {
                        ReportProgress($"✓ Collected: {name} ({fileCount} files)");
                        return (true, string.Empty);
                    }
                    else
                    {
                        ReportProgress($"⚠ Collected: {name} (0 files - may be access denied)");
                        return (true, string.Empty);
                    }
                }
                else
                {
                    ReportProgress($"⚠ Not found: {name}");
                    return (false, "Path not found");
                }
            }
            catch (UnauthorizedAccessException)
            {
                ReportProgress($"✗ Access denied: {name} (requires KAPE for raw disk access)");
                return (false, "Access denied - requires KAPE");
            }
            catch (IOException ex) when (ex.HResult == -2147024864) // File in use
            {
                ReportProgress($"✗ File locked: {name} (requires KAPE for raw disk access)");
                return (false, "File locked - requires KAPE");
            }
            catch (Exception ex)
            {
                ReportProgress($"✗ Error collecting {name}: {ex.Message}");
                return (false, ex.Message);
            }
        }

        private async Task<int> CopyDirectoryAsync(string source, string dest)
        {
            return await Task.Run(() =>
            {
                Directory.CreateDirectory(dest);
                var copiedCount = 0;

                foreach (var file in Directory.GetFiles(source))
                {
                    try
                    {
                        File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                        copiedCount++;
                    }
                    catch { /* Skip locked files */ }
                }

                foreach (var dir in Directory.GetDirectories(source))
                {
                    try
                    {
                        copiedCount += CopyDirectorySync(dir, Path.Combine(dest, Path.GetFileName(dir)));
                    }
                    catch { /* Skip inaccessible dirs */ }
                }

                return copiedCount;
            });
        }

        private int CopyDirectorySync(string source, string dest)
        {
            int copiedCount = 0;
            Directory.CreateDirectory(dest);

            foreach (var file in Directory.GetFiles(source))
            {
                try
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                    copiedCount++;
                }
                catch { }
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                try
                {
                    copiedCount += CopyDirectorySync(dir, Path.Combine(dest, Path.GetFileName(dir)));
                }
                catch { }
            }

            return copiedCount;
        }

        private List<string> BuildTargetsList()
        {
            var targets = new List<string>();

            if (CollectPrefetch) targets.Add("Prefetch");
            if (CollectAmcache) targets.Add("Amcache");
            if (CollectEventLogs) targets.Add("EventLogs");
            if (CollectMft) targets.Add("$MFT");
            if (CollectRegistry) targets.Add("RegistryHives");
            if (CollectSrum) targets.Add("SRUM");
            if (CollectShellBags) targets.Add("ShellBags");
            if (CollectJumpLists) targets.Add("JumpLists");
            if (CollectLnkFiles) targets.Add("LNKFiles");
            if (CollectShimcache) targets.Add("RegistryHives");
            if (CollectRecycleBin) targets.Add("RecycleBin");
            if (CollectUsnJrnl) targets.Add("$J");
            if (CollectBrowserData) targets.Add("WebBrowsers");

            return targets;
        }

        private List<(bool enabled, string name, string path)> GetManualTargets()
        {
            return new List<(bool, string, string)>
            {
                (CollectPrefetch, "Prefetch", "Windows\\Prefetch"),
                (CollectAmcache, "Amcache", "Windows\\appcompat\\Programs\\Amcache.hve"),
                (CollectEventLogs, "EventLogs", "Windows\\System32\\winevt\\Logs"),
                (CollectMft, "$MFT", "$MFT"),
                (CollectRegistry, "Registry", "Windows\\System32\\config"),
                (CollectSrum, "SRUM", "Windows\\System32\\sru"),
                (CollectShellBags, "ShellBags", "Users"),
                (CollectJumpLists, "JumpLists", "Users"),
                (CollectLnkFiles, "LNK Files", "Users"),
                (CollectShimcache, "Shimcache", "Windows\\System32\\config\\SYSTEM"),
                (CollectRecycleBin, "RecycleBin", "$Recycle.Bin"),
                (CollectUsnJrnl, "$UsnJrnl", "$Extend\\$UsnJrnl")
            };
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {suffixes[order]}";
        }

        #endregion
    }

    #region Result Models

    public class KapeResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<KapeArtifact> Artifacts { get; } = new();
        public List<string> Errors { get; } = new();
    }

    public class KapeArtifact
    {
        public string TargetName { get; set; } = string.Empty;
        public string SourcePath { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public int FileCount { get; set; }
        public DateTime Timestamp { get; set; }
    }

    #endregion
}
