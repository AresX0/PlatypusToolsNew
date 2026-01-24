using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Service for Velociraptor endpoint visibility and artifact collection.
    /// Supports VQL-based threat hunting and forensic collection.
    /// </summary>
    public class VelociraptorService : ForensicOperationBase
    {
        public override string OperationName => "Velociraptor Collection";

        #region Configuration

        /// <summary>
        /// Gets or sets the path to Velociraptor executable.
        /// </summary>
        public string VelociraptorPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the Velociraptor config file path.
        /// </summary>
        public string ConfigPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output directory for collected artifacts.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the artifact to collect.
        /// </summary>
        public string Artifact { get; set; } = "Windows.KapeFiles.Targets";

        #endregion

        #region Predefined Artifacts

        public static readonly string[] CommonArtifacts = new[]
        {
            "Windows.KapeFiles.Targets",
            "Windows.Timeline.MFT",
            "Windows.Sysinternals.Autoruns",
            "Windows.System.Pslist",
            "Windows.Network.Netstat",
            "Windows.Forensics.SRUM",
            "Windows.Forensics.Prefetch",
            "Windows.EventLogs.Hayabusa",
            "Windows.EventLogs.Chainsaw",
            "Generic.Forensic.LocalHashes.Glob"
        };

        #endregion

        #region Collection

        /// <summary>
        /// Runs Velociraptor artifact collection.
        /// </summary>
        public async Task<VelociraptorResult> CollectAsync(CancellationToken cancellationToken = default)
        {
            var result = new VelociraptorResult();

            if (string.IsNullOrWhiteSpace(VelociraptorPath) || !File.Exists(VelociraptorPath))
            {
                ReportError("Please select Velociraptor executable");
                ReportVelociraptorHelp();
                return result;
            }

            await ExecuteWithHandlingAsync(async () =>
            {
                var outputDir = Path.Combine(OutputPath, "Velociraptor");
                Directory.CreateDirectory(outputDir);

                ReportProgress("========================================");
                ReportProgress("Velociraptor Artifact Collection");
                ReportProgress("========================================");
                ReportProgress($"Executable: {VelociraptorPath}");
                ReportProgress($"Artifact: {Artifact}");
                ReportProgress($"Output Dir: {outputDir}");
                ReportProgress("");

                var outputFile = Path.Combine(outputDir,
                    $"velociraptor_{Artifact.Replace(".", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                var args = BuildArguments();

                ReportProgress($"[COMMAND] {VelociraptorPath}");
                ReportProgress($"[ARGS] {args}");
                ReportProgress("");
                ReportProgress("--- Velociraptor Output ---");

                var (success, output, error) = await RunVelociraptorAsync(args, cancellationToken);

                ReportProgress("");
                ReportProgress("--- End Velociraptor Output ---");

                // Save output to file
                if (!string.IsNullOrEmpty(output))
                {
                    await File.WriteAllTextAsync(outputFile, output, cancellationToken);
                }

                if (success)
                {
                    var recordCount = CountJsonRecords(outputFile);
                    ReportProgress("");
                    ReportProgress($"✓ Collection complete: {Artifact}");
                    ReportProgress($"  Records collected: {recordCount}");
                    ReportProgress($"  Output file: {outputFile}");

                    result.Success = true;
                    result.OutputPath = outputFile;
                    result.RecordCount = recordCount;
                    result.Message = $"Velociraptor collection complete: {recordCount} records";
                }
                else
                {
                    ReportProgress("");
                    ReportProgress("✗ Velociraptor collection failed.");
                    ReportProgress($"Error: {error}");
                    ReportProgress("");
                    ReportProgress("Troubleshooting:");
                    ReportProgress("  • Run as Administrator");
                    ReportProgress("  • Verify artifact name exists");
                    ReportProgress("  • Check Velociraptor version");

                    result.Success = false;
                    result.Message = "Velociraptor collection failed - see log";
                    result.Error = error;
                }

            }, cancellationToken);

            return result;
        }

        /// <summary>
        /// Lists available artifacts.
        /// </summary>
        public async Task<string[]> ListArtifactsAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(VelociraptorPath) || !File.Exists(VelociraptorPath))
                return CommonArtifacts;

            try
            {
                var args = "artifacts list --format json";
                if (!string.IsNullOrEmpty(ConfigPath) && File.Exists(ConfigPath))
                    args = $"--config \"{ConfigPath}\" " + args;

                var (success, output, _) = await RunVelociraptorAsync(args, cancellationToken);

                if (success && !string.IsNullOrEmpty(output))
                {
                    // Parse JSON array of artifact names
                    using var doc = JsonDocument.Parse(output);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var artifacts = new System.Collections.Generic.List<string>();
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            var name = element.GetProperty("name").GetString();
                            if (!string.IsNullOrEmpty(name))
                                artifacts.Add(name);
                        }
                        return artifacts.ToArray();
                    }
                }
            }
            catch { }

            return CommonArtifacts;
        }

        private string BuildArguments()
        {
            var args = new StringBuilder();

            if (!string.IsNullOrEmpty(ConfigPath) && File.Exists(ConfigPath))
            {
                args.Append($"--config \"{ConfigPath}\" ");
            }

            args.Append($"artifacts collect {Artifact} --format json");

            return args.ToString();
        }

        private async Task<(bool success, string output, string error)> RunVelociraptorAsync(
            string arguments, CancellationToken cancellationToken)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = VelociraptorPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(VelociraptorPath) ?? ""
                };

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        ReportProgress(e.Data);
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        ReportProgress($"[STDERR] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken);

                ReportProgress($"[EXIT CODE] {process.ExitCode}");

                return (process.ExitCode == 0, outputBuilder.ToString(), errorBuilder.ToString());
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        private int CountJsonRecords(string filePath)
        {
            if (!File.Exists(filePath)) return 0;

            try
            {
                var json = File.ReadAllText(filePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    return doc.RootElement.GetArrayLength();
            }
            catch { }

            return 0;
        }

        private void ReportVelociraptorHelp()
        {
            ReportProgress("========================================");
            ReportProgress("Velociraptor - Endpoint Visibility Tool");
            ReportProgress("========================================");
            ReportProgress("");
            ReportProgress("⚠ Velociraptor executable not configured.");
            ReportProgress("");
            ReportProgress("Velociraptor enables:");
            ReportProgress("  • Live forensic collection");
            ReportProgress("  • VQL-based threat hunting");
            ReportProgress("  • Fleet-wide artifact collection");
            ReportProgress("  • Real-time monitoring");
            ReportProgress("");
            ReportProgress("💡 Click 'Download Velociraptor' to get the tool.");
            ReportProgress("");
            ReportProgress("Download: https://github.com/Velocidex/velociraptor/releases");
        }

        #endregion
    }

    #region Result Models

    public class VelociraptorResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public int RecordCount { get; set; }
    }

    #endregion
}
