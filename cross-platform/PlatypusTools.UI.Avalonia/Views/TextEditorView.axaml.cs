using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class TextEditorView : UserControl
{
    public TextEditorView() => InitializeComponent();

    protected override void OnDataContextChanged(System.EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is TextEditorViewModel vm && this.FindControl<TextEditor>("Editor") is { } editor)
            editor.Document = vm.Document;
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextEditorViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open text file",
            AllowMultiple = false
        });
        var path = files.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(path)) await vm.LoadAsync(path!);
    }

    private async void OnSaveAsClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TextEditorViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top is null) return;

        var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save as…"
        });
        var path = file?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(path)) await vm.SaveAsAsync(path);
    }
}
