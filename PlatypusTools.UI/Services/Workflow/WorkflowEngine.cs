using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.Workflow
{
    /// <summary>
    /// Phase 2.3 — minimal in-process workflow engine.
    /// Workflows are linear lists of nodes (the simplest DAG). Each node executes,
    /// optionally produces an output string, and feeds it to the next node via
    /// the shared `Context["last"]` slot. Persisted to JSON (`.platypusflow`).
    /// </summary>
    public sealed class WorkflowEngine
    {
        public sealed class WorkflowNode
        {
            public string Type { get; set; } = "shell";
            public string Name { get; set; } = "Step";
            public Dictionary<string, string> Inputs { get; set; } = new();
        }

        public sealed class Workflow
        {
            public string Name { get; set; } = "New Workflow";
            public string Description { get; set; } = "";
            public List<WorkflowNode> Nodes { get; set; } = new();
        }

        public sealed class StepResult
        {
            public string Node { get; set; } = "";
            public bool Success { get; set; }
            public string Output { get; set; } = "";
            public string? Error { get; set; }
            public TimeSpan Duration { get; set; }
        }

        public event Action<StepResult>? StepCompleted;

        public async Task<List<StepResult>> RunAsync(Workflow flow, CancellationToken ct = default)
        {
            var results = new List<StepResult>();
            var context = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var node in flow.Nodes)
            {
                ct.ThrowIfCancellationRequested();
                var sw = Stopwatch.StartNew();
                var result = new StepResult { Node = node.Name };
                try
                {
                    result.Output = await ExecuteNodeAsync(node, context, ct).ConfigureAwait(false);
                    context["last"] = result.Output;
                    result.Success = true;
                }
                catch (Exception ex)
                {
                    result.Error = ex.Message;
                    result.Success = false;
                }
                sw.Stop();
                result.Duration = sw.Elapsed;
                results.Add(result);
                StepCompleted?.Invoke(result);
                if (!result.Success) break; // stop at first failure
            }
            return results;
        }

        private static async Task<string> ExecuteNodeAsync(WorkflowNode node, Dictionary<string, string> ctx, CancellationToken ct)
        {
            string Subst(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                foreach (var (k, v) in ctx)
                    s = s.Replace($"{{{{{k}}}}}", v);
                return Environment.ExpandEnvironmentVariables(s);
            }

            switch (node.Type.ToLowerInvariant())
            {
                case "shell":
                {
                    var cmd = Subst(node.Inputs.GetValueOrDefault("command", ""));
                    var args = Subst(node.Inputs.GetValueOrDefault("arguments", ""));
                    var psi = new ProcessStartInfo(cmd, args)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi)!;
                    var stdout = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                    var stderr = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                    await p.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (p.ExitCode != 0)
                        throw new InvalidOperationException($"Exit {p.ExitCode}: {stderr.Trim()}");
                    return stdout.Trim();
                }
                case "powershell":
                {
                    var script = Subst(node.Inputs.GetValueOrDefault("script", ""));
                    var psi = new ProcessStartInfo("powershell.exe",
                        $"-NoProfile -ExecutionPolicy Bypass -Command \"{script.Replace("\"", "\\\"")}\"")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var p = Process.Start(psi)!;
                    var stdout = await p.StandardOutput.ReadToEndAsync(ct).ConfigureAwait(false);
                    var stderr = await p.StandardError.ReadToEndAsync(ct).ConfigureAwait(false);
                    await p.WaitForExitAsync(ct).ConfigureAwait(false);
                    if (p.ExitCode != 0)
                        throw new InvalidOperationException($"PS Exit {p.ExitCode}: {stderr.Trim()}");
                    return stdout.Trim();
                }
                case "writefile":
                {
                    var path = Subst(node.Inputs.GetValueOrDefault("path", ""));
                    var content = Subst(node.Inputs.GetValueOrDefault("content", ""));
                    File.WriteAllText(path, content);
                    return path;
                }
                case "readfile":
                {
                    var path = Subst(node.Inputs.GetValueOrDefault("path", ""));
                    return await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                }
                case "delay":
                {
                    var ms = int.TryParse(Subst(node.Inputs.GetValueOrDefault("milliseconds", "1000")),
                        out var d) ? d : 1000;
                    await Task.Delay(ms, ct).ConfigureAwait(false);
                    return $"slept {ms}ms";
                }
                case "log":
                    return Subst(node.Inputs.GetValueOrDefault("message", ""));
                default:
                    throw new NotSupportedException($"Unknown node type '{node.Type}'.");
            }
        }

        public static IReadOnlyList<string> SupportedNodeTypes => new[]
        {
            "shell", "powershell", "writefile", "readfile", "delay", "log"
        };

        public static string Serialize(Workflow flow) =>
            JsonSerializer.Serialize(flow, new JsonSerializerOptions { WriteIndented = true });

        public static Workflow Deserialize(string json) =>
            JsonSerializer.Deserialize<Workflow>(json) ?? new Workflow();
    }
}
