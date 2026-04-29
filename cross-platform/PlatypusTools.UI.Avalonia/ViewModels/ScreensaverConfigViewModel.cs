using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ScreensaverConfigViewModel : ObservableObject
{
    [ObservableProperty] private int _idleMinutes = 10;
    [ObservableProperty] private bool _lockOnResume = true;
    [ObservableProperty] private string _imageFolder = "";
    [ObservableProperty] private int _intervalSeconds = 5;
    [ObservableProperty] private bool _shuffle = true;
    [ObservableProperty] private string _status = "Configure the slideshow screensaver. Saved to settings.json.";

    public ScreensaverConfigViewModel()
    {
        var s = AppSettings.Load();
        IdleMinutes = s.ScreensaverIdleMin > 0 ? s.ScreensaverIdleMin : 10;
        LockOnResume = s.ScreensaverLock;
        ImageFolder = s.ScreensaverFolder ?? "";
        IntervalSeconds = s.ScreensaverInterval > 0 ? s.ScreensaverInterval : 5;
        Shuffle = s.ScreensaverShuffle;
    }

    [RelayCommand]
    private void Save()
    {
        var s = AppSettings.Load();
        s.ScreensaverIdleMin = IdleMinutes;
        s.ScreensaverLock = LockOnResume;
        s.ScreensaverFolder = ImageFolder;
        s.ScreensaverInterval = IntervalSeconds;
        s.ScreensaverShuffle = Shuffle;
        s.Save();
        Status = "Saved";
    }
}
