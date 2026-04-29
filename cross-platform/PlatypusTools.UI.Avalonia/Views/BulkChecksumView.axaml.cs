using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class BulkChecksumView : UserControl
{
    public BulkChecksumView() => InitializeComponent();
    private async void OnPickClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BulkChecksumViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick root", AllowMultiple = false });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Root = p!;
    }
    private async void OnExportClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BulkChecksumViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Save manifest", DefaultExtension = "txt", SuggestedFileName = "checksums.txt" });
        var path = f?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) await vm.ExportManifestCommand.ExecuteAsync(path!);
    }
}
