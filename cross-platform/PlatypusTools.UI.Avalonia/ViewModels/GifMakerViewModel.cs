using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class GifMakerViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private double _start = 0;
    [ObservableProperty] private double _duration = 5;
    [ObservableProperty] private int _fps = 15;
    [ObservableProperty] private int _width = 480;
    [ObservableProperty] private string _log = "";
    [ObservableProperty] private bool _isRunning;
    public string FFmpegStatus => FFmpegRunner.IsAvailable ? "ffmpeg ready" : "ffmpeg NOT FOUND";

    [RelayCommand]
    public async Task MakeAsync()
    {
        if (IsRunning || !File.Exists(Input)) { Log = "Pick an input video first."; return; }
        if (string.IsNullOrEmpty(Output))
            Output = Path.Combine(Path.GetDirectoryName(Input)!, Path.GetFileNameWithoutExtension(Input) + ".gif");

        var vf = $"fps={Fps},scale={Width}:-1:flags=lanczos";
        var args = $"-y -ss {Start} -t {Duration} -i {FFmpegRunner.Q(Input)} -vf \"{vf}\" -loop 0 {FFmpegRunner.Q(Output)}";
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
