using System;
using System.Collections;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class EnvironmentVariablesViewModel : ObservableObject
{
    [ObservableProperty] private EnvVarEntry? _selected;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newValue = "";
    [ObservableProperty] private string _scope = "Process"; // Process, User, Machine
    [ObservableProperty] private string _status = "";

    public ObservableCollection<EnvVarEntry> Entries { get; } = new();
    public string[] Scopes => ShellHelper.IsWindows
        ? new[] { "Process", "User", "Machine" } : new[] { "Process" };

    public EnvironmentVariablesViewModel() { Refresh(); }

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        var target = Scope switch
        {
            "User" => EnvironmentVariableTarget.User,
            "Machine" => EnvironmentVariableTarget.Machine,
            _ => EnvironmentVariableTarget.Process
        };
        IDictionary vars;
        try { vars = Environment.GetEnvironmentVariables(target); }
        catch { vars = Environment.GetEnvironmentVariables(); }
        foreach (DictionaryEntry e in vars)
            Entries.Add(new EnvVarEntry { Name = e.Key?.ToString() ?? "", Value = e.Value?.ToString() ?? "" });
        var ordered = Entries.OrderBy(e => e.Name).ToList();
        Entries.Clear();
        foreach (var e in ordered) Entries.Add(e);
        Status = $"{Entries.Count} variables ({Scope})";
    }

    partial void OnScopeChanged(string value) => Refresh();

    [RelayCommand]
    private void SetVar()
    {
        if (string.IsNullOrWhiteSpace(NewName)) { Status = "Name required"; return; }
        try
        {
            var target = Scope switch
            {
                "User" => EnvironmentVariableTarget.User,
                "Machine" => EnvironmentVariableTarget.Machine,
                _ => EnvironmentVariableTarget.Process
            };
            Environment.SetEnvironmentVariable(NewName, NewValue, target);
            Status = $"Set {NewName}";
            Refresh();
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void DeleteVar()
    {
        if (Selected is null) return;
        try
        {
            var target = Scope switch
            {
                "User" => EnvironmentVariableTarget.User,
                "Machine" => EnvironmentVariableTarget.Machine,
                _ => EnvironmentVariableTarget.Process
            };
            Environment.SetEnvironmentVariable(Selected.Name, null, target);
            Status = $"Deleted {Selected.Name}";
            Refresh();
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }
}

public sealed class EnvVarEntry
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}
