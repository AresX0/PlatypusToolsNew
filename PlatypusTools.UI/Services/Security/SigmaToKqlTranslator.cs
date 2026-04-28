using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PlatypusTools.UI.Services.Security
{
    /// <summary>
    /// Phase 4.3 — minimal Sigma → KQL translator. Covers the most common Sigma
    /// patterns (single `selection` block, `condition: selection`, basic AND/OR,
    /// equality, contains, startswith/endswith, regex). Unsupported constructs
    /// surface as KQL comments so the analyst can complete by hand.
    /// </summary>
    public static class SigmaToKqlTranslator
    {
        public sealed record Result(bool Success, string Kql, IReadOnlyList<string> Notes);

        public static Result Translate(string sigmaYaml)
        {
            if (string.IsNullOrWhiteSpace(sigmaYaml))
                return new Result(false, "", new[] { "Empty input." });

            var notes = new List<string>();
            var doc = ParseSimpleYaml(sigmaYaml, notes);

            var title = doc.GetValueOrDefault("title", "Untitled rule");
            var logsource = doc.TryGetValue("logsource", out var lsObj) && lsObj is Dictionary<string, object> ls
                ? ls : new Dictionary<string, object>();

            // Build a KQL `where` from the first selection in `detection`.
            string tableHint = MapTable(logsource);
            var kql = new StringBuilder();
            kql.AppendLine($"// Sigma rule: {title}");
            if (!string.IsNullOrEmpty(tableHint))
                kql.AppendLine(tableHint);

            if (!doc.TryGetValue("detection", out var detObj) || detObj is not Dictionary<string, object> detection)
            {
                notes.Add("No detection block found.");
                return new Result(false, kql.ToString(), notes);
            }

            // Pick the first non-condition key as the selection.
            var selectionKey = detection.Keys.FirstOrDefault(k => !string.Equals(k, "condition", StringComparison.OrdinalIgnoreCase));
            if (selectionKey == null || detection[selectionKey] is not Dictionary<string, object> selection)
            {
                notes.Add("No selection block recognized; only the simplest detection layout is supported.");
                return new Result(false, kql.ToString(), notes);
            }

            var clauses = new List<string>();
            foreach (var (k, v) in selection)
            {
                clauses.Add(MapField(k, v, notes));
            }

            kql.AppendLine($"| where {string.Join(" and ", clauses)}");

            // Honor a simple `condition: not selection` if present.
            if (detection.TryGetValue("condition", out var condObj) && condObj is string cond)
            {
                cond = cond.Trim();
                if (cond.StartsWith("not ", StringComparison.OrdinalIgnoreCase))
                {
                    // Wrap the where in not()
                    var whereLine = $"| where not({string.Join(" and ", clauses)})";
                    var lines = kql.ToString().TrimEnd().Split('\n').ToList();
                    lines[^1] = whereLine;
                    kql.Clear();
                    foreach (var l in lines) kql.AppendLine(l);
                }
                else if (!string.Equals(cond, selectionKey, StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add($"Condition '{cond}' has multi-selection logic that this minimal translator does not yet handle.");
                }
            }

            return new Result(true, kql.ToString(), notes);
        }

        private static string MapTable(Dictionary<string, object> logsource)
        {
            var product = (logsource.GetValueOrDefault("product", "") ?? "").ToLowerInvariant();
            var category = (logsource.GetValueOrDefault("category", "") ?? "").ToLowerInvariant();
            var service = (logsource.GetValueOrDefault("service", "") ?? "").ToLowerInvariant();

            // Common mappings for Microsoft Sentinel / Defender XDR.
            if (product == "windows")
            {
                if (category == "process_creation") return "DeviceProcessEvents";
                if (category == "image_load")       return "DeviceImageLoadEvents";
                if (category == "network_connection") return "DeviceNetworkEvents";
                if (category == "registry_event")   return "DeviceRegistryEvents";
                if (category == "file_event")       return "DeviceFileEvents";
                if (service == "security")          return "SecurityEvent";
                if (service == "sysmon")            return "DeviceProcessEvents // (Sysmon → MDE mapping is approximate)";
                return "SecurityEvent";
            }
            if (product == "azure" || product == "m365") return "SigninLogs // adjust to AuditLogs/SigninLogs as needed";
            return $"// (No automatic table mapping for product='{product}' category='{category}' service='{service}')";
        }

        private static string MapField(string key, object? value, List<string> notes)
        {
            // Sigma modifiers: Field|contains, Field|startswith, etc.
            string field = key;
            string op = "==";
            int pipe = key.IndexOf('|');
            if (pipe > 0)
            {
                field = key[..pipe];
                var mod = key[(pipe + 1)..].ToLowerInvariant();
                op = mod switch
                {
                    "contains" => "contains",
                    "startswith" => "startswith",
                    "endswith" => "endswith",
                    "re" => "matches regex",
                    _ => "=="
                };
                if (op == "==" && mod != "")
                    notes.Add($"Modifier '|{mod}' not supported; falling back to equality.");
            }

            // Field name remap to MDE column conventions.
            field = MapFieldName(field);

            if (value is List<object> list)
            {
                if (list.Count == 0) return "true";
                var parts = list.Select(v => SingleClause(field, op, v));
                return "(" + string.Join(" or ", parts) + ")";
            }
            return SingleClause(field, op, value);
        }

        private static string SingleClause(string field, string op, object? v)
        {
            var s = v?.ToString() ?? "";
            // Escape double-quotes
            s = s.Replace("\"", "\\\"");
            return op switch
            {
                "contains" or "startswith" or "endswith" or "matches regex" => $"{field} {op} \"{s}\"",
                _ => $"{field} == \"{s}\""
            };
        }

        private static string MapFieldName(string name) => name switch
        {
            "Image" => "FileName",
            "ProcessName" => "FileName",
            "CommandLine" => "ProcessCommandLine",
            "ParentImage" => "InitiatingProcessFileName",
            "ParentCommandLine" => "InitiatingProcessCommandLine",
            "TargetFilename" => "FolderPath",
            "User" => "AccountName",
            _ => name
        };

        // Extremely tiny YAML reader handling Sigma's typical shape only:
        // top-level mapping, nested mappings (1 level), inline lists `[a, b]`,
        // block lists `- a`, comments, and quoted strings.
        private static Dictionary<string, object> ParseSimpleYaml(string text, List<string> notes)
        {
            var root = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<(int Indent, Dictionary<string, object> Map, string? PendingKey)>();
            stack.Push((0, root, null));

            foreach (var rawLine in text.Replace("\r", "").Split('\n'))
            {
                var line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;

                int indent = line.Length - line.TrimStart().Length;
                while (stack.Count > 1 && stack.Peek().Indent >= indent)
                    stack.Pop();

                var content = line.Trim();
                if (content.StartsWith("- "))
                {
                    notes.Add("Block-list items are not fully supported.");
                    continue;
                }

                int colon = content.IndexOf(':');
                if (colon < 0) continue;
                var key = content[..colon].Trim();
                var rest = content[(colon + 1)..].Trim();

                var top = stack.Peek();
                if (string.IsNullOrEmpty(rest))
                {
                    var child = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                    top.Map[key] = child;
                    stack.Push((indent + 1, child, null));
                }
                else if (rest.StartsWith("[") && rest.EndsWith("]"))
                {
                    var inner = rest[1..^1];
                    var items = inner.Split(',').Select(p => (object)Unquote(p.Trim())).ToList();
                    top.Map[key] = items;
                }
                else
                {
                    top.Map[key] = Unquote(rest);
                }
            }
            return root;
        }

        private static string Unquote(string s)
        {
            if (s.Length >= 2 && (s[0] == '"' && s[^1] == '"' || s[0] == '\'' && s[^1] == '\''))
                return s[1..^1];
            return s;
        }
    }

    internal static class DictExtensions
    {
        public static string GetValueOrDefault(this Dictionary<string, object> dict, string key, string defaultValue)
        {
            return dict.TryGetValue(key, out var v) ? v?.ToString() ?? defaultValue : defaultValue;
        }
    }
}
