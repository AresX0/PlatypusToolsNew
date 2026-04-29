using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class VideoMetadataViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Pick a media file and run ffprobe.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task ProbeAsync()
    {
        var ff = FFmpegRunner.FFprobePath;
        if (ff is null) { Status = "ffprobe not found"; return; }
        if (string.IsNullOrEmpty(Input)) { Status = "Pick a file"; return; }
        IsBusy = true; Status = "Probing…"; Output = "";
        var r = await ShellHelper.RunAsync(ff, $"-v quiet -print_format json -show_format -show_streams \"{Input}\"");
        Output = r.ExitCode == 0 ? r.StdOut : r.StdErr;
        Status = r.ExitCode == 0 ? "Done" : $"ffprobe exit {r.ExitCode}";
        IsBusy = false;
    }
}
