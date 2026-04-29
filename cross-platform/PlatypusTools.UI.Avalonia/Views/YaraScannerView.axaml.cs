using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class YaraScannerView : UserControl
{
    public YaraScannerView() => InitializeComponent();
    private async void OnPickRulesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not YaraScannerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "YARA rules (.yar/.yara)" });
        var p = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.RulesPath = p!;
    }
    private async void OnPickTargetClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not YaraScannerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Target folder" });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.TargetPath = p!;
    }
}
