using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class FileAnalyzerView : UserControl
{
    public FileAnalyzerView() => InitializeComponent();

    private async void OnPick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not FileAnalyzerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
        var p = f.Select(x => x.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Path = p!;
    }
}
