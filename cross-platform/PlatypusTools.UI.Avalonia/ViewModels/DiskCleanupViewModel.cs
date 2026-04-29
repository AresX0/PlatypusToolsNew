using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class DiskCleanupViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "Scan to discover candidates.";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<CleanupTarget> Targets { get; } = new();

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true; Status = "Scanning…"; Targets.Clear();
        await Task.Run(() =>
        {
            void Add(string name, string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                if (!Directory.Exists(path)) return;
                long bytes = 0;
                try { bytes = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Sum(f => { try { return new FileInfo(f).Length; } catch { return 0; } }); }
                catch { }
                Targets.Add(new CleanupTarget { IsSelected = false, Name = name, Path = path, Bytes = bytes });
            }
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add("Temp", Path.GetTempPath());
            if (ShellHelper.IsWindows)
            {
                Add("Windows Temp", @"C:\Windows\Temp");
                Add("INetCache", Path.Combine(home, "AppData", "Local", "Microsoft", "Windows", "INetCache"));
                Add("Crash Dumps", Path.Combine(home, "AppData", "Local", "CrashDumps"));
            }
            else if (ShellHelper.IsMac)
            {
                Add("~/Library/Caches", Path.Combine(home, "Library", "Caches"));
                Add("~/Library/Logs", Path.Combine(home, "Library", "Logs"));
                Add("/var/log (read)", "/var/log");
            }
            else if (ShellHelper.IsLinux)
            {
                Add("~/.cache", Path.Combine(home, ".cache"));
                Add("~/.local/share/Trash", Path.Combine(home, ".local", "share", "Trash"));
                Add("/var/tmp", "/var/tmp");
            }
        });
        Status = $"Found {Targets.Count} targets, total {Format(Targets.Sum(t => t.Bytes))}";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        var sel = Targets.Where(t => t.IsSelected).ToList();
        if (sel.Count == 0) { Status = "Nothing selected"; return; }
        IsBusy = true; long freed = 0; int errs = 0;
        await Task.Run(() =>
        {
            foreach (var t in sel)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(t.Path, "*", SearchOption.AllDirectories))
                    {
                        try { var sz = new FileInfo(f).Length; File.Delete(f); freed += sz; }
                        catch { errs++; }
                    }
                }
                catch { errs++; }
            }
        });
        Status = $"Freed {Format(freed)} ({errs} errors)";
        IsBusy = false;
        await ScanAsync();
    }

    private static string Format(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):F2} GB" :
        b >= 1L << 20 ? $"{b / (double)(1L << 20):F2} MB" :
        b >= 1L << 10 ? $"{b / (double)(1L << 10):F1} KB" : $"{b} B";
}

public partial class CleanupTarget : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public string SizeDisplay =>
        Bytes >= 1L << 30 ? $"{Bytes / (double)(1L << 30):F2} GB" :
        Bytes >= 1L << 20 ? $"{Bytes / (double)(1L << 20):F2} MB" :
        Bytes >= 1L << 10 ? $"{Bytes / (double)(1L << 10):F1} KB" : $"{Bytes} B";
}
