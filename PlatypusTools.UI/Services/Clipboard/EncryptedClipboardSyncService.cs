using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services.ClipboardSync
{
    // E2E encrypted clipboard payload using AES-GCM with a pre-shared 32-byte key.
    // Sends to peer Remote.Server's /api/v1/clipboard endpoint; payload is opaque base64 — server stores latest blob in memory only.
    public class EncryptedClipboardSyncService
    {
        private static readonly Lazy<EncryptedClipboardSyncService> _instance = new(() => new EncryptedClipboardSyncService());
        public static EncryptedClipboardSyncService Instance => _instance.Value;

        private readonly HttpClient _http;

        public EncryptedClipboardSyncService()
        {
#pragma warning disable CA2000 // handler ownership transferred to HttpClient
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
#pragma warning restore CA2000
            _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(15) };
        }

        public string KeyFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "clipboard-key.bin");

        public byte[] LoadOrCreateKey()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KeyFilePath)!);
                if (File.Exists(KeyFilePath))
                {
                    var k = File.ReadAllBytes(KeyFilePath);
                    if (k.Length == 32) return k;
                }
            }
            catch { }
            var key = RandomNumberGenerator.GetBytes(32);
            try { File.WriteAllBytes(KeyFilePath, key); } catch { }
            return key;
        }

        private static (byte[] nonce, byte[] cipher, byte[] tag) Encrypt(byte[] key, byte[] plain)
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using var gcm = new AesGcm(key, 16);
            gcm.Encrypt(nonce, plain, cipher, tag);
            return (nonce, cipher, tag);
        }

        private static byte[] Decrypt(byte[] key, byte[] nonce, byte[] cipher, byte[] tag)
        {
            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return plain;
        }

        public async Task PushAsync(string peerBaseUrl, string apiToken, byte[] key, string text, CancellationToken ct = default)
        {
            var (nonce, cipher, tag) = Encrypt(key, Encoding.UTF8.GetBytes(text));
            var payload = new
            {
                nonce = Convert.ToBase64String(nonce),
                cipher = Convert.ToBase64String(cipher),
                tag = Convert.ToBase64String(tag)
            };
            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, peerBaseUrl.TrimEnd('/') + "/api/v1/clipboard")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            using var resp = await _http.SendAsync(req, ct);
            resp.EnsureSuccessStatusCode();
        }

        public async Task<string?> PullAsync(string peerBaseUrl, string apiToken, byte[] key, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, peerBaseUrl.TrimEnd('/') + "/api/v1/clipboard");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            using var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("nonce", out var n) || !root.TryGetProperty("cipher", out var c) || !root.TryGetProperty("tag", out var t))
                return null;
            try
            {
                var plain = Decrypt(key,
                    Convert.FromBase64String(n.GetString() ?? ""),
                    Convert.FromBase64String(c.GetString() ?? ""),
                    Convert.FromBase64String(t.GetString() ?? ""));
                return Encoding.UTF8.GetString(plain);
            }
            catch { return null; }
        }
    }
}
