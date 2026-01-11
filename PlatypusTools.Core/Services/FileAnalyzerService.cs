using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace PlatypusTools.Core.Services
{
    public class FileAnalyzerService
    {
        public FileAnalysisResult AnalyzeDirectory(string path, bool includeSubdirectories = true)
        {
            var result = new FileAnalysisResult
            {
                RootPath = path,
                AnalysisDate = DateTime.Now
            };

            if (!Directory.Exists(path))
            {
                result.ErrorMessage = "Directory does not exist";
                return result;
            }

            try
            {
                var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(path, "*.*", searchOption);

                result.TotalFiles = files.Length;
                result.FilesByExtension = new Dictionary<string, FileTypeStats>();
                result.FilesBySize = new List<FileSizeInfo>();
                result.LargestFiles = new List<FileInfo>();
                result.OldestFiles = new List<FileInfo>();
                result.NewestFiles = new List<FileInfo>();
                result.EmptyFiles = new List<string>();

                var fileInfos = new List<System.IO.FileInfo>();

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new System.IO.FileInfo(file);
                        fileInfos.Add(fileInfo);

                        result.TotalSize += fileInfo.Length;

                        // Group by extension
                        var ext = fileInfo.Extension.ToLowerInvariant();
                        if (string.IsNullOrEmpty(ext))
                            ext = "(no extension)";

                        if (!result.FilesByExtension.ContainsKey(ext))
                        {
                            result.FilesByExtension[ext] = new FileTypeStats
                            {
                                Extension = ext,
                                Count = 0,
                                TotalSize = 0
                            };
                        }

                        result.FilesByExtension[ext].Count++;
                        result.FilesByExtension[ext].TotalSize += fileInfo.Length;

                        // Check for empty files
                        if (fileInfo.Length == 0)
                        {
                            result.EmptyFiles.Add(file);
                        }

                        // Size categorization
                        result.FilesBySize.Add(new FileSizeInfo
                        {
                            Path = file,
                            Size = fileInfo.Length,
                            Category = GetSizeCategory(fileInfo.Length)
                        });
                    }
                    catch
                    {
                        // Skip files that can't be accessed
                    }
                }

                // Get largest files
                result.LargestFiles = fileInfos
                    .OrderByDescending(f => f.Length)
                    .Take(100)
                    .ToList();

                // Get oldest files
                result.OldestFiles = fileInfos
                    .OrderBy(f => f.CreationTime)
                    .Take(100)
                    .ToList();

                // Get newest files
                result.NewestFiles = fileInfos
                    .OrderByDescending(f => f.CreationTime)
                    .Take(100)
                    .ToList();

                // Calculate directory statistics
                result.DirectoryStats = CalculateDirectoryStats(path, includeSubdirectories);

                // Age analysis
                result.FilesByAge = AnalyzeFileAges(fileInfos);

            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
            }

            return result;
        }

        public List<DuplicateFileGroup> FindDuplicates(string path, bool includeSubdirectories = true)
        {
            var groups = new List<DuplicateFileGroup>();
            var filesBySize = new Dictionary<long, List<string>>();

            try
            {
                var searchOption = includeSubdirectories ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var files = Directory.GetFiles(path, "*.*", searchOption);

                // Group by size first (quick filter)
                foreach (var file in files)
                {
                    try
                    {
                        var size = new System.IO.FileInfo(file).Length;
                        if (!filesBySize.ContainsKey(size))
                            filesBySize[size] = new List<string>();
                        filesBySize[size].Add(file);
                    }
                    catch { }
                }

                // Check hashes for files with same size
                foreach (var sizeGroup in filesBySize.Where(g => g.Value.Count > 1))
                {
                    var hashGroups = new Dictionary<string, List<string>>();

                    foreach (var file in sizeGroup.Value)
                    {
                        try
                        {
                            var hash = CalculateFileHash(file);
                            if (!hashGroups.ContainsKey(hash))
                                hashGroups[hash] = new List<string>();
                            hashGroups[hash].Add(file);
                        }
                        catch { }
                    }

                    foreach (var hashGroup in hashGroups.Where(g => g.Value.Count > 1))
                    {
                        groups.Add(new DuplicateFileGroup
                        {
                            Hash = hashGroup.Key,
                            Size = sizeGroup.Key,
                            Files = hashGroup.Value
                        });
                    }
                }
            }
            catch { }

            return groups;
        }

        public DirectoryTreeNode BuildDirectoryTree(string path)
        {
            var root = new DirectoryTreeNode
            {
                Name = Path.GetFileName(path) ?? path,
                FullPath = path,
                IsDirectory = true
            };

            try
            {
                var dirInfo = new DirectoryInfo(path);

                // Add subdirectories
                foreach (var dir in dirInfo.GetDirectories())
                {
                    try
                    {
                        var node = new DirectoryTreeNode
                        {
                            Name = dir.Name,
                            FullPath = dir.FullName,
                            IsDirectory = true,
                            FileCount = dir.GetFiles("*.*", SearchOption.AllDirectories).Length,
                            Size = CalculateDirectorySize(dir.FullName)
                        };
                        root.Children.Add(node);
                    }
                    catch { }
                }

                // Add files
                foreach (var file in dirInfo.GetFiles())
                {
                    try
                    {
                        var node = new DirectoryTreeNode
                        {
                            Name = file.Name,
                            FullPath = file.FullName,
                            IsDirectory = false,
                            Size = file.Length
                        };
                        root.Children.Add(node);
                        root.FileCount++;
                    }
                    catch { }
                }

                root.Size = CalculateDirectorySize(path);
            }
            catch { }

            return root;
        }

        private string GetSizeCategory(long bytes)
        {
            if (bytes == 0) return "Empty";
            if (bytes < 1024) return "< 1 KB";
            if (bytes < 1024 * 1024) return "1 KB - 1 MB";
            if (bytes < 10 * 1024 * 1024) return "1 MB - 10 MB";
            if (bytes < 100 * 1024 * 1024) return "10 MB - 100 MB";
            if (bytes < 1024 * 1024 * 1024) return "100 MB - 1 GB";
            return "> 1 GB";
        }

        private Dictionary<string, int> AnalyzeFileAges(List<System.IO.FileInfo> files)
        {
            var now = DateTime.Now;
            var ageGroups = new Dictionary<string, int>
            {
                ["< 1 day"] = 0,
                ["1-7 days"] = 0,
                ["1-4 weeks"] = 0,
                ["1-6 months"] = 0,
                ["6-12 months"] = 0,
                ["> 1 year"] = 0
            };

            foreach (var file in files)
            {
                var age = now - file.CreationTime;
                if (age.TotalDays < 1)
                    ageGroups["< 1 day"]++;
                else if (age.TotalDays < 7)
                    ageGroups["1-7 days"]++;
                else if (age.TotalDays < 28)
                    ageGroups["1-4 weeks"]++;
                else if (age.TotalDays < 180)
                    ageGroups["1-6 months"]++;
                else if (age.TotalDays < 365)
                    ageGroups["6-12 months"]++;
                else
                    ageGroups["> 1 year"]++;
            }

            return ageGroups;
        }

        private Dictionary<string, long> CalculateDirectoryStats(string path, bool recursive)
        {
            var stats = new Dictionary<string, long>();
            try
            {
                var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var directories = Directory.GetDirectories(path, "*", searchOption);

                foreach (var dir in directories)
                {
                    try
                    {
                        var size = CalculateDirectorySize(dir);
                        stats[dir] = size;
                    }
                    catch { }
                }
            }
            catch { }

            return stats;
        }

        private long CalculateDirectorySize(string path)
        {
            try
            {
                return Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Sum(file => new System.IO.FileInfo(file).Length);
            }
            catch
            {
                return 0;
            }
        }

        private string CalculateFileHash(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = md5.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }

    public class FileAnalysisResult
    {
        public string RootPath { get; set; } = string.Empty;
        public DateTime AnalysisDate { get; set; }
        public int TotalFiles { get; set; }
        public long TotalSize { get; set; }
        public Dictionary<string, FileTypeStats> FilesByExtension { get; set; } = new();
        public List<FileSizeInfo> FilesBySize { get; set; } = new();
        public List<System.IO.FileInfo> LargestFiles { get; set; } = new();
        public List<System.IO.FileInfo> OldestFiles { get; set; } = new();
        public List<System.IO.FileInfo> NewestFiles { get; set; } = new();
        public List<string> EmptyFiles { get; set; } = new();
        public Dictionary<string, int> FilesByAge { get; set; } = new();
        public Dictionary<string, long> DirectoryStats { get; set; } = new();
        public string? ErrorMessage { get; set; }
    }

    public class FileTypeStats
    {
        public string Extension { get; set; } = string.Empty;
        public int Count { get; set; }
        public long TotalSize { get; set; }
    }

    public class FileSizeInfo
    {
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Category { get; set; } = string.Empty;
    }

    public class DuplicateFileGroup
    {
        public string Hash { get; set; } = string.Empty;
        public long Size { get; set; }
        public List<string> Files { get; set; } = new();
    }

    public class DirectoryTreeNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public int FileCount { get; set; }
        public List<DirectoryTreeNode> Children { get; set; } = new();
    }
}
