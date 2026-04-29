using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

/// <summary>AES-256-GCM file encryption with PBKDF2-SHA256 (200k) key derivation.</summary>
public partial class FileEncryptionViewModel : ObservableObject
{
    private const int KeyLen = 32, NonceLen = 12, TagLen = 16, SaltLen = 16, Iter = 200_000;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("PTENC1\0\0");

    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _status = "";

    [RelayCommand]
    public async Task EncryptAsync() => await DoAsync(true);

    [RelayCommand]
    public async Task DecryptAsync() => await DoAsync(false);

    private async Task DoAsync(bool encrypt)
    {
        if (!File.Exists(Input)) { Status = "Input file not found"; return; }
        if (string.IsNullOrEmpty(Password)) { Status = "Password required"; return; }
        if (string.IsNullOrEmpty(Output))
            Output = encrypt ? Input + ".enc"
                             : (Input.EndsWith(".enc") ? Input[..^4] : Input + ".dec");

        try
        {
            await Task.Run(() =>
            {
                if (encrypt) EncryptFile(Input, Output, Password);
                else DecryptFile(Input, Output, Password);
            });
            Status = encrypt ? $"Encrypted → {Output}" : $"Decrypted → {Output}";
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }

    private static void EncryptFile(string inPath, string outPath, string password)
    {
        var plain = File.ReadAllBytes(inPath);
        var salt = RandomNumberGenerator.GetBytes(SaltLen);
        var nonce = RandomNumberGenerator.GetBytes(NonceLen);
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iter, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(KeyLen);
        var ct = new byte[plain.Length];
        var tag = new byte[TagLen];
        using (var aes = new AesGcm(key, TagLen))
            aes.Encrypt(nonce, plain, ct, tag);
        using var fs = File.Create(outPath);
        fs.Write(Magic);
        fs.Write(salt); fs.Write(nonce); fs.Write(tag); fs.Write(ct);
    }

    private static void DecryptFile(string inPath, string outPath, string password)
    {
        var data = File.ReadAllBytes(inPath);
        if (data.Length < Magic.Length + SaltLen + NonceLen + TagLen)
            throw new InvalidDataException("File too small");
        for (int i = 0; i < Magic.Length; i++)
            if (data[i] != Magic[i]) throw new InvalidDataException("Not a PTENC1 file");
        int o = Magic.Length;
        var salt = data[o..(o + SaltLen)]; o += SaltLen;
        var nonce = data[o..(o + NonceLen)]; o += NonceLen;
        var tag = data[o..(o + TagLen)]; o += TagLen;
        var ct = data[o..];
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, Iter, HashAlgorithmName.SHA256);
        var key = pbkdf2.GetBytes(KeyLen);
        var pt = new byte[ct.Length];
        using (var aes = new AesGcm(key, TagLen))
            aes.Decrypt(nonce, ct, tag, pt);
        File.WriteAllBytes(outPath, pt);
    }
}
