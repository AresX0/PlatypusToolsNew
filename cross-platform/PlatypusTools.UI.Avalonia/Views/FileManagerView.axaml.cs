using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class FileManagerView : UserControl
{
    public FileManagerView() => InitializeComponent();

    private async void OnRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is FileManagerViewModel vm && vm.SelectedEntry is { } entry)
            await vm.ActivateCommand.ExecuteAsync(entry);
    }

    private async void OnOpenClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileEntry entry } &&
            DataContext is FileManagerViewModel vm)
            await vm.ActivateCommand.ExecuteAsync(entry);
    }

    private async void OnRevealClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileEntry entry } &&
            DataContext is FileManagerViewModel vm)
            await vm.RevealCommand.ExecuteAsync(entry);
    }

    private async void OnTrashClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: FileEntry entry } &&
            DataContext is FileManagerViewModel vm)
            await vm.TrashCommand.ExecuteAsync(entry);
    }
}
