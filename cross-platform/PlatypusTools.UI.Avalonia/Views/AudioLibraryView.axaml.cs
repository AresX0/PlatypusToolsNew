using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class AudioLibraryView : UserControl
{
    public AudioLibraryView() => InitializeComponent();

    private async void OnPickFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AudioLibraryViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose music folder",
            AllowMultiple = false
        });
        var path = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(path)) vm.LibraryRoot = path!;
    }

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AudioLibraryViewModel vm && vm.SelectedTrack is not null)
            await vm.PlaySelectedCommand.ExecuteAsync(null);
    }
}
