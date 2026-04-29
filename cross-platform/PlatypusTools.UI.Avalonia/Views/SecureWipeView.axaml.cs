using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class SecureWipeView : UserControl
{
    public SecureWipeView() => InitializeComponent();
    private async void OnPick(object? s, RoutedEventArgs e)
    {
        if (DataContext is not SecureWipeViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var fs = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick file to wipe", AllowMultiple = false });
        var p = fs.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Path = p!;
    }
}
