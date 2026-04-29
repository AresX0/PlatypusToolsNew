using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class BulkChecksumViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _algorithm = "SHA256";
    [ObservableProperty] private bool _recurse = true;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private double _progress;

    public ObservableCollection<ChecksumRow> Rows { get; } = new();
    public string[] Algorithms { get; } = { "SHA256", "SHA1", "MD5", "SHA512" };

    public BulkChecksumViewModel() { Root = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments); }

    [RelayCommand]
    public async Task ComputeAsync()
    {
        if (IsRunning || !Directory.Exists(Root)) { Status = "Root not found"; return; }
        Rows.Clear();
        IsRunning = true;
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var files = Directory.EnumerateFiles(Root, "*",
            Recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToArray();
        var total = files.Length;
        var done = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var f in files)
                {
                    if (token.IsCancellationRequested) break;
                    string hash = "(error)";
                    long size = 0;
                    try { size = new FileInfo(f).Length; hash = HashFile(f, Algorithm, token); }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                    var row = new ChecksumRow { Path = f, Hash = hash, Bytes = size };
                    global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Rows.Add(row));
                    done++;
                    Progress = total == 0 ? 0 : (double)done / total * 100;
                }
            }, token);
            Status = $"{done}/{total} hashed";
        }
        catch (OperationCanceledException) { Status = "Cancelled"; }
        finally { IsRunning = false; _cts?.Dispose(); _cts = null; }
    }

    [RelayCommand] private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    public async Task ExportManifestAsync(string outPath)
    {
        if (string.IsNullOrEmpty(outPath)) return;
        var sb = new StringBuilder();
        sb.AppendLine($"# {Algorithm} manifest generated {DateTime.Now:O}");
        foreach (var r in Rows) sb.AppendLine($"{r.Hash}  {r.Path}");
        await File.WriteAllTextAsync(outPath, sb.ToString());
        Status = $"Wrote {outPath}";
    }

    private static string HashFile(string path, string algo, CancellationToken token)
    {
        using HashAlgorithm h = algo switch
        {
            "SHA1" => SHA1.Create(),
            "MD5" => MD5.Create(),
            "SHA512" => SHA512.Create(),
            _ => SHA256.Create()
        };
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        var buf = new byte[1 << 20];
        int read;
        while ((read = fs.Read(buf, 0, buf.Length)) > 0)
        {
            token.ThrowIfCancellationRequested();
            h.TransformBlock(buf, 0, read, null, 0);
        }
        h.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(h.Hash!).ToLowerInvariant();
    }
}

public sealed class ChecksumRow
{
    public string Path { get; set; } = "";
    public string Hash { get; set; } = "";
    public long Bytes { get; set; }
    public string SizeDisplay => Bytes switch
    {
        < 1024 => $"{Bytes} B",
        < 1024L * 1024 => $"{Bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{Bytes / (1024.0 * 1024):F1} MB",
        _ => $"{Bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
