using Avalonia.Controls;
using Avalonia.Interactivity;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class ColorPickerView : UserControl
{
    public ColorPickerView() => InitializeComponent();
    private void OnParse(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ColorPickerViewModel vm)
            vm.ParseHexCommand.Execute(HexInput.Text ?? "");
    }
}
