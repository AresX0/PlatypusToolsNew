using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class IconConverterViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _outputDir = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Generates 16/32/48/64/128/256 px PNGs (and one .ico on Windows).";
    public ObservableCollection<string> Output { get; } = new();
    public int[] Sizes { get; } = { 16, 32, 48, 64, 128, 256 };

    [RelayCommand]
    private async Task ConvertAsync()
    {
        var ff = FFmpegRunner.FFmpegPath;
        if (ff is null) { Status = "ffmpeg not found"; return; }
        if (!File.Exists(Input)) { Status = "Pick an input image"; return; }
        Directory.CreateDirectory(OutputDir);
        IsBusy = true; Output.Clear();
        var name = Path.GetFileNameWithoutExtension(Input);
        foreach (var sz in Sizes)
        {
            var outPath = Path.Combine(OutputDir, $"{name}_{sz}.png");
            var r = await ShellHelper.RunAsync(ff, $"-y -i \"{Input}\" -vf scale={sz}:{sz} \"{outPath}\"");
            Output.Add($"{(r.ExitCode == 0 ? "✓" : "✗")} {Path.GetFileName(outPath)}");
        }
        if (ShellHelper.IsWindows)
        {
            // ffmpeg can write a multi-resolution .ico via concat + scale list — emit a single 256 .ico for simplicity
            var icoPath = Path.Combine(OutputDir, $"{name}.ico");
            await ShellHelper.RunAsync(ff, $"-y -i \"{Input}\" -vf scale=256:256 \"{icoPath}\"");
            Output.Add($"✓ {Path.GetFileName(icoPath)}");
        }
        Status = $"Done: {Output.Count} files";
        IsBusy = false;
    }
}
