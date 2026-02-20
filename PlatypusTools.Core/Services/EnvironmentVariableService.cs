using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for viewing and managing system and user environment variables.
    /// </summary>
    public class EnvironmentVariableService
    {
        public class EnvVariable
        {
            public string Name { get; set; } = "";
            public string Value { get; set; } = "";
            public EnvironmentVariableTarget Target { get; set; }
            public string TargetName => Target.ToString();
            public bool IsPath => Name.Equals("PATH", StringComparison.OrdinalIgnoreCase) ||
                                  Name.Equals("PATHEXT", StringComparison.OrdinalIgnoreCase) ||
                                  Name.Equals("PSModulePath", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get all environment variables from all scopes.
        /// </summary>
        public List<EnvVariable> GetAll()
        {
            var result = new List<EnvVariable>();

            foreach (var target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User, EnvironmentVariableTarget.Process })
            {
                try
                {
                    var vars = Environment.GetEnvironmentVariables(target);
                    foreach (string key in vars.Keys)
                    {
                        result.Add(new EnvVariable
                        {
                            Name = key,
                            Value = vars[key]?.ToString() ?? "",
                            Target = target
                        });
                    }
                }
                catch { }
            }

            return result.OrderBy(v => v.Target).ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Set an environment variable.
        /// </summary>
        public bool SetVariable(string name, string value, EnvironmentVariableTarget target)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, value, target);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Delete an environment variable.
        /// </summary>
        public bool DeleteVariable(string name, EnvironmentVariableTarget target)
        {
            try
            {
                Environment.SetEnvironmentVariable(name, null, target);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get PATH entries as separate items.
        /// </summary>
        public List<string> GetPathEntries(EnvironmentVariableTarget target)
        {
            var path = Environment.GetEnvironmentVariable("PATH", target) ?? "";
            return path.Split(';', StringSplitOptions.RemoveEmptyEntries)
                       .Select(p => p.Trim())
                       .ToList();
        }

        /// <summary>
        /// Add a new PATH entry.
        /// </summary>
        public bool AddPathEntry(string entry, EnvironmentVariableTarget target)
        {
            try
            {
                var entries = GetPathEntries(target);
                if (entries.Contains(entry, StringComparer.OrdinalIgnoreCase))
                    return true; // Already exists

                entries.Add(entry);
                var newPath = string.Join(";", entries);
                Environment.SetEnvironmentVariable("PATH", newPath, target);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Remove a PATH entry.
        /// </summary>
        public bool RemovePathEntry(string entry, EnvironmentVariableTarget target)
        {
            try
            {
                var entries = GetPathEntries(target);
                entries.RemoveAll(e => e.Equals(entry, StringComparison.OrdinalIgnoreCase));
                var newPath = string.Join(";", entries);
                Environment.SetEnvironmentVariable("PATH", newPath, target);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Export all environment variables to a file.
        /// </summary>
        public async Task ExportAsync(string filePath, EnvironmentVariableTarget? targetFilter = null)
        {
            var vars = GetAll();
            if (targetFilter.HasValue)
                vars = vars.Where(v => v.Target == targetFilter.Value).ToList();

            var lines = new List<string>();
            lines.Add($"# Environment Variables Export - {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            lines.Add($"# Target: {(targetFilter?.ToString() ?? "All")}");
            lines.Add("");

            var currentTarget = "";
            foreach (var v in vars)
            {
                if (v.TargetName != currentTarget)
                {
                    if (currentTarget != "") lines.Add("");
                    lines.Add($"# === {v.TargetName} ===");
                    currentTarget = v.TargetName;
                }
                lines.Add($"{v.Name}={v.Value}");
            }

            await System.IO.File.WriteAllLinesAsync(filePath, lines);
        }

        /// <summary>
        /// Import environment variables from a file.
        /// </summary>
        public async Task<int> ImportAsync(string filePath, EnvironmentVariableTarget target)
        {
            int imported = 0;
            var lines = await System.IO.File.ReadAllLinesAsync(filePath);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#')) continue;
                var idx = line.IndexOf('=');
                if (idx <= 0) continue;

                var name = line[..idx].Trim();
                var value = line[(idx + 1)..];

                if (SetVariable(name, value, target))
                    imported++;
            }

            return imported;
        }

        /// <summary>
        /// Search variables by name or value.
        /// </summary>
        public List<EnvVariable> Search(string query)
        {
            if (string.IsNullOrEmpty(query)) return GetAll();
            return GetAll().Where(v =>
                v.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                v.Value.Contains(query, StringComparison.OrdinalIgnoreCase)
            ).ToList();
        }
    }
}
