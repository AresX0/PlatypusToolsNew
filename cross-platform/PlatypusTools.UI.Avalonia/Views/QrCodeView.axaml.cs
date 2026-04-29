using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class QrCodeView : UserControl
{
    public QrCodeView() => InitializeComponent();
    private async void OnSaveClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not QrCodeViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Save QR PNG", SuggestedFileName = "qrcode.png", DefaultExtension = "png" });
        var p = f?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) await vm.SaveAsync(p!);
    }
}
