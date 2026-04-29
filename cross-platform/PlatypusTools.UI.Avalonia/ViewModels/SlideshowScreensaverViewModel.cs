using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;
using PlatypusTools.UI.Avalonia.Views;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SlideshowScreensaverViewModel : ObservableObject
{
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private int _intervalSeconds = 5;
    [ObservableProperty] private bool _shuffle = true;
    [ObservableProperty] private string _status = "Pick an image folder, click Start. Window goes fullscreen — Esc / click closes.";

    public SlideshowScreensaverViewModel()
    {
        var s = AppSettings.Load();
        Folder = s.ScreensaverFolder ?? "";
        IntervalSeconds = s.ScreensaverInterval > 0 ? s.ScreensaverInterval : 5;
        Shuffle = s.ScreensaverShuffle;
    }

    [RelayCommand]
    private void Start()
    {
        if (string.IsNullOrEmpty(Folder) || !Directory.Exists(Folder)) { Status = "Pick a folder"; return; }
        var exts = new[] { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp" };
        var files = Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
                             .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
                             .ToList();
        if (files.Count == 0) { Status = "No images"; return; }
        if (Shuffle) { var rnd = new System.Random(); files = files.OrderBy(_ => rnd.Next()).ToList(); }
        var w = new SlideshowWindow(files, IntervalSeconds);
        w.Show();
        Status = $"Started: {files.Count} images";
    }
}
