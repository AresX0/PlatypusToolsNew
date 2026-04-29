namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Tiny service locator. Avalonia + CommunityToolkit.Mvvm don't ship a built-in
/// DI container, and we explicitly do NOT want a Microsoft.Extensions.DependencyInjection
/// dependency in the cross-platform port (keeps deps minimal and the same code path
/// works on .NET trimmed AOT publishes later).
/// </summary>
public static class AppServices
{
    public static IFileLauncher FileLauncher { get; } = new FileLauncher();
    public static ITrashService Trash { get; } = new TrashService();
    public static INotificationService Notifications { get; } = new NotificationService();
    public static IThemeService Theme { get; } = new ThemeService();
}
