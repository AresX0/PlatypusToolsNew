using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class HashScannerView : UserControl
{
    public HashScannerView() => InitializeComponent();

    private async void OnPickFilesClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not HashScannerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select files to hash",
            AllowMultiple = true
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToList();

        if (paths.Count > 0)
            await vm.HashFilesCommand.ExecuteAsync(paths);
    }
}
