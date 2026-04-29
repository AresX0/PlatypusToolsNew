using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;
public partial class AdvancedForensicsView : UserControl
{
    public AdvancedForensicsView() => InitializeComponent();
    private async void OnPickClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AdvancedForensicsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var fs = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "File" });
        var p = fs.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.File = p!;
    }
}
