using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class RecentWorkspacesView : UserControl
{
    public RecentWorkspacesView() => InitializeComponent();

    private async void OnPick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not RecentWorkspacesViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        var p = f.Select(x => x.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.NewPath = p!;
    }
}
