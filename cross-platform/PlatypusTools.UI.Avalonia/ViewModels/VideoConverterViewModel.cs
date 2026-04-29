using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class VideoConverterViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _preset = "MP4 (H.264 AAC)";
    [ObservableProperty] private string _log = "";
    [ObservableProperty] private bool _isRunning;
    public string FFmpegStatus => FFmpegRunner.IsAvailable ? $"ffmpeg: {FFmpegRunner.FFmpegPath}" : "ffmpeg NOT FOUND in PATH";

    public string[] Presets { get; } =
    {
        "MP4 (H.264 AAC)",
        "MKV (H.265 AAC)",
        "WebM (VP9 Opus)",
        "MP3 audio only",
        "WAV audio only"
    };

    [RelayCommand]
    public async Task ConvertAsync()
    {
        if (IsRunning) return;
        if (!File.Exists(Input)) { Log = "Input file not found."; return; }
        if (string.IsNullOrEmpty(Output))
        {
            var ext = Preset switch
            {
                "MKV (H.265 AAC)" => ".mkv",
                "WebM (VP9 Opus)" => ".webm",
                "MP3 audio only" => ".mp3",
                "WAV audio only" => ".wav",
                _ => ".mp4"
            };
            Output = Path.Combine(Path.GetDirectoryName(Input)!,
                Path.GetFileNameWithoutExtension(Input) + "_converted" + ext);
        }
        var args = Preset switch
        {
            "MKV (H.265 AAC)"  => $"-y -i {FFmpegRunner.Q(Input)} -c:v libx265 -crf 23 -c:a aac -b:a 192k {FFmpegRunner.Q(Output)}",
            "WebM (VP9 Opus)"  => $"-y -i {FFmpegRunner.Q(Input)} -c:v libvpx-vp9 -crf 32 -b:v 0 -c:a libopus {FFmpegRunner.Q(Output)}",
            "MP3 audio only"   => $"-y -i {FFmpegRunner.Q(Input)} -vn -c:a libmp3lame -b:a 192k {FFmpegRunner.Q(Output)}",
            "WAV audio only"   => $"-y -i {FFmpegRunner.Q(Input)} -vn -c:a pcm_s16le {FFmpegRunner.Q(Output)}",
            _                  => $"-y -i {FFmpegRunner.Q(Input)} -c:v libx264 -crf 23 -preset medium -c:a aac -b:a 192k {FFmpegRunner.Q(Output)}"
        };
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
