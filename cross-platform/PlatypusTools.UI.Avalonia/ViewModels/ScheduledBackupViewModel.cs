using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ScheduledBackupViewModel : ObservableObject
{
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _destDir = "";
    [ObservableProperty] private string _archiveBase = "backup";
    [ObservableProperty] private string _status = "Creates a zip archive named <base>-yyyyMMdd-HHmmss.zip in the destination folder.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (string.IsNullOrEmpty(Source) || string.IsNullOrEmpty(DestDir)) { Status = "Pick source + destination"; return; }
        IsBusy = true; Status = "Archiving…";
        try
        {
            Directory.CreateDirectory(DestDir);
            var name = $"{ArchiveBase}-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
            var dest = Path.Combine(DestDir, name);
            await Task.Run(() => System.IO.Compression.ZipFile.CreateFromDirectory(Source, dest, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false));
            Status = "Created " + dest;
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
        IsBusy = false;
    }

    [RelayCommand]
    private async Task ScheduleAsync()
    {
        // Best-effort: install a daily cron / scheduled task that runs THIS app with no args plus a marker file.
        if (ShellHelper.IsWindows)
        {
            var exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
            var r = await ShellHelper.RunAsync("schtasks.exe", $"/Create /SC DAILY /TN \"PlatypusBackup_{ArchiveBase}\" /TR \"\\\"{exe}\\\"\" /ST 02:00 /F");
            Status = r.ExitCode == 0 ? "Scheduled (Task Scheduler 02:00 daily)" : "Failed: " + r.StdErr;
        }
        else
        {
            var line = $"0 2 * * * /bin/sh -c 'cd \"{Source}\" && zip -r \"{DestDir}/{ArchiveBase}-$(date +\\%Y\\%m\\%d-\\%H\\%M\\%S).zip\" .'";
            var r = await ShellHelper.RunAsync("/bin/sh", $"-c \"(crontab -l 2>/dev/null; echo \\\"{line}\\\") | crontab -\"");
            Status = r.ExitCode == 0 ? "Cron entry added (02:00 daily). Run `crontab -l` to verify." : "Failed: " + r.StdErr;
        }
    }
}
