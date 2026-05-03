using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Konscious.Security.Cryptography;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// Encrypted cross-device backup of the user's Platytalk state, gated by a
    /// passphrase that is *separate* from the Microsoft sign-in. The relay
    /// only ever sees opaque ciphertext + a base64 salt + KDF parameters; it
    /// cannot read or recover the contents.
    ///
    /// Cipher format (versioned):
    ///   byte 0       = format version (currently 1)
    ///   bytes 1..12  = AES-GCM nonce (12 bytes)
    ///   bytes 13..28 = AES-GCM tag (16 bytes)
    ///   bytes 29..N  = ciphertext
    ///
    /// KDF: Argon2id(passphrase, salt, m=65536 KiB, t=3, p=1, len=32).
    /// Salt: 16 random bytes generated on every PUT.
    /// </summary>
    public sealed class PlatytalkBackupService
    {
        private const byte FormatVersion = 1;
        private const int SaltLength = 16;
        private const int NonceLength = 12;
        private const int TagLength = 16;
        private const int KeyLength = 32;

        // Argon2id defaults — secure for desktop, derives in ~0.5–1s.
        public int MemoryKiB { get; init; } = 65536;
        public int Iterations { get; init; } = 3;
        public int Parallelism { get; init; } = 1;

        private readonly PlatytalkRelayClient _relay;

        public PlatytalkBackupService(PlatytalkRelayClient relay)
        {
            _relay = relay;
        }

        public async Task<BackupPutResponse> CreateAsync(BackupSnapshot snapshot, string passphrase, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required.", nameof(passphrase));
            var salt = RandomNumberGenerator.GetBytes(SaltLength);
            var key = DeriveKey(passphrase, salt);
            try
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(snapshot);
                var nonce = RandomNumberGenerator.GetBytes(NonceLength);
                var ciphertext = new byte[json.Length];
                var tag = new byte[TagLength];
                using (var aes = new AesGcm(key, TagLength))
                    aes.Encrypt(nonce, json, ciphertext, tag);
                CryptographicOperations.ZeroMemory(json);

                var blob = new byte[1 + NonceLength + TagLength + ciphertext.Length];
                blob[0] = FormatVersion;
                Buffer.BlockCopy(nonce, 0, blob, 1, NonceLength);
                Buffer.BlockCopy(tag, 0, blob, 1 + NonceLength, TagLength);
                Buffer.BlockCopy(ciphertext, 0, blob, 1 + NonceLength + TagLength, ciphertext.Length);

                return await _relay.PutBackupAsync(
                    Convert.ToBase64String(blob),
                    Convert.ToBase64String(salt),
                    new
                    {
                        algo = "argon2id",
                        m = MemoryKiB,
                        t = Iterations,
                        p = Parallelism,
                        len = KeyLength,
                    },
                    cipherAlg: "aes-256-gcm",
                    ct: ct).ConfigureAwait(false);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        public async Task<BackupSnapshot?> RestoreAsync(string passphrase, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(passphrase)) throw new ArgumentException("Passphrase required.", nameof(passphrase));
            var blob = await _relay.GetBackupAsync(ct).ConfigureAwait(false);
            if (blob is null) return null;

            var salt = Convert.FromBase64String(blob.KdfSalt);
            // Honor server-stored params if present (in case defaults change in future).
            int m = MemoryKiB, t = Iterations, p = Parallelism, len = KeyLength;
            if (blob.KdfParams is { } kp)
            {
                if (kp.TryGetValue("m", out var em) && em.TryGetInt32(out var mv)) m = mv;
                if (kp.TryGetValue("t", out var et) && et.TryGetInt32(out var tv)) t = tv;
                if (kp.TryGetValue("p", out var ep) && ep.TryGetInt32(out var pv)) p = pv;
                if (kp.TryGetValue("len", out var el) && el.TryGetInt32(out var lv)) len = lv;
                if (kp.TryGetValue("algo", out var ea) && ea.ValueKind == JsonValueKind.String &&
                    !string.Equals(ea.GetString(), "argon2id", StringComparison.OrdinalIgnoreCase))
                    throw new CryptographicException("Unsupported KDF algorithm in backup metadata.");
            }

            var key = DeriveKey(passphrase, salt, m, t, p, len);
            try
            {
                var bytes = Convert.FromBase64String(blob.CipherBlob);
                if (bytes.Length < 1 + NonceLength + TagLength + 1) throw new CryptographicException("Backup blob truncated.");
                if (bytes[0] != FormatVersion) throw new CryptographicException("Unsupported backup format version.");
                var nonce = new byte[NonceLength];
                var tag = new byte[TagLength];
                var ciphertext = new byte[bytes.Length - 1 - NonceLength - TagLength];
                Buffer.BlockCopy(bytes, 1, nonce, 0, NonceLength);
                Buffer.BlockCopy(bytes, 1 + NonceLength, tag, 0, TagLength);
                Buffer.BlockCopy(bytes, 1 + NonceLength + TagLength, ciphertext, 0, ciphertext.Length);
                var plaintext = new byte[ciphertext.Length];
                using (var aes = new AesGcm(key, TagLength))
                    aes.Decrypt(nonce, ciphertext, tag, plaintext);
                try
                {
                    return JsonSerializer.Deserialize<BackupSnapshot>(plaintext);
                }
                finally { CryptographicOperations.ZeroMemory(plaintext); }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
            }
        }

        public Task<BackupInfoResponse?> GetInfoAsync(CancellationToken ct = default) => _relay.GetBackupInfoAsync(ct);
        public Task DeleteAsync(CancellationToken ct = default) => _relay.DeleteBackupAsync(ct);

        private byte[] DeriveKey(string passphrase, byte[] salt) =>
            DeriveKey(passphrase, salt, MemoryKiB, Iterations, Parallelism, KeyLength);

        private static byte[] DeriveKey(string passphrase, byte[] salt, int memoryKiB, int iterations, int parallelism, int len)
        {
            using var argon2 = new Argon2id(Encoding.UTF8.GetBytes(passphrase));
            argon2.Salt = salt;
            argon2.MemorySize = memoryKiB;
            argon2.Iterations = iterations;
            argon2.DegreeOfParallelism = parallelism;
            return argon2.GetBytes(len);
        }
    }

    /// <summary>
    /// Plain-text portable representation of a Platytalk account that can be
    /// serialized, encrypted, and restored on another device. Identity private
    /// keys are *not* included by default — they remain device-bound. To allow
    /// full account portability, set <see cref="IncludePrivateIdentity"/> when
    /// constructing.
    /// </summary>
    public sealed class BackupSnapshot
    {
        public int FormatVersion { get; set; } = 1;
        public string? UserId { get; set; }
        public string? Handle { get; set; }
        public string? DisplayName { get; set; }
        public string? IdentityKeyPublicBase64 { get; set; }
        public string? IdentityKeyPrivateBase64 { get; set; }
        public string? SigningKeyPublicBase64 { get; set; }
        public string? SigningKeyPrivateBase64 { get; set; }
        public List<BackupContact> Contacts { get; set; } = new();
        public List<BackupConversation> Conversations { get; set; } = new();
        public List<BackupGroup> Groups { get; set; } = new();
        public string? CreatedUtc { get; set; }
    }

    public sealed class BackupContact
    {
        public string ContactId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? IdentityKeyPublicBase64 { get; set; }
        public string? Handle { get; set; }
        public string? PhoneE164 { get; set; }
        public string? Email { get; set; }
        public bool IsVerified { get; set; }
    }

    public sealed class BackupConversation
    {
        public string ConversationId { get; set; } = string.Empty;
        public string Kind { get; set; } = "Direct";
        public string Title { get; set; } = string.Empty;
        public List<string> ParticipantIds { get; set; } = new();
        public int DisappearingSeconds { get; set; }
    }

    public sealed class BackupGroup
    {
        public string GroupId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OwnerUserId { get; set; } = string.Empty;
        public int DisappearingSeconds { get; set; }
        public bool InvitesAdminOnly { get; set; }
        public List<string> MemberIds { get; set; } = new();
    }
}
