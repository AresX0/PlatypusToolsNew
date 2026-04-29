using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class DuplicatesView : UserControl
{
    public DuplicatesView() => InitializeComponent();

    private async void OnPickFolderClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DuplicatesViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose folder to scan",
            AllowMultiple = false
        });

        var path = folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(path))
            vm.SelectedFolder = path!;
    }
}
