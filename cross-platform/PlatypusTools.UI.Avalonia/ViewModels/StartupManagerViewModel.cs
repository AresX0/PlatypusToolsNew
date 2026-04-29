using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class StartupManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private StartupEntry? _selected;
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newCommand = "";

    public ObservableCollection<StartupEntry> Items { get; } = new();
    public string AutostartFolder { get; }

    public StartupManagerViewModel()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (ShellHelper.IsWindows)
            AutostartFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Microsoft", "Windows", "Start Menu", "Programs", "Startup");
        else if (ShellHelper.IsMac)
            AutostartFolder = Path.Combine(home, "Library", "LaunchAgents");
        else
            AutostartFolder = Path.Combine(home, ".config", "autostart");
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        try
        {
            if (!Directory.Exists(AutostartFolder)) { Status = "Autostart folder not present"; return; }
            foreach (var f in Directory.EnumerateFiles(AutostartFolder).OrderBy(f => f))
                Items.Add(new StartupEntry { Path = f, Name = Path.GetFileName(f) });
            Status = $"{Items.Count} entries";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewName) || string.IsNullOrWhiteSpace(NewCommand))
        { Status = "Name and command required"; return; }
        try
        {
            Directory.CreateDirectory(AutostartFolder);
            string path, content;
            if (ShellHelper.IsWindows)
            {
                path = Path.Combine(AutostartFolder, NewName + ".bat");
                content = $"@echo off\n{NewCommand}\n";
            }
            else if (ShellHelper.IsMac)
            {
                path = Path.Combine(AutostartFolder, $"com.platypustools.{NewName}.plist");
                content = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
                    "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
                    "<plist version=\"1.0\">\n<dict>\n" +
                    $"  <key>Label</key><string>com.platypustools.{NewName}</string>\n" +
                    $"  <key>ProgramArguments</key><array><string>/bin/sh</string><string>-c</string><string>{System.Net.WebUtility.HtmlEncode(NewCommand)}</string></array>\n" +
                    "  <key>RunAtLoad</key><true/>\n" +
                    "</dict>\n</plist>\n";
            }
            else
            {
                path = Path.Combine(AutostartFolder, NewName + ".desktop");
                content = $"[Desktop Entry]\nType=Application\nName={NewName}\nExec={NewCommand}\nX-GNOME-Autostart-enabled=true\n";
            }
            File.WriteAllText(path, content);
            Status = $"Created {path}";
            NewName = ""; NewCommand = "";
            Refresh();
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        try { File.Delete(Selected.Path); Status = "Deleted"; Refresh(); }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }
}

public sealed class StartupEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
}
