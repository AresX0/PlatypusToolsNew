using System;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class AboutViewModel : ObservableObject
{
    public string AppName => "PlatypusTools";
    public string AppVersion =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "?";
    public string Platform => $"{ShellHelper.PlatformName} ({Environment.OSVersion.VersionString})";
    public string Runtime => $".NET {Environment.Version}";
    public string DataDir => ShellHelper.AppDataDir;
    public string Description =>
        "PlatypusTools — cross-platform side-port of the Windows Platypus Tools suite.\n\n" +
        "Built with Avalonia 11 + .NET 10. Wraps the shared PlatypusTools.Core library\n" +
        "and adds platform-specific helpers (gsettings/osascript/qdbus/feh, ffmpeg, etc.).";
    public string KeyboardShortcuts =>
        "Ctrl+P — Command palette (sidebar focus)\n" +
        "F1     — Show this Help / About view\n" +
        "Esc    — Close dialogs\n" +
        "(Per-feature shortcuts shown in their own pages.)";
    public string CreditsAndLicenses =>
        "Avalonia (MIT) — UI framework\n" +
        "AvaloniaEdit (MIT) — text editor\n" +
        "LibVLCSharp (LGPL) — audio playback\n" +
        "CommunityToolkit.Mvvm (MIT) — MVVM source generators\n" +
        "FFmpeg (LGPL/GPL) — media tooling (separately installed)";
}
