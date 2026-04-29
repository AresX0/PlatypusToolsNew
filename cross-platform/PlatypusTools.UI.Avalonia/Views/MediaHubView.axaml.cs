using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;
public partial class MediaHubView : UserControl
{
    public MediaHubView() => InitializeComponent();
    private async void OnPickClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaHubViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var fs = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Root" });
        var p = fs.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Root = p!;
    }
    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MediaHubViewModel vm) return;
        if (sender is Button b && b.DataContext is MediaItem mi) await vm.OpenCommand.ExecuteAsync(mi);
    }
}
