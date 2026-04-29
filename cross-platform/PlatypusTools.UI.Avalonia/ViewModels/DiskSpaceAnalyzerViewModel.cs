using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class DiskSpaceAnalyzerViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isScanning;

    public ObservableCollection<DirEntry> TopFolders { get; } = new();

    public DiskSpaceAnalyzerViewModel()
    {
        Root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning || string.IsNullOrEmpty(Root) || !Directory.Exists(Root)) return;
        TopFolders.Clear();
        IsScanning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Status = "Scanning…";
        try
        {
            await Task.Run(() =>
            {
                string[] subs;
                try { subs = Directory.GetDirectories(Root); }
                catch { subs = Array.Empty<string>(); }

                var results = new List<DirEntry>();
                foreach (var d in subs)
                {
                    if (token.IsCancellationRequested) break;
                    long bytes = SafeSize(d, token);
                    results.Add(new DirEntry { Path = d, Bytes = bytes });
                    var preview = results.OrderByDescending(r => r.Bytes).Take(50).ToList();
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        TopFolders.Clear();
                        foreach (var r in preview) TopFolders.Add(r);
                    });
                }
            }, token);
            Status = $"Done. {TopFolders.Count} top folders.";
        }
        catch (OperationCanceledException) { Status = "Cancelled."; }
        finally { IsScanning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    private static long SafeSize(string dir, CancellationToken token)
    {
        long total = 0;
        var stack = new Stack<string>();
        stack.Push(dir);
        while (stack.Count > 0 && !token.IsCancellationRequested)
        {
            var cur = stack.Pop();
            try
            {
                foreach (var f in Directory.EnumerateFiles(cur))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                foreach (var s in Directory.EnumerateDirectories(cur)) stack.Push(s);
            }
            catch { }
        }
        return total;
    }
}

public sealed class DirEntry
{
    public string Path { get; set; } = "";
    public long Bytes { get; set; }
    public string SizeDisplay => Bytes switch
    {
        < 1024 => $"{Bytes} B",
        < 1024L * 1024 => $"{Bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{Bytes / (1024.0 * 1024):F1} MB",
        _ => $"{Bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
