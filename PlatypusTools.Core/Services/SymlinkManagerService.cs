using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Service for creating, viewing, and managing symbolic links, hard links, and directory junctions.
    /// </summary>
    public class SymlinkManagerService
    {
        public enum LinkType { SymbolicLink, HardLink, Junction, Unknown }

        public class LinkInfo
        {
            public string Path { get; set; } = "";
            public string Target { get; set; } = "";
            public LinkType Type { get; set; }
            public bool IsValid { get; set; }
            public bool IsDirectory { get; set; }
            public DateTime Created { get; set; }
            public long Size { get; set; }
        }

        /// <summary>
        /// Create a symbolic link.
        /// </summary>
        public async Task<bool> CreateSymbolicLinkAsync(string linkPath, string targetPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    bool isDir = Directory.Exists(targetPath);
                    // Use mklink via cmd (requires admin for symlinks, or developer mode enabled)
                    var args = isDir ? $"/c mklink /D \"{linkPath}\" \"{targetPath}\"" : $"/c mklink \"{linkPath}\" \"{targetPath}\"";
                    var psi = new ProcessStartInfo("cmd.exe", args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(10000);
                    return p?.ExitCode == 0;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Create a hard link (files only).
        /// </summary>
        public async Task<bool> CreateHardLinkAsync(string linkPath, string targetPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var args = $"/c mklink /H \"{linkPath}\" \"{targetPath}\"";
                    var psi = new ProcessStartInfo("cmd.exe", args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(10000);
                    return p?.ExitCode == 0;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Create a directory junction.
        /// </summary>
        public async Task<bool> CreateJunctionAsync(string linkPath, string targetPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var args = $"/c mklink /J \"{linkPath}\" \"{targetPath}\"";
                    var psi = new ProcessStartInfo("cmd.exe", args)
                    {
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit(10000);
                    return p?.ExitCode == 0;
                }
                catch { return false; }
            });
        }

        /// <summary>
        /// Scan a directory for symbolic links, hard links, and junctions.
        /// </summary>
        public async Task<List<LinkInfo>> ScanDirectoryAsync(string directory, bool recursive = false, IProgress<string>? progress = null)
        {
            var links = new List<LinkInfo>();
            if (!Directory.Exists(directory)) return links;

            await Task.Run(() =>
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

                try
                {
                    // Scan directories for junctions/symlinks
                    foreach (var dir in Directory.GetDirectories(directory, "*", searchOption))
                    {
                        try
                        {
                            var di = new DirectoryInfo(dir);
                            if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            {
                                progress?.Report($"Found link: {dir}");
                                var target = GetLinkTarget(dir);
                                links.Add(new LinkInfo
                                {
                                    Path = dir,
                                    Target = target ?? "(unknown)",
                                    Type = target != null ? (IsJunction(dir) ? LinkType.Junction : LinkType.SymbolicLink) : LinkType.Unknown,
                                    IsValid = !string.IsNullOrEmpty(target) && Directory.Exists(target),
                                    IsDirectory = true,
                                    Created = di.CreationTime
                                });
                            }
                        }
                        catch { }
                    }

                    // Scan files for symlinks
                    foreach (var file in Directory.GetFiles(directory, "*", searchOption))
                    {
                        try
                        {
                            var fi = new FileInfo(file);
                            if (fi.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            {
                                progress?.Report($"Found link: {file}");
                                var target = GetLinkTarget(file);
                                links.Add(new LinkInfo
                                {
                                    Path = file,
                                    Target = target ?? "(unknown)",
                                    Type = LinkType.SymbolicLink,
                                    IsValid = !string.IsNullOrEmpty(target) && File.Exists(target),
                                    IsDirectory = false,
                                    Created = fi.CreationTime,
                                    Size = fi.Length
                                });
                            }
                        }
                        catch { }
                    }
                }
                catch { }
            });

            return links;
        }

        /// <summary>
        /// Delete a link (symlink, junction, or hard link).
        /// </summary>
        public bool DeleteLink(string linkPath)
        {
            try
            {
                var info = new FileInfo(linkPath);
                if (info.Attributes.HasFlag(FileAttributes.Directory))
                {
                    // For junctions and directory symlinks, use Directory.Delete
                    Directory.Delete(linkPath, false);
                }
                else
                {
                    File.Delete(linkPath);
                }
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Get the target path of a symlink/junction.
        /// </summary>
        public static string? GetLinkTarget(string linkPath)
        {
            try
            {
                var fi = new FileInfo(linkPath);
                var target = fi.LinkTarget;
                if (target != null) return target;

                // Fallback for junctions
                var di = new DirectoryInfo(linkPath);
                return di.LinkTarget;
            }
            catch { return null; }
        }

        private static bool IsJunction(string path)
        {
            try
            {
                var di = new DirectoryInfo(path);
                return di.Attributes.HasFlag(FileAttributes.ReparsePoint) && di.LinkTarget != null;
            }
            catch { return false; }
        }

        /// <summary>
        /// Verify all links in a list still point to valid targets.
        /// </summary>
        public void ValidateLinks(List<LinkInfo> links)
        {
            foreach (var link in links)
            {
                link.IsValid = link.IsDirectory
                    ? Directory.Exists(link.Target)
                    : File.Exists(link.Target);
            }
        }
    }
}
