using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class CommandPaletteViewModel : ObservableObject
{
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _status = "Type to search every feature, double-click or press Go to navigate.";
    [ObservableProperty] private CommandPaletteEntry? _selected;

    public ObservableCollection<CommandPaletteEntry> All { get; } = new();
    public ObservableCollection<CommandPaletteEntry> Results { get; } = new();

    /// <summary>Set by MainWindowViewModel after Categories are built.</summary>
    public void Populate(MainWindowViewModel main)
    {
        All.Clear();
        foreach (var cat in main.AllCategories)
            foreach (var item in cat.Items)
                All.Add(new CommandPaletteEntry(cat.Title, item.Title, item.Icon, item.Content.GetType().Name));
        Refresh();
    }

    partial void OnQueryChanged(string value) => Refresh();

    private void Refresh()
    {
        Results.Clear();
        var q = (Query ?? "").Trim();
        var src = string.IsNullOrEmpty(q)
            ? All
            : (System.Collections.Generic.IEnumerable<CommandPaletteEntry>)All
                .Where(e => e.Title.Contains(q, System.StringComparison.OrdinalIgnoreCase)
                         || e.Category.Contains(q, System.StringComparison.OrdinalIgnoreCase));
        foreach (var r in src.Take(60)) Results.Add(r);
        Status = $"{Results.Count} of {All.Count}";
    }

    [RelayCommand]
    private void Go()
    {
        if (Selected is null) return;
        var win = global::Avalonia.Application.Current?.ApplicationLifetime as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime;
        if (win?.MainWindow?.DataContext is MainWindowViewModel mvm)
            mvm.NavigateToVmTypeName(Selected.VmTypeName);
    }
}

public sealed record CommandPaletteEntry(string Category, string Title, string Icon, string VmTypeName);
