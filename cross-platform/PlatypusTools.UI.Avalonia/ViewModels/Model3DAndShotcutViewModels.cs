using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class Model3DEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _status = "Launches a host-installed 3D editor.";

    [RelayCommand] private async Task BlenderAsync() => await L("blender");
    [RelayCommand] private async Task FreecadAsync() => await L("freecad");
    [RelayCommand] private async Task MeshlabAsync() => await L("meshlab");
    [RelayCommand] private async Task OpenScadAsync() => await L("openscad");

    private async Task L(string app)
    {
        var args = string.IsNullOrEmpty(Input) ? "" : $"\"{Input}\"";
        var r = await ShellHelper.RunAsync(app, args);
        Status = r.ExitCode == 0 ? $"{app} launched" : $"{app}: not found / error";
    }
}

public partial class ShotcutNativeEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _status = "Launches Shotcut (https://shotcut.org).";

    [RelayCommand]
    private async Task LaunchAsync()
    {
        var args = string.IsNullOrEmpty(Input) ? "" : $"\"{Input}\"";
        var r = await ShellHelper.RunAsync("shotcut", args);
        Status = r.ExitCode == 0 ? "Shotcut launched" : "Shotcut not found";
    }
}
