using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class CloudSyncViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _localPath = "";
    [ObservableProperty] private string _remoteName = "remote";
    [ObservableProperty] private string _remotePath = "";
    [ObservableProperty] private string _direction = "Push (local → remote)";
    [ObservableProperty] private bool _dryRun = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Wraps `rclone`. Configure remotes with `rclone config` first.";

    public string[] Directions { get; } = { "Push (local → remote)", "Pull (remote → local)", "Bisync" };

    [RelayCommand]
    private async Task RunAsync()
    {
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        IsBusy = true; Output = "";
        var verb = Direction.StartsWith("Pull") ? "sync" : Direction.StartsWith("Bisync") ? "bisync" : "sync";
        var src = Direction.StartsWith("Pull") ? $"{RemoteName}:{RemotePath}" : LocalPath;
        var dst = Direction.StartsWith("Pull") ? LocalPath : $"{RemoteName}:{RemotePath}";
        if (verb == "bisync") { src = LocalPath; dst = $"{RemoteName}:{RemotePath}"; }
        var args = $"{verb} \"{src}\" \"{dst}\" -v --progress" + (DryRun ? " --dry-run" : "");
        var r = await ShellHelper.RunAsync("rclone", args, _cts.Token, line => Output += line + "\n");
        Status = $"rclone exit {r.ExitCode}";
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ListRemotesAsync()
    {
        var r = await ShellHelper.RunAsync("rclone", "listremotes");
        Output = r.StdOut + r.StdErr;
        Status = r.ExitCode == 0 ? "OK" : "rclone not found / error";
    }

    [RelayCommand] private void Cancel() { _cts?.Cancel(); Status = "Cancelled"; }
}
