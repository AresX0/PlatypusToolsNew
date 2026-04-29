using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class CredentialManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _service = "platypus.example";
    [ObservableProperty] private string _account = "";
    [ObservableProperty] private string _secret = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status =
        ShellHelper.IsWindows ? "Wraps `cmdkey` (Windows Credential Manager)." :
        ShellHelper.IsMac ? "Wraps `security` (macOS Keychain)." :
        "Wraps `secret-tool` (libsecret).";

    [RelayCommand]
    private async Task StoreAsync()
    {
        ShellHelper.Result r;
        if (ShellHelper.IsWindows)
            r = await ShellHelper.RunAsync("cmdkey.exe", $"/generic:\"{Service}\" /user:\"{Account}\" /pass:\"{Secret}\"");
        else if (ShellHelper.IsMac)
            r = await ShellHelper.RunAsync("security", $"add-generic-password -a \"{Account}\" -s \"{Service}\" -w \"{Secret}\" -U");
        else
            r = await ShellHelper.RunAsync("/bin/sh", $"-c \"echo -n '{Secret}' | secret-tool store --label='{Service}' service '{Service}' account '{Account}'\"");
        Output = r.StdOut + r.StdErr;
        Status = r.ExitCode == 0 ? "Stored" : $"exit {r.ExitCode}";
    }

    [RelayCommand]
    private async Task ReadAsync()
    {
        ShellHelper.Result r;
        if (ShellHelper.IsWindows)
            r = await ShellHelper.RunAsync("cmdkey.exe", $"/list:\"{Service}\"");
        else if (ShellHelper.IsMac)
            r = await ShellHelper.RunAsync("security", $"find-generic-password -a \"{Account}\" -s \"{Service}\" -w");
        else
            r = await ShellHelper.RunAsync("secret-tool", $"lookup service \"{Service}\" account \"{Account}\"");
        Output = r.StdOut + r.StdErr;
        Status = r.ExitCode == 0 ? "Read" : $"exit {r.ExitCode}";
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        ShellHelper.Result r;
        if (ShellHelper.IsWindows)
            r = await ShellHelper.RunAsync("cmdkey.exe", $"/delete:\"{Service}\"");
        else if (ShellHelper.IsMac)
            r = await ShellHelper.RunAsync("security", $"delete-generic-password -a \"{Account}\" -s \"{Service}\"");
        else
            r = await ShellHelper.RunAsync("secret-tool", $"clear service \"{Service}\" account \"{Account}\"");
        Output = r.StdOut + r.StdErr;
        Status = r.ExitCode == 0 ? "Deleted" : $"exit {r.ExitCode}";
    }
}
