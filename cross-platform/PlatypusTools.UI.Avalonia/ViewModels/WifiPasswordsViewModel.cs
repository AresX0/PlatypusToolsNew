using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

/// <summary>Lists saved Wi-Fi network passwords using OS keyring/profile facilities.</summary>
public partial class WifiPasswordsViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _isRunning;
    public string PlatformInfo => $"Platform: {ShellHelper.PlatformName}";

    [RelayCommand]
    public async Task ListAsync()
    {
        if (IsRunning) return;
        IsRunning = true;
        Output = "";
        try
        {
            if (ShellHelper.IsLinux)
            {
                Output += "$ nmcli -t -f NAME,TYPE connection show\n";
                var r = await ShellHelper.RunAsync("nmcli", "-t -f NAME,TYPE connection show");
                Output += r.StdOut + "\n";
                Output += "Tip: 'sudo cat /etc/NetworkManager/system-connections/<NAME>.nmconnection' shows the PSK.\n";
            }
            else if (ShellHelper.IsMac)
            {
                Output += "$ networksetup -listallhardwareports\n";
                var r = await ShellHelper.RunAsync("networksetup", "-listallhardwareports");
                Output += r.StdOut + "\n\n";
                Output += "To retrieve a saved password (will prompt Keychain):\n  security find-generic-password -wga <SSID>\n";
            }
            else if (ShellHelper.IsWindows)
            {
                Output += "$ netsh wlan show profiles\n";
                var r = await ShellHelper.RunAsync("netsh", "wlan show profiles");
                Output += r.StdOut + "\n";
                Output += "To get a password:\n  netsh wlan show profile name=\"<SSID>\" key=clear\n";
            }
        }
        finally { IsRunning = false; }
    }
}
