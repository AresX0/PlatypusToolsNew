using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class NativeAudioTrimViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _start = "00:00:00";
    [ObservableProperty] private string _end = "00:00:30";
    [ObservableProperty] private bool _reencode;
    [ObservableProperty] private string _status = "Native audio trim — uses FFmpeg directly (-ss/-to). Toggle re-encode for sample-accurate cuts.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task TrimAsync()
    {
        if (string.IsNullOrEmpty(Input) || string.IsNullOrEmpty(Output)) { Status = "Pick input + output"; return; }
        IsBusy = true;
        var args = $"-y -i \"{Input}\" -ss {Start} -to {End}" + (Reencode ? " -c:a libmp3lame -q:a 2" : " -c copy") + $" \"{Output}\"";
        var r = await ShellHelper.RunAsync(FFmpegRunner.FFmpegPath, args);
        Status = r.ExitCode == 0 ? "Done" : "Failed: " + r.StdErr.Split('\n', 2)[0];
        IsBusy = false;
    }
}

public partial class NativeImageEditViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private int _rotate;
    [ObservableProperty] private bool _flipH;
    [ObservableProperty] private bool _flipV;
    [ObservableProperty] private int _brightness;
    [ObservableProperty] private int _contrast;
    [ObservableProperty] private string _status = "Native image edit — FFmpeg filter chain (rotate / flip / eq).";
    [ObservableProperty] private bool _isBusy;

    public int[] Rotations { get; } = { 0, 90, 180, 270 };

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (string.IsNullOrEmpty(Input) || string.IsNullOrEmpty(Output)) { Status = "Pick input + output"; return; }
        IsBusy = true;
        var filters = new System.Collections.Generic.List<string>();
        if (Rotate == 90) filters.Add("transpose=1");
        else if (Rotate == 180) filters.Add("transpose=2,transpose=2");
        else if (Rotate == 270) filters.Add("transpose=2");
        if (FlipH) filters.Add("hflip");
        if (FlipV) filters.Add("vflip");
        var b = Brightness / 100.0; var c = 1 + Contrast / 100.0;
        if (Brightness != 0 || Contrast != 0) filters.Add($"eq=brightness={b:0.00}:contrast={c:0.00}");
        var vf = filters.Count == 0 ? "" : $"-vf \"{string.Join(',', filters)}\"";
        var args = $"-y -i \"{Input}\" {vf} \"{Output}\"";
        var r = await ShellHelper.RunAsync(FFmpegRunner.FFmpegPath, args);
        Status = r.ExitCode == 0 ? "Done" : "Failed: " + r.StdErr.Split('\n', 2)[0];
        IsBusy = false;
    }
}

public partial class NativeVideoPlayerViewModel : ObservableObject
{
    [ObservableProperty] private string _file = "";
    [ObservableProperty] private string _status = "Launches the OS-native player (xdg-open / open / start) — Avalonia has no embedded video.";

    [RelayCommand]
    private async Task PlayAsync()
    {
        if (string.IsNullOrEmpty(File)) return;
        ShellHelper.Result r;
        if (ShellHelper.IsWindows) r = await ShellHelper.RunAsync("cmd.exe", $"/c start \"\" \"{File}\"");
        else if (ShellHelper.IsMac) r = await ShellHelper.RunAsync("open", $"\"{File}\"");
        else r = await ShellHelper.RunAsync("xdg-open", $"\"{File}\"");
        Status = r.ExitCode == 0 ? "Launched" : "Failed";
    }

    [RelayCommand] private async Task PlayVlcAsync()
    {
        if (string.IsNullOrEmpty(File)) return;
        var r = await ShellHelper.RunAsync("vlc", $"\"{File}\"");
        Status = r.ExitCode == 0 ? "VLC launched" : "VLC not found / error";
    }

    [RelayCommand] private async Task PlayMpvAsync()
    {
        if (string.IsNullOrEmpty(File)) return;
        var r = await ShellHelper.RunAsync("mpv", $"\"{File}\"");
        Status = r.ExitCode == 0 ? "mpv launched" : "mpv not found / error";
    }
}
