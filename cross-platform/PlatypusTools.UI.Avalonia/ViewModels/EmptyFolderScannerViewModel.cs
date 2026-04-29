using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class EmptyFolderScannerViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isScanning;

    public ObservableCollection<string> EmptyFolders { get; } = new();

    public EmptyFolderScannerViewModel()
    {
        Root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning || !Directory.Exists(Root)) { Status = "Root not found."; return; }
        EmptyFolders.Clear();
        IsScanning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            await Task.Run(() =>
            {
                var stack = new Stack<string>();
                stack.Push(Root);
                while (stack.Count > 0 && !token.IsCancellationRequested)
                {
                    var cur = stack.Pop();
                    try
                    {
                        var subs = Directory.GetDirectories(cur);
                        foreach (var s in subs) stack.Push(s);
                        if (subs.Length == 0 && Directory.GetFiles(cur).Length == 0)
                        {
                            global::Avalonia.Threading.Dispatcher.UIThread.Post(() => EmptyFolders.Add(cur));
                        }
                    }
                    catch { }
                }
            }, token);
            Status = $"Found {EmptyFolders.Count} empty folders.";
        }
        catch (OperationCanceledException) { Status = "Cancelled."; }
        finally { IsScanning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task DeleteAllAsync()
    {
        var copy = EmptyFolders.ToArray();
        int deleted = 0;
        foreach (var f in copy)
        {
            try { Directory.Delete(f, recursive: false); deleted++; }
            catch { }
        }
        Status = $"Deleted {deleted} empty folders.";
        EmptyFolders.Clear();
        await Task.CompletedTask;
    }
}
