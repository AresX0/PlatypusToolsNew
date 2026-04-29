using Avalonia;
using Avalonia.Styling;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Switches Avalonia's Fluent theme variant. OS-dark-mode follow can be added later
/// via platform-specific D-Bus / NSDistributedNotificationCenter listeners.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public bool IsDarkMode => Application.Current?.RequestedThemeVariant == ThemeVariant.Dark;

    public void ApplyTheme(bool dark)
    {
        if (Application.Current is null) return;
        Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
    }
}
