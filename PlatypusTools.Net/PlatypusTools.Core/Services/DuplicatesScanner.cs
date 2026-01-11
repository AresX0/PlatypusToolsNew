using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PlatypusTools.Core.Services
{
    public class DuplicateGroup
    {
        public string Hash { get; set; } = string.Empty;
        public List<string> Files { get; set; } = new List<string>();
    }

    public static class DuplicatesScanner
    {
        public static IEnumerable<DuplicateGroup> FindDuplicates(IEnumerable<string> paths, bool recurse = true)
        {
            var walkOption = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            var fileList = new List<string>();
            foreach (var p in paths)
            {
                if (Directory.Exists(p)) fileList.AddRange(Directory.EnumerateFiles(p, "*", walkOption));
                else if (File.Exists(p)) fileList.Add(p);
            }

            var dict = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in fileList)
            {
                try
                {
                    using var sha = SHA256.Create();
                    using var s = File.OpenRead(f);
                    var hash = BitConverter.ToString(sha.ComputeHash(s)).Replace("-", string.Empty);
                    if (!dict.TryGetValue(hash, out var list)) { list = new List<string>(); dict[hash] = list; }
                    list.Add(f);
                }
                catch { }
            }

            return dict.Where(kv => kv.Value.Count > 1).Select(kv => new DuplicateGroup { Hash = kv.Key, Files = kv.Value });
        }
    }
}