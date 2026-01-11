using System;
using System.IO;
using System.Text.Json;
using PlatypusTools.Core.Models;
using System.Collections.Generic;

namespace PlatypusTools.Core.Services
{
    public static class HiderService
    {
        public static HiderConfig GetDefaultConfig() => new HiderConfig();

        public static HiderConfig? LoadConfig(string path)
        {
            try
            {
                if (!File.Exists(path)) return GetDefaultConfig();
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<HiderConfig>(json);
                if (cfg == null) return null;

                // Decrypt any encrypted password blobs into PasswordRecord instances
                var migrated = false;
                foreach (var r in cfg.Folders)
                {
                    if (!string.IsNullOrEmpty(r.EncryptedPasswordRef))
                    {
                        try
                        {
                            var blob = ReadCredentialBlob(r.EncryptedPasswordRef);
                            if (!string.IsNullOrEmpty(blob)) r.PasswordRecord = UnprotectObject<PasswordRecord>(blob);
                        }
                        catch { r.PasswordRecord = null; }
                    }
                    else if (!string.IsNullOrEmpty(r.EncryptedPassword))
                    {
                        // legacy: DPAPI blob in config
                        try
                        {
                            r.PasswordRecord = UnprotectObject<PasswordRecord>(r.EncryptedPassword);

                            // Migrate to credential manager for future runs
                            try
                            {
                                var key = GenerateCredentialKey(r.FolderPath);
                                WriteCredentialBlob(key, r.EncryptedPassword);
                                r.EncryptedPasswordRef = key;
                                r.EncryptedPassword = null;
                                migrated = true;
                            }
                            catch { }
                        }
                        catch { r.PasswordRecord = null; }
                    }
                }

                if (migrated)
                {
                    try { SaveConfig(cfg, path); } catch { }
                }

                return cfg;
            }
            catch { return null; }
        }

        public static bool SaveConfig(HiderConfig cfg, string path)
        {
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // Prepare a copy for serialization so we don't leave plaintext PasswordRecord in file
                var cfgCopy = new HiderConfig { AutoHideEnabled = cfg.AutoHideEnabled, AutoHideMinutes = cfg.AutoHideMinutes };
                foreach (var r in cfg.Folders)
                {
                    var copy = new HiderRecord { FolderPath = r.FolderPath, AclRestricted = r.AclRestricted, EfsEnabled = r.EfsEnabled };
                    if (r.PasswordRecord != null)
                    {
                        try
                        {
                            // Protect and store in Credential Manager; reference it in config
                            var blob = ProtectObject(r.PasswordRecord);
                            var key = GenerateCredentialKey(r.FolderPath);
                            WriteCredentialBlob(key, blob);
                            copy.EncryptedPasswordRef = key;
                        }
                        catch
                        {
                            // fallback to embedding DPAPI blob in config
                            try { copy.EncryptedPassword = ProtectObject(r.PasswordRecord); } catch { copy.EncryptedPassword = null; }
                        }
                    }
                    else if (!string.IsNullOrEmpty(r.EncryptedPasswordRef))
                    {
                        // preserve existing credential ref
                        copy.EncryptedPasswordRef = r.EncryptedPasswordRef;
                    }
                    else if (!string.IsNullOrEmpty(r.EncryptedPassword))
                    {
                        // preserve existing legacy encrypted blob
                        copy.EncryptedPassword = r.EncryptedPassword;
                    }
                    cfgCopy.Folders.Add(copy);
                }

                var json = JsonSerializer.Serialize(cfgCopy, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
                return true;
            }
            catch { return false; }
        }

        public static bool AddRecord(HiderConfig cfg, HiderRecord rec)
        {
            if (cfg == null || rec == null) return false;
            if (cfg.Folders.Exists(r => string.Equals(r.FolderPath, rec.FolderPath, StringComparison.OrdinalIgnoreCase))) return false;
            cfg.Folders.Add(rec);
            return true;
        }

        public static bool RemoveRecord(HiderConfig cfg, string path)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(path)) return false;
            var idx = cfg.Folders.FindIndex(r => string.Equals(r.FolderPath, path, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            cfg.Folders.RemoveAt(idx);
            return true;
        }

        public static bool UpdateRecord(HiderConfig cfg, string path, Action<HiderRecord> update)
        {
            if (cfg == null || string.IsNullOrWhiteSpace(path) || update == null) return false;
            var rec = cfg.Folders.Find(r => string.Equals(r.FolderPath, path, StringComparison.OrdinalIgnoreCase));
            if (rec == null) return false;
            update(rec);
            return true;
        }

        public static bool GetHiddenState(string path)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) return false;
                var attrs = File.GetAttributes(path);
                return (attrs & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System);
            }
            catch { return false; }
        }

        public static bool SetHidden(string path, bool hide)
        {
            return SecurityService.HideFolder(path, hide);
        }

        private static byte[] DerivePbkdf2HmacSha256(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int iterations, int dkLen)
        {
            // Implements PBKDF2-HMAC-SHA256 per RFC 2898.
            // Output length dkLen bytes.
            var hLen = 32; // SHA256 output size
            var l = (int)Math.Ceiling(dkLen / (double)hLen);
            var r = dkLen - (l - 1) * hLen;
            var derived = new byte[dkLen];

            // Avoid stackalloc in the loop to prevent potential stack overflow warnings
            var block = new byte[hLen];
            var intBlock = new byte[4];
            var salted = new byte[salt.Length + 4];

            using (var hmac = new System.Security.Cryptography.HMACSHA256(password.ToArray()))
            {
                for (uint i = 1; i <= (uint)l; i++)
                {
                    // INT(i)
                    intBlock[0] = (byte)((i >> 24) & 0xFF);
                    intBlock[1] = (byte)((i >> 16) & 0xFF);
                    intBlock[2] = (byte)((i >> 8) & 0xFF);
                    intBlock[3] = (byte)(i & 0xFF);

                    // salt || INT(i)
                    salt.CopyTo(salted.AsSpan(0, salt.Length));
                    intBlock.CopyTo(salted.AsSpan(salt.Length, 4));

                    var u = hmac.ComputeHash(salted);
                    Array.Copy(u, block, hLen);

                    var t = new byte[hLen];
                    Array.Copy(u, t, hLen);

                    for (int iter = 1; iter < iterations; iter++)
                    {
                        u = hmac.ComputeHash(u);
                        for (int k = 0; k < hLen; k++) t[k] ^= u[k];
                    }

                    int destOffset = (int)((i - 1) * hLen);
                    int copyLen = (i == (uint)l) ? r : hLen;
                    Array.Copy(t, 0, derived, destOffset, copyLen);
                }
            }

            return derived;
        }

        public static PasswordRecord CreatePasswordRecord(string plainPassword, int iterations = 100000)
        {
            try
            {
                var salt = new byte[16];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(salt);
                var pwdBytes = System.Text.Encoding.UTF8.GetBytes(plainPassword);
                var dk = DerivePbkdf2HmacSha256(pwdBytes, salt, iterations, 32);
                return new PasswordRecord { Salt = Convert.ToBase64String(salt), Hash = Convert.ToBase64String(dk), Iterations = iterations };
            }
            catch { return null!; }
        }

        public static bool TestPassword(string plainPassword, PasswordRecord rec)
        {
            try
            {
                var salt = Convert.FromBase64String(rec.Salt);
                var iterations = rec.Iterations;
                var pwdBytes = System.Text.Encoding.UTF8.GetBytes(plainPassword);
                var dk = DerivePbkdf2HmacSha256(pwdBytes, salt, iterations, 32);
                return Convert.ToBase64String(dk) == rec.Hash;
            }
            catch { return false; }
        }

        private static string ProtectObject<T>(T obj)
        {
            var json = JsonSerializer.Serialize(obj);
            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var protectedBytes = System.Security.Cryptography.ProtectedData.Protect(bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        private static T? UnprotectObject<T>(string blob)
        {
            var bytes = Convert.FromBase64String(blob);
            var unprotected = System.Security.Cryptography.ProtectedData.Unprotect(bytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
            var json = System.Text.Encoding.UTF8.GetString(unprotected);
            return JsonSerializer.Deserialize<T>(json);
        }

        private static string GenerateCredentialKey(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant());
                var hash = sha.ComputeHash(bytes);
                return "PlatypusTools_Hider_" + Convert.ToBase64String(hash).Replace("=", "").Replace("+", "").Replace("/", "_");
            }
        }

        private const uint CRED_TYPE_GENERIC = 1;
        private const uint CRED_PERSIST_LOCAL_MACHINE = 2;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public System.IntPtr TargetName;
            public System.IntPtr Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public System.IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public System.IntPtr Attributes;
            public System.IntPtr TargetAlias;
            public System.IntPtr UserName;
        }

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWrite([System.Runtime.InteropServices.In] ref CREDENTIAL userCredential, uint flags);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
        private static extern bool CredRead(string target, uint type, uint flags, out System.IntPtr credentialPtr);

        [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(System.IntPtr buffer);

        private static void WriteCredentialBlob(string key, string blob)
        {
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(blob);
                var cred = new CREDENTIAL();
                cred.Flags = 0;
                cred.Type = CRED_TYPE_GENERIC;
                cred.TargetName = System.Runtime.InteropServices.Marshal.StringToCoTaskMemUni(key);
                cred.CredentialBlobSize = (uint)bytes.Length;
                cred.CredentialBlob = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(bytes.Length);
                System.Runtime.InteropServices.Marshal.Copy(bytes, 0, cred.CredentialBlob, bytes.Length);
                cred.Persist = CRED_PERSIST_LOCAL_MACHINE;

                try
                {
                    CredWrite(ref cred, 0);
                }
                finally
                {
                    if (cred.TargetName != System.IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeCoTaskMem(cred.TargetName);
                    if (cred.CredentialBlob != System.IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeCoTaskMem(cred.CredentialBlob);
                }
            }
            catch { }
        }

        private static string? ReadCredentialBlob(string key)
        {
            try
            {
                if (CredRead(key, CRED_TYPE_GENERIC, 0, out var credPtr) && credPtr != System.IntPtr.Zero)
                {
                    try
                    {
                        var cred = System.Runtime.InteropServices.Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                        if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != System.IntPtr.Zero)
                        {
                            var bytes = new byte[cred.CredentialBlobSize];
                            System.Runtime.InteropServices.Marshal.Copy(cred.CredentialBlob, bytes, 0, (int)cred.CredentialBlobSize);
                            return System.Text.Encoding.UTF8.GetString(bytes);
                        }
                    }
                    finally
                    {
                        CredFree(credPtr);
                    }
                }
            }
            catch { }
            return null;
        }
        internal static string ExportLegacyBlob(PasswordRecord rec)
        {
            return ProtectObject(rec);
        }


    }
}
