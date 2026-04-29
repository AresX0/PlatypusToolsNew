using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class AudioTrimViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private double _start = 0;
    [ObservableProperty] private double _duration = 30;
    [ObservableProperty] private bool _reencode;
    [ObservableProperty] private string _log = "";
    [ObservableProperty] private bool _isRunning;
    public string FFmpegStatus => FFmpegRunner.IsAvailable ? "ffmpeg ready" : "ffmpeg NOT FOUND";

    [RelayCommand]
    public async Task TrimAsync()
    {
        if (IsRunning || !File.Exists(Input)) { Log = "Pick an input file first."; return; }
        if (string.IsNullOrEmpty(Output))
            Output = Path.Combine(Path.GetDirectoryName(Input)!,
                Path.GetFileNameWithoutExtension(Input) + "_trim" + Path.GetExtension(Input));

        var copy = Reencode ? "" : "-c copy";
        var args = $"-y -ss {Start} -t {Duration} -i {FFmpegRunner.Q(Input)} {copy} {FFmpegRunner.Q(Output)}";
        Log = $"$ ffmpeg {args}\n\n";
        IsRunning = true;
        _cts = new CancellationTokenSource();
        try
        {
            var r = await FFmpegRunner.RunAsync(args, _cts.Token,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Log += line + "\n"));
            Log += $"\n[exit {r.ExitCode}] {r.StdErr}";
        }
        finally { IsRunning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();
}
