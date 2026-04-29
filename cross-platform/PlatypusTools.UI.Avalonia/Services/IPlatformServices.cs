using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

public interface IFileLauncher
{
    Task OpenFileAsync(string path);
    Task RevealInFileManagerAsync(string path);
}

public interface ITrashService
{
    Task<bool> MoveToTrashAsync(string path);
}

public interface INotificationService
{
    Task NotifyAsync(string title, string message);
}

public interface IThemeService
{
    bool IsDarkMode { get; }
    void ApplyTheme(bool dark);
}
