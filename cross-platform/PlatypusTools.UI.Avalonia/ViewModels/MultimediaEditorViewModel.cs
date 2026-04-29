using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class MultimediaEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _status = "Launches a non-linear multimedia editor on the host. Pick the editor that's installed.";

    [RelayCommand] private async Task OpenInShotcutAsync() => Status = await Launch("shotcut");
    [RelayCommand] private async Task OpenInKdenliveAsync() => Status = await Launch("kdenlive");
    [RelayCommand] private async Task OpenInOpenShotAsync() => Status = await Launch("openshot-qt");
    [RelayCommand] private async Task OpenInDavinciAsync() => Status = await Launch(ShellHelper.IsMac ? "/Applications/DaVinci Resolve/DaVinci Resolve.app/Contents/MacOS/Resolve" : "resolve");
    [RelayCommand] private async Task OpenInIMovieAsync() => Status = await Launch("open", "-a iMovie \"" + Input + "\"");
    [RelayCommand] private async Task OpenInAudacityAsync() => Status = await Launch("audacity");
    [RelayCommand] private async Task OpenInBlenderAsync() => Status = await Launch("blender");
    [RelayCommand] private async Task OpenInGimpAsync() => Status = await Launch("gimp");
    [RelayCommand] private async Task OpenInInkscapeAsync() => Status = await Launch("inkscape");

    private async Task<string> Launch(string app, string? args = null)
    {
        var a = args ?? (string.IsNullOrEmpty(Input) ? "" : $"\"{Input}\"");
        var r = await ShellHelper.RunAsync(app, a);
        return r.ExitCode == 0 ? $"{app} launched" : $"{app}: not found / error";
    }
}
