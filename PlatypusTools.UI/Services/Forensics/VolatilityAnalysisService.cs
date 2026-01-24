using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Forensics
{
    /// <summary>
    /// Service for running Volatility 3 memory analysis.
    /// Extracts process lists, network connections, malware artifacts, DLLs, handles, and command lines.
    /// </summary>
    public class VolatilityAnalysisService : ForensicOperationBase
    {
        public override string OperationName => "Volatility Analysis";

        #region Plugin Configuration

        /// <summary>
        /// Gets or sets the path to Volatility installation.
        /// </summary>
        public string VolatilityPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the memory dump file to analyze.
        /// </summary>
        public string MemoryDumpPath { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the output directory for analysis results.
        /// </summary>
        public string OutputPath { get; set; } = string.Empty;

        // Plugin toggles
        public bool RunPsList { get; set; } = true;
        public bool RunNetScan { get; set; } = true;
        public bool RunMalfind { get; set; } = true;
        public bool RunDllList { get; set; } = false;
        public bool RunHandles { get; set; } = false;
        public bool RunCmdline { get; set; } = true;
        public bool RunPsScan { get; set; } = false;
        public bool RunFileScan { get; set; } = false;
        public bool RunRegistry { get; set; } = false;

        #endregion

        #region Analysis

        /// <summary>
        /// Runs Volatility 3 analysis with selected plugins.
        /// </summary>
        public async Task<VolatilityResult> AnalyzeAsync(CancellationToken cancellationToken = default)
        {
            var result = new VolatilityResult();

            if (string.IsNullOrWhiteSpace(MemoryDumpPath) || !File.Exists(MemoryDumpPath))
            {
                LogError("Please select a valid memory dump file");
                return result;
            }

            await ExecuteWithHandlingAsync(async (token) =>
            {
                Directory.CreateDirectory(OutputPath);
                Log($"Starting Volatility 3 analysis on: {MemoryDumpPath}");

                var plugins = GetSelectedPlugins();
                var completed = 0;

                foreach (var (plugin, name) in plugins)
                {
                    token.ThrowIfCancellationRequested();

                    StatusMessage = $"Running {name}...";
                    Progress = (completed * 100.0) / plugins.Count;

                    var outputFile = Path.Combine(OutputPath, $"vol_{plugin.Replace(".", "_")}.json");
                    var pluginResult = await RunPluginAsync(plugin, outputFile, cancellationToken);

                    if (pluginResult.Success)
                    {
                        var recordCount = CountJsonRecords(outputFile);
                        result.Artifacts.Add(new VolatilityArtifact
                        {
                            PluginName = plugin,
                            DisplayName = name,
                            OutputPath = outputFile,
                            RecordCount = recordCount,
                            Timestamp = DateTime.Now,
                            IsSimulated = pluginResult.IsSimulated
                        });
                        LogSuccess($"{name}: {recordCount} records");
                    }
                    else
                    {
                        LogError($"{name} failed: {pluginResult.Error}");
                        result.Errors.Add($"{name}: {pluginResult.Error}");
                    }

                    completed++;
                }

                result.Success = result.Artifacts.Any();
                result.Message = $"Analysis complete: {result.Artifacts.Count} artifacts generated";
                StatusMessage = result.Message;
                Progress = 100;

            });

            return result;
        }

        private List<(string plugin, string name)> GetSelectedPlugins()
        {
            var plugins = new List<(string, string)>();

            if (RunPsList) plugins.Add(("windows.pslist", "Process List"));
            if (RunNetScan) plugins.Add(("windows.netscan", "Network Connections"));
            if (RunMalfind) plugins.Add(("windows.malfind", "Malware Detection"));
            if (RunDllList) plugins.Add(("windows.dlllist", "DLL List"));
            if (RunHandles) plugins.Add(("windows.handles", "Handles"));
            if (RunCmdline) plugins.Add(("windows.cmdline", "Command Lines"));
            if (RunPsScan) plugins.Add(("windows.psscan", "Process Scan"));
            if (RunFileScan) plugins.Add(("windows.filescan", "File Scan"));
            if (RunRegistry) plugins.Add(("windows.registry.printkey", "Registry Keys"));

            return plugins;
        }

        private async Task<PluginRunResult> RunPluginAsync(string plugin, string outputFile, CancellationToken cancellationToken)
        {
            var volExe = FindVolatilityExecutable();

            if (volExe == null)
            {
                LogWarning($"Volatility not found. Simulating {plugin} output...");
                await File.WriteAllTextAsync(outputFile, $"[{{\"plugin\":\"{plugin}\",\"status\":\"simulated\"}}]", cancellationToken);
                return new PluginRunResult { Success = true, IsSimulated = true };
            }

            try
            {
                var args = $"\"{volExe}\" -f \"{MemoryDumpPath}\" -r json {plugin}";
                var isPython = volExe.EndsWith(".py", StringComparison.OrdinalIgnoreCase);
                var fileName = isPython ? "python" : volExe;
                var arguments = isPython ? args : $"-f \"{MemoryDumpPath}\" -r json {plugin}";

                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(volExe) ?? ""
                };

                using var process = new Process { StartInfo = startInfo };
                var output = new StringBuilder();
                var error = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null) output.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null) error.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode == 0)
                {
                    await File.WriteAllTextAsync(outputFile, output.ToString(), cancellationToken);
                    return new PluginRunResult { Success = true };
                }

                return new PluginRunResult
                {
                    Success = false,
                    Error = error.Length > 0 ? error.ToString() : $"Exit code: {process.ExitCode}"
                };
            }
            catch (Exception ex)
            {
                return new PluginRunResult { Success = false, Error = ex.Message };
            }
        }

        private string? FindVolatilityExecutable()
        {
            if (!string.IsNullOrEmpty(VolatilityPath))
            {
                var volPy = Path.Combine(VolatilityPath, "vol.py");
                if (File.Exists(volPy)) return volPy;

                var volExe = Path.Combine(VolatilityPath, "vol.exe");
                if (File.Exists(volExe)) return volExe;

                if (File.Exists(VolatilityPath)) return VolatilityPath;
            }

            // Try system PATH
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "vol",
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

        #endregion

        #region Helper Classes

        private class PluginRunResult
        {
            public bool Success { get; set; }
            public bool IsSimulated { get; set; }
            public string Error { get; set; } = string.Empty;
        }

        #endregion
    }

    #region Result Models

    public class VolatilityResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public List<VolatilityArtifact> Artifacts { get; } = new();
        public List<string> Errors { get; } = new();
    }

    public class VolatilityArtifact
    {
        public string PluginName { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public int RecordCount { get; set; }
        public DateTime Timestamp { get; set; }
        public bool IsSimulated { get; set; }
    }

    #endregion
}
