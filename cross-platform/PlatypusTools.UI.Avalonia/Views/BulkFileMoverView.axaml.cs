using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class BulkFileMoverView : UserControl
{
    public BulkFileMoverView() => InitializeComponent();

    private async void OnPickSrc(object? sender, RoutedEventArgs e) => await Pick(true);
    private async void OnPickDst(object? sender, RoutedEventArgs e) => await Pick(false);

    private async System.Threading.Tasks.Task Pick(bool source)
    {
        if (DataContext is not BulkFileMoverViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        var p = f.Select(x => x.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (string.IsNullOrEmpty(p)) return;
        if (source) vm.Source = p!; else vm.Destination = p!;
    }
}
