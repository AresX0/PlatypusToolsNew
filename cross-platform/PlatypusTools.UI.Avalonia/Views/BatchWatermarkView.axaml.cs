using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PlatypusTools.UI.Avalonia.ViewModels;

namespace PlatypusTools.UI.Avalonia.Views;

public partial class BatchWatermarkView : UserControl
{
    public BatchWatermarkView() => InitializeComponent();

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BatchWatermarkViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = true });
        foreach (var x in f) { var p = x.TryGetLocalPath(); if (!string.IsNullOrEmpty(p)) vm.AddInput(p!); }
    }

    private async void OnPickWm(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BatchWatermarkViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = false });
        var p = f.Select(x => x.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.Watermark = p!;
    }

    private async void OnPickOut(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BatchWatermarkViewModel vm) return;
        var top = TopLevel.GetTopLevel(this); if (top is null) return;
        var f = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { AllowMultiple = false });
        var p = f.Select(x => x.TryGetLocalPath()).FirstOrDefault(p => !string.IsNullOrEmpty(p));
        if (!string.IsNullOrEmpty(p)) vm.OutputFolder = p!;
    }
}
