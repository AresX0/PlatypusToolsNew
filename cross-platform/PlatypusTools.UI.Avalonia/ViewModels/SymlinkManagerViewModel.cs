using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SymlinkManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _scanFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    [ObservableProperty] private string _linkPath = "";
    [ObservableProperty] private string _targetPath = "";
    [ObservableProperty] private bool _isDirectoryLink;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private SymlinkEntry? _selected;

    public ObservableCollection<SymlinkEntry> Links { get; } = new();

    [RelayCommand]
    private void Scan()
    {
        Links.Clear();
        try
        {
            if (!Directory.Exists(ScanFolder)) { Status = "Folder not found"; return; }
            foreach (var entry in Directory.EnumerateFileSystemEntries(ScanFolder, "*", new EnumerationOptions
                     { RecurseSubdirectories = false, IgnoreInaccessible = true }))
            {
                FileSystemInfo fi = File.Exists(entry) ? new FileInfo(entry) : new DirectoryInfo(entry);
                if ((fi.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    Links.Add(new SymlinkEntry
                    {
                        LinkPath = entry,
                        Target = fi.LinkTarget ?? "(unknown)",
                        Kind = fi is DirectoryInfo ? "Directory" : "File"
                    });
                }
            }
            Status = $"{Links.Count} link(s) found";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void Create()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(LinkPath) || string.IsNullOrWhiteSpace(TargetPath))
            { Status = "Both link and target paths required"; return; }
            if (IsDirectoryLink)
                Directory.CreateSymbolicLink(LinkPath, TargetPath);
            else
                File.CreateSymbolicLink(LinkPath, TargetPath);
            Status = $"Created: {LinkPath} → {TargetPath}";
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        try
        {
            if (Selected.Kind == "Directory") Directory.Delete(Selected.LinkPath);
            else File.Delete(Selected.LinkPath);
            Links.Remove(Selected);
            Status = "Deleted";
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }
}

public sealed class SymlinkEntry
{
    public string LinkPath { get; set; } = "";
    public string Target { get; set; } = "";
    public string Kind { get; set; } = "";
}
