using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class PrivacyCleanerViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isRunning;

    public ObservableCollection<PrivacyTarget> Targets { get; } = new();

    public PrivacyCleanerViewModel() { Discover(); }

    private void Discover()
    {
        Targets.Clear();
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (ShellHelper.IsWindows)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Add("Windows Recent files", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Windows", "Recent"));
            Add("Temp", Path.GetTempPath());
            Add("Edge Cache", Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache"));
            Add("Chrome Cache", Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache"));
            Add("Firefox Cache", Path.Combine(local, "Mozilla", "Firefox", "Profiles"));
        }
        else if (ShellHelper.IsMac)
        {
            Add("~/Library/Caches", Path.Combine(home, "Library", "Caches"));
            Add("Trash", Path.Combine(home, ".Trash"));
            Add("Recent items", Path.Combine(home, "Library", "Application Support", "com.apple.sharedfilelist"));
            Add("Tmp", "/tmp");
        }
        else
        {
            Add("~/.cache", Path.Combine(home, ".cache"));
            Add("Trash", Path.Combine(home, ".local", "share", "Trash"));
            Add("Recently used", Path.Combine(home, ".local", "share", "recently-used.xbel"));
            Add("Tmp", "/tmp");
        }
    }

    private void Add(string name, string path)
    {
        try
        {
            long size = 0;
            if (Directory.Exists(path))
                size = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Select(f => { try { return new FileInfo(f).Length; } catch { return 0L; } }).Sum();
            else if (File.Exists(path))
                size = new FileInfo(path).Length;
            Targets.Add(new PrivacyTarget { Name = name, Path = path, Bytes = size, IsSelected = false });
        }
        catch { Targets.Add(new PrivacyTarget { Name = name, Path = path, Bytes = 0 }); }
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        IsRunning = true; Output = ""; long freed = 0;
        await Task.Run(() =>
        {
            foreach (var t in Targets.Where(t => t.IsSelected))
            {
                try
                {
                    if (Directory.Exists(t.Path))
                    {
                        foreach (var f in Directory.EnumerateFiles(t.Path, "*", SearchOption.AllDirectories))
                        {
                            try { var len = new FileInfo(f).Length; File.Delete(f); freed += len; } catch { }
                        }
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += $"Cleaned {t.Path}\n");
                    }
                    else if (File.Exists(t.Path))
                    {
                        var len = new FileInfo(t.Path).Length; File.Delete(t.Path); freed += len;
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += $"Deleted {t.Path}\n");
                    }
                }
                catch (Exception ex) { global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += $"Failed {t.Path}: {ex.Message}\n"); }
            }
        });
        Status = $"Freed {freed / (1024.0 * 1024.0):F1} MB";
        Discover();
        IsRunning = false;
    }

    [RelayCommand] private void Refresh() => Discover();
}

public partial class PrivacyTarget : ObservableObject
{
    [ObservableProperty] private bool _isSelected;
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public string SizeDisplay => $"{Bytes / (1024.0 * 1024.0):F2} MB";
}
