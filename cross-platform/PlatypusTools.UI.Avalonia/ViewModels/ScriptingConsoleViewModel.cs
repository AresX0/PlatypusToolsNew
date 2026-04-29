using System;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ScriptingConsoleViewModel : ObservableObject
{
    [ObservableProperty] private string _shell = ShellHelper.IsWindows ? "powershell" : "bash";
    [ObservableProperty] private string _script = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isRunning;

    private CancellationTokenSource? _cts;

    public string[] Shells => ShellHelper.IsWindows
        ? new[] { "powershell", "pwsh", "cmd" }
        : new[] { "bash", "sh", "zsh", "pwsh", "python3", "ruby" };

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsRunning) return;
        if (string.IsNullOrWhiteSpace(Script)) { Status = "Enter a script"; return; }
        Output = "";
        IsRunning = true;
        _cts = new CancellationTokenSource();
        try
        {
            string file, args;
            switch (Shell)
            {
                case "powershell":
                case "pwsh":
                    file = Shell;
                    args = $"-NoProfile -Command \"{Script.Replace("\"", "`\"")}\"";
                    break;
                case "cmd":
                    file = "cmd.exe";
                    args = $"/c {Script}";
                    break;
                case "python3":
                    file = "python3";
                    args = $"-c \"{Script.Replace("\"", "\\\"")}\"";
                    break;
                case "ruby":
                    file = "ruby";
                    args = $"-e \"{Script.Replace("\"", "\\\"")}\"";
                    break;
                default:
                    file = Shell;
                    args = $"-c \"{Script.Replace("\"", "\\\"")}\"";
                    break;
            }
            var r = await ShellHelper.RunAsync(file, args, _cts.Token,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
            if (!string.IsNullOrEmpty(r.StdErr))
                Output += "\n[stderr]\n" + r.StdErr;
            Status = $"Exit {r.ExitCode}";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally { IsRunning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();
    [RelayCommand] private void Clear() => Output = "";
}
