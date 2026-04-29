using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class NetworkTrafficViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Lists active TCP/UDP connections via OS-native tools.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true; Status = "Querying…";
        ShellHelper.Result r;
        if (ShellHelper.IsWindows)
            r = await ShellHelper.RunAsync("netstat", "-ano");
        else if (ShellHelper.IsMac)
            r = await ShellHelper.RunAsync("/bin/sh", "-c \"netstat -anv -p tcp; echo; netstat -anv -p udp\"");
        else
        {
            r = await ShellHelper.RunAsync("/bin/sh", "-c \"ss -tunap 2>/dev/null || netstat -tunap\"");
        }
        Output = r.ExitCode == 0 ? r.StdOut : (r.StdOut + "\n" + r.StdErr);
        Status = $"Done (exit {r.ExitCode})";
        IsBusy = false;
    }
}
