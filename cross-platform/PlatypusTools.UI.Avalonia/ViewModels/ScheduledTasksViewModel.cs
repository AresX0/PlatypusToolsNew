using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ScheduledTasksViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Lists scheduled tasks via OS-native scheduler tools.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true; Status = "Querying…";
        ShellHelper.Result r;
        if (ShellHelper.IsWindows)
            r = await ShellHelper.RunAsync("schtasks.exe", "/query /fo LIST /v");
        else if (ShellHelper.IsMac)
            r = await ShellHelper.RunAsync("/bin/sh", "-c \"echo '=== launchctl list ==='; launchctl list; echo; echo '=== crontab -l ==='; crontab -l 2>/dev/null || echo '(no crontab)'\"");
        else
            r = await ShellHelper.RunAsync("/bin/sh", "-c \"echo '=== systemctl list-timers ==='; systemctl list-timers --all 2>/dev/null; echo; echo '=== crontab -l ==='; crontab -l 2>/dev/null || echo '(no crontab)'; echo; echo '=== /etc/cron.d ==='; ls -la /etc/cron.d 2>/dev/null\"");
        Output = r.StdOut + (string.IsNullOrEmpty(r.StdErr) ? "" : "\n" + r.StdErr);
        Status = $"Done (exit {r.ExitCode})";
        IsBusy = false;
    }
}
