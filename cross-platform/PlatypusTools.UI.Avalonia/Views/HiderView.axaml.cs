using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class HiderView : UserControl
{
    public HiderView() => InitializeComponent();
    private async void OnPickFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HiderViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick folder" });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Path = p!;
    }
    private async void OnPickFileClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HiderViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick file" });
        var p = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Path = p!;
    }
}
