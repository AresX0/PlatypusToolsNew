using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SecureWipeViewModel : ObservableObject
{
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private int _passes = 3;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isRunning;

    [RelayCommand]
    public async Task WipeAsync()
    {
        if (IsRunning) return;
        if (!File.Exists(Path)) { Status = "File not found"; return; }
        IsRunning = true;
        try
        {
            await Task.Run(() =>
            {
                var info = new FileInfo(Path);
                var len = info.Length;
                var buf = new byte[1 << 16]; // 64K chunks
                for (int p = 0; p < Passes; p++)
                {
                    using var fs = new FileStream(Path, FileMode.Open, FileAccess.Write, FileShare.None);
                    long written = 0;
                    while (written < len)
                    {
                        var chunk = (int)Math.Min(buf.Length, len - written);
                        if (p == Passes - 1) Array.Clear(buf, 0, chunk);
                        else RandomNumberGenerator.Fill(buf.AsSpan(0, chunk));
                        fs.Write(buf, 0, chunk);
                        written += chunk;
                    }
                    fs.Flush(true);
                }
                File.Delete(Path);
            });
            Status = "Wiped + deleted.";
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
        finally { IsRunning = false; }
    }
}
