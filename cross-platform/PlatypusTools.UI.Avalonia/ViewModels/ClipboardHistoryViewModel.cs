using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ClipboardHistoryViewModel : ObservableObject
{
    [ObservableProperty] private string _newEntry = "";
    [ObservableProperty] private ClipboardEntry? _selected;
    [ObservableProperty] private string _status = "In-memory only. Copy/paste persists for the app session.";

    public ObservableCollection<ClipboardEntry> Entries { get; } = new();

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrEmpty(NewEntry)) return;
        Entries.Insert(0, new ClipboardEntry { When = DateTime.Now, Text = NewEntry });
        if (Entries.Count > 200) Entries.RemoveAt(Entries.Count - 1);
        NewEntry = "";
        Status = $"{Entries.Count} entries";
    }

    [RelayCommand]
    private void Remove()
    {
        if (Selected is null) return;
        Entries.Remove(Selected);
    }

    [RelayCommand]
    private void Clear() { Entries.Clear(); Status = "Cleared"; }
}

public sealed class ClipboardEntry
{
    public DateTime When { get; set; }
    public string Text { get; set; } = "";
    public string Preview => Text.Length > 80 ? Text[..80] + "…" : Text;
}
