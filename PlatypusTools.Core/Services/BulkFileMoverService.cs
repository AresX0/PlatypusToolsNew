using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for automatically organizing files based on user-defined rules.
    /// Moves/copies files to destination folders based on extension, name pattern, size, or date.
    /// </summary>
    public class BulkFileMoverService
    {
        public enum RuleAction { Move, Copy }
        public enum RuleCondition { Extension, NameContains, NameRegex, SizeLargerThan, SizeSmallerThan, OlderThan, NewerThan }

        public class MoverRule
        {
            public string Name { get; set; } = "";
            public bool IsEnabled { get; set; } = true;
            public RuleCondition Condition { get; set; }
            public string Value { get; set; } = "";
            public string DestinationFolder { get; set; } = "";
            public RuleAction Action { get; set; } = RuleAction.Move;
            public bool CreateSubfolders { get; set; }
        }

        public class MoverProfile
        {
            public string Name { get; set; } = "Default";
            public string SourceFolder { get; set; } = "";
            public bool IncludeSubfolders { get; set; }
            public List<MoverRule> Rules { get; set; } = new();
        }

        public class MoveResult
        {
            public string SourcePath { get; set; } = "";
            public string DestinationPath { get; set; } = "";
            public string RuleName { get; set; } = "";
            public RuleAction Action { get; set; }
            public bool Success { get; set; }
            public string? Error { get; set; }
        }

        public event EventHandler<string>? LogMessage;

        private static readonly string ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "MoverProfiles");

        /// <summary>
        /// Execute all enabled rules in a profile against the source folder.
        /// </summary>
        public async Task<List<MoveResult>> ExecuteProfileAsync(MoverProfile profile, IProgress<int>? progress = null, CancellationToken ct = default)
        {
            var results = new List<MoveResult>();
            if (string.IsNullOrEmpty(profile.SourceFolder) || !Directory.Exists(profile.SourceFolder))
                return results;

            var searchOption = profile.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(profile.SourceFolder, "*", searchOption);
            var enabledRules = profile.Rules.Where(r => r.IsEnabled).ToList();

            for (int i = 0; i < files.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                var file = files[i];

                foreach (var rule in enabledRules)
                {
                    if (MatchesRule(file, rule))
                    {
                        var result = await ApplyRuleAsync(file, rule);
                        results.Add(result);
                        LogMessage?.Invoke(this, $"[{(result.Success ? "OK" : "FAIL")}] {result.Action}: {Path.GetFileName(result.SourcePath)} -> {result.DestinationPath}");
                        break; // First matching rule wins
                    }
                }

                progress?.Report((int)((i + 1.0) / files.Length * 100));
            }

            return results;
        }

        /// <summary>
        /// Preview what would happen without actually moving/copying files.
        /// </summary>
        public List<MoveResult> PreviewProfile(MoverProfile profile)
        {
            var results = new List<MoveResult>();
            if (string.IsNullOrEmpty(profile.SourceFolder) || !Directory.Exists(profile.SourceFolder))
                return results;

            var searchOption = profile.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var files = Directory.GetFiles(profile.SourceFolder, "*", searchOption);
            var enabledRules = profile.Rules.Where(r => r.IsEnabled).ToList();

            foreach (var file in files)
            {
                foreach (var rule in enabledRules)
                {
                    if (MatchesRule(file, rule))
                    {
                        var destPath = GetDestinationPath(file, rule);
                        results.Add(new MoveResult
                        {
                            SourcePath = file,
                            DestinationPath = destPath,
                            RuleName = rule.Name,
                            Action = rule.Action,
                            Success = true
                        });
                        break;
                    }
                }
            }

            return results;
        }

        public bool MatchesRule(string filePath, MoverRule rule)
        {
            try
            {
                var info = new FileInfo(filePath);
                return rule.Condition switch
                {
                    RuleCondition.Extension => rule.Value.Split(';', ',')
                        .Any(ext => info.Extension.Equals(ext.Trim().StartsWith('.') ? ext.Trim() : "." + ext.Trim(), StringComparison.OrdinalIgnoreCase)),
                    RuleCondition.NameContains => info.Name.Contains(rule.Value, StringComparison.OrdinalIgnoreCase),
                    RuleCondition.NameRegex => Regex.IsMatch(info.Name, rule.Value, RegexOptions.IgnoreCase),
                    RuleCondition.SizeLargerThan => info.Length > ParseSize(rule.Value),
                    RuleCondition.SizeSmallerThan => info.Length < ParseSize(rule.Value),
                    RuleCondition.OlderThan => info.LastWriteTime < DateTime.Now.AddDays(-int.Parse(rule.Value)),
                    RuleCondition.NewerThan => info.LastWriteTime > DateTime.Now.AddDays(-int.Parse(rule.Value)),
                    _ => false
                };
            }
            catch { return false; }
        }

        private string GetDestinationPath(string filePath, MoverRule rule)
        {
            var destDir = rule.DestinationFolder;
            if (rule.CreateSubfolders)
            {
                var ext = Path.GetExtension(filePath).TrimStart('.').ToUpperInvariant();
                if (string.IsNullOrEmpty(ext)) ext = "NoExtension";
                destDir = Path.Combine(destDir, ext);
            }
            return Path.Combine(destDir, Path.GetFileName(filePath));
        }

        private async Task<MoveResult> ApplyRuleAsync(string filePath, MoverRule rule)
        {
            var result = new MoveResult
            {
                SourcePath = filePath,
                RuleName = rule.Name,
                Action = rule.Action
            };

            try
            {
                var destPath = GetDestinationPath(filePath, rule);
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                // Handle name collision
                destPath = GetUniqueFileName(destPath);
                result.DestinationPath = destPath;

                if (rule.Action == RuleAction.Move)
                    File.Move(filePath, destPath);
                else
                    await Task.Run(() => File.Copy(filePath, destPath));

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                result.Success = false;
            }

            return result;
        }

        private static string GetUniqueFileName(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path) ?? "";
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            int counter = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(dir, $"{name} ({counter}){ext}");
                counter++;
            }
            return path;
        }

        private static long ParseSize(string value)
        {
            value = value.Trim().ToUpperInvariant();
            if (value.EndsWith("GB")) return (long)(double.Parse(value[..^2]) * 1024 * 1024 * 1024);
            if (value.EndsWith("MB")) return (long)(double.Parse(value[..^2]) * 1024 * 1024);
            if (value.EndsWith("KB")) return (long)(double.Parse(value[..^2]) * 1024);
            return long.Parse(value);
        }

        public async Task SaveProfileAsync(MoverProfile profile)
        {
            Directory.CreateDirectory(ProfilesDir);
            var path = Path.Combine(ProfilesDir, $"{profile.Name}.json");
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(path, json);
        }

        public async Task<MoverProfile?> LoadProfileAsync(string name)
        {
            var path = Path.Combine(ProfilesDir, $"{name}.json");
            if (!File.Exists(path)) return null;
            var json = await File.ReadAllTextAsync(path);
            return JsonSerializer.Deserialize<MoverProfile>(json);
        }

        public List<string> GetSavedProfiles()
        {
            if (!Directory.Exists(ProfilesDir)) return new();
            return Directory.GetFiles(ProfilesDir, "*.json")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .ToList();
        }

        public void DeleteProfile(string name)
        {
            var path = Path.Combine(ProfilesDir, $"{name}.json");
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
