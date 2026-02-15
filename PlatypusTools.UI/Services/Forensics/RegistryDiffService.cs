using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services.Forensics
{
    #region Models

    /// <summary>
    /// Registry snapshot for comparison.
    /// </summary>
    public class RegistrySnapshot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string MachineName { get; set; } = Environment.MachineName;
        public List<RegistryKeySnapshot> Keys { get; set; } = new();
        public List<string> ScannedHives { get; set; } = new();
        public int TotalKeys { get; set; }
        public int TotalValues { get; set; }
    }

    /// <summary>
    /// Snapshot of a single registry key.
    /// </summary>
    public class RegistryKeySnapshot
    {
        public string Path { get; set; } = string.Empty;
        public DateTime LastWriteTime { get; set; }
        public List<RegistryValueSnapshot> Values { get; set; } = new();
        public List<string> SubKeys { get; set; } = new();
    }

    /// <summary>
    /// Snapshot of a registry value.
    /// </summary>
    public class RegistryValueSnapshot
    {
        public string Name { get; set; } = string.Empty;
        public RegistryValueKind Kind { get; set; }
        public string? Value { get; set; }
        public string? ValueHash { get; set; } // For binary values
    }

    /// <summary>
    /// Types of registry changes.
    /// </summary>
    public enum RegistryChangeType
    {
        KeyAdded,
        KeyRemoved,
        KeyModified,
        ValueAdded,
        ValueRemoved,
        ValueModified
    }

    /// <summary>
    /// A single registry change between snapshots.
    /// </summary>
    public class RegistryChange
    {
        public RegistryChangeType ChangeType { get; set; }
        public string KeyPath { get; set; } = string.Empty;
        public string? ValueName { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public RegistryValueKind? ValueKind { get; set; }
        public string? Category { get; set; } // persistence, startup, etc.
        public string? Severity { get; set; } // info, warning, critical
    }

    /// <summary>
    /// Result of comparing two registry snapshots.
    /// </summary>
    public class RegistryDiffResult
    {
        public string BaselineId { get; set; } = string.Empty;
        public string CompareId { get; set; } = string.Empty;
        public DateTime BaselineTime { get; set; }
        public DateTime CompareTime { get; set; }
        public DateTime DiffTime { get; set; } = DateTime.Now;
        public List<RegistryChange> Changes { get; } = new();
        
        public int KeysAdded => Changes.Count(c => c.ChangeType == RegistryChangeType.KeyAdded);
        public int KeysRemoved => Changes.Count(c => c.ChangeType == RegistryChangeType.KeyRemoved);
        public int ValuesAdded => Changes.Count(c => c.ChangeType == RegistryChangeType.ValueAdded);
        public int ValuesRemoved => Changes.Count(c => c.ChangeType == RegistryChangeType.ValueRemoved);
        public int ValuesModified => Changes.Count(c => c.ChangeType == RegistryChangeType.ValueModified);
        public int TotalChanges => Changes.Count;
    }

    #endregion

    /// <summary>
    /// Registry Diff Tool for creating snapshots and detecting changes.
    /// Useful for malware analysis, persistence detection, and forensic analysis.
    /// </summary>
    public class RegistryDiffService : ForensicOperationBase
    {
        private static readonly string SnapshotsPath = Path.Combine(
            PlatypusTools.UI.Services.SettingsManager.DataDirectory, "RegistrySnapshots");

        public override string OperationName => "Registry Diff Tool";

        // Default keys to monitor for persistence
        private static readonly string[] PersistenceKeys = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunServices",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunServicesOnce",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer\Run",
            @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Winlogon",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows",
            @"SYSTEM\CurrentControlSet\Services",
            @"SYSTEM\CurrentControlSet\Control\Session Manager",
            @"SOFTWARE\Classes\*\shell",
            @"SOFTWARE\Classes\Directory\shell",
            @"SOFTWARE\Classes\Folder\shell",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellExecuteHooks",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Browser Helper Objects",
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options"
        };

        // Security-sensitive keys
        private static readonly string[] SecurityKeys = new[]
        {
            @"SYSTEM\CurrentControlSet\Control\Lsa",
            @"SYSTEM\CurrentControlSet\Control\SecurityProviders",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies",
            @"SOFTWARE\Policies\Microsoft\Windows",
            @"SECURITY\Policy"
        };

        // Options
        public bool ScanHKLM { get; set; } = true;
        public bool ScanHKCU { get; set; } = true;
        public bool ScanHKU { get; set; } = false;
        public bool ScanPersistenceKeysOnly { get; set; } = false;
        public bool IncludeSecurityKeys { get; set; } = true;
        public int MaxDepth { get; set; } = 10;
        public bool SkipBinaryValues { get; set; } = false;

        public RegistryDiffService()
        {
            Directory.CreateDirectory(SnapshotsPath);
        }

        #region Snapshot Creation

        /// <summary>
        /// Creates a full registry snapshot.
        /// </summary>
        public async Task<RegistrySnapshot> CreateSnapshotAsync(string name, string? description = null, CancellationToken token = default)
        {
            var snapshot = new RegistrySnapshot
            {
                Name = name,
                Description = description ?? $"Snapshot created at {DateTime.Now}"
            };

            LogHeader($"Creating Registry Snapshot: {name}");

            var keysToScan = GetKeysToScan();
            var totalKeys = 0;
            var totalValues = 0;

            await Task.Run(() =>
            {
                foreach (var (hive, hiveName) in GetHivesToScan())
                {
                    token.ThrowIfCancellationRequested();
                    snapshot.ScannedHives.Add(hiveName);

                    foreach (var keyPath in keysToScan)
                    {
                        token.ThrowIfCancellationRequested();

                        try
                        {
                            using var key = hive.OpenSubKey(keyPath);
                            if (key != null)
                            {
                                var keySnapshot = ScanKey(hiveName, keyPath, key, 0, ref totalKeys, ref totalValues, token);
                                if (keySnapshot != null)
                                {
                                    snapshot.Keys.Add(keySnapshot);
                                }
                            }
                        }
                        catch (System.Security.SecurityException)
                        {
                            // Skip keys we don't have access to
                        }
                        catch (Exception ex)
                        {
                            LogWarning($"Error scanning {hiveName}\\{keyPath}: {ex.Message}");
                        }
                    }

                    ReportProgress(hiveName);
                }
            }, token);

            snapshot.TotalKeys = totalKeys;
            snapshot.TotalValues = totalValues;

            LogSuccess($"Snapshot complete: {totalKeys} keys, {totalValues} values");

            return snapshot;
        }

        private IEnumerable<string> GetKeysToScan()
        {
            if (ScanPersistenceKeysOnly)
            {
                foreach (var key in PersistenceKeys) yield return key;
                if (IncludeSecurityKeys)
                {
                    foreach (var key in SecurityKeys) yield return key;
                }
            }
            else
            {
                yield return "SOFTWARE";
                yield return "SYSTEM";
                if (IncludeSecurityKeys) yield return "SECURITY";
            }
        }

        private IEnumerable<(RegistryKey hive, string name)> GetHivesToScan()
        {
            if (ScanHKLM) yield return (Registry.LocalMachine, "HKLM");
            if (ScanHKCU) yield return (Registry.CurrentUser, "HKCU");
            if (ScanHKU) yield return (Registry.Users, "HKU");
        }

        private RegistryKeySnapshot? ScanKey(string hiveName, string keyPath, RegistryKey key, int depth, ref int totalKeys, ref int totalValues, CancellationToken token)
        {
            if (depth > MaxDepth) return null;
            token.ThrowIfCancellationRequested();

            totalKeys++;

            var snapshot = new RegistryKeySnapshot
            {
                Path = $"{hiveName}\\{keyPath}"
            };

            // Get values
            foreach (var valueName in key.GetValueNames())
            {
                try
                {
                    var kind = key.GetValueKind(valueName);
                    var value = key.GetValue(valueName);
                    
                    var valueSnapshot = new RegistryValueSnapshot
                    {
                        Name = valueName,
                        Kind = kind
                    };

                    if (kind == RegistryValueKind.Binary && SkipBinaryValues)
                    {
                        valueSnapshot.Value = "[Binary data skipped]";
                        valueSnapshot.ValueHash = ComputeHash((byte[]?)value);
                    }
                    else
                    {
                        valueSnapshot.Value = FormatValue(value, kind);
                    }

                    snapshot.Values.Add(valueSnapshot);
                    totalValues++;
                }
                catch { /* Skip unreadable values */ }
            }

            // Get subkeys
            foreach (var subKeyName in key.GetSubKeyNames())
            {
                snapshot.SubKeys.Add(subKeyName);

                try
                {
                    using var subKey = key.OpenSubKey(subKeyName);
                    if (subKey != null)
                    {
                        var subSnapshot = ScanKey(hiveName, $"{keyPath}\\{subKeyName}", subKey, depth + 1, ref totalKeys, ref totalValues, token);
                        // We don't store nested snapshots to save memory, just the paths
                    }
                }
                catch { /* Skip inaccessible subkeys */ }
            }

            return snapshot;
        }

        private string FormatValue(object? value, RegistryValueKind kind)
        {
            return kind switch
            {
                RegistryValueKind.Binary => value is byte[] bytes ? BitConverter.ToString(bytes).Replace("-", "").Substring(0, Math.Min(100, bytes.Length * 2)) : "[null]",
                RegistryValueKind.MultiString => value is string[] arr ? string.Join("; ", arr) : "[null]",
                RegistryValueKind.DWord or RegistryValueKind.QWord => value?.ToString() ?? "[null]",
                _ => value?.ToString() ?? "[null]"
            };
        }

        private string? ComputeHash(byte[]? data)
        {
            if (data == null) return null;
            using var md5 = System.Security.Cryptography.MD5.Create();
            var hash = md5.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        #endregion

        #region Comparison

        /// <summary>
        /// Compares two snapshots and returns the differences.
        /// </summary>
        public RegistryDiffResult Compare(RegistrySnapshot baseline, RegistrySnapshot current)
        {
            LogHeader("Comparing Registry Snapshots");
            Log($"Baseline: {baseline.Name} ({baseline.CreatedAt})");
            Log($"Current: {current.Name} ({current.CreatedAt})");

            var result = new RegistryDiffResult
            {
                BaselineId = baseline.Id,
                CompareId = current.Id,
                BaselineTime = baseline.CreatedAt,
                CompareTime = current.CreatedAt
            };

            var baselineKeys = baseline.Keys.ToDictionary(k => k.Path, k => k, StringComparer.OrdinalIgnoreCase);
            var currentKeys = current.Keys.ToDictionary(k => k.Path, k => k, StringComparer.OrdinalIgnoreCase);

            // Find added keys
            foreach (var key in currentKeys.Keys.Except(baselineKeys.Keys, StringComparer.OrdinalIgnoreCase))
            {
                result.Changes.Add(new RegistryChange
                {
                    ChangeType = RegistryChangeType.KeyAdded,
                    KeyPath = key,
                    Category = CategorizeKey(key),
                    Severity = GetKeySeverity(key)
                });

                // Add all values as new
                if (currentKeys.TryGetValue(key, out var addedKey))
                {
                    foreach (var value in addedKey.Values)
                    {
                        result.Changes.Add(new RegistryChange
                        {
                            ChangeType = RegistryChangeType.ValueAdded,
                            KeyPath = key,
                            ValueName = value.Name,
                            NewValue = value.Value,
                            ValueKind = value.Kind,
                            Category = CategorizeKey(key),
                            Severity = GetKeySeverity(key)
                        });
                    }
                }
            }

            // Find removed keys
            foreach (var key in baselineKeys.Keys.Except(currentKeys.Keys, StringComparer.OrdinalIgnoreCase))
            {
                result.Changes.Add(new RegistryChange
                {
                    ChangeType = RegistryChangeType.KeyRemoved,
                    KeyPath = key,
                    Category = CategorizeKey(key),
                    Severity = GetKeySeverity(key)
                });
            }

            // Find modified keys
            foreach (var key in baselineKeys.Keys.Intersect(currentKeys.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var baseKey = baselineKeys[key];
                var currKey = currentKeys[key];

                CompareKeyValues(baseKey, currKey, result);
            }

            LogSuccess($"Comparison complete: {result.TotalChanges} changes found");
            Log($"  Keys added: {result.KeysAdded}");
            Log($"  Keys removed: {result.KeysRemoved}");
            Log($"  Values added: {result.ValuesAdded}");
            Log($"  Values removed: {result.ValuesRemoved}");
            Log($"  Values modified: {result.ValuesModified}");

            return result;
        }

        private void CompareKeyValues(RegistryKeySnapshot baseline, RegistryKeySnapshot current, RegistryDiffResult result)
        {
            var baseValues = baseline.Values.ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);
            var currValues = current.Values.ToDictionary(v => v.Name, v => v, StringComparer.OrdinalIgnoreCase);

            // Added values
            foreach (var name in currValues.Keys.Except(baseValues.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var value = currValues[name];
                result.Changes.Add(new RegistryChange
                {
                    ChangeType = RegistryChangeType.ValueAdded,
                    KeyPath = current.Path,
                    ValueName = name,
                    NewValue = value.Value,
                    ValueKind = value.Kind,
                    Category = CategorizeKey(current.Path),
                    Severity = GetKeySeverity(current.Path)
                });
            }

            // Removed values
            foreach (var name in baseValues.Keys.Except(currValues.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var value = baseValues[name];
                result.Changes.Add(new RegistryChange
                {
                    ChangeType = RegistryChangeType.ValueRemoved,
                    KeyPath = baseline.Path,
                    ValueName = name,
                    OldValue = value.Value,
                    ValueKind = value.Kind,
                    Category = CategorizeKey(baseline.Path),
                    Severity = GetKeySeverity(baseline.Path)
                });
            }

            // Modified values
            foreach (var name in baseValues.Keys.Intersect(currValues.Keys, StringComparer.OrdinalIgnoreCase))
            {
                var baseVal = baseValues[name];
                var currVal = currValues[name];

                if (baseVal.Value != currVal.Value || baseVal.Kind != currVal.Kind)
                {
                    result.Changes.Add(new RegistryChange
                    {
                        ChangeType = RegistryChangeType.ValueModified,
                        KeyPath = baseline.Path,
                        ValueName = name,
                        OldValue = baseVal.Value,
                        NewValue = currVal.Value,
                        ValueKind = currVal.Kind,
                        Category = CategorizeKey(baseline.Path),
                        Severity = GetKeySeverity(baseline.Path)
                    });
                }
            }
        }

        private string CategorizeKey(string keyPath)
        {
            var upper = keyPath.ToUpperInvariant();

            if (upper.Contains("RUN") || upper.Contains("STARTUP") || upper.Contains("WINLOGON") || upper.Contains("SHELL"))
                return "Persistence";
            if (upper.Contains("SERVICES"))
                return "Services";
            if (upper.Contains("SECURITY") || upper.Contains("LSA") || upper.Contains("POLICY"))
                return "Security";
            if (upper.Contains("EXPLORER"))
                return "Explorer";
            if (upper.Contains("BROWSER HELPER") || upper.Contains("SHELLEXECUTEHOOKS"))
                return "Browser/Shell Extensions";
            if (upper.Contains("IMAGE FILE EXECUTION"))
                return "Debugging/IFEO";

            return "Other";
        }

        private string GetKeySeverity(string keyPath)
        {
            var upper = keyPath.ToUpperInvariant();

            if (upper.Contains("RUN") || upper.Contains("WINLOGON") || upper.Contains("SERVICES") || upper.Contains("IMAGE FILE EXECUTION"))
                return "critical";
            if (upper.Contains("SECURITY") || upper.Contains("LSA") || upper.Contains("POLICY") || upper.Contains("BROWSER HELPER"))
                return "high";
            if (upper.Contains("SHELL") || upper.Contains("EXPLORER"))
                return "medium";

            return "info";
        }

        #endregion

        #region Persistence

        /// <summary>
        /// Saves a snapshot to disk.
        /// </summary>
        public async Task SaveSnapshotAsync(RegistrySnapshot snapshot, CancellationToken token = default)
        {
            var filePath = Path.Combine(SnapshotsPath, $"{snapshot.Id}.json");
            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json, token);
            Log($"Snapshot saved: {filePath}");
        }

        /// <summary>
        /// Loads a snapshot from disk.
        /// </summary>
        public async Task<RegistrySnapshot?> LoadSnapshotAsync(string id, CancellationToken token = default)
        {
            var filePath = Path.Combine(SnapshotsPath, $"{id}.json");
            if (!File.Exists(filePath)) return null;

            var json = await File.ReadAllTextAsync(filePath, token);
            return JsonSerializer.Deserialize<RegistrySnapshot>(json);
        }

        /// <summary>
        /// Gets all saved snapshots.
        /// </summary>
        public IEnumerable<(string Id, string Name, DateTime Created)> GetSavedSnapshots()
        {
            var results = new List<(string Id, string Name, DateTime Created)>();
            
            foreach (var file in Directory.GetFiles(SnapshotsPath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    results.Add((
                        root.GetProperty("Id").GetString() ?? "",
                        root.GetProperty("Name").GetString() ?? "",
                        root.GetProperty("CreatedAt").GetDateTime()
                    ));
                }
                catch { /* Skip invalid files */ }
            }
            
            return results;
        }

        /// <summary>
        /// Deletes a saved snapshot.
        /// </summary>
        public void DeleteSnapshot(string id)
        {
            var filePath = Path.Combine(SnapshotsPath, $"{id}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log($"Snapshot deleted: {id}");
            }
        }

        #endregion

        #region Export

        /// <summary>
        /// Exports diff results to HTML report.
        /// </summary>
        public string ExportToHtml(RegistryDiffResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><title>Registry Diff Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', sans-serif; margin: 20px; }");
            sb.AppendLine("h1 { color: #333; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 20px; }");
            sb.AppendLine("th, td { border: 1px solid #ddd; padding: 8px; text-align: left; }");
            sb.AppendLine("th { background: #4a90d9; color: white; }");
            sb.AppendLine("tr:nth-child(even) { background: #f2f2f2; }");
            sb.AppendLine(".added { color: green; }");
            sb.AppendLine(".removed { color: red; }");
            sb.AppendLine(".modified { color: orange; }");
            sb.AppendLine(".critical { background: #ffcccc; }");
            sb.AppendLine(".high { background: #ffd9b3; }");
            sb.AppendLine(".summary { background: #e7f3ff; padding: 15px; border-radius: 5px; }");
            sb.AppendLine("</style></head><body>");

            sb.AppendLine($"<h1>Registry Diff Report</h1>");
            sb.AppendLine($"<div class='summary'>");
            sb.AppendLine($"<p><strong>Baseline:</strong> {result.BaselineTime}</p>");
            sb.AppendLine($"<p><strong>Current:</strong> {result.CompareTime}</p>");
            sb.AppendLine($"<p><strong>Total Changes:</strong> {result.TotalChanges}</p>");
            sb.AppendLine($"<p>Keys Added: {result.KeysAdded} | Keys Removed: {result.KeysRemoved} | Values Added: {result.ValuesAdded} | Values Removed: {result.ValuesRemoved} | Values Modified: {result.ValuesModified}</p>");
            sb.AppendLine("</div>");

            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Type</th><th>Key Path</th><th>Value Name</th><th>Old Value</th><th>New Value</th><th>Category</th><th>Severity</th></tr>");

            foreach (var change in result.Changes.OrderByDescending(c => c.Severity == "critical").ThenByDescending(c => c.Severity == "high"))
            {
                var cssClass = change.ChangeType switch
                {
                    RegistryChangeType.KeyAdded or RegistryChangeType.ValueAdded => "added",
                    RegistryChangeType.KeyRemoved or RegistryChangeType.ValueRemoved => "removed",
                    _ => "modified"
                };

                var rowClass = change.Severity == "critical" ? "critical" : change.Severity == "high" ? "high" : "";

                sb.AppendLine($"<tr class='{rowClass}'>");
                sb.AppendLine($"<td class='{cssClass}'>{change.ChangeType}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(change.KeyPath)}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(change.ValueName ?? "")}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(change.OldValue ?? "")}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(change.NewValue ?? "")}</td>");
                sb.AppendLine($"<td>{change.Category}</td>");
                sb.AppendLine($"<td>{change.Severity}</td>");
                sb.AppendLine("</tr>");
            }

            sb.AppendLine("</table></body></html>");
            return sb.ToString();
        }

        /// <summary>
        /// Exports diff results to CSV.
        /// </summary>
        public string ExportToCsv(RegistryDiffResult result)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ChangeType,KeyPath,ValueName,OldValue,NewValue,ValueKind,Category,Severity");

            foreach (var change in result.Changes)
            {
                sb.AppendLine($"\"{change.ChangeType}\",\"{change.KeyPath}\",\"{change.ValueName ?? ""}\",\"{change.OldValue ?? ""}\",\"{change.NewValue ?? ""}\",\"{change.ValueKind}\",\"{change.Category}\",\"{change.Severity}\"");
            }

            return sb.ToString();
        }

        #endregion
    }
}
