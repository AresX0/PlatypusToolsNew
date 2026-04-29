using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class PdfToolsView : UserControl
{
    public PdfToolsView() => InitializeComponent();
    private async void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PdfToolsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Pick PDFs", AllowMultiple = true });
        foreach (var f in files)
        {
            var p = f.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p)) vm.Inputs.Add(p!);
        }
    }
    private async void OnOutClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not PdfToolsViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { Title = "Output PDF", SuggestedFileName = "out.pdf", DefaultExtension = "pdf" });
        var p = f?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) vm.Output = p!;
    }
}
