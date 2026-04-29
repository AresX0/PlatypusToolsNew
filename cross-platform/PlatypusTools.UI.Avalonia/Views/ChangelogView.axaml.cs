using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class ChangelogView : UserControl
{
    public ChangelogView() => InitializeComponent();
    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ChangelogViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open markdown" });
        var p = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.LoadFile(p!);
    }
}
