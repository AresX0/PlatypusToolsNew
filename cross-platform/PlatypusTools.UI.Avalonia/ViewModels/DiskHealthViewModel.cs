using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class DiskHealthViewModel : ObservableObject
{
    [ObservableProperty] private string _device = ShellHelper.IsWindows ? "C:" : ShellHelper.IsMac ? "disk0" : "/dev/sda";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Wraps `smartctl`. Install: apt install smartmontools · brew install smartmontools · scoop install smartmontools.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(Device)) return;
        IsBusy = true; Output = ""; Status = "Querying SMART…";
        var r = await ShellHelper.RunAsync("smartctl", $"-a \"{Device}\"");
        Output = r.StdOut + r.StdErr;
        Status = r.ExitCode == 0 ? "OK" : $"smartctl exit {r.ExitCode} (run as root/admin?)";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ListDevicesAsync()
    {
        var r = await ShellHelper.RunAsync("smartctl", "--scan");
        Output = r.StdOut + r.StdErr;
        Status = r.ExitCode == 0 ? "Listed" : "smartctl error";
    }
}
