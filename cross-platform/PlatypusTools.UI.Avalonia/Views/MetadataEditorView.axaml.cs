using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class MetadataEditorView : UserControl
{
    public MetadataEditorView() => InitializeComponent();
    private async void OnPickClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MetadataEditorViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick file", AllowMultiple = false });
        var p = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Input = p!;
    }
}
