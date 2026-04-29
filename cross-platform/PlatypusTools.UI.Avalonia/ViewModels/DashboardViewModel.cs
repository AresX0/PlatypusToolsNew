using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    public ObservableCollection<DashboardTile> Tiles { get; } = new()
    {
        new("Hash Scanner",     "🔐", "HashScannerViewModel"),
        new("File Manager",     "📁", "FileManagerViewModel"),
        new("Audio Player",     "🎵", "AudioPlayerViewModel"),
        new("Vault",            "🔒", "VaultViewModel"),
        new("Disk Cleanup",     "🧹", "DiskCleanupViewModel"),
        new("Process Manager",  "⚙",  "ProcessManagerViewModel"),
        new("System Audit",     "🩺", "SystemAuditViewModel"),
        new("Network Tools",    "🌐", "NetworkToolsViewModel"),
        new("QR Code",          "🔳", "QrCodeViewModel"),
        new("Terminal",         "🖥",  "TerminalClientViewModel"),
        new("File Sync",        "🔁", "FileSyncViewModel"),
        new("Settings",         "⚙️", "SettingsViewModel"),
    };
}

public sealed class DashboardTile
{
    public string Title { get; }
    public string Icon { get; }
    public string Target { get; }
    public DashboardTile(string title, string icon, string target)
    { Title = title; Icon = icon; Target = target; }
}
