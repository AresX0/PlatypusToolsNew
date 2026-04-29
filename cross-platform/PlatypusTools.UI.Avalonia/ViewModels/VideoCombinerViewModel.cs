using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class VideoCombinerViewModel : ObservableObject
{
    [ObservableProperty] private string _outputPath = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isRunning;

    public ObservableCollection<string> Inputs { get; } = new();

    public void AddInput(string path) { if (!string.IsNullOrEmpty(path)) Inputs.Add(path); }
    [RelayCommand] private void Clear() => Inputs.Clear();
    [RelayCommand] private void Remove(string p) => Inputs.Remove(p);

    [RelayCommand]
    private async Task CombineAsync()
    {
        if (Inputs.Count < 2) { Status = "Add at least 2 inputs"; return; }
        if (string.IsNullOrWhiteSpace(OutputPath)) { Status = "Set output path"; return; }
        var ff = FFmpegRunner.FFmpegPath;
        if (ff is null) { Status = "ffmpeg not found"; return; }

        IsRunning = true;
        var listFile = Path.Combine(Path.GetTempPath(), $"pt_concat_{Guid.NewGuid():N}.txt");
        try
        {
            using (var sw = new StreamWriter(listFile))
                foreach (var f in Inputs) sw.WriteLine($"file '{f.Replace("'", "'\\''")}'");
            Output = "";
            var args = $"-y -f concat -safe 0 -i {FFmpegRunner.Q(listFile)} -c copy {FFmpegRunner.Q(OutputPath)}";
            var r = await ShellHelper.RunAsync(ff, args, default,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
            Status = r.ExitCode == 0 ? $"Saved {OutputPath}" : $"ffmpeg exit {r.ExitCode}";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally { try { File.Delete(listFile); } catch { } IsRunning = false; }
    }
}
