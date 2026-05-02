using Avalonia;
using Avalonia.ReactiveUI;
using PlatypusTools.Core.Services;
using System;

namespace PlatypusTools.UI.Avalonia;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ApplyEditionOverride(args);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static void ApplyEditionOverride(string[] args)
    {
        if (args == null) return;
        foreach (var raw in args)
        {
            if (string.IsNullOrEmpty(raw)) continue;
            var a = raw.TrimStart('-', '/').Replace(':', '=');
            if (a.StartsWith("edition=", StringComparison.OrdinalIgnoreCase))
            {
                var val = a.Substring("edition=".Length);
                if (Enum.TryParse<AppEdition>(val, true, out var ed))
                    EditionService.Override(ed);
                return;
            }
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}
