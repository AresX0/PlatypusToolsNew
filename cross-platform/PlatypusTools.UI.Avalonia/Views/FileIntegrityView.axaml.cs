using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class FileIntegrityView : UserControl
{
    public FileIntegrityView() => InitializeComponent();
    private async void OnPickManifestClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileIntegrityViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick manifest", AllowMultiple = false });
        var p = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.ManifestPath = p!;
    }
    private async void OnPickBaseClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileIntegrityViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick base dir", AllowMultiple = false });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.BaseDir = p!;
    }
}
