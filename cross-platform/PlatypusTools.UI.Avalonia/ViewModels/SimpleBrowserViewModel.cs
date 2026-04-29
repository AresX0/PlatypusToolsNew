using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SimpleBrowserViewModel : ObservableObject
{
    [ObservableProperty] private string _url = "https://github.com/AresX0/PlatypusToolsNew";
    [ObservableProperty] private string _status = "Opens URLs in the OS default browser. (No embedded engine — Avalonia is renderer-only.)";

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (string.IsNullOrWhiteSpace(Url)) return;
        var url = Url.Trim();
        if (!url.StartsWith("http://") && !url.StartsWith("https://")) url = "https://" + url;
        ShellHelper.Result r;
        if (ShellHelper.IsWindows) r = await ShellHelper.RunAsync("cmd.exe", $"/c start \"\" \"{url}\"");
        else if (ShellHelper.IsMac) r = await ShellHelper.RunAsync("open", url);
        else r = await ShellHelper.RunAsync("xdg-open", url);
        Status = r.ExitCode == 0 ? "Opened in default browser" : "Failed: " + r.StdErr;
    }
}
