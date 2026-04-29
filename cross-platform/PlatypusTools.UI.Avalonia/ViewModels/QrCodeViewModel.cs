using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class QrCodeViewModel : ObservableObject
{
    [ObservableProperty] private string _text = "https://github.com/AresX0/PlatypusToolsNew";
    [ObservableProperty] private global::Avalonia.Media.Imaging.Bitmap? _image;
    [ObservableProperty] private string _status = "Type text/URL → Generate. Save writes a PNG.";

    [RelayCommand]
    private void Generate()
    {
        try
        {
            using var gen = new QRCoder.QRCodeGenerator();
            using var data = gen.CreateQrCode(Text, QRCoder.QRCodeGenerator.ECCLevel.Q);
            var png = new QRCoder.PngByteQRCode(data);
            var bytes = png.GetGraphic(8);
            using var ms = new MemoryStream(bytes);
            Image = new global::Avalonia.Media.Imaging.Bitmap(ms);
            Status = $"Generated ({bytes.Length} bytes)";
        }
        catch (System.Exception ex) { Status = "Failed: " + ex.Message; }
    }

    public async Task SaveAsync(string path)
    {
        try
        {
            using var gen = new QRCoder.QRCodeGenerator();
            using var data = gen.CreateQrCode(Text, QRCoder.QRCodeGenerator.ECCLevel.Q);
            var png = new QRCoder.PngByteQRCode(data);
            var bytes = png.GetGraphic(8);
            await File.WriteAllBytesAsync(path, bytes);
            Status = $"Saved {path}";
        }
        catch (System.Exception ex) { Status = "Save failed: " + ex.Message; }
    }
}
