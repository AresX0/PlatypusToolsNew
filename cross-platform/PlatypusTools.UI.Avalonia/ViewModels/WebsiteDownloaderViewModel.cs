using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class WebsiteDownloaderViewModel : ObservableObject
{
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _outDir = "";
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private string _status = "Uses curl (default) or wget. Toggle Recursive for full-site mirror (wget only).";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _log = "";

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (string.IsNullOrEmpty(Url)) { Status = "Enter a URL"; return; }
        if (string.IsNullOrEmpty(OutDir)) { Status = "Pick output dir"; return; }
        Directory.CreateDirectory(OutDir);
        IsBusy = true; Log = ""; Status = "Downloading…";

        string tool, args;
        if (Recursive)
        {
            tool = ShellHelper.IsWindows ? "wget.exe" : "wget";
            args = $"-r -np -E -k -p -P \"{OutDir}\" \"{Url}\"";
        }
        else
        {
            tool = ShellHelper.IsWindows ? "curl.exe" : "curl";
            var name = Path.GetFileName(new System.Uri(Url).LocalPath);
            if (string.IsNullOrEmpty(name)) name = "download.bin";
            args = $"-L -o \"{Path.Combine(OutDir, name)}\" \"{Url}\"";
        }

        var sb = new System.Text.StringBuilder();
        var r = await ShellHelper.RunAsync(tool, args, default, line => { sb.AppendLine(line); Log = sb.ToString(); });
        Log += "\n" + r.StdErr;
        Status = r.ExitCode == 0 ? "Done" : $"{tool} exit {r.ExitCode}";
        IsBusy = false;
    }
}
