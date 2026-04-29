using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class WallpaperRotatorViewModel : ObservableObject
{
    private static readonly string[] ImgExt = { ".jpg", ".jpeg", ".png", ".bmp", ".webp" };
    private readonly WallpaperService _svc = new();
    private CancellationTokenSource? _cts;
    private int _index = -1;

    [ObservableProperty] private string _folder = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
    [ObservableProperty] private int _intervalSeconds = 600;
    [ObservableProperty] private bool _isRotating;
    [ObservableProperty] private string _currentImage = "";
    [ObservableProperty] private string _status = "";

    public ObservableCollection<string> Wallpapers { get; } = new();

    [RelayCommand]
    public void Refresh()
    {
        Wallpapers.Clear();
        try
        {
            if (!Directory.Exists(Folder)) { Status = "Folder not found"; return; }
            foreach (var f in Directory.EnumerateFiles(Folder)
                         .Where(f => ImgExt.Contains(Path.GetExtension(f).ToLowerInvariant()))
                         .OrderBy(f => f))
                Wallpapers.Add(f);
            Status = $"{Wallpapers.Count} wallpapers";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task ApplyNextAsync()
    {
        if (Wallpapers.Count == 0) Refresh();
        if (Wallpapers.Count == 0) return;
        _index = (_index + 1) % Wallpapers.Count;
        var p = Wallpapers[_index];
        var (ok, detail) = await _svc.SetAsync(p);
        CurrentImage = p;
        Status = ok ? $"Applied: {Path.GetFileName(p)}" : $"Failed: {detail}";
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsRotating) return;
        Refresh();
        if (Wallpapers.Count == 0) return;
        _cts = new CancellationTokenSource();
        IsRotating = true;
        var token = _cts.Token;
        try
        {
            while (!token.IsCancellationRequested)
            {
                await ApplyNextAsync();
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, IntervalSeconds)), token);
            }
        }
        catch (OperationCanceledException) { }
        finally { IsRotating = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Stop() => _cts?.Cancel();
}
