using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class BatchUpscaleView : UserControl
{
    public BatchUpscaleView() => InitializeComponent();
    private async void OnPickInClicked(object? sender, RoutedEventArgs e) => await Pick(true);
    private async void OnPickOutClicked(object? sender, RoutedEventArgs e) => await Pick(false);
    private async System.Threading.Tasks.Task Pick(bool input)
    {
        if (DataContext is not BatchUpscaleViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Pick folder", AllowMultiple = false });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (string.IsNullOrEmpty(p)) return;
        if (input) vm.InputDir = p!; else vm.OutputDir = p!;
    }
}
