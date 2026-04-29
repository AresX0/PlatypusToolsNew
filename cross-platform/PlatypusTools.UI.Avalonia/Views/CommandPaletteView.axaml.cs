using Avalonia.Controls;
using Avalonia.Input;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView() => InitializeComponent();
    private void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm) vm.GoCommand.Execute(null);
    }
}
