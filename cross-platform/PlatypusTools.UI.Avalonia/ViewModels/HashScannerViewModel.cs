using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class HashScannerViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "Pick one or more files to compute hashes.";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private double _progress;

    public ObservableCollection<HashResult> Results { get; } = new();

    public string[] Algorithms { get; } = ["SHA256", "SHA1", "MD5", "SHA512"];

    [ObservableProperty] private string _selectedAlgorithm = "SHA256";

    private CancellationTokenSource? _cts;

    [RelayCommand]
    private async Task HashFilesAsync(System.Collections.Generic.IEnumerable<string>? paths)
    {
        if (paths is null) return;
        var list = paths.Where(File.Exists).ToList();
        if (list.Count == 0)
        {
            Status = "No valid files selected.";
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        IsBusy = true;
        Progress = 0;
        Status = $"Hashing {list.Count} file(s) with {SelectedAlgorithm}…";

        try
        {
            for (int i = 0; i < list.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var path = list[i];
                var fi = new FileInfo(path);
                string hash;
                try
                {
                    hash = await Task.Run(() => ComputeHash(path, SelectedAlgorithm, ct), ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    hash = $"<error: {ex.Message}>";
                }

                Results.Add(new HashResult
                {
                    FileName = fi.Name,
                    FullPath = fi.FullName,
                    SizeBytes = fi.Length,
                    Algorithm = SelectedAlgorithm,
                    Hash = hash
                });

                Progress = (i + 1) * 100.0 / list.Count;
            }

            Status = $"Done. {Results.Count} file(s) hashed.";
            await AppServices.Notifications.NotifyAsync("PlatypusTools",
                $"Hashed {list.Count} file(s) with {SelectedAlgorithm}.").ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            Status = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void Clear()
    {
        Results.Clear();
        Status = "Cleared.";
        Progress = 0;
    }

    private static string ComputeHash(string path, string algorithm, CancellationToken ct)
    {
        using HashAlgorithm hasher = algorithm switch
        {
            "MD5"    => MD5.Create(),
            "SHA1"   => SHA1.Create(),
            "SHA512" => SHA512.Create(),
            _        => SHA256.Create(),
        };

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: false);

        var buf = new byte[1 << 20];
        int read;
        while ((read = stream.Read(buf, 0, buf.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            hasher.TransformBlock(buf, 0, read, null, 0);
        }
        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        return Convert.ToHexString(hasher.Hash ?? Array.Empty<byte>());
    }
}

public sealed class HashResult
{
    public string FileName { get; set; } = "";
    public string FullPath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Algorithm { get; set; } = "";
    public string Hash { get; set; } = "";

    public string SizeDisplay => SizeBytes switch
    {
        < 1024L => $"{SizeBytes} B",
        < 1024L * 1024 => $"{SizeBytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):F1} MB",
        _ => $"{SizeBytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
