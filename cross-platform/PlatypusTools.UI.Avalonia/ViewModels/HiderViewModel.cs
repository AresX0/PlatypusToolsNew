using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class HiderViewModel : ObservableObject
{
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _status = "Pick file/folder, Hide or Show. Linux/Mac uses leading dot. Windows uses +H attribute.";
    public ObservableCollection<string> Recents { get; } = new();

    [RelayCommand]
    private async Task HideAsync()
    {
        if (string.IsNullOrEmpty(Path)) return;
        if (ShellHelper.IsWindows)
        {
            var r = await ShellHelper.RunAsync("attrib.exe", $"+H \"{Path}\"");
            Status = r.ExitCode == 0 ? "Hidden (attrib +H)" : "Failed: " + r.StdErr;
        }
        else
        {
            var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
            var name = System.IO.Path.GetFileName(Path);
            if (name.StartsWith('.')) { Status = "Already hidden"; return; }
            var newPath = System.IO.Path.Combine(dir, "." + name);
            try
            {
                if (Directory.Exists(Path)) Directory.Move(Path, newPath);
                else File.Move(Path, newPath);
                Status = "Hidden → " + newPath; Path = newPath;
            }
            catch (System.Exception ex) { Status = "Failed: " + ex.Message; }
        }
        if (!Recents.Contains(Path)) Recents.Insert(0, Path);
    }

    [RelayCommand]
    private async Task ShowAsync()
    {
        if (string.IsNullOrEmpty(Path)) return;
        if (ShellHelper.IsWindows)
        {
            var r = await ShellHelper.RunAsync("attrib.exe", $"-H \"{Path}\"");
            Status = r.ExitCode == 0 ? "Visible (attrib -H)" : "Failed: " + r.StdErr;
        }
        else
        {
            var dir = System.IO.Path.GetDirectoryName(Path) ?? "";
            var name = System.IO.Path.GetFileName(Path);
            if (!name.StartsWith('.')) { Status = "Already visible"; return; }
            var newPath = System.IO.Path.Combine(dir, name.TrimStart('.'));
            try
            {
                if (Directory.Exists(Path)) Directory.Move(Path, newPath);
                else File.Move(Path, newPath);
                Status = "Visible → " + newPath; Path = newPath;
            }
            catch (System.Exception ex) { Status = "Failed: " + ex.Message; }
        }
    }
}
