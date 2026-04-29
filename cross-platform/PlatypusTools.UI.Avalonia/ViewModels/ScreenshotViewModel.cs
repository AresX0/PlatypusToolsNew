using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ScreenshotViewModel : ObservableObject
{
    [ObservableProperty] private string _outputPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
    [ObservableProperty] private int _delaySeconds;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _output = "";

    [RelayCommand]
    private async Task CaptureAsync()
    {
        if (DelaySeconds > 0) { Status = $"Waiting {DelaySeconds}s…"; await Task.Delay(DelaySeconds * 1000); }
        var ff = FFmpegRunner.FFmpegPath;
        if (ff is null) { Status = "ffmpeg not found (install ffmpeg)"; return; }
        var p = NextPath();
        string args;
        if (ShellHelper.IsWindows)
            args = $"-y -f gdigrab -framerate 1 -i desktop -frames:v 1 {FFmpegRunner.Q(p)}";
        else if (ShellHelper.IsMac)
            args = $"-y -f avfoundation -framerate 1 -i \"1\" -frames:v 1 {FFmpegRunner.Q(p)}";
        else
            args = $"-y -f x11grab -framerate 1 -i :0.0 -frames:v 1 {FFmpegRunner.Q(p)}";
        Output = $"$ ffmpeg {args}\n";
        var r = await ShellHelper.RunAsync(ff, args, default,
            line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
        Status = r.ExitCode == 0 ? $"Saved {p}" : $"ffmpeg exit {r.ExitCode}";
        if (r.ExitCode == 0) OutputPath = p;
    }

    private string NextPath()
    {
        var dir = Path.GetDirectoryName(OutputPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        return Path.Combine(dir, $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png");
    }
}
