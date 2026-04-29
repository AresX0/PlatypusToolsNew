using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class YaraScannerViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _rulesPath = "";
    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private bool _recursive = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Wraps `yara`. Install: apt install yara · brew install yara · scoop install yara.";

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(RulesPath) || string.IsNullOrEmpty(TargetPath)) { Status = "Pick rules + target"; return; }
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        IsBusy = true; Output = ""; Status = "Scanning…";
        var args = (Recursive ? "-r " : "") + $"\"{RulesPath}\" \"{TargetPath}\"";
        var r = await ShellHelper.RunAsync("yara", args, _cts.Token, line => Output += line + "\n");
        Status = $"yara exit {r.ExitCode}";
        IsBusy = false;
    }

    [RelayCommand] private void Cancel() { _cts?.Cancel(); Status = "Cancelled"; }
}
