using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class BatchWatermarkViewModel : ObservableObject
{
    [ObservableProperty] private string _watermark = "";
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _position = "bottom-right";
    [ObservableProperty] private int _padding = 10;
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "";

    public ObservableCollection<string> Inputs { get; } = new();
    public string[] Positions { get; } = new[] { "top-left", "top-right", "bottom-left", "bottom-right", "center" };

    public void AddInput(string p) { if (!string.IsNullOrEmpty(p)) Inputs.Add(p); }
    [RelayCommand] private void Clear() => Inputs.Clear();

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Inputs.Count == 0) { Status = "Add inputs"; return; }
        if (string.IsNullOrWhiteSpace(Watermark) || !File.Exists(Watermark)) { Status = "Set watermark image"; return; }
        if (string.IsNullOrWhiteSpace(OutputFolder)) { Status = "Set output folder"; return; }
        Directory.CreateDirectory(OutputFolder);
        var ff = FFmpegRunner.FFmpegPath;
        if (ff is null) { Status = "ffmpeg not found"; return; }

        string overlay = Position switch
        {
            "top-left" => $"{Padding}:{Padding}",
            "top-right" => $"main_w-overlay_w-{Padding}:{Padding}",
            "bottom-left" => $"{Padding}:main_h-overlay_h-{Padding}",
            "center" => "(main_w-overlay_w)/2:(main_h-overlay_h)/2",
            _ => $"main_w-overlay_w-{Padding}:main_h-overlay_h-{Padding}"
        };

        Output = ""; int n = 0, fail = 0;
        foreach (var src in Inputs)
        {
            var dst = Path.Combine(OutputFolder, Path.GetFileName(src));
            var args = $"-y -i {FFmpegRunner.Q(src)} -i {FFmpegRunner.Q(Watermark)} -filter_complex \"overlay={overlay}\" {FFmpegRunner.Q(dst)}";
            var r = await ShellHelper.RunAsync(ff, args, default,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
            if (r.ExitCode == 0) n++; else fail++;
        }
        Status = $"Watermarked {n}, {fail} failed";
    }
}
