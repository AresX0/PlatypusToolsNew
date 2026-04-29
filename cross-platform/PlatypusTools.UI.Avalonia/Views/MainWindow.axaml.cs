using Avalonia.Controls;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (sender is not TreeView tv) return;
        if (tv.SelectedItem is NavigationItem item)
            vm.SelectedItem = item;
        // ignore selecting a NavigationCategory header
    }
}
