using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class PdfToolsViewModel : ObservableObject
{
    [ObservableProperty] private string _operation = "Merge";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Requires qpdf in PATH (Linux: apt install qpdf · Mac: brew install qpdf · Win: scoop install qpdf).";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _pageRange = "1-z";
    public ObservableCollection<string> Inputs { get; } = new();
    public string[] Operations { get; } = { "Merge", "Split", "Rotate-90", "Rotate-180", "Rotate-270", "Encrypt", "Decrypt" };
    [ObservableProperty] private string _password = "";

    [RelayCommand]
    private async Task RunAsync()
    {
        if (Inputs.Count == 0) { Status = "Add at least one input PDF"; return; }
        if (string.IsNullOrEmpty(Output)) { Status = "Pick output"; return; }
        IsBusy = true; Status = "Running qpdf…";
        var qpdf = ShellHelper.IsWindows ? "qpdf.exe" : "qpdf";
        ShellHelper.Result r;
        switch (Operation)
        {
            case "Merge":
                {
                    var args = $"--empty --pages " + string.Join(' ', System.Linq.Enumerable.Select(Inputs, p => $"\"{p}\"")) + $" -- \"{Output}\"";
                    r = await ShellHelper.RunAsync(qpdf, args);
                    break;
                }
            case "Split":
                {
                    r = await ShellHelper.RunAsync(qpdf, $"\"{Inputs[0]}\" --pages \"{Inputs[0]}\" {PageRange} -- \"{Output}\"");
                    break;
                }
            case "Encrypt":
                r = await ShellHelper.RunAsync(qpdf, $"--encrypt \"{Password}\" \"{Password}\" 256 -- \"{Inputs[0]}\" \"{Output}\""); break;
            case "Decrypt":
                r = await ShellHelper.RunAsync(qpdf, $"--password=\"{Password}\" --decrypt \"{Inputs[0]}\" \"{Output}\""); break;
            default:
                {
                    var deg = Operation.Replace("Rotate-", "");
                    r = await ShellHelper.RunAsync(qpdf, $"\"{Inputs[0]}\" --rotate=+{deg} \"{Output}\"");
                    break;
                }
        }
        Status = r.ExitCode == 0 ? "Done" : "qpdf: " + r.StdErr;
        IsBusy = false;
    }

    [RelayCommand] private void Clear() => Inputs.Clear();
}
