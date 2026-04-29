using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class BatchUpscaleViewModel : ObservableObject
{
    [ObservableProperty] private string _inputDir = "";
    [ObservableProperty] private string _outputDir = "";
    [ObservableProperty] private int _scale = 2;
    [ObservableProperty] private string _algorithm = "lanczos";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Uses ffmpeg scale filter. Quality < ESRGAN but free, fast, dependency-light.";
    public ObservableCollection<string> Done { get; } = new();
    public string[] Algorithms { get; } = { "lanczos", "bicubic", "bilinear", "neighbor" };
    public int[] Scales { get; } = { 2, 3, 4 };

    [RelayCommand]
    private async Task RunAsync()
    {
        var ff = FFmpegRunner.FFmpegPath;
        if (ff is null) { Status = "ffmpeg not found"; return; }
        if (!Directory.Exists(InputDir)) { Status = "Input dir missing"; return; }
        Directory.CreateDirectory(OutputDir);
        IsBusy = true; Done.Clear();
        var exts = new[] { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff" };
        foreach (var f in Directory.EnumerateFiles(InputDir))
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            if (System.Array.IndexOf(exts, ext) < 0) continue;
            var outPath = Path.Combine(OutputDir, Path.GetFileNameWithoutExtension(f) + $"_x{Scale}" + ext);
            var args = $"-y -i \"{f}\" -vf scale=iw*{Scale}:ih*{Scale}:flags={Algorithm} \"{outPath}\"";
            var r = await ShellHelper.RunAsync(ff, args);
            Done.Add($"{(r.ExitCode == 0 ? "✓" : "✗")} {Path.GetFileName(outPath)}");
            Status = $"{Done.Count} processed";
        }
        IsBusy = false;
        Status = $"Done: {Done.Count}";
    }
}
