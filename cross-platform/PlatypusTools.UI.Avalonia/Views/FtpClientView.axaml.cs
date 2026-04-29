using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class FtpClientView : UserControl
{
    public FtpClientView() => InitializeComponent();
    private async void OnDownloadClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FtpClientViewModel vm) return;
        if (vm.Selected is null) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Save into…" });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) await vm.DownloadAsync(p!);
    }
}
