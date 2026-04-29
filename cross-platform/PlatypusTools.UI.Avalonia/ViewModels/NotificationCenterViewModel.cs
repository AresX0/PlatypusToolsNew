using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class NotificationCenterViewModel : ObservableObject
{
    private readonly string _file = Path.Combine(ShellHelper.AppDataDir, "notifications.log");
    [ObservableProperty] private string _newMessage = "";
    [ObservableProperty] private string _newLevel = "Info";
    [ObservableProperty] private string _status = "";

    public ObservableCollection<NotificationEntry> Items { get; } = new();
    public string[] Levels { get; } = new[] { "Info", "Warning", "Error" };

    public NotificationCenterViewModel() { Load(); }

    private void Load()
    {
        Items.Clear();
        try
        {
            if (!File.Exists(_file)) { Status = "(no notifications yet)"; return; }
            foreach (var line in File.ReadAllLines(_file).Reverse())
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('|', 3);
                if (parts.Length == 3)
                    Items.Add(new NotificationEntry { Time = parts[0], Level = parts[1], Message = parts[2] });
            }
            Status = $"{Items.Count} notification(s)";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void Add()
    {
        if (string.IsNullOrWhiteSpace(NewMessage)) return;
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}|{NewLevel}|{NewMessage.Replace("\n", " ")}";
        try
        {
            File.AppendAllText(_file, line + Environment.NewLine);
            NewMessage = "";
            Load();
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }

    [RelayCommand]
    private void Clear()
    {
        try { if (File.Exists(_file)) File.Delete(_file); Items.Clear(); Status = "Cleared"; }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }

    [RelayCommand] private void Refresh() => Load();
}

public sealed class NotificationEntry
{
    public string Time { get; set; } = "";
    public string Level { get; set; } = "";
    public string Message { get; set; } = "";
}
