using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ImageViewerViewModel : ObservableObject
{
    [ObservableProperty] private Bitmap? _image;
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _status = "Open an image to view.";
    [ObservableProperty] private double _zoom = 1.0;

    public Task LoadAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Status = $"Not found: {path}";
                return Task.CompletedTask;
            }
            using var s = File.OpenRead(path);
            Image?.Dispose();
            Image = new Bitmap(s);
            FilePath = path;
            Status = $"Loaded: {Path.GetFileName(path)} " +
                     $"({Image.PixelSize.Width}x{Image.PixelSize.Height})";
            Zoom = 1.0;
        }
        catch (Exception ex)
        {
            Status = $"Failed to load: {ex.Message}";
        }
        return Task.CompletedTask;
    }

    [RelayCommand] private void ZoomIn()  => Zoom = Math.Min(8.0, Zoom * 1.25);
    [RelayCommand] private void ZoomOut() => Zoom = Math.Max(0.05, Zoom / 1.25);
    [RelayCommand] private void ZoomReset() => Zoom = 1.0;

    [RelayCommand]
    private async Task RevealAsync()
    {
        if (!string.IsNullOrEmpty(FilePath))
            await AppServices.FileLauncher.RevealInFileManagerAsync(FilePath).ConfigureAwait(true);
    }
}
