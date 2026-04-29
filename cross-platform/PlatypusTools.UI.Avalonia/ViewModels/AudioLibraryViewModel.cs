using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class AudioLibraryViewModel : ObservableObject
{
    private static readonly string[] AudioExt =
        { ".mp3", ".flac", ".wav", ".ogg", ".oga", ".m4a", ".aac", ".opus", ".wma", ".alac" };

    private CancellationTokenSource? _cts;

    [ObservableProperty] private string _libraryRoot = "";
    [ObservableProperty] private string _status = "Pick a folder to scan.";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _progressValue;
    [ObservableProperty] private TrackEntry? _selectedTrack;

    public ObservableCollection<TrackEntry> Tracks { get; } = new();

    public AudioLibraryViewModel()
    {
        // Default to user's Music folder
        LibraryRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
    }

    public async Task ScanAsync()
    {
        if (string.IsNullOrEmpty(LibraryRoot) || !Directory.Exists(LibraryRoot))
        { Status = "Folder does not exist."; return; }
        if (IsScanning) return;
        IsScanning = true;
        Tracks.Clear();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Status = "Scanning…";
        try
        {
            await Task.Run(() =>
            {
                var found = 0;
                foreach (var path in EnumerateSafe(LibraryRoot, token))
                {
                    if (token.IsCancellationRequested) break;
                    var ext = Path.GetExtension(path).ToLowerInvariant();
                    if (Array.IndexOf(AudioExt, ext) < 0) continue;
                    try
                    {
                        var fi = new FileInfo(path);
                        var t = new TrackEntry
                        {
                            FileName = fi.Name,
                            FullPath = fi.FullName,
                            SizeBytes = fi.Length,
                            Modified = fi.LastWriteTime,
                            Folder = fi.DirectoryName ?? ""
                        };
                        global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Tracks.Add(t));
                        found++;
                        if (found % 25 == 0)
                        {
                            var f = found;
                            global::Avalonia.Threading.Dispatcher.UIThread.Post(
                                () => Status = $"Scanning… {f} tracks");
                        }
                    }
                    catch { /* skip unreadable */ }
                }
            }, token);
            Status = $"Found {Tracks.Count} tracks in {LibraryRoot}";
        }
        catch (OperationCanceledException) { Status = $"Cancelled. {Tracks.Count} tracks found."; }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateSafe(
        string root, CancellationToken token)
    {
        var stack = new System.Collections.Generic.Stack<string>();
        stack.Push(root);
        while (stack.Count > 0 && !token.IsCancellationRequested)
        {
            var cur = stack.Pop();
            string[] subs;
            try { subs = Directory.GetDirectories(cur); }
            catch { subs = Array.Empty<string>(); }
            foreach (var s in subs) stack.Push(s);
            string[] files;
            try { files = Directory.GetFiles(cur); }
            catch { continue; }
            foreach (var f in files) yield return f;
        }
    }

    [RelayCommand] private Task Scan() => ScanAsync();

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private async Task RevealAsync()
    {
        if (SelectedTrack is null) return;
        await AppServices.FileLauncher.RevealInFileManagerAsync(SelectedTrack.FullPath)
            .ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task PlaySelectedAsync()
    {
        if (SelectedTrack is null) return;
        // Hand off to the OS default audio app via xdg-open / open / explorer.
        await AppServices.FileLauncher.OpenFileAsync(SelectedTrack.FullPath)
            .ConfigureAwait(true);
    }
}

public sealed class TrackEntry
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public string Folder { get; set; } = "";
    public long SizeBytes { get; set; }
    public DateTime Modified { get; set; }
    public string SizeDisplay => SizeBytes switch
    {
        < 1024 => $"{SizeBytes} B",
        < 1024 * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };
    public string ModifiedDisplay => Modified.ToString("yyyy-MM-dd HH:mm");
}
