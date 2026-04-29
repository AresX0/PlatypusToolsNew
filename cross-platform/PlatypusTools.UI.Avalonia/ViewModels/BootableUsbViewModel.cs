using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class BootableUsbViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _isoPath = "";
    [ObservableProperty] private string _device = ShellHelper.IsWindows ? @"\\.\PhysicalDriveX" : ShellHelper.IsMac ? "/dev/diskN" : "/dev/sdX";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "⚠ DESTRUCTIVE. Wraps `dd` (Linux/Mac). On Windows install Win32 DiskImager or Rufus.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _confirmed;

    [RelayCommand]
    private async Task WriteAsync()
    {
        if (!Confirmed) { Status = "Tick the confirmation box first."; return; }
        if (string.IsNullOrEmpty(IsoPath) || string.IsNullOrEmpty(Device)) { Status = "Pick ISO + device"; return; }
        _cts?.Cancel(); _cts = new CancellationTokenSource();
        IsBusy = true; Output = "";
        if (ShellHelper.IsWindows)
        {
            Status = "Windows: open Rufus or Win32 DiskImager — `dd` not provided.";
            IsBusy = false; return;
        }
        var args = $"if=\"{IsoPath}\" of=\"{Device}\" bs=4M status=progress oflag=sync";
        var r = await ShellHelper.RunAsync("dd", args, _cts.Token, line => Output += line + "\n");
        Status = r.ExitCode == 0 ? "Done" : $"dd exit {r.ExitCode}";
        IsBusy = false;
    }

    [RelayCommand] private void Cancel() { _cts?.Cancel(); Status = "Cancelled"; }
}
