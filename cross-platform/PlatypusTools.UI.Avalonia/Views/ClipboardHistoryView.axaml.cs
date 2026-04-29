using Avalonia.Controls;
using Avalonia.Interactivity;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class ClipboardHistoryView : UserControl
{
    public ClipboardHistoryView() => InitializeComponent();
    private async void OnPasteClicked(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ClipboardHistoryViewModel vm) return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null) return;
        var text = await top.Clipboard.GetTextAsync();
        if (!string.IsNullOrEmpty(text)) { vm.NewEntry = text!; vm.AddCommand.Execute(null); }
    }
}
