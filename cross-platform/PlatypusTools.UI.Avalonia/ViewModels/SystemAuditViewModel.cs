using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SystemAuditViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Collects basic system information.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task CollectAsync()
    {
        IsBusy = true; Status = "Collecting…";
        var sb = new StringBuilder();
        sb.AppendLine("=== System Information ===");
        sb.AppendLine($"OS:           {RuntimeInformation.OSDescription}");
        sb.AppendLine($"Platform:     {ShellHelper.PlatformName}");
        sb.AppendLine($"Architecture: {RuntimeInformation.ProcessArchitecture}");
        sb.AppendLine($"Runtime:      {RuntimeInformation.FrameworkDescription}");
        sb.AppendLine($"User:         {Environment.UserName}");
        sb.AppendLine($"Machine:      {Environment.MachineName}");
        sb.AppendLine($"Cores:        {Environment.ProcessorCount}");
        sb.AppendLine($"Working Dir:  {Environment.CurrentDirectory}");
        sb.AppendLine();
        sb.AppendLine("=== Drives ===");
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                sb.AppendLine($"  {d.Name}  {d.DriveType}  {d.DriveFormat}  free {Format(d.AvailableFreeSpace)} / {Format(d.TotalSize)}");
            }
            catch { }
        }
        sb.AppendLine();
        if (ShellHelper.IsLinux)
        {
            var u = await ShellHelper.RunAsync("/bin/sh", "-c \"uname -a; echo; cat /etc/os-release 2>/dev/null; echo; free -h 2>/dev/null; echo; lscpu 2>/dev/null | head -20\"");
            sb.AppendLine("=== uname / os-release / free / lscpu ===");
            sb.Append(u.StdOut);
        }
        else if (ShellHelper.IsMac)
        {
            var u = await ShellHelper.RunAsync("/bin/sh", "-c \"uname -a; echo; sw_vers; echo; sysctl -n machdep.cpu.brand_string; echo; vm_stat | head\"");
            sb.AppendLine("=== uname / sw_vers / cpu / vm_stat ===");
            sb.Append(u.StdOut);
        }
        else if (ShellHelper.IsWindows)
        {
            var u = await ShellHelper.RunAsync("cmd.exe", "/c \"systeminfo | findstr /B /C:\"OS Name\" /C:\"OS Version\" /C:\"System Manufacturer\" /C:\"System Model\" /C:\"Total Physical Memory\"\"");
            sb.AppendLine("=== systeminfo (excerpt) ===");
            sb.Append(u.StdOut);
        }
        Output = sb.ToString();
        Status = "Done";
        IsBusy = false;
    }

    private static System.IO.DriveInfo[] _;
    private static string Format(long b) =>
        b >= 1L << 30 ? $"{b / (double)(1L << 30):F2} GB" :
        b >= 1L << 20 ? $"{b / (double)(1L << 20):F2} MB" :
        b >= 1L << 10 ? $"{b / (double)(1L << 10):F1} KB" : $"{b} B";
}
