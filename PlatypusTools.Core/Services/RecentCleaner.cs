using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace PlatypusTools.Core.Services
{
    public class RecentMatch
    {
        public string Type { get; set; } = "Shortcut";
        public string Path { get; set; } = string.Empty;
        public string? Target { get; set; }
        public IList<string>? MatchedPaths { get; set; }
    }

    public static class RecentCleaner
    {
        public static IList<RecentMatch> RemoveRecentShortcuts(IEnumerable<string> targetDirs, bool dryRun = true, bool includeSubDirs = true, string? backupPath = null, string? recentFolder = null)
        {
            var results = new List<RecentMatch>();
            if (targetDirs == null) { return results; }
            
            // Get Recent folder path
            var recent = recentFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            if (string.IsNullOrEmpty(recent) || !Directory.Exists(recent)) 
            {
                // Try alternative path
                recent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
                    "Microsoft", "Windows", "Recent");
            }
            
            if (!Directory.Exists(recent)) { return results; }

            // Normalize target directories
            var normalizedTargets = targetDirs
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => {
                    try { return Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                    catch { return d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                })
                .Where(d => !string.IsNullOrEmpty(d))
                .ToList();

            if (!normalizedTargets.Any()) { return results; }

            IEnumerable<string> lnkFiles;
            try
            {
                lnkFiles = Directory.EnumerateFiles(recent, "*.lnk", SearchOption.AllDirectories);
            }
            catch
            {
                // Fallback to top directory only
                try
                {
                    lnkFiles = Directory.EnumerateFiles(recent, "*.lnk", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    return results;
                }
            }

            foreach (var lnk in lnkFiles)
            {
                try
                {
                    var target = ResolveShortcutTarget(lnk);
                    if (string.IsNullOrEmpty(target)) { continue; }

                    string compTarget;
                    try { compTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
                    catch { compTarget = target; }

                    foreach (var compDir in normalizedTargets)
                    {
                        bool isMatch;
                        if (includeSubDirs)
                        {
                            isMatch = compTarget.StartsWith(compDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                                      compTarget.Equals(compDir, StringComparison.OrdinalIgnoreCase);
                        }
                        else
                        {
                            var parent = Path.GetDirectoryName(compTarget) ?? string.Empty;
                            isMatch = string.Equals(parent, compDir, StringComparison.OrdinalIgnoreCase);
                        }

                        if (isMatch)
                        {
                            results.Add(new RecentMatch { Type = "Shortcut", Path = lnk, Target = target });
                            if (!dryRun)
                            {
                                if (!string.IsNullOrEmpty(backupPath))
                                {
                                    try { Directory.CreateDirectory(backupPath); File.Copy(lnk, Path.Combine(backupPath, Path.GetFileName(lnk)), true); } catch { }
                                }
                                try { File.Delete(lnk); } catch { }
                            }
                            break;
                        }
                    }
                }
                catch { /* keep fault-tolerant */ }
            }
            
            return results;
        }

        private static string? ResolveShortcutTarget(string shortcutPath)
        {
            // Method 1: Try WSH Shell COM
            try
            {
                var wsh = Type.GetTypeFromProgID("WScript.Shell");
                if (wsh != null)
                {
                    var shellObj = Activator.CreateInstance(wsh);
                    if (shellObj != null)
                    {
                        dynamic shell = shellObj;
                        dynamic lnk = shell.CreateShortcut(shortcutPath);
                        string? target = (string?)lnk?.TargetPath;
                        if (!string.IsNullOrEmpty(target))
                            return target;
                    }
                }
            }
            catch { }

            // Method 2: Try to read .lnk file directly (basic parsing)
            try
            {
                return ReadLnkTargetDirect(shortcutPath);
            }
            catch { }

            return null;
        }

        private static string? ReadLnkTargetDirect(string lnkPath)
        {
            try
            {
                var bytes = File.ReadAllBytes(lnkPath);
                if (bytes.Length < 0x4C) return null;
                
                // Check magic number (0x4C bytes at start)
                if (bytes[0] != 0x4C || bytes[1] != 0x00 || bytes[2] != 0x00 || bytes[3] != 0x00)
                    return null;

                // Flags at offset 0x14
                var flags = BitConverter.ToUInt32(bytes, 0x14);
                bool hasLinkTargetIdList = (flags & 0x01) != 0;
                bool hasLinkInfo = (flags & 0x02) != 0;
                
                int offset = 0x4C; // Start after header

                // Skip LinkTargetIDList if present
                if (hasLinkTargetIdList)
                {
                    if (offset + 2 > bytes.Length) return null;
                    var idListSize = BitConverter.ToUInt16(bytes, offset);
                    offset += 2 + idListSize;
                }

                // Read LinkInfo if present
                if (hasLinkInfo && offset + 4 <= bytes.Length)
                {
                    var linkInfoSize = BitConverter.ToInt32(bytes, offset);
                    if (linkInfoSize > 0 && offset + linkInfoSize <= bytes.Length)
                    {
                        var linkInfoHeaderSize = BitConverter.ToInt32(bytes, offset + 4);
                        var linkInfoFlags = BitConverter.ToUInt32(bytes, offset + 8);
                        
                        bool hasVolumeIdAndLocalBasePath = (linkInfoFlags & 0x01) != 0;
                        
                        if (hasVolumeIdAndLocalBasePath && linkInfoHeaderSize >= 0x1C)
                        {
                            var localBasePathOffset = BitConverter.ToInt32(bytes, offset + 0x10);
                            if (localBasePathOffset > 0 && offset + localBasePathOffset < bytes.Length)
                            {
                                var pathStart = offset + localBasePathOffset;
                                var pathEnd = Array.IndexOf(bytes, (byte)0, pathStart);
                                if (pathEnd > pathStart)
                                {
                                    return Encoding.Default.GetString(bytes, pathStart, pathEnd - pathStart);
                                }
                            }
                        }
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Scans all Windows recent item locations and returns a comprehensive list.
        /// Includes Recent folder, Jump Lists, Quick Access, Favorites, and Start Menu recent items.
        /// </summary>
        public static IList<RecentMatch> ScanAllRecentItems()
        {
            var results = new List<RecentMatch>();
            
            // 1. Windows Recent folder (.lnk shortcuts)
            try
            {
                var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                if (string.IsNullOrEmpty(recentPath))
                {
                    recentPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Recent");
                }
                
                if (Directory.Exists(recentPath))
                {
                    foreach (var file in Directory.GetFiles(recentPath, "*.*", SearchOption.TopDirectoryOnly))
                    {
                        string target = "";
                        if (file.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                        {
                            target = ResolveShortcutTarget(file) ?? Path.GetFileNameWithoutExtension(file);
                        }
                        else
                        {
                            target = file;
                        }
                        
                        results.Add(new RecentMatch
                        {
                            Type = "Recent",
                            Path = file,
                            Target = target
                        });
                    }
                }
            }
            catch { /* Continue on error */ }
            
            // 2. Jump Lists - AutomaticDestinations (most recent documents per app)
            try
            {
                var autoDestPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Recent", "AutomaticDestinations");
                
                if (Directory.Exists(autoDestPath))
                {
                    foreach (var file in Directory.GetFiles(autoDestPath, "*.automaticDestinations-ms"))
                    {
                        var appId = Path.GetFileNameWithoutExtension(file).Split('.')[0];
                        results.Add(new RecentMatch
                        {
                            Type = "JumpList-Auto",
                            Path = file,
                            Target = $"App ID: {appId}"
                        });
                    }
                }
            }
            catch { /* Continue on error */ }
            
            // 3. Jump Lists - CustomDestinations (pinned items per app)
            try
            {
                var customDestPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Windows", "Recent", "CustomDestinations");
                
                if (Directory.Exists(customDestPath))
                {
                    foreach (var file in Directory.GetFiles(customDestPath, "*.customDestinations-ms"))
                    {
                        var appId = Path.GetFileNameWithoutExtension(file).Split('.')[0];
                        results.Add(new RecentMatch
                        {
                            Type = "JumpList-Custom",
                            Path = file,
                            Target = $"App ID: {appId} (pinned)"
                        });
                    }
                }
            }
            catch { /* Continue on error */ }
            
            // 4. Favorites folder
            try
            {
                var favoritesPath = Environment.GetFolderPath(Environment.SpecialFolder.Favorites);
                if (Directory.Exists(favoritesPath))
                {
                    foreach (var file in Directory.GetFiles(favoritesPath, "*.*", SearchOption.AllDirectories))
                    {
                        results.Add(new RecentMatch
                        {
                            Type = "Favorite",
                            Path = file,
                            Target = Path.GetFileNameWithoutExtension(file)
                        });
                    }
                }
            }
            catch { /* Continue on error */ }
            
            // 5. Start Menu recent items (if any)
            try
            {
                var startMenuPath = Environment.GetFolderPath(Environment.SpecialFolder.StartMenu);
                var recentInStartMenu = Path.Combine(startMenuPath, "Recent");
                if (Directory.Exists(recentInStartMenu))
                {
                    foreach (var file in Directory.GetFiles(recentInStartMenu, "*.lnk", SearchOption.AllDirectories))
                    {
                        var target = ResolveShortcutTarget(file) ?? Path.GetFileNameWithoutExtension(file);
                        results.Add(new RecentMatch
                        {
                            Type = "StartMenu-Recent",
                            Path = file,
                            Target = target
                        });
                    }
                }
            }
            catch { /* Continue on error */ }
            
            // 6. Office MRU locations (common paths)
            try
            {
                var officeRecentPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Microsoft", "Office", "Recent");
                
                if (Directory.Exists(officeRecentPath))
                {
                    foreach (var file in Directory.GetFiles(officeRecentPath, "*.lnk", SearchOption.AllDirectories))
                    {
                        var target = ResolveShortcutTarget(file) ?? Path.GetFileNameWithoutExtension(file);
                        results.Add(new RecentMatch
                        {
                            Type = "Office-Recent",
                            Path = file,
                            Target = target
                        });
                    }
                }
            }
            catch { /* Continue on error */ }
            
            return results;
        }
    }
}