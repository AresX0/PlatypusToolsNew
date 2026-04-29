using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class TerminalClientViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _shell;
    [ObservableProperty] private string _command = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Pick a shell, type a command, hit Run.";
    [ObservableProperty] private bool _isBusy;

    public string[] Shells { get; }

    public TerminalClientViewModel()
    {
        if (ShellHelper.IsWindows) { Shells = new[] { "pwsh", "powershell", "cmd" }; _shell = "pwsh"; }
        else if (ShellHelper.IsMac) { Shells = new[] { "zsh", "bash", "sh" }; _shell = "zsh"; }
        else { Shells = new[] { "bash", "sh", "zsh" }; _shell = "bash"; }
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (string.IsNullOrEmpty(Command)) return;
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        IsBusy = true; Status = "Running…";
        Output += $"$ {Command}\n";
        var args = Shell == "cmd" ? $"/c {Command}" : $"-c \"{Command.Replace("\"", "\\\"")}\"";
        var r = await ShellHelper.RunAsync(Shell, args, _cts.Token, line => Output += line + "\n");
        if (!string.IsNullOrEmpty(r.StdErr)) Output += r.StdErr + "\n";
        Status = $"exit {r.ExitCode}";
        IsBusy = false;
    }

    [RelayCommand]
    private void Cancel() { _cts?.Cancel(); Status = "Cancelled"; }

    [RelayCommand]
    private void ClearOutput() { Output = ""; Status = "Cleared"; }
}
