using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class DuplicatesViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "Pick folder(s) to scan for duplicates.";
    [ObservableProperty] private string _selectedFolder = "";
    [ObservableProperty] private bool _recurse = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    public ObservableCollection<DuplicateGroup> Groups { get; } = new();

    private CancellationTokenSource? _cts;

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFolder)) return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        Progress = 0;
        Groups.Clear();
        Status = $"Scanning {SelectedFolder}…";

        try
        {
            var result = await DuplicatesScanner.FindDuplicatesAsync(
                new[] { SelectedFolder },
                recurse: Recurse,
                onProgress: (cur, total, msg) =>
                {
                    if (total > 0) Progress = cur * 100.0 / total;
                    Status = msg;
                },
                cancellationToken: ct).ConfigureAwait(true);

            foreach (var g in result) Groups.Add(g);

            Status = $"Done. {Groups.Count} duplicate group(s) " +
                     $"covering {Groups.Sum(g => g.Files.Count)} file(s).";

            await AppServices.Notifications.NotifyAsync("PlatypusTools",
                $"Duplicate scan complete: {Groups.Count} group(s).").ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Progress = 0;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task RevealFileAsync(string? path)
    {
        if (!string.IsNullOrEmpty(path))
            await AppServices.FileLauncher.RevealInFileManagerAsync(path).ConfigureAwait(true);
    }
}
