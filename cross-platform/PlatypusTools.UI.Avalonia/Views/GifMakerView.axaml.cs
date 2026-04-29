using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class GifMakerView : UserControl
{
    public GifMakerView() => InitializeComponent();
    private async void OnPickInput(object? s, RoutedEventArgs e)
    {
        if (DataContext is not GifMakerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var fs = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick video", AllowMultiple = false });
        var p = fs.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Input = p!;
    }
    private async void OnPickOutput(object? s, RoutedEventArgs e)
    {
        if (DataContext is not GifMakerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save GIF", DefaultExtension = "gif" });
        var p = f?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) vm.Output = p!;
    }
}
