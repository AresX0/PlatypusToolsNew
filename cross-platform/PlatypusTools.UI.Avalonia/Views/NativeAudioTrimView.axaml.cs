using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;
public partial class NativeAudioTrimView : UserControl
{
    public NativeAudioTrimView() => InitializeComponent();
    private async void OnPickInClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NativeAudioTrimViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var fs = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Audio in" });
        var p = fs.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Input = p!;
    }
    private async void OnPickOutClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NativeAudioTrimViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Audio out", DefaultExtension = "mp3" });
        var p = f?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) vm.Output = p!;
    }
}
