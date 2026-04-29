using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SmartRenameViewModel : ObservableObject
{
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private string _pattern = "*.*";
    [ObservableProperty] private string _findRegex = "";
    [ObservableProperty] private string _replaceWith = "";
    [ObservableProperty] private bool _useRegex = true;
    [ObservableProperty] private bool _recursive;
    [ObservableProperty] private string _status = "";

    public ObservableCollection<RenameRow> Rows { get; } = new();

    [RelayCommand]
    private void Preview()
    {
        Rows.Clear();
        try
        {
            if (!Directory.Exists(Folder)) { Status = "Folder not found"; return; }
            var opts = new EnumerationOptions { RecurseSubdirectories = Recursive, IgnoreInaccessible = true };
            Regex? rx = UseRegex && !string.IsNullOrEmpty(FindRegex) ? new Regex(FindRegex) : null;
            foreach (var f in Directory.EnumerateFiles(Folder, string.IsNullOrEmpty(Pattern) ? "*" : Pattern, opts))
            {
                var name = Path.GetFileName(f);
                string newName;
                if (rx is not null) newName = rx.Replace(name, ReplaceWith ?? "");
                else newName = string.IsNullOrEmpty(FindRegex) ? name : name.Replace(FindRegex, ReplaceWith ?? "");
                if (newName == name) continue;
                Rows.Add(new RenameRow { OldPath = f, NewName = newName });
            }
            Status = $"{Rows.Count} rename(s) preview";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void Apply()
    {
        int n = 0, fail = 0;
        foreach (var r in Rows)
        {
            try
            {
                var newPath = Path.Combine(Path.GetDirectoryName(r.OldPath)!, r.NewName);
                File.Move(r.OldPath, newPath);
                n++;
            }
            catch { fail++; }
        }
        Status = $"Renamed {n}, {fail} failed";
        Preview();
    }
}

public sealed class RenameRow
{
    public string OldPath { get; set; } = "";
    public string NewName { get; set; } = "";
    public string OldName => Path.GetFileName(OldPath);
}
