using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class ArchiveManagerView : UserControl
{
    public ArchiveManagerView() => InitializeComponent();

    private async System.Threading.Tasks.Task<string?> PickFolderAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return null;
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
    }
    private async System.Threading.Tasks.Task<string?> PickFileAsync(string title)
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return null;
        var fs = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = title, AllowMultiple = false });
        return fs.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
    }
    private async System.Threading.Tasks.Task<string?> SaveFileAsync(string title, string ext)
    {
        var top = TopLevel.GetTopLevel(this); if (top is null) return null;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = title, DefaultExtension = ext });
        return f?.TryGetLocalPath();
    }

    private async void OnPickSource(object? s, RoutedEventArgs e)
    { if (DataContext is ArchiveManagerViewModel vm) { var p = await PickFolderAsync("Source folder"); if (!string.IsNullOrEmpty(p)) vm.SourceFolder = p!; } }
    private async void OnPickZipOut(object? s, RoutedEventArgs e)
    { if (DataContext is ArchiveManagerViewModel vm) { var p = await SaveFileAsync("Save ZIP as", "zip"); if (!string.IsNullOrEmpty(p)) vm.ArchivePath = p!; } }
    private async void OnPickZipIn(object? s, RoutedEventArgs e)
    { if (DataContext is ArchiveManagerViewModel vm) { var p = await PickFileAsync("Pick ZIP"); if (!string.IsNullOrEmpty(p)) vm.ArchivePath = p!; } }
    private async void OnPickExtractTo(object? s, RoutedEventArgs e)
    { if (DataContext is ArchiveManagerViewModel vm) { var p = await PickFolderAsync("Extract to"); if (!string.IsNullOrEmpty(p)) vm.ExtractTo = p!; } }
}
