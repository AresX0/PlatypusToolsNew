using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class FileAnalyzerViewModel : ObservableObject
{
    [ObservableProperty] private string _path = "";
    [ObservableProperty] private string _report = "";
    [ObservableProperty] private string _status = "";

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (string.IsNullOrWhiteSpace(Path) || !File.Exists(Path)) { Status = "File not found"; return; }
        var sb = new StringBuilder();
        try
        {
            var fi = new FileInfo(Path);
            sb.AppendLine($"Path:       {fi.FullName}");
            sb.AppendLine($"Size:       {fi.Length:N0} bytes ({fi.Length / 1024.0 / 1024.0:F2} MB)");
            sb.AppendLine($"Created:    {fi.CreationTime}");
            sb.AppendLine($"Modified:   {fi.LastWriteTime}");
            sb.AppendLine($"Accessed:   {fi.LastAccessTime}");
            sb.AppendLine($"Attributes: {fi.Attributes}");
            sb.AppendLine();
            // Magic bytes
            using (var fs = File.OpenRead(Path))
            {
                var magic = new byte[16];
                int read = await fs.ReadAsync(magic.AsMemory());
                sb.Append("Magic:      ");
                for (int i = 0; i < read; i++) sb.Append($"{magic[i]:X2} ");
                sb.AppendLine();
                sb.Append("Magic ASCII: ");
                for (int i = 0; i < read; i++)
                {
                    char c = (char)magic[i];
                    sb.Append(char.IsControl(c) ? '.' : c);
                }
                sb.AppendLine();
                sb.AppendLine($"File type:  {DetectType(magic, read)}");
            }
            sb.AppendLine();
            sb.AppendLine("Hashing… (this may take a moment for large files)");
            Report = sb.ToString();
            Status = "Hashing…";
            await Task.Run(() =>
            {
                using var fs = File.OpenRead(Path);
                using var md5 = MD5.Create();
                using var sha1 = SHA1.Create();
                using var sha256 = SHA256.Create();
                var buf = new byte[1 << 20]; int n;
                while ((n = fs.Read(buf)) > 0)
                {
                    md5.TransformBlock(buf, 0, n, null, 0);
                    sha1.TransformBlock(buf, 0, n, null, 0);
                    sha256.TransformBlock(buf, 0, n, null, 0);
                }
                md5.TransformFinalBlock([], 0, 0);
                sha1.TransformFinalBlock([], 0, 0);
                sha256.TransformFinalBlock([], 0, 0);
                sb.AppendLine($"MD5:        {Convert.ToHexString(md5.Hash!)}");
                sb.AppendLine($"SHA1:       {Convert.ToHexString(sha1.Hash!)}");
                sb.AppendLine($"SHA256:     {Convert.ToHexString(sha256.Hash!)}");
            });
            Report = sb.ToString();
            Status = "Done";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    private static string DetectType(byte[] m, int n)
    {
        if (n < 4) return "(too small)";
        // Common magics
        if (m[0] == 0x4D && m[1] == 0x5A) return "PE executable / DLL";
        if (m[0] == 0x7F && m[1] == 'E' && m[2] == 'L' && m[3] == 'F') return "ELF (Linux executable)";
        if (m[0] == 0xCF && m[1] == 0xFA && m[2] == 0xED && m[3] == 0xFE) return "Mach-O 64-bit";
        if (m[0] == 0x50 && m[1] == 0x4B) return "ZIP / Office / JAR / APK";
        if (m[0] == 0x89 && m[1] == 'P' && m[2] == 'N' && m[3] == 'G') return "PNG image";
        if (m[0] == 0xFF && m[1] == 0xD8) return "JPEG image";
        if (m[0] == 'G' && m[1] == 'I' && m[2] == 'F') return "GIF image";
        if (m[0] == 0x25 && m[1] == 0x50 && m[2] == 0x44 && m[3] == 0x46) return "PDF";
        if (m[0] == 0x52 && m[1] == 0x49 && m[2] == 0x46 && m[3] == 0x46) return "RIFF (WAV/AVI/WebP)";
        if (m[4] == 'f' && m[5] == 't' && m[6] == 'y' && m[7] == 'p') return "MP4 / MOV / HEIC";
        if (m[0] == 0x1F && m[1] == 0x8B) return "GZIP";
        if (m[0] == '7' && m[1] == 'z') return "7-Zip";
        return "(unknown)";
    }
}
