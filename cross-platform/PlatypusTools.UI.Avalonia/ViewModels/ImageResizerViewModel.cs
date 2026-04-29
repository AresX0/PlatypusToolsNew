using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ImageResizerViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private int _width = 1280;
    [ObservableProperty] private int _height = -1; // -1 = auto preserve aspect
    [ObservableProperty] private string _format = "jpg";
    [ObservableProperty] private string _log = "";
    [ObservableProperty] private bool _isRunning;
    public string[] Formats { get; } = { "jpg", "png", "webp", "bmp" };
    public string FFmpegStatus => FFmpegRunner.IsAvailable ? "ffmpeg ready" : "ffmpeg NOT FOUND";

    [RelayCommand]
    public async Task ResizeAsync()
    {
        if (IsRunning || !File.Exists(Input)) { Log = "Pick an input image first."; return; }
        if (string.IsNullOrEmpty(Output))
            Output = Path.Combine(Path.GetDirectoryName(Input)!,
                Path.GetFileNameWithoutExtension(Input) + $"_{Width}x{Height}." + Format);

        var hp = Height <= 0 ? "-1" : Height.ToString();
        var vf = $"scale={Width}:{hp}:flags=lanczos";
        var args = $"-y -i {FFmpegRunner.Q(Input)} -vf \"{vf}\" {FFmpegRunner.Q(Output)}";
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
