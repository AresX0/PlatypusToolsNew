using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class FileSyncViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _destination = "";
    [ObservableProperty] private bool _mirror;
    [ObservableProperty] private bool _dryRun;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status =
        ShellHelper.IsWindows ? "Wraps robocopy. Mirror = /MIR." : "Wraps rsync -avh. Mirror adds --delete.";

    [RelayCommand]
    private async Task RunAsync()
    {
        if (string.IsNullOrEmpty(Source) || string.IsNullOrEmpty(Destination)) { Status = "Pick source and destination"; return; }
        Directory.CreateDirectory(Destination);
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        IsBusy = true; Output = "";
        ShellHelper.Result r;
        if (ShellHelper.IsWindows)
        {
            var args = $"\"{Source.TrimEnd('\\')}\" \"{Destination.TrimEnd('\\')}\" /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NP";
            if (Mirror) args = args.Replace("/E", "/MIR");
            if (DryRun) args += " /L";
            r = await ShellHelper.RunAsync("robocopy.exe", args, _cts.Token, line => Output += line + "\n");
        }
        else
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("-avh ");
            if (Mirror) sb.Append("--delete ");
            if (DryRun) sb.Append("--dry-run ");
            sb.Append('"').Append(Source.TrimEnd('/')).Append('/').Append("\" \"").Append(Destination).Append('"');
            r = await ShellHelper.RunAsync("rsync", sb.ToString(), _cts.Token, line => Output += line + "\n");
        }
        Status = $"exit {r.ExitCode}";
        IsBusy = false;
    }

    [RelayCommand] private void Cancel() { _cts?.Cancel(); Status = "Cancelled"; }
}
