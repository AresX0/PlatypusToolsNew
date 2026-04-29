using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class RemoteDesktopViewModel : ObservableObject
{
    [ObservableProperty] private string _host = "host.example.com";
    [ObservableProperty] private string _user = "";
    [ObservableProperty] private string _protocol = ShellHelper.IsWindows ? "RDP (mstsc)" : "VNC (vncviewer)";
    [ObservableProperty] private string _status = "Launches the OS-native remote-desktop client.";

    public string[] Protocols { get; } = ShellHelper.IsWindows
        ? new[] { "RDP (mstsc)", "SSH (ssh)", "VNC (vncviewer)" }
        : new[] { "SSH (ssh)", "VNC (vncviewer)", "RDP (xfreerdp / rdesktop)" };

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (string.IsNullOrEmpty(Host)) { Status = "Enter a host"; return; }
        ShellHelper.Result r;
        if (Protocol.StartsWith("SSH"))
            r = await ShellHelper.RunAsync("ssh", string.IsNullOrEmpty(User) ? Host : $"{User}@{Host}");
        else if (Protocol.StartsWith("VNC"))
            r = await ShellHelper.RunAsync("vncviewer", Host);
        else if (Protocol.StartsWith("RDP (mstsc)"))
            r = await ShellHelper.RunAsync("mstsc.exe", $"/v:{Host}");
        else
            r = await ShellHelper.RunAsync("xfreerdp", string.IsNullOrEmpty(User) ? $"/v:{Host}" : $"/u:{User} /v:{Host}");
        Status = r.ExitCode == 0 ? "Launched" : $"exit {r.ExitCode}: {r.StdErr.Split('\n', 2)[0]}";
    }
}
