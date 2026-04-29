using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class MetadataEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _tag = "Author";
    [ObservableProperty] private string _value = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Requires exiftool in PATH (Linux: apt install libimage-exiftool-perl · Mac: brew install exiftool · Win: download from exiftool.org).";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task ReadAsync()
    {
        if (string.IsNullOrEmpty(Input)) { Status = "Pick a file"; return; }
        IsBusy = true;
        var r = await ShellHelper.RunAsync("exiftool", $"-a -G1 \"{Input}\"");
        Output = r.ExitCode == 0 ? r.StdOut : r.StdErr;
        Status = r.ExitCode == 0 ? "Done" : $"exiftool exit {r.ExitCode}";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task WriteAsync()
    {
        if (string.IsNullOrEmpty(Input)) { Status = "Pick a file"; return; }
        if (string.IsNullOrEmpty(Tag)) { Status = "Enter a tag"; return; }
        IsBusy = true;
        var r = await ShellHelper.RunAsync("exiftool", $"-overwrite_original -{Tag}=\"{Value}\" \"{Input}\"");
        Output = r.StdOut + r.StdErr;
        Status = r.ExitCode == 0 ? "Wrote" : $"exiftool exit {r.ExitCode}";
        IsBusy = false;
    }
}
