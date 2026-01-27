using System;
using System.Collections.Generic;
using System.IO;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Static utility class for file cleaning operations.
    /// Provides methods for filtering, enumerating, and removing files.
    /// </summary>
    public static class FileCleaner
    {

        public static IEnumerable<FileInfo> GetFilteredFiles(string path, bool recurse, IEnumerable<string>? extensions = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) yield break;
            var search = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            IEnumerable<FileInfo> entries;
            try
            {
                entries = new DirectoryInfo(path).EnumerateFiles("*", search);
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"Failed to enumerate files in '{path}': {ex.Message}");
                yield break;
            }

            foreach (var fi in entries)
            {
                if (extensions == null)
                {
                    yield return fi;
                }
                else
                {
                    var ext = fi.Extension.ToLowerInvariant();
                    foreach (var e in extensions)
                    {
                        if (e.Equals(ext, StringComparison.OrdinalIgnoreCase)) { yield return fi; break; }
                    }
                }
            }
        }

        public static IEnumerable<string> GetFiles(string path, IEnumerable<string>? includePatterns = null, bool recurse = true)
        {
            var opts = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            if (!Directory.Exists(path)) return Array.Empty<string>();
            if (includePatterns == null) return Directory.EnumerateFiles(path, "*", opts);

            var files = new List<string>();
            foreach (var pat in includePatterns)
            {
                try
                {
                    files.AddRange(Directory.EnumerateFiles(path, pat, opts));
                }
                catch (Exception ex)
                {
                    SimpleLogger.Warn($"Pattern '{pat}' failed when enumerating files in '{path}': {ex.Message}");
                }
            }
            return files.Distinct(StringComparer.OrdinalIgnoreCase);
        }

        public static IList<string> RemoveFiles(IEnumerable<string> files, bool dryRun = true, string? backupPath = null, string? basePath = null)
        {
            var removed = new List<string>();
            // Normalize basePath
            string? normalizedBase = null;
            if (!string.IsNullOrWhiteSpace(basePath))
            {
                try { normalizedBase = Path.GetFullPath(basePath); }
                catch { normalizedBase = null; }
            }

            foreach (var f in files)
            {
                try
                {
                    if (!File.Exists(f)) continue;

                    if (dryRun)
                    {
                        SimpleLogger.Info($"[DryRun] Would remove '{f}'");
                        removed.Add(f);
                        continue;
                    }

                    // If backup is requested, attempt it first. If backup fails, skip deletion to be safe.
                    if (!string.IsNullOrEmpty(backupPath))
                    {
                        try
                        {
                            var dest = Path.Combine(backupPath, Path.GetFileName(f));

                            // If basePath provided and file is under it, preserve relative structure
                            if (!string.IsNullOrEmpty(normalizedBase))
                            {
                                try
                                {
                                    var full = Path.GetFullPath(f);
                                    if (full.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                                    {
                                        var rel = full.Substring(normalizedBase.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                        dest = Path.Combine(backupPath, rel);
                                    }
                                }
                                catch { /* fallback to flat backup */ }
                            }

                            var destDir = Path.GetDirectoryName(dest) ?? backupPath;
                            Directory.CreateDirectory(destDir);
                            File.Copy(f, dest, true);
                            SimpleLogger.Info($"Backed up '{f}' to '{dest}'");
                        }
                        catch (Exception ex)
                        {
                            SimpleLogger.Error($"Failed to backup '{f}' to '{backupPath}': {ex.Message}");
                            // Skip deletion if backup failed
                            continue;
                        }
                    }

                    try
                    {
                        File.Delete(f);
                        SimpleLogger.Info($"Deleted '{f}'");
                        removed.Add(f);
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Error($"Failed to delete '{f}': {ex.Message}");
                    }
                }
                catch (Exception ex)
                {
                    SimpleLogger.Error($"Error processing file '{f}': {ex.Message}");
                }
            }
            return removed;
        }

        public static IDictionary<string,string> ComputeBackupMapping(IEnumerable<string> files, string backupPath, string? basePath = null)
        {
            var mapping = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(backupPath)) return mapping;

            string? normalizedBase = null;
            if (!string.IsNullOrWhiteSpace(basePath))
            {
                try { normalizedBase = Path.GetFullPath(basePath); }
                catch { normalizedBase = null; }
            }

            foreach (var f in files)
            {
                try
                {
                    if (!File.Exists(f)) continue;
                    var dest = Path.Combine(backupPath, Path.GetFileName(f));
                    if (!string.IsNullOrEmpty(normalizedBase))
                    {
                        try
                        {
                            var full = Path.GetFullPath(f);
                            if (full.StartsWith(normalizedBase, StringComparison.OrdinalIgnoreCase))
                            {
                                var rel = full.Substring(normalizedBase.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                                dest = Path.Combine(backupPath, rel);
                            }
                        }
                        catch { }
                    }
                    mapping[f] = dest;
                }
                catch { }
            }

            return mapping;
        }
    }
}