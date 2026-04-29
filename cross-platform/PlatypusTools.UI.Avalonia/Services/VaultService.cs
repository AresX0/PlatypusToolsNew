using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.Services;

/// <summary>
/// Cross-platform encrypted password vault. AES-256-GCM with PBKDF2-SHA256
/// (200k iterations) key derivation. Stored as a single JSON file under the
/// user's local data folder (~/.local/share/PlatypusTools on Linux, the
/// equivalent on macOS/Windows).
///
/// File format (version 1):
///   { "v":1, "salt":"<base64>", "iv":"<base64>", "tag":"<base64>",
///     "ct":"<base64>" }
///   ct = AES-GCM(plaintext = JSON serialization of <see cref="VaultPayload"/>).
/// </summary>
public sealed class VaultService
{
    private const int Iterations = 200_000;
    private const int KeyBytes = 32;
    private const int SaltBytes = 16;
    private const int IvBytes = 12;
    private const int TagBytes = 16;

    public string VaultPath { get; }

    public VaultService()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrEmpty(root))
        {
            var home = Environment.GetEnvironmentVariable("HOME") ?? ".";
            root = Path.Combine(home, ".local", "share");
        }
        var dir = Path.Combine(root, "PlatypusTools");
        Directory.CreateDirectory(dir);
        VaultPath = Path.Combine(dir, "vault.json");
    }

    public bool VaultExists => File.Exists(VaultPath);

    public async Task<VaultPayload> CreateAsync(string masterPassword)
    {
        if (string.IsNullOrEmpty(masterPassword))
            throw new ArgumentException("Master password required.", nameof(masterPassword));
        var payload = new VaultPayload();
        await SaveAsync(payload, masterPassword).ConfigureAwait(false);
        return payload;
    }

    public async Task<VaultPayload> UnlockAsync(string masterPassword)
    {
        var json = await File.ReadAllTextAsync(VaultPath).ConfigureAwait(false);
        var env = JsonSerializer.Deserialize<VaultEnvelope>(json)
                  ?? throw new InvalidDataException("Vault file is empty or corrupt.");
        var salt = Convert.FromBase64String(env.salt);
        var iv = Convert.FromBase64String(env.iv);
        var tag = Convert.FromBase64String(env.tag);
        var ct = Convert.FromBase64String(env.ct);
        var key = DeriveKey(masterPassword, salt);
        var plain = new byte[ct.Length];
        using (var aes = new AesGcm(key, TagBytes))
            aes.Decrypt(iv, ct, tag, plain);
        return JsonSerializer.Deserialize<VaultPayload>(plain)
               ?? new VaultPayload();
    }

    public async Task SaveAsync(VaultPayload payload, string masterPassword)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var iv = RandomNumberGenerator.GetBytes(IvBytes);
        var key = DeriveKey(masterPassword, salt);
        var plain = JsonSerializer.SerializeToUtf8Bytes(payload);
        var ct = new byte[plain.Length];
        var tag = new byte[TagBytes];
        using (var aes = new AesGcm(key, TagBytes))
            aes.Encrypt(iv, plain, ct, tag);
        var env = new VaultEnvelope
        {
            v = 1,
            salt = Convert.ToBase64String(salt),
            iv = Convert.ToBase64String(iv),
            tag = Convert.ToBase64String(tag),
            ct = Convert.ToBase64String(ct)
        };
        var tmp = VaultPath + ".tmp";
        await File.WriteAllTextAsync(tmp,
            JsonSerializer.Serialize(env, new JsonSerializerOptions { WriteIndented = false }))
            .ConfigureAwait(false);
        File.Move(tmp, VaultPath, overwrite: true);
    }

    private static byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(KeyBytes);
    }

    private sealed class VaultEnvelope
    {
        public int v { get; set; }
        public string salt { get; set; } = "";
        public string iv { get; set; } = "";
        public string tag { get; set; } = "";
        public string ct { get; set; } = "";
    }
}

public sealed class VaultPayload
{
    public System.Collections.Generic.List<VaultEntry> Entries { get; set; } = new();
}

public sealed class VaultEntry
{
    public string Title { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string Url { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Modified { get; set; } = DateTime.UtcNow;
}
