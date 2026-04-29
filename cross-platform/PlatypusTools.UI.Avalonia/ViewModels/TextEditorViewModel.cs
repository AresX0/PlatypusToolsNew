using System.IO;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class TextEditorViewModel : ObservableObject
{
    [ObservableProperty] private string _filePath = "";
    [ObservableProperty] private string _status = "New document.";
    [ObservableProperty] private bool _isModified;

    public TextDocument Document { get; } = new(
        "// Welcome to PlatypusTools (Linux/macOS).\n" +
        "// AvaloniaEdit-backed cross-platform editor.\n");

    public TextEditorViewModel()
    {
        Document.TextChanged += (_, _) => IsModified = true;
    }

    public async Task LoadAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path).ConfigureAwait(true);
        Document.Text = text;
        FilePath = path;
        IsModified = false;
        Status = $"Loaded: {Path.GetFileName(path)} ({text.Length} chars)";
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrEmpty(FilePath))
        {
            Status = "Use Save As… to choose a file path.";
            return;
        }
        await File.WriteAllTextAsync(FilePath, Document.Text).ConfigureAwait(true);
        IsModified = false;
        Status = $"Saved: {Path.GetFileName(FilePath)}";
    }

    public async Task SaveAsAsync(string path)
    {
        await File.WriteAllTextAsync(path, Document.Text).ConfigureAwait(true);
        FilePath = path;
        IsModified = false;
        Status = $"Saved as: {Path.GetFileName(path)}";
    }
}
