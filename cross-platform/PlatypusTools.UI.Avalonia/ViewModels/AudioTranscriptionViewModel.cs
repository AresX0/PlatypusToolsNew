using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class AudioTranscriptionViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _model = "base";
    [ObservableProperty] private string _language = "auto";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Wraps `whisper` (OpenAI Whisper) or `whisper.cpp`. Install with pip or from github.com/ggml-org/whisper.cpp.";
    [ObservableProperty] private bool _isBusy;

    public string[] Models { get; } = { "tiny", "base", "small", "medium", "large-v3" };
    public string[] Languages { get; } = { "auto", "en", "es", "fr", "de", "it", "ja", "ko", "zh", "ru", "pt", "nl" };

    [RelayCommand]
    private async Task RunAsync()
    {
        if (string.IsNullOrEmpty(Input)) { Status = "Pick an audio file"; return; }
        IsBusy = true; Output = ""; Status = "Transcribing…";
        var args = $"\"{Input}\" --model {Model}" + (Language == "auto" ? "" : $" --language {Language}") + " --output_format txt";
        var r = await ShellHelper.RunAsync("whisper", args, default, line => Output += line + "\n");
        if (r.ExitCode != 0)
        {
            // try whisper.cpp `main` binary
            var r2 = await ShellHelper.RunAsync("main", $"-m models/ggml-{Model}.bin -f \"{Input}\" -otxt", default, line => Output += line + "\n");
            Status = r2.ExitCode == 0 ? "Done (whisper.cpp)" : $"whisper exit {r.ExitCode} / fallback {r2.ExitCode}";
        }
        else Status = "Done";
        IsBusy = false;
    }
}
