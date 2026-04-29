using System;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

/// <summary>Simple persistent activity log (append-only file under AppDataDir/activity.log).</summary>
public partial class ActivityLogViewModel : ObservableObject
{
    private readonly string _logPath;
    [ObservableProperty] private string _content = "";
    [ObservableProperty] private string _newEntry = "";

    public string LogPath => _logPath;

    public ActivityLogViewModel()
    {
        _logPath = Path.Combine(ShellHelper.AppDataDir, "activity.log");
        _ = ReloadAsync();
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        try
        {
            if (!File.Exists(_logPath)) { Content = ""; return; }
            Content = await File.ReadAllTextAsync(_logPath);
        }
        catch (Exception ex) { Content = $"(error reading log: {ex.Message})"; }
    }

    [RelayCommand]
    public async Task AppendAsync()
    {
        if (string.IsNullOrWhiteSpace(NewEntry)) return;
        var line = $"[{DateTime.Now:O}] {NewEntry}\n";
        await File.AppendAllTextAsync(_logPath, line);
        NewEntry = "";
        await ReloadAsync();
    }

    [RelayCommand]
    public async Task ClearAsync()
    {
        if (File.Exists(_logPath)) File.Delete(_logPath);
        Content = "";
        await Task.CompletedTask;
    }
}
