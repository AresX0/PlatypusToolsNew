using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class FileManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _currentPath;
    [ObservableProperty] private string _status = "Ready.";
    [ObservableProperty] private FileEntry? _selectedEntry;

    public ObservableCollection<FileEntry> Entries { get; } = new();

    public FileManagerViewModel()
    {
        _currentPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(_currentPath) || !Directory.Exists(_currentPath))
            _currentPath = Directory.GetCurrentDirectory();
        _ = RefreshAsync();
    }

    partial void OnCurrentPathChanged(string value)
    {
        _ = RefreshAsync();
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Entries.Clear();
        try
        {
            if (!Directory.Exists(CurrentPath))
            {
                Status = $"Not a directory: {CurrentPath}";
                return;
            }

            var dirs = await Task.Run(() =>
                new DirectoryInfo(CurrentPath).EnumerateDirectories()
                    .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(d => new FileEntry(d.Name, d.FullName, true, 0, d.LastWriteTime))
                    .ToList()).ConfigureAwait(true);

            var files = await Task.Run(() =>
                new DirectoryInfo(CurrentPath).EnumerateFiles()
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(f => new FileEntry(f.Name, f.FullName, false, f.Length, f.LastWriteTime))
                    .ToList()).ConfigureAwait(true);

            // ".." entry for navigation
            var parent = Directory.GetParent(CurrentPath);
            if (parent is not null)
                Entries.Add(new FileEntry("..", parent.FullName, true, 0, parent.LastWriteTime));

            foreach (var d in dirs) Entries.Add(d);
            foreach (var f in files) Entries.Add(f);

            Status = $"{dirs.Count} folder(s), {files.Count} file(s).";
        }
        catch (UnauthorizedAccessException)
        {
            Status = "Access denied.";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task ActivateAsync(FileEntry? entry)
    {
        if (entry is null) return;
        if (entry.IsDirectory)
        {
            CurrentPath = entry.FullPath;
        }
        else
        {
            await AppServices.FileLauncher.OpenFileAsync(entry.FullPath).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    public async Task RevealAsync(FileEntry? entry)
    {
        if (entry is null) return;
        await AppServices.FileLauncher.RevealInFileManagerAsync(entry.FullPath).ConfigureAwait(true);
    }

    [RelayCommand]
    public async Task TrashAsync(FileEntry? entry)
    {
        if (entry is null) return;
        var ok = await AppServices.Trash.MoveToTrashAsync(entry.FullPath).ConfigureAwait(true);
        if (ok)
        {
            Status = $"Moved to trash: {entry.Name}";
            Entries.Remove(entry);
            await AppServices.Notifications.NotifyAsync("PlatypusTools",
                $"Moved {entry.Name} to trash.").ConfigureAwait(true);
        }
        else
        {
            Status = $"Failed to trash: {entry.Name}";
        }
    }

    [RelayCommand]
    public void GoUp()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent is not null) CurrentPath = parent.FullName;
    }

    [RelayCommand]
    public void GoHome()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && Directory.Exists(home))
            CurrentPath = home;
    }
}

public sealed class FileEntry
{
    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public long SizeBytes { get; }
    public DateTime Modified { get; }

    public FileEntry(string name, string fullPath, bool isDirectory, long sizeBytes, DateTime modified)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        SizeBytes = sizeBytes;
        Modified = modified;
    }

    public string Icon => IsDirectory ? "📁" : "📄";

    public string SizeDisplay => IsDirectory ? "—" : SizeBytes switch
    {
        < 1024L => $"{SizeBytes} B",
        < 1024L * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };

    public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");
}
