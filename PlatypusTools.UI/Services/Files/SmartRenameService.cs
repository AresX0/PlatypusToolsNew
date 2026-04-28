using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.UI.Services.AI;

namespace PlatypusTools.UI.Services.Files
{
    public class SmartRenameSuggestion
    {
        public string OriginalPath { get; set; } = "";
        public string SuggestedName { get; set; } = "";
        public bool Apply { get; set; } = true;
    }

    // Uses LocalLlmService to suggest filenames based on path/extension/size hints.
    public class SmartRenameService
    {
        public async Task<List<SmartRenameSuggestion>> SuggestAsync(IEnumerable<string> files, string? hint = null, CancellationToken ct = default)
        {
            var list = new List<SmartRenameSuggestion>();
            var llm = LocalLlmService.Instance;
            foreach (var f in files)
            {
                if (!File.Exists(f)) continue;
                var fi = new FileInfo(f);
                string sys = "You rename files to short, descriptive, snake_case names. Reply with ONLY the new base filename (no extension, no path, no quotes).";
                string user = $"Original filename: {fi.Name}\nFolder: {fi.DirectoryName}\nSize: {fi.Length} bytes\nExtension: {fi.Extension}";
                if (!string.IsNullOrWhiteSpace(hint)) user += $"\nUser hint: {hint}";
                try
                {
                    var msgs = new List<LocalLlmService.ChatMessage>
                    {
                        new("system", sys),
                        new("user", user)
                    };
                    var reply = await llm.ChatAsync(msgs, ct: ct);
                    var name = Sanitize(reply);
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        list.Add(new SmartRenameSuggestion
                        {
                            OriginalPath = f,
                            SuggestedName = name + fi.Extension
                        });
                    }
                }
                catch
                {
                    // Skip on LLM error; user sees fewer suggestions
                }
            }
            return list;
        }

        public int Apply(IEnumerable<SmartRenameSuggestion> suggestions)
        {
            int n = 0;
            foreach (var s in suggestions)
            {
                if (!s.Apply) continue;
                if (!File.Exists(s.OriginalPath)) continue;
                var dir = Path.GetDirectoryName(s.OriginalPath)!;
                var dest = Path.Combine(dir, s.SuggestedName);
                if (string.Equals(dest, s.OriginalPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (File.Exists(dest)) dest = Path.Combine(dir, Path.GetFileNameWithoutExtension(s.SuggestedName) + "_" + Guid.NewGuid().ToString("N").Substring(0,6) + Path.GetExtension(s.SuggestedName));
                try { File.Move(s.OriginalPath, dest); n++; } catch { }
            }
            return n;
        }

        private static string Sanitize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var line = raw.Trim().Split('\n')[0].Trim().Trim('"', '\'', '`');
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder();
            foreach (var c in line)
            {
                if (Array.IndexOf(invalid, c) < 0 && c != ' ') sb.Append(c);
                else if (c == ' ') sb.Append('_');
            }
            var s = sb.ToString();
            if (s.Length > 80) s = s.Substring(0, 80);
            return s;
        }
    }
}
