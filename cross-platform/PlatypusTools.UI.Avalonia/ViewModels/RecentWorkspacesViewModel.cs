using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class RecentWorkspacesViewModel : ObservableObject
{
    private readonly string _file = Path.Combine(ShellHelper.AppDataDir, "recent_workspaces.txt");
    [ObservableProperty] private string _newPath = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string? _selected;

    public ObservableCollection<string> Items { get; } = new();

    public RecentWorkspacesViewModel() { Load(); }

    private void Load()
    {
        Items.Clear();
        try
        {
            if (File.Exists(_file))
                foreach (var line in File.ReadAllLines(_file))
                    if (!string.IsNullOrWhiteSpace(line)) Items.Add(line.Trim());
            Status = $"{Items.Count} workspace(s)";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    private void Save()
    {
        try { File.WriteAllLines(_file, Items.Take(50)); } catch { }
    }

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewPath)) return;
        var p = NewPath.Trim();
        Items.Remove(p);
        Items.Insert(0, p);
        Save();
        NewPath = "";
        Status = $"Added (top): {p}";
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is null) return;
        Items.Remove(Selected);
        Save();
        Status = "Removed";
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (Selected is null) return;
        try { _ = new FileLauncher().OpenFileAsync(Selected); Status = $"Opened {Selected}"; }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }
}
