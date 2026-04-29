using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ScreensaverViewModel : ObservableObject
{
    private readonly ScreensaverService _svc = new();
    [ObservableProperty] private int _idleSeconds = 600;
    [ObservableProperty] private string _status = "";
    public string PlatformInfo => $"Platform: {ShellHelper.PlatformName}";

    [RelayCommand]
    private async Task LockNowAsync()
    {
        var (ok, detail) = await _svc.LockAsync();
        Status = ok ? $"Locked ({detail})" : $"Failed: {detail}";
    }

    [RelayCommand]
    private async Task ApplyTimeoutAsync()
    {
        var (ok, detail) = await _svc.SetIdleSecondsAsync(IdleSeconds);
        Status = ok ? $"Idle = {IdleSeconds}s ({detail})" : $"Failed: {detail}";
    }
}
