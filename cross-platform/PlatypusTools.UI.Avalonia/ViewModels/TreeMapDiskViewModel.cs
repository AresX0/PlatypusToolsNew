using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class TreeMapDiskViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Pick a folder, Scan, then a tree-map of the largest 50 entries renders to the right.";
    public ObservableCollection<TreeMapEntry> Entries { get; } = new();

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(Root) || !Directory.Exists(Root)) { Status = "Pick an existing folder"; return; }
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        IsBusy = true; Status = "Scanning…"; Entries.Clear();
        var ct = _cts.Token;
        var rows = await Task.Run(() => CollectTopLevel(Root!, ct), ct);
        var top = rows.OrderByDescending(r => r.Bytes).Take(50).ToList();
        long total = top.Sum(r => r.Bytes); if (total <= 0) total = 1;
        foreach (var r in top) { r.Percent = (double)r.Bytes / total * 100.0; Entries.Add(r); }
        Status = $"{Entries.Count} entries · total {Pretty(total)}";
        IsBusy = false;
    }

    private static List<TreeMapEntry> CollectTopLevel(string root, CancellationToken ct)
    {
        var list = new List<TreeMapEntry>();
        try
        {
            foreach (var d in Directory.EnumerateDirectories(root))
            {
                ct.ThrowIfCancellationRequested();
                long bytes = 0;
                try { bytes = new DirectoryInfo(d).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => { try { return f.Length; } catch { return 0; } }); }
                catch { }
                list.Add(new TreeMapEntry { Name = Path.GetFileName(d), FullPath = d, Bytes = bytes });
            }
            foreach (var f in Directory.EnumerateFiles(root))
            {
                ct.ThrowIfCancellationRequested();
                try { var fi = new FileInfo(f); list.Add(new TreeMapEntry { Name = fi.Name, FullPath = f, Bytes = fi.Length }); } catch { }
            }
        }
        catch { }
        return list;
    }

    private static string Pretty(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" }; double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }

    [RelayCommand] private void Cancel() { _cts?.Cancel(); Status = "Cancelled"; }
}

public partial class TreeMapEntry : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _fullPath = "";
    [ObservableProperty] private long _bytes;
    [ObservableProperty] private double _percent;
    public string Pretty
    {
        get { string[] u = { "B", "KB", "MB", "GB", "TB" }; double v = Bytes; int i = 0; while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; } return $"{v:0.##} {u[i]}"; }
    }
}
