using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class FileDiffView : UserControl
{
    public FileDiffView() => InitializeComponent();
    private async void OnPickLeft(object? s, RoutedEventArgs e) => await PickAsync(true);
    private async void OnPickRight(object? s, RoutedEventArgs e) => await PickAsync(false);
    private async System.Threading.Tasks.Task PickAsync(bool left)
    {
        if (DataContext is not FileDiffViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick file", AllowMultiple = false });
        var p = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (string.IsNullOrEmpty(p)) return;
        if (left) vm.LeftPath = p!; else vm.RightPath = p!;
    }
}
