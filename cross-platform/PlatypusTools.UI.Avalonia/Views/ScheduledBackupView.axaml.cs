using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class ScheduledBackupView : UserControl
{
    public ScheduledBackupView() => InitializeComponent();
    private async void OnPickSrcClicked(object? sender, RoutedEventArgs e) => await Pick(true);
    private async void OnPickDstClicked(object? sender, RoutedEventArgs e) => await Pick(false);
    private async System.Threading.Tasks.Task Pick(bool src)
    {
        if (DataContext is not ScheduledBackupViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = src ? "Source" : "Destination" });
        var p = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (string.IsNullOrEmpty(p)) return;
        if (src) vm.Source = p!; else vm.DestDir = p!;
    }
}
