using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class FileIntegrityViewModel : ObservableObject
{
    [ObservableProperty] private string _manifestPath = "";
    [ObservableProperty] private string _baseDir = "";
    [ObservableProperty] private string _algorithm = "SHA256";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _status = "Load a manifest exported from Bulk Checksum.";
    public ObservableCollection<IntegrityRow> Rows { get; } = new();
    public string[] Algorithms { get; } = { "SHA256", "SHA1", "MD5", "SHA512" };

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (!File.Exists(ManifestPath)) { Status = "Manifest not found"; return; }
        IsBusy = true; Rows.Clear();
        var lines = await File.ReadAllLinesAsync(ManifestPath);
        int ok = 0, bad = 0, missing = 0;
        await Task.Run(() =>
        {
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                // Format:  <hash> *<path>  or  <hash>  <path>
                var idx = line.IndexOf(' ');
                if (idx <= 0) continue;
                var expect = line[..idx].Trim().ToLowerInvariant();
                var rest = line[(idx + 1)..].TrimStart('*', ' ');
                var path = string.IsNullOrEmpty(BaseDir) ? rest : Path.Combine(BaseDir, rest);
                string actual = "";
                string verdict;
                if (!File.Exists(path)) { verdict = "MISSING"; missing++; }
                else
                {
                    try
                    {
                        using var s = File.OpenRead(path);
                        using HashAlgorithm h = Algorithm switch
                        {
                            "SHA1" => SHA1.Create(),
                            "MD5" => MD5.Create(),
                            "SHA512" => SHA512.Create(),
                            _ => SHA256.Create()
                        };
                        actual = Convert.ToHexString(h.ComputeHash(s)).ToLowerInvariant();
                        if (actual == expect) { verdict = "OK"; ok++; }
                        else { verdict = "MISMATCH"; bad++; }
                    }
                    catch (Exception ex) { verdict = "ERROR: " + ex.Message; bad++; }
                }
                Rows.Add(new IntegrityRow { Path = path, Expected = expect, Actual = actual, Verdict = verdict });
            }
        });
        Status = $"OK: {ok}   Mismatch: {bad}   Missing: {missing}";
        IsBusy = false;
    }
}

public sealed class IntegrityRow
{
    public string Path { get; set; } = "";
    public string Expected { get; set; } = "";
    public string Actual { get; set; } = "";
    public string Verdict { get; set; } = "";
}
