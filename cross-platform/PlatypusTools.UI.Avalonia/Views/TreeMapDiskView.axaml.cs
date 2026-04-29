using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class TreeMapDiskView : UserControl
{
    public TreeMapDiskView() => InitializeComponent();
    private async void OnPickClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TreeMapDiskViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Root" });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Root = p!;
    }
}
