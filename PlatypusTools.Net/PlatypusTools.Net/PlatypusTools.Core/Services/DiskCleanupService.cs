using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services;

public class DiskCleanupService
{
    public async Task<CleanupAnalysisResult> AnalyzeAsync(
        DiskCleanupCategories categories,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<CleanupCategoryResult>();
        
        if (categories.HasFlag(DiskCleanupCategories.WindowsTempFiles))
        {
            progress?.Report("Analyzing Windows Temp Files...");
            results.Add(await AnalyzeFolderAsync("Windows Temp", GetWindowsTempPath(), cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.UserTempFiles))
        {
            progress?.Report("Analyzing User Temp Files...");
            results.Add(await AnalyzeFolderAsync("User Temp", GetUserTempPath(), cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.PrefetchFiles))
        {
            progress?.Report("Analyzing Prefetch Files...");
            results.Add(await AnalyzeFolderAsync("Prefetch", GetPrefetchPath(), cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.RecycleBin))
        {
            progress?.Report("Analyzing Recycle Bin...");
            results.Add(await AnalyzeRecycleBinAsync(cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.DownloadsOlderThan30Days))
        {
            progress?.Report("Analyzing old Downloads...");
            results.Add(await AnalyzeOldDownloadsAsync(30, cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.WindowsUpdateCache))
        {
            progress?.Report("Analyzing Windows Update Cache...");
            results.Add(await AnalyzeFolderAsync("Windows Update Cache", GetUpdateCachePath(), cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.ThumbnailCache))
        {
            progress?.Report("Analyzing Thumbnail Cache...");
            results.Add(await AnalyzeFolderAsync("Thumbnail Cache", GetThumbnailCachePath(), cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.WindowsErrorReports))
        {
            progress?.Report("Analyzing Windows Error Reports...");
            results.Add(await AnalyzeFolderAsync("Windows Error Reports", GetErrorReportsPath(), cancellationToken));
        }

        if (categories.HasFlag(DiskCleanupCategories.OldLogFiles))
        {
            progress?.Report("Analyzing Old Log Files...");
            results.Add(await AnalyzeLogFilesAsync(cancellationToken));
        }

        progress?.Report("Analysis complete");

        return new CleanupAnalysisResult
        {
            Categories = results,
            TotalFiles = results.Sum(r => r.FileCount),
            TotalSize = results.Sum(r => r.TotalSize)
        };
    }

    public async Task<CleanupExecutionResult> CleanAsync(
        CleanupAnalysisResult analysisResult,
        bool dryRun = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int deletedFiles = 0;
        long freedSpace = 0;
        var errors = new List<string>();

        foreach (var category in analysisResult.Categories)
        {
            if (category.FileCount == 0) continue;

            progress?.Report($"Cleaning {category.Category}...");

            foreach (var file in category.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!dryRun && File.Exists(file.Path))
                    {
                        File.Delete(file.Path);
                        deletedFiles++;
                        freedSpace += file.Size;
                    }
                    else if (dryRun)
                    {
                        deletedFiles++;
                        freedSpace += file.Size;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{file.Path}: {ex.Message}");
                }
            }
        }

        progress?.Report($"Cleanup complete. Freed {FormatBytes(freedSpace)}");

        return new CleanupExecutionResult
        {
            FilesDeleted = deletedFiles,
            SpaceFreed = freedSpace,
            Errors = errors,
            WasDryRun = dryRun
        };
    }

    private async Task<CleanupCategoryResult> AnalyzeFolderAsync(
        string categoryName,
        string path,
        CancellationToken cancellationToken)
    {
        var result = new CleanupCategoryResult { Category = categoryName, Path = path };

        if (!Directory.Exists(path))
        {
            return result;
        }

        try
        {
            var files = await Task.Run(() =>
                Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Select(f =>
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            return new CleanupFile { Path = f, Size = fi.Length };
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(f => f != null)
                    .Cast<CleanupFile>()
                    .ToList(), cancellationToken);

            result.Files = files;
            result.FileCount = files.Count;
            result.TotalSize = files.Sum(f => f.Size);
        }
        catch
        {
            // Ignore access denied and other errors
        }

        return result;
    }

    private async Task<CleanupCategoryResult> AnalyzeRecycleBinAsync(CancellationToken cancellationToken)
    {
        var result = new CleanupCategoryResult { Category = "Recycle Bin", Path = "$Recycle.Bin" };

        try
        {
            // Get all drives and check their recycle bins
            var drives = DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed);
            
            var allFiles = new List<CleanupFile>();

            foreach (var drive in drives)
            {
                var recycleBinPath = Path.Combine(drive.RootDirectory.FullName, "$Recycle.Bin");
                if (Directory.Exists(recycleBinPath))
                {
                    var files = await Task.Run(() =>
                        Directory.GetFiles(recycleBinPath, "*.*", SearchOption.AllDirectories)
                            .Select(f =>
                            {
                                try
                                {
                                    var fi = new FileInfo(f);
                                    return new CleanupFile { Path = f, Size = fi.Length };
                                }
                                catch
                                {
                                    return null;
                                }
                            })
                            .Where(f => f != null)
                            .Cast<CleanupFile>()
                            .ToList(), cancellationToken);

                    allFiles.AddRange(files);
                }
            }

            result.Files = allFiles;
            result.FileCount = allFiles.Count;
            result.TotalSize = allFiles.Sum(f => f.Size);
        }
        catch
        {
            // Ignore errors
        }

        return result;
    }

    private async Task<CleanupCategoryResult> AnalyzeOldDownloadsAsync(int daysOld, CancellationToken cancellationToken)
    {
        var result = new CleanupCategoryResult 
        { 
            Category = $"Downloads Older Than {daysOld} Days",
            Path = GetDownloadsPath()
        };

        var downloadsPath = GetDownloadsPath();
        if (!Directory.Exists(downloadsPath))
        {
            return result;
        }

        try
        {
            var cutoffDate = DateTime.Now.AddDays(-daysOld);

            var files = await Task.Run(() =>
                Directory.GetFiles(downloadsPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Select(f =>
                    {
                        try
                        {
                            var fi = new FileInfo(f);
                            if (fi.LastWriteTime < cutoffDate)
                            {
                                return new CleanupFile { Path = f, Size = fi.Length };
                            }
                        }
                        catch { }
                        return null;
                    })
                    .Where(f => f != null)
                    .Cast<CleanupFile>()
                    .ToList(), cancellationToken);

            result.Files = files;
            result.FileCount = files.Count;
            result.TotalSize = files.Sum(f => f.Size);
        }
        catch
        {
            // Ignore errors
        }

        return result;
    }

    private async Task<CleanupCategoryResult> AnalyzeLogFilesAsync(CancellationToken cancellationToken)
    {
        var result = new CleanupCategoryResult { Category = "Old Log Files", Path = "Various" };

        var logPaths = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Panther"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")
        };

        var allFiles = new List<CleanupFile>();

        foreach (var logPath in logPaths.Where(Directory.Exists))
        {
            try
            {
                var files = await Task.Run(() =>
                    Directory.GetFiles(logPath, "*.log", SearchOption.AllDirectories)
                        .Concat(Directory.GetFiles(logPath, "*.txt", SearchOption.AllDirectories))
                        .Select(f =>
                        {
                            try
                            {
                                var fi = new FileInfo(f);
                                // Only include logs older than 7 days
                                if (fi.LastWriteTime < DateTime.Now.AddDays(-7))
                                {
                                    return new CleanupFile { Path = f, Size = fi.Length };
                                }
                            }
                            catch { }
                            return null;
                        })
                        .Where(f => f != null)
                        .Cast<CleanupFile>()
                        .ToList(), cancellationToken);

                allFiles.AddRange(files);
            }
            catch { }
        }

        result.Files = allFiles;
        result.FileCount = allFiles.Count;
        result.TotalSize = allFiles.Sum(f => f.Size);

        return result;
    }

    private static string GetWindowsTempPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
    private static string GetUserTempPath() => Path.GetTempPath();
    private static string GetPrefetchPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
    private static string GetUpdateCachePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download");
    private static string GetThumbnailCachePath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
    private static string GetErrorReportsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "WER");
    private static string GetDownloadsPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }
}

[Flags]
public enum DiskCleanupCategories
{
    None = 0,
    WindowsTempFiles = 1 << 0,
    UserTempFiles = 1 << 1,
    PrefetchFiles = 1 << 2,
    RecycleBin = 1 << 3,
    DownloadsOlderThan30Days = 1 << 4,
    WindowsUpdateCache = 1 << 5,
    ThumbnailCache = 1 << 6,
    WindowsErrorReports = 1 << 7,
    OldLogFiles = 1 << 8,
    All = WindowsTempFiles | UserTempFiles | PrefetchFiles | RecycleBin | 
          DownloadsOlderThan30Days | WindowsUpdateCache | ThumbnailCache | 
          WindowsErrorReports | OldLogFiles
}

public class CleanupAnalysisResult
{
    public List<CleanupCategoryResult> Categories { get; init; } = new();
    public int TotalFiles { get; init; }
    public long TotalSize { get; init; }
}

public class CleanupCategoryResult
{
    public string Category { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public List<CleanupFile> Files { get; set; } = new();
    public int FileCount { get; set; }
    public long TotalSize { get; set; }
}

public class CleanupFile
{
    public string Path { get; init; } = string.Empty;
    public long Size { get; init; }
}

public class CleanupExecutionResult
{
    public int FilesDeleted { get; init; }
    public long SpaceFreed { get; init; }
    public List<string> Errors { get; init; } = new();
    public bool WasDryRun { get; init; }
}
