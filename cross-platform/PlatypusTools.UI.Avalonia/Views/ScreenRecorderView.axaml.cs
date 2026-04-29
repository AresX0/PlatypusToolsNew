using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class ScreenRecorderView : UserControl
{
    public ScreenRecorderView() => InitializeComponent();

    private async void OnPick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ScreenRecorderViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        { Title = "Save recording", DefaultExtension = "mp4" });
        var p = f?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) vm.OutputPath = p!;
    }
}
