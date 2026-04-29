using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class LogViewerViewModel : ObservableObject
{
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private int _tailLines = 500;
    [ObservableProperty] private string _status = "Open a log file. Tail = last N lines.";

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!File.Exists(Path)) { Status = "Not found"; return; }
        Status = "Loading…";
        var all = await File.ReadAllLinesAsync(Path);
        var slice = all.Length > TailLines ? all[(all.Length - TailLines)..] : all;
        if (!string.IsNullOrEmpty(Filter))
            slice = System.Array.FindAll(slice, l => l.Contains(Filter, System.StringComparison.OrdinalIgnoreCase));
        Content = string.Join('\n', slice);
        Status = $"{slice.Length} lines (of {all.Length} total)";
    }
}
