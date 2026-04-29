using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class BatchJobQueueViewModel : ObservableObject
{
    [ObservableProperty] private string _command = "";
    [ObservableProperty] private string _status = "Queue arbitrary shell commands. They run sequentially.";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<BatchJob> Jobs { get; } = new();

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(Command)) return;
        Jobs.Add(new BatchJob { Command = Command, State = "Queued" });
        Command = "";
    }

    [RelayCommand]
    private async Task RunAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        foreach (var job in Jobs)
        {
            if (job.State == "Done" || job.State == "Failed") continue;
            job.State = "Running"; job.Started = DateTime.Now;
            var shell = ShellHelper.IsWindows ? "pwsh" : "bash";
            var args = ShellHelper.IsWindows ? $"-NoProfile -Command \"{job.Command.Replace("\"", "\\\"")}\"" : $"-c \"{job.Command.Replace("\"", "\\\"")}\"";
            var r = await ShellHelper.RunAsync(shell, args);
            job.ExitCode = r.ExitCode;
            job.Output = r.StdOut + r.StdErr;
            job.State = r.ExitCode == 0 ? "Done" : "Failed";
            job.Finished = DateTime.Now;
        }
        IsBusy = false; Status = "Queue complete";
    }

    [RelayCommand] private void Clear() => Jobs.Clear();
}

public partial class BatchJob : ObservableObject
{
    [ObservableProperty] private string _command = "";
    [ObservableProperty] private string _state = "Queued";
    [ObservableProperty] private int _exitCode;
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private DateTime? _started;
    [ObservableProperty] private DateTime? _finished;
}
