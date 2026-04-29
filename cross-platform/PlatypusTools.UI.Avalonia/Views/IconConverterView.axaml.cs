using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class IconConverterView : UserControl
{
    public IconConverterView() => InitializeComponent();
    private async void OnPickInClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IconConverterViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick image", AllowMultiple = false });
        var p = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Input = p!;
    }
    private async void OnPickOutClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not IconConverterViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick output", AllowMultiple = false });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.OutputDir = p!;
    }
}
