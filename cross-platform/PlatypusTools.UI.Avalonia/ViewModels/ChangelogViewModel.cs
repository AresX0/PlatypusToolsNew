using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ChangelogViewModel : ObservableObject
{
    [ObservableProperty] private string _markdown = "# Changelog\n\nLoad a CHANGELOG.md or paste markdown here.";
    [ObservableProperty] private string _status = "Open a CHANGELOG.md from any project.";

    [RelayCommand]
    private void LoadDefault()
    {
        var candidates = new[]
        {
            Path.Combine(System.AppContext.BaseDirectory, "CHANGELOG.md"),
            Path.Combine(System.AppContext.BaseDirectory, "..", "..", "..", "CHANGELOG.md"),
            "CHANGELOG.md"
        };
        foreach (var c in candidates)
        {
            try { if (File.Exists(c)) { Markdown = File.ReadAllText(c); Status = "Loaded " + c; return; } }
            catch { }
        }
        Status = "No CHANGELOG.md found in common locations.";
    }

    public void LoadFile(string path)
    {
        try { Markdown = File.ReadAllText(path); Status = "Loaded " + path; }
        catch (System.Exception ex) { Status = "Failed: " + ex.Message; }
    }
}
