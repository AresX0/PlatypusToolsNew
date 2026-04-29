using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ForensicsAnalyzerViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private int _minStrings = 6;
    [ObservableProperty] private int _maxStrings = 500;
    [ObservableProperty] private string _status = "Pick a binary, Analyze. Extracts ASCII strings, hashes, and basic header.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrEmpty(Input) || !File.Exists(Input)) { Status = "Pick a file"; return; }
        IsBusy = true; Output = ""; Status = "Analyzing…";
        var sb = new StringBuilder();
        await Task.Run(() =>
        {
            byte[] bytes = File.ReadAllBytes(Input);
            sb.AppendLine($"== {Input} ({bytes.Length:n0} bytes) ==");
            sb.AppendLine($"MD5    : {Hex(MD5.HashData(bytes))}");
            sb.AppendLine($"SHA1   : {Hex(SHA1.HashData(bytes))}");
            sb.AppendLine($"SHA256 : {Hex(SHA256.HashData(bytes))}");
            sb.AppendLine();
            sb.AppendLine("== Header (first 16 bytes hex) ==");
            sb.AppendLine(string.Join(' ', bytes.Take(16).Select(b => b.ToString("x2"))));
            sb.AppendLine();
            sb.Append("Detected: ");
            if (bytes.Length >= 4 && bytes[0] == 0x4D && bytes[1] == 0x5A) sb.AppendLine("PE (Windows EXE/DLL)");
            else if (bytes.Length >= 4 && bytes[0] == 0x7F && bytes[1] == 'E' && bytes[2] == 'L' && bytes[3] == 'F') sb.AppendLine("ELF (Linux)");
            else if (bytes.Length >= 4 && (bytes[0] == 0xCF || bytes[0] == 0xCE) && bytes[1] == 0xFA && bytes[2] == 0xED && bytes[3] == 0xFE) sb.AppendLine("Mach-O");
            else if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50) sb.AppendLine("PNG");
            else if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8) sb.AppendLine("JPEG");
            else if (bytes.Length >= 4 && bytes[0] == 0x25 && bytes[1] == 0x50) sb.AppendLine("PDF");
            else if (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B) sb.AppendLine("ZIP/Office");
            else sb.AppendLine("Unknown");
            sb.AppendLine();
            sb.AppendLine($"== Strings (>= {MinStrings} chars, max {MaxStrings}) ==");
            int count = 0; var cur = new StringBuilder();
            foreach (var b in bytes)
            {
                if (b >= 0x20 && b < 0x7F) cur.Append((char)b);
                else
                {
                    if (cur.Length >= MinStrings) { sb.AppendLine(cur.ToString()); if (++count >= MaxStrings) break; }
                    cur.Clear();
                }
            }
            if (cur.Length >= MinStrings && count < MaxStrings) sb.AppendLine(cur.ToString());
        });
        Output = sb.ToString();
        Status = "Done";
        IsBusy = false;
    }

    private static string Hex(byte[] b) => string.Concat(b.Select(x => x.ToString("x2")));
}
