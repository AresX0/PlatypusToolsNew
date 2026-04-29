using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class VideoCombinerView : UserControl
{
    public VideoCombinerView() => InitializeComponent();

    private async void OnAddInputs(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoCombinerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true });
        foreach (var x in f)
        {
            var p = x.TryGetLocalPath();
            if (!string.IsNullOrEmpty(p)) vm.AddInput(p!);
        }
    }

    private async void OnPickOutput(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VideoCombinerViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions { DefaultExtension = "mp4" });
        var p = f?.TryGetLocalPath();
        if (!string.IsNullOrEmpty(p)) vm.OutputPath = p!;
    }
}
