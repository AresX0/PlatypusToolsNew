using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ImageConverterViewModel : ObservableObject
{
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _format = "png";
    [ObservableProperty] private int _quality = 90;
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isRunning;

    public ObservableCollection<string> Inputs { get; } = new();
    public string[] Formats { get; } = new[] { "png", "jpg", "webp", "bmp", "tiff", "gif" };

    public void AddInput(string p) { if (!string.IsNullOrEmpty(p)) Inputs.Add(p); }
    [RelayCommand] private void Clear() => Inputs.Clear();

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (Inputs.Count == 0) { Status = "Add inputs"; return; }
        if (string.IsNullOrWhiteSpace(OutputFolder) || !Directory.Exists(OutputFolder))
        { Status = "Set output folder"; return; }
        var ff = FFmpegRunner.FFmpegPath;
        if (ff is null) { Status = "ffmpeg not found"; return; }

        IsRunning = true; Output = ""; int n = 0, fail = 0;
        foreach (var src in Inputs)
        {
            var dst = Path.Combine(OutputFolder, Path.GetFileNameWithoutExtension(src) + "." + Format);
            string qArg = Format switch
            {
                "jpg" => $"-q:v {Math.Clamp(31 - Quality / 4, 2, 31)}",
                "webp" => $"-quality {Quality}",
                _ => ""
            };
            var args = $"-y -i {FFmpegRunner.Q(src)} {qArg} {FFmpegRunner.Q(dst)}";
            var r = await ShellHelper.RunAsync(ff, args, default,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
            if (r.ExitCode == 0) n++; else fail++;
        }
        Status = $"Converted {n}, {fail} failed";
        IsRunning = false;
    }
}
