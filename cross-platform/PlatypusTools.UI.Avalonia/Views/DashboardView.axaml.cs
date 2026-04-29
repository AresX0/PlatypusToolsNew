using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class DashboardView : UserControl
{
    public DashboardView() => InitializeComponent();
    private void OnTileClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string typeName }) return;
        var win = this.FindAncestorOfType<MainWindow>();
        if (win?.DataContext is MainWindowViewModel vm) vm.NavigateToVmTypeName(typeName);
    }
}

