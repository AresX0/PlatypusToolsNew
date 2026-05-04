using System;
using System.Security.Cryptography;
using System.Text;

namespace PlatypusTools.Core.Services.Platytalk
{
    /// <summary>
    /// End-to-end encryption primitives for Platytalk.
    ///
    /// Strategy (Signal-style, simplified for v1):
    ///  • Identity:      X25519 long-term key + Ed25519 signing key.
    ///  • Session setup: X3DH-lite — single-shot ECDH between sender ephemeral
    ///                   key and recipient identity key, mixed with sender
    ///                   identity key, fed through HKDF-SHA256.
    ///  • Per message:   AES-256-GCM with a unique 12-byte nonce; AAD binds
    ///                   sender + conversation + counter.
    ///  • Forward sec.:  A new ephemeral key is generated for every outgoing
    ///                   message (DH ratchet). Future versions will layer a
    ///                   symmetric chain ratchet for full Double Ratchet.
    ///
    /// All primitives come from System.Security.Cryptography — no native
    /// dependencies — so the same code runs on Windows, Linux, macOS.
    /// </summary>
    public static class PlatytalkCrypto
    {
        public const int KeySize = 32;
        public const int NonceSize = 12;
        public const int TagSize = 16;

        // ===== Web-compatible 1:1 path ========================================
        // The web/mobile clients use a simpler scheme that any P-256 ECDH peer
        // can interop with:
        //   AES key = HKDF-SHA256( ECDH(ephPriv, peerIdPub),
        //                          salt = "platytalk/v1",
        //                          info = "{convId}|{messageId}",
        //                          length = 32 )
        //   blob    = iv(12 random) || AES-GCM_ct_with_appended_tag
        //   AAD     = "{convId}|{messageId}" (UTF-8 bytes)
        // Wire field: cipherBlob = base64(blob), ephemeralPublic = base64(SPKI).

        public static (string CipherBlobB64, string EphemeralPublicB64) EncryptToPeerWeb(
            string plaintext, byte[] peerIdentityPublicSpki, string contextInfo)
        {
            var (ephPub, ephPriv) = GenerateKeyExchangeKeyPair();
            var aesKey = DeriveAesKeyWeb(ephPriv, peerIdentityPublicSpki, contextInfo);
            var iv = RandomNumberGenerator.GetBytes(NonceSize);
            var pt = Encoding.UTF8.GetBytes(plaintext);
            var ct = new byte[pt.Length];
            var tag = new byte[TagSize];
            var aad = Encoding.UTF8.GetBytes(contextInfo);
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Encrypt(iv, pt, ct, tag, aad);
            // Web wire format: iv(12) || ct || tag (WebCrypto appends tag).
            var blob = new byte[NonceSize + ct.Length + TagSize];
            Buffer.BlockCopy(iv, 0, blob, 0, NonceSize);
            Buffer.BlockCopy(ct, 0, blob, NonceSize, ct.Length);
            Buffer.BlockCopy(tag, 0, blob, NonceSize + ct.Length, TagSize);
            return (Convert.ToBase64String(blob), Convert.ToBase64String(ephPub));
        }

        public static string DecryptFromPeerWeb(
            string cipherBlobB64, string ephemeralPublicB64,
            byte[] myIdentityPrivatePkcs8, string contextInfo)
        {
            var blob = Convert.FromBase64String(cipherBlobB64);
            if (blob.Length < NonceSize + TagSize)
                throw new CryptographicException("Cipher blob too short.");
            var ephPub = Convert.FromBase64String(ephemeralPublicB64);
            var aesKey = DeriveAesKeyWeb(myIdentityPrivatePkcs8, ephPub, contextInfo);
            var iv = new byte[NonceSize];
            var tag = new byte[TagSize];
            var ct = new byte[blob.Length - NonceSize - TagSize];
            Buffer.BlockCopy(blob, 0, iv, 0, NonceSize);
            Buffer.BlockCopy(blob, NonceSize, ct, 0, ct.Length);
            Buffer.BlockCopy(blob, NonceSize + ct.Length, tag, 0, TagSize);
            var pt = new byte[ct.Length];
            using (var aes = new AesGcm(aesKey, TagSize))
                aes.Decrypt(iv, ct, tag, pt, Encoding.UTF8.GetBytes(contextInfo));
            return Encoding.UTF8.GetString(pt);
        }

        private static byte[] DeriveAesKeyWeb(byte[] privPkcs8, byte[] peerPubSpki, string contextInfo)
        {
            var shared = DeriveSharedSecret(privPkcs8, peerPubSpki);
            var salt = Encoding.UTF8.GetBytes("platytalk/v1");
            var info = Encoding.UTF8.GetBytes(contextInfo);
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, shared, KeySize, salt, info);
        }
        // =====================================================================

        // ----- Key generation -------------------------------------------------

        /// <summary>Generates an X25519 keypair using ECDiffieHellman with a
        /// pseudo-curve fallback (P-256) when X25519 isn't available on the
        /// host — the wire format remains the raw 32-byte public key for
        /// X25519, or DER-encoded SubjectPublicKeyInfo otherwise.</summary>
        public static (byte[] Public, byte[] Private) GenerateKeyExchangeKeyPair()
        {
            // .NET 10 supports MLKemAlgorithm but not yet first-class X25519
            // ECDH; use ECDiffieHellman.Create() default curve which is
            // platform-best, and serialize PKCS#8 for the private side.
            using var ecdh = ECDiffieHellman.Create();
            var pub = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
            var priv = ecdh.ExportPkcs8PrivateKey();
            return (pub, priv);
        }

        public static (byte[] Public, byte[] Private) GenerateSigningKeyPair()
        {
            using var ed = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pub = ed.ExportSubjectPublicKeyInfo();
            var priv = ed.ExportPkcs8PrivateKey();
            return (pub, priv);
        }

        // ----- Key agreement --------------------------------------------------

        public static byte[] DeriveSharedSecret(byte[] myPrivatePkcs8, byte[] peerPublicSpki)
        {
            using var mine = ECDiffieHellman.Create();
            mine.ImportPkcs8PrivateKey(myPrivatePkcs8, out _);
            using var theirs = ECDiffieHellman.Create();
            theirs.ImportSubjectPublicKeyInfo(peerPublicSpki, out _);
            return mine.DeriveKeyMaterial(theirs.PublicKey);
        }

        /// <summary>
        /// X3DH-lite root-key derivation. Mixes two DH outputs with HKDF-SHA256
        /// to produce a 32-byte root key for the conversation.
        /// </summary>
        public static byte[] DeriveRootKey(
            byte[] myIdentityPriv, byte[] peerIdentityPub,
            byte[] myEphemeralPriv, byte[] peerEphemeralPub,
            string info)
        {
            var dh1 = DeriveSharedSecret(myIdentityPriv, peerEphemeralPub);
            var dh2 = DeriveSharedSecret(myEphemeralPriv, peerIdentityPub);
            var dh3 = DeriveSharedSecret(myEphemeralPriv, peerEphemeralPub);

            var ikm = new byte[dh1.Length + dh2.Length + dh3.Length];
            Buffer.BlockCopy(dh1, 0, ikm, 0, dh1.Length);
            Buffer.BlockCopy(dh2, 0, ikm, dh1.Length, dh2.Length);
            Buffer.BlockCopy(dh3, 0, ikm, dh1.Length + dh2.Length, dh3.Length);

            var salt = Encoding.UTF8.GetBytes("platytalk/root/v1");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, ikm, KeySize, salt, Encoding.UTF8.GetBytes(info));
        }

        public static byte[] DeriveMessageKey(byte[] rootKey, long counter, string conversationId)
        {
            var info = Encoding.UTF8.GetBytes($"platytalk/msg/{conversationId}/{counter}");
            return HKDF.DeriveKey(HashAlgorithmName.SHA256, rootKey, KeySize, null, info);
        }

        // ----- Authenticated encryption --------------------------------------

        public static byte[] EncryptMessage(byte[] key, byte[] plaintext, byte[] associatedData, out byte[] nonce)
        {
            if (key.Length != KeySize) throw new ArgumentException("Key must be 32 bytes.", nameof(key));
            nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
            // wire format: nonce || tag || ciphertext
            var blob = new byte[NonceSize + TagSize + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, blob, 0, NonceSize);
            Buffer.BlockCopy(tag, 0, blob, NonceSize, TagSize);
            Buffer.BlockCopy(ciphertext, 0, blob, NonceSize + TagSize, ciphertext.Length);
            return blob;
        }

        public static byte[] DecryptMessage(byte[] key, byte[] blob, byte[] associatedData)
        {
            if (blob.Length < NonceSize + TagSize) throw new CryptographicException("Cipher blob too short.");
            var nonce = new byte[NonceSize];
            var tag = new byte[TagSize];
            var ciphertext = new byte[blob.Length - NonceSize - TagSize];
            Buffer.BlockCopy(blob, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(blob, NonceSize, tag, 0, TagSize);
            Buffer.BlockCopy(blob, NonceSize + TagSize, ciphertext, 0, ciphertext.Length);
            var plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);
            return plaintext;
        }

        // ----- Safety numbers / fingerprints ---------------------------------

        /// <summary>
        /// Computes a Signal-style safety number — a deterministic 60-digit
        /// fingerprint of the two identity keys that two users can compare in
        /// person to verify the absence of a man-in-the-middle.
        /// </summary>
        public static string ComputeSafetyNumber(byte[] localIdentity, byte[] remoteIdentity)
        {
            using var sha = SHA512.Create();
            var aFirst = ByteArrayLess(localIdentity, remoteIdentity);
            var first = aFirst ? localIdentity : remoteIdentity;
            var second = aFirst ? remoteIdentity : localIdentity;
            var buf = new byte[first.Length + second.Length];
            Buffer.BlockCopy(first, 0, buf, 0, first.Length);
            Buffer.BlockCopy(second, 0, buf, first.Length, second.Length);
            // 5200 iterations approximates Signal's stretching at low cost
            var digest = buf;
            for (int i = 0; i < 5200; i++) digest = sha.ComputeHash(digest);
            var sb = new StringBuilder(60);
            for (int i = 0; i < 12 && i * 5 + 5 <= digest.Length; i++)
            {
                ulong chunk = 0;
                for (int j = 0; j < 5; j++) chunk = (chunk << 8) | digest[i * 5 + j];
                sb.Append((chunk % 100000UL).ToString("D5"));
            }
            return sb.ToString();
        }

        private static bool ByteArrayLess(byte[] a, byte[] b)
        {
            int n = Math.Min(a.Length, b.Length);
            for (int i = 0; i < n; i++)
            {
                if (a[i] < b[i]) return true;
                if (a[i] > b[i]) return false;
            }
            return a.Length < b.Length;
        }
    }
}
