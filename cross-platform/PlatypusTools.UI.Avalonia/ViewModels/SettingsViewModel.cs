using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _useDarkTheme = true;

    public string PlatformInfo { get; } =
        $"OS: {RuntimeInformation.OSDescription}\n" +
        $"Architecture: {RuntimeInformation.ProcessArchitecture}\n" +
        $"Framework: {RuntimeInformation.FrameworkDescription}";

    public string PortInfo { get; } =
        "PlatypusTools (Cross-Platform) — Avalonia 11 port. " +
        "Side-by-side with the WPF Windows build. " +
        "Source: cross-platform/ folder.";

    public SettingsViewModel()
    {
        UseDarkTheme = AppServices.Theme.IsDarkMode;
    }

    partial void OnUseDarkThemeChanged(bool value)
    {
        AppServices.Theme.ApplyTheme(value);
    }
}
