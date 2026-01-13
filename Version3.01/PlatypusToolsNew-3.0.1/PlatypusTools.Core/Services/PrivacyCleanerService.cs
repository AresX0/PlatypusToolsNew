using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Services;

public class PrivacyCleanerService
{
    public async Task<PrivacyAnalysisResult> AnalyzeAsync(
        PrivacyCategories categories,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<PrivacyCategoryResult>();

        if (categories.HasFlag(PrivacyCategories.BrowserChrome))
        {
            progress?.Report("Analyzing Chrome data...");
            results.Add(await AnalyzeChromeAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.BrowserEdge))
        {
            progress?.Report("Analyzing Edge data...");
            results.Add(await AnalyzeEdgeAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.BrowserFirefox))
        {
            progress?.Report("Analyzing Firefox data...");
            results.Add(await AnalyzeFirefoxAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.BrowserBrave))
        {
            progress?.Report("Analyzing Brave data...");
            results.Add(await AnalyzeBraveAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.CloudOneDrive))
        {
            progress?.Report("Analyzing OneDrive tokens...");
            results.Add(await AnalyzeOneDriveAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.CloudGoogle))
        {
            progress?.Report("Analyzing Google Drive tokens...");
            results.Add(await AnalyzeGoogleDriveAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.CloudDropbox))
        {
            progress?.Report("Analyzing Dropbox tokens...");
            results.Add(await AnalyzeDropboxAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.CloudiCloud))
        {
            progress?.Report("Analyzing iCloud tokens...");
            results.Add(await AnalyzeiCloudAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.WindowsRecentDocs))
        {
            progress?.Report("Analyzing Recent Documents...");
            results.Add(await AnalyzeRecentDocsAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.WindowsJumpLists))
        {
            progress?.Report("Analyzing Jump Lists...");
            results.Add(await AnalyzeJumpListsAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.WindowsExplorerHistory))
        {
            progress?.Report("Analyzing Explorer History...");
            results.Add(await AnalyzeExplorerHistoryAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.WindowsClipboard))
        {
            progress?.Report("Analyzing Clipboard...");
            results.Add(AnalyzeClipboard());
        }

        if (categories.HasFlag(PrivacyCategories.ApplicationOffice))
        {
            progress?.Report("Analyzing Office data...");
            results.Add(await AnalyzeOfficeAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.ApplicationAdobe))
        {
            progress?.Report("Analyzing Adobe data...");
            results.Add(await AnalyzeAdobeAsync(cancellationToken));
        }

        if (categories.HasFlag(PrivacyCategories.ApplicationMediaPlayers))
        {
            progress?.Report("Analyzing Media Players data...");
            results.Add(await AnalyzeMediaPlayersAsync(cancellationToken));
        }

        progress?.Report("Analysis complete");

        return new PrivacyAnalysisResult
        {
            Categories = results,
            TotalItems = results.Sum(r => r.ItemCount),
            TotalSize = results.Sum(r => r.TotalSize)
        };
    }

    public async Task<PrivacyCleanupResult> CleanAsync(
        PrivacyAnalysisResult analysisResult,
        bool dryRun = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        int deletedItems = 0;
        long freedSpace = 0;
        var errors = new List<string>();

        foreach (var category in analysisResult.Categories)
        {
            if (category.ItemCount == 0) continue;

            progress?.Report($"Cleaning {category.Category}...");

            foreach (var item in category.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (!dryRun)
                    {
                        if (item.IsFile && File.Exists(item.Path))
                        {
                            File.Delete(item.Path);
                            deletedItems++;
                            freedSpace += item.Size;
                        }
                        else if (!item.IsFile && Directory.Exists(item.Path))
                        {
                            Directory.Delete(item.Path, true);
                            deletedItems++;
                            freedSpace += item.Size;
                        }
                    }
                    else
                    {
                        deletedItems++;
                        freedSpace += item.Size;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.Path}: {ex.Message}");
                }
            }
        }

        progress?.Report($"Cleanup complete. Freed {FormatBytes(freedSpace)}");

        return new PrivacyCleanupResult
        {
            ItemsDeleted = deletedItems,
            SpaceFreed = freedSpace,
            Errors = errors,
            WasDryRun = dryRun
        };
    }

    private async Task<PrivacyCategoryResult> AnalyzeChromeAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Chrome Browser Data" };
        var chromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Google", "Chrome", "User Data", "Default");

        var targets = new[] { "History", "Cookies", "Cache", "Web Data", "Visited Links" };
        await AnalyzeBrowserPaths(result, chromePath, targets, cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeEdgeAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Edge Browser Data" };
        var edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Microsoft", "Edge", "User Data", "Default");

        var targets = new[] { "History", "Cookies", "Cache", "Web Data", "Visited Links" };
        await AnalyzeBrowserPaths(result, edgePath, targets, cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeFirefoxAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Firefox Browser Data" };
        var firefoxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Mozilla", "Firefox", "Profiles");

        if (Directory.Exists(firefoxPath))
        {
            await Task.Run(() =>
            {
                foreach (var profile in Directory.GetDirectories(firefoxPath))
                {
                    var targets = new[] { "cookies.sqlite", "places.sqlite", "cache2" };
                    foreach (var target in targets)
                    {
                        var fullPath = Path.Combine(profile, target);
                        if (File.Exists(fullPath))
                        {
                            var fi = new FileInfo(fullPath);
                            result.Items.Add(new PrivacyItem { Path = fullPath, Size = fi.Length, IsFile = true });
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            var size = GetDirectorySize(fullPath);
                            result.Items.Add(new PrivacyItem { Path = fullPath, Size = size, IsFile = false });
                        }
                    }
                }
            }, cancellationToken);
        }

        result.ItemCount = result.Items.Count;
        result.TotalSize = result.Items.Sum(i => i.Size);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeBraveAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Brave Browser Data" };
        var bravePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "BraveSoftware", "Brave-Browser", "User Data", "Default");

        var targets = new[] { "History", "Cookies", "Cache", "Web Data" };
        await AnalyzeBrowserPaths(result, bravePath, targets, cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeOneDriveAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "OneDrive Tokens" };
        var oneDrivePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Microsoft", "OneDrive", "settings");

        await AnalyzeTokenPath(result, oneDrivePath, "*.dat", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeGoogleDriveAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Google Drive Tokens" };
        var googlePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Google", "Drive");

        await AnalyzeTokenPath(result, googlePath, "*.db", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeDropboxAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Dropbox Tokens" };
        var dropboxPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Dropbox");

        await AnalyzeTokenPath(result, dropboxPath, "*.dbx", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeiCloudAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "iCloud Tokens" };
        var iCloudPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Apple Computer", "iCloud");

        await AnalyzeTokenPath(result, iCloudPath, "*.db", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeRecentDocsAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Recent Documents" };
        var recentPath = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

        await AnalyzeFolder(result, recentPath, "*.lnk", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeJumpListsAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Jump Lists" };
        var jumpListPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Microsoft", "Windows", "Recent", "AutomaticDestinations");

        await AnalyzeFolder(result, jumpListPath, "*", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeExplorerHistoryAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Explorer History" };
        var explorerPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Microsoft", "Windows", "Explorer");

        await AnalyzeFolder(result, explorerPath, "*.db", cancellationToken);
        return result;
    }

    private PrivacyCategoryResult AnalyzeClipboard()
    {
        var result = new PrivacyCategoryResult 
        { 
            Category = "Clipboard Data",
            ItemCount = 1,
            TotalSize = 0 
        };
        result.Items.Add(new PrivacyItem { Path = "[System Clipboard]", Size = 0, IsFile = false });
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeOfficeAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Office Recent Files" };
        var officePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Microsoft", "Office", "Recent");

        await AnalyzeFolder(result, officePath, "*", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeAdobeAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Adobe Recent Files" };
        var adobePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
            "Adobe");

        await AnalyzeFolder(result, adobePath, "RecentFiles*", cancellationToken);
        return result;
    }

    private async Task<PrivacyCategoryResult> AnalyzeMediaPlayersAsync(CancellationToken cancellationToken)
    {
        var result = new PrivacyCategoryResult { Category = "Media Players History" };
        
        var vlcPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vlc");
        var wmPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
            "Microsoft", "Media Player");

        await AnalyzeFolder(result, vlcPath, "*.log", cancellationToken);
        await AnalyzeFolder(result, wmPath, "*.wmdb", cancellationToken);

        return result;
    }

    private async Task AnalyzeBrowserPaths(PrivacyCategoryResult result, string basePath, string[] targets, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(basePath)) return;

        await Task.Run(() =>
        {
            foreach (var target in targets)
            {
                var fullPath = Path.Combine(basePath, target);
                if (File.Exists(fullPath))
                {
                    var fi = new FileInfo(fullPath);
                    result.Items.Add(new PrivacyItem { Path = fullPath, Size = fi.Length, IsFile = true });
                }
                else if (Directory.Exists(fullPath))
                {
                    var size = GetDirectorySize(fullPath);
                    result.Items.Add(new PrivacyItem { Path = fullPath, Size = size, IsFile = false });
                }
            }
        }, cancellationToken);

        result.ItemCount = result.Items.Count;
        result.TotalSize = result.Items.Sum(i => i.Size);
    }

    private async Task AnalyzeTokenPath(PrivacyCategoryResult result, string path, string pattern, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) return;

        await Task.Run(() =>
        {
            var files = Directory.GetFiles(path, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    result.Items.Add(new PrivacyItem { Path = file, Size = fi.Length, IsFile = true });
                }
                catch { }
            }
        }, cancellationToken);

        result.ItemCount = result.Items.Count;
        result.TotalSize = result.Items.Sum(i => i.Size);
    }

    private async Task AnalyzeFolder(PrivacyCategoryResult result, string path, string pattern, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(path)) return;

        await Task.Run(() =>
        {
            var files = Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly);
            foreach (var file in files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    result.Items.Add(new PrivacyItem { Path = file, Size = fi.Length, IsFile = true });
                }
                catch { }
            }
        }, cancellationToken);

        result.ItemCount = result.Items.Count;
        result.TotalSize = result.Items.Sum(i => i.Size);
    }

    private static long GetDirectorySize(string path)
    {
        try
        {
            return Directory.GetFiles(path, "*", SearchOption.AllDirectories)
                .Sum(f => new FileInfo(f).Length);
        }
        catch
        {
            return 0;
        }
    }

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
public enum PrivacyCategories
{
    None = 0,
    BrowserChrome = 1 << 0,
    BrowserEdge = 1 << 1,
    BrowserFirefox = 1 << 2,
    BrowserBrave = 1 << 3,
    CloudOneDrive = 1 << 4,
    CloudGoogle = 1 << 5,
    CloudDropbox = 1 << 6,
    CloudiCloud = 1 << 7,
    WindowsRecentDocs = 1 << 8,
    WindowsJumpLists = 1 << 9,
    WindowsExplorerHistory = 1 << 10,
    WindowsClipboard = 1 << 11,
    ApplicationOffice = 1 << 12,
    ApplicationAdobe = 1 << 13,
    ApplicationMediaPlayers = 1 << 14,
    AllBrowsers = BrowserChrome | BrowserEdge | BrowserFirefox | BrowserBrave,
    AllCloud = CloudOneDrive | CloudGoogle | CloudDropbox | CloudiCloud,
    AllWindows = WindowsRecentDocs | WindowsJumpLists | WindowsExplorerHistory | WindowsClipboard,
    AllApplications = ApplicationOffice | ApplicationAdobe | ApplicationMediaPlayers,
    All = AllBrowsers | AllCloud | AllWindows | AllApplications
}

public class PrivacyAnalysisResult
{
    public List<PrivacyCategoryResult> Categories { get; init; } = new();
    public int TotalItems { get; init; }
    public long TotalSize { get; init; }
}

public class PrivacyCategoryResult
{
    public string Category { get; init; } = string.Empty;
    public List<PrivacyItem> Items { get; init; } = new();
    public int ItemCount { get; set; }
    public long TotalSize { get; set; }
}

public class PrivacyItem
{
    public string Path { get; init; } = string.Empty;
    public long Size { get; init; }
    public bool IsFile { get; init; }
}

public class PrivacyCleanupResult
{
    public int ItemsDeleted { get; init; }
    public long SpaceFreed { get; init; }
    public List<string> Errors { get; init; } = new();
    public bool WasDryRun { get; init; }
}
