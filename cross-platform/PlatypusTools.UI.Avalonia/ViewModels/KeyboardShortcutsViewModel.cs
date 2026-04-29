using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class KeyboardShortcutsViewModel : ObservableObject
{
    public ObservableCollection<ShortcutRow> Rows { get; } = new()
    {
        new("Ctrl+F",       "Focus sidebar filter"),
        new("Ctrl+P",       "Open Command Palette (in-page)"),
        new("Ctrl+T",       "Toggle dark / light theme"),
        new("Ctrl+,",       "Open Settings"),
        new("F1",           "About"),
        new("Esc",          "Cancel running operation (where applicable)"),
        new("Ctrl+S",       "Save (text editor / vault)"),
        new("Ctrl+O",       "Open file (file pickers in pages)"),
    };
}

public sealed record ShortcutRow(string Keys, string Action);
