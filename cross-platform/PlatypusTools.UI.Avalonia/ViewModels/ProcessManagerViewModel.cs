using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ProcessManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private ProcessEntry? _selected;

    public ObservableCollection<ProcessEntry> Processes { get; } = new();

    public ProcessManagerViewModel() { _ = RefreshAsync(); }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await Task.Run(() =>
        {
            var snapshot = Process.GetProcesses()
                .Select(p =>
                {
                    try { return new ProcessEntry { Id = p.Id, Name = p.ProcessName,
                        WorkingSetMB = p.WorkingSet64 / (1024.0 * 1024.0),
                        Threads = p.Threads.Count }; }
                    catch { return null; }
                })
                .Where(e => e is not null)
                .Cast<ProcessEntry>()
                .OrderByDescending(e => e.WorkingSetMB)
                .ToList();
            global::Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                Processes.Clear();
                foreach (var e in snapshot)
                {
                    if (string.IsNullOrEmpty(Filter) ||
                        e.Name.Contains(Filter, StringComparison.OrdinalIgnoreCase))
                        Processes.Add(e);
                }
                Status = $"{Processes.Count} processes";
            });
        });
    }

    [RelayCommand]
    private void Kill()
    {
        if (Selected is null) return;
        try { using var p = Process.GetProcessById(Selected.Id); p.Kill(); Status = $"Killed PID {Selected.Id}"; }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
        _ = RefreshAsync();
    }
}

public sealed class ProcessEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public double WorkingSetMB { get; set; }
    public int Threads { get; set; }
    public string MemoryDisplay => $"{WorkingSetMB:F1} MB";
}
