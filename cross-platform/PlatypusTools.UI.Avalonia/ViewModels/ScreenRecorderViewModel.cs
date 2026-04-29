using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ScreenRecorderViewModel : ObservableObject
{
    [ObservableProperty] private string _outputPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), $"screen_{DateTime.Now:yyyyMMdd_HHmmss}.mp4");
    [ObservableProperty] private int _fps = 30;
    [ObservableProperty] private int _seconds = 30;
    [ObservableProperty] private string _status = "Idle";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _isRecording;

    private CancellationTokenSource? _cts;

    [RelayCommand]
    private async Task RecordAsync()
    {
        if (IsRecording) return;
        var ff = FFmpegRunner.FFmpegPath;
        if (ff is null) { Status = "ffmpeg not found"; return; }
        IsRecording = true;
        Output = "";
        _cts = new CancellationTokenSource();
        try
        {
            string args;
            if (ShellHelper.IsWindows)
                args = $"-y -f gdigrab -framerate {Fps} -i desktop -t {Seconds} -c:v libx264 -preset veryfast {FFmpegRunner.Q(OutputPath)}";
            else if (ShellHelper.IsMac)
                args = $"-y -f avfoundation -framerate {Fps} -i \"1:none\" -t {Seconds} -c:v libx264 -preset veryfast {FFmpegRunner.Q(OutputPath)}";
            else
                args = $"-y -f x11grab -framerate {Fps} -i :0.0 -t {Seconds} -c:v libx264 -preset veryfast {FFmpegRunner.Q(OutputPath)}";
            Status = $"Recording to {OutputPath}…";
            var r = await ShellHelper.RunAsync(ff, args, _cts.Token,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
            Status = r.ExitCode == 0 ? $"Saved {OutputPath}" : $"ffmpeg exit {r.ExitCode}";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally { IsRecording = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Stop() => _cts?.Cancel();
}
