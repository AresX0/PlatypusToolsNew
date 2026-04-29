using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class BulkFileMoverViewModel : ObservableObject
{
    [ObservableProperty] private string _source = "";
    [ObservableProperty] private string _destination = "";
    [ObservableProperty] private string _pattern = "*.*";
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private bool _copyMode;
    [ObservableProperty] private bool _overwrite;
    [ObservableProperty] private string _status = "";

    public ObservableCollection<string> Preview { get; } = new();

    [RelayCommand]
    private void RefreshPreview()
    {
        Preview.Clear();
        try
        {
            if (!Directory.Exists(Source)) { Status = "Source folder not found"; return; }
            var opts = new EnumerationOptions { RecurseSubdirectories = Recursive, IgnoreInaccessible = true };
            foreach (var f in Directory.EnumerateFiles(Source, string.IsNullOrWhiteSpace(Pattern) ? "*" : Pattern, opts))
                Preview.Add(f);
            Status = $"{Preview.Count} files matched";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (!Directory.Exists(Destination))
        {
            try { Directory.CreateDirectory(Destination); }
            catch (Exception ex) { Status = $"Cannot create dest: {ex.Message}"; return; }
        }
        int n = 0, fail = 0;
        await Task.Run(() =>
        {
            foreach (var src in Preview.ToArray())
            {
                try
                {
                    var dest = Path.Combine(Destination, Path.GetFileName(src));
                    if (CopyMode) File.Copy(src, dest, Overwrite);
                    else
                    {
                        if (Overwrite && File.Exists(dest)) File.Delete(dest);
                        File.Move(src, dest);
                    }
                    n++;
                }
                catch { fail++; }
            }
        });
        Status = $"{(CopyMode ? "Copied" : "Moved")} {n}, {fail} failed";
    }
}
