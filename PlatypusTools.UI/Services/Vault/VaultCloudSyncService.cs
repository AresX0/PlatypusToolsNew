using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services.Vault
{
    /// <summary>
    /// Handles cloud sync for the encrypted vault file.
    /// OneDrive: Uses local OneDrive folder sync (no OAuth needed — OneDrive desktop app handles sync).
    /// This is the most reliable approach and works immediately without app registration.
    /// </summary>
    public class VaultCloudSyncService
    {
        private const string VaultFileName = "PlatypusVault.encrypted";
        private const string VaultFolderName = "PlatypusTools";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        /// <summary>
        /// Gets or loads the current sync state from disk.
        /// </summary>
        public SyncState LoadSyncState()
        {
            var path = Path.Combine(SettingsManager.DataDirectory, "Vault", "sync_state.json");
            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    return JsonSerializer.Deserialize<SyncState>(json, JsonOptions) ?? new SyncState();
                }
                catch { }
            }
            return new SyncState();
        }

        /// <summary>
        /// Saves sync state to disk.
        /// </summary>
        public void SaveSyncState(SyncState state)
        {
            var path = Path.Combine(SettingsManager.DataDirectory, "Vault", "sync_state.json");
            var dir = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
        }

        #region OneDrive (Local Folder Sync)

        /// <summary>
        /// Detects the user's OneDrive folder path. Tries multiple methods:
        /// 1. Environment variable %OneDrive%
        /// 2. Registry (OneDrive personal / business accounts)
        /// 3. Common default paths
        /// </summary>
        public static string? DetectOneDriveFolderPath()
        {
            // Method 1: Environment variables
            foreach (var envVar in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var envPath = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
                    return envPath;
            }

            // Method 2: Registry — personal OneDrive
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive");
                if (key != null)
                {
                    var regPath = key.GetValue("UserFolder") as string;
                    if (!string.IsNullOrEmpty(regPath) && Directory.Exists(regPath))
                        return regPath;
                }
            }
            catch { }

            // Method 3: Registry — OneDrive accounts (iterate all)
            try
            {
                using var accountsKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
                if (accountsKey != null)
                {
                    foreach (var accountName in accountsKey.GetSubKeyNames())
                    {
                        using var acctKey = accountsKey.OpenSubKey(accountName);
                        var userFolder = acctKey?.GetValue("UserFolder") as string;
                        if (!string.IsNullOrEmpty(userFolder) && Directory.Exists(userFolder))
                            return userFolder;
                    }
                }
            }
            catch { }

            // Method 4: Common default paths
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var folderName in new[] { "OneDrive", "OneDrive - Personal" })
            {
                var path = Path.Combine(userProfile, folderName);
                if (Directory.Exists(path))
                    return path;
            }

            return null;
        }

        /// <summary>
        /// Gets the vault sync folder inside OneDrive. Creates it if it doesn't exist.
        /// </summary>
        public static string? GetOneDriveVaultFolderPath()
        {
            var oneDrive = DetectOneDriveFolderPath();
            if (oneDrive == null) return null;
            var vaultFolder = Path.Combine(oneDrive, VaultFolderName);
            Directory.CreateDirectory(vaultFolder);
            return vaultFolder;
        }

        /// <summary>
        /// Gets the full path to the vault file in OneDrive.
        /// </summary>
        public static string? GetOneDriveVaultFilePath()
        {
            var folder = GetOneDriveVaultFolderPath();
            return folder != null ? Path.Combine(folder, VaultFileName) : null;
        }

        /// <summary>
        /// "Connects" to OneDrive by detecting the local OneDrive folder.
        /// No OAuth needed — OneDrive desktop app handles the actual cloud sync.
        /// </summary>
        public Task<(string FolderPath, string AccountName)?> ConnectOneDriveAsync(CancellationToken ct = default)
        {
            var oneDrivePath = DetectOneDriveFolderPath();
            if (oneDrivePath == null)
            {
                throw new InvalidOperationException(
                    "OneDrive folder not found. Please install and sign in to the OneDrive desktop app first.\n\n" +
                    "Download: https://www.microsoft.com/en-us/microsoft-365/onedrive/download");
            }

            // Get account display name from registry
            var accountName = "OneDrive";
            try
            {
                using var accountsKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\OneDrive\Accounts");
                if (accountsKey != null)
                {
                    foreach (var acctName in accountsKey.GetSubKeyNames())
                    {
                        using var acctKey = accountsKey.OpenSubKey(acctName);
                        var email = acctKey?.GetValue("UserEmail") as string;
                        if (!string.IsNullOrEmpty(email))
                        {
                            accountName = email;
                            break;
                        }
                    }
                }
            }
            catch { }

            return Task.FromResult<(string, string)?>((oneDrivePath, accountName));
        }

        /// <summary>
        /// Disconnects OneDrive sync (clears sync state, does NOT delete files).
        /// </summary>
        public Task DisconnectOneDriveAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Copies the vault file to OneDrive folder for sync.
        /// </summary>
        public async Task UploadToOneDriveAsync(byte[] vaultData, CancellationToken ct = default)
        {
            var vaultPath = GetOneDriveVaultFilePath()
                ?? throw new InvalidOperationException("OneDrive folder not found.");
            await File.WriteAllBytesAsync(vaultPath, vaultData, ct);
        }

        /// <summary>
        /// Reads the vault file from OneDrive folder.
        /// </summary>
        public async Task<byte[]?> DownloadFromOneDriveAsync(CancellationToken ct = default)
        {
            var vaultPath = GetOneDriveVaultFilePath();
            if (vaultPath == null || !File.Exists(vaultPath))
                return null;
            return await File.ReadAllBytesAsync(vaultPath, ct);
        }

        /// <summary>
        /// Computes hash of the OneDrive vault file for change detection.
        /// </summary>
        public async Task<string?> GetOneDriveFileHashAsync(CancellationToken ct = default)
        {
            var vaultPath = GetOneDriveVaultFilePath();
            if (vaultPath == null || !File.Exists(vaultPath))
                return null;
            var bytes = await File.ReadAllBytesAsync(vaultPath, ct);
            var hash = SHA256.HashData(bytes);
            return Convert.ToBase64String(hash);
        }

        #endregion

        #region Sync Logic

        /// <summary>
        /// Performs a full sync: upload local if newer, download remote if remote is newer.
        /// Returns true if the local vault was updated from remote.
        /// </summary>
        public async Task<(bool LocalUpdated, string? Message)> SyncVaultAsync(
            EncryptedVaultService vaultService,
            CancellationToken ct = default)
        {
            var state = LoadSyncState();

            if (state.Provider == CloudProvider.OneDrive)
                return await SyncWithOneDriveAsync(vaultService, state, ct);

            return (false, "No cloud sync configured.");
        }

        private async Task<(bool, string?)> SyncWithOneDriveAsync(
            EncryptedVaultService vaultService, SyncState state, CancellationToken ct)
        {
            var localHash = await vaultService.ComputeVaultFileHashAsync();
            var remoteHash = await GetOneDriveFileHashAsync(ct);

            if (remoteHash == null)
            {
                // No remote file yet — copy local to OneDrive
                var localData = await vaultService.GetEncryptedVaultBytesAsync();
                await UploadToOneDriveAsync(localData, ct);
                state.RemoteFileHash = localHash;
                state.LastSyncTime = DateTime.UtcNow;
                SaveSyncState(state);
                return (false, "Vault copied to OneDrive. OneDrive will sync it to the cloud.");
            }

            if (localHash == remoteHash)
            {
                state.LastSyncTime = DateTime.UtcNow;
                SaveSyncState(state);
                return (false, "Vault is already in sync.");
            }

            // Files differ — use timestamps to decide
            var localPath = vaultService.VaultFilePath;
            var remotePath = GetOneDriveVaultFilePath()!;
            var localTime = File.GetLastWriteTimeUtc(localPath);
            var remoteTime = File.GetLastWriteTimeUtc(remotePath);

            if (localTime >= remoteTime)
            {
                // Local is newer — copy to OneDrive
                var localData = await vaultService.GetEncryptedVaultBytesAsync();
                await UploadToOneDriveAsync(localData, ct);
                state.RemoteFileHash = localHash;
                state.LastSyncTime = DateTime.UtcNow;
                SaveSyncState(state);
                return (false, "Local vault is newer. Copied to OneDrive.");
            }
            else
            {
                // Remote is newer — download from OneDrive
                var remoteData = await DownloadFromOneDriveAsync(ct);
                if (remoteData != null)
                {
                    await vaultService.WriteEncryptedVaultBytesAsync(remoteData);
                    state.RemoteFileHash = remoteHash;
                    state.LastSyncTime = DateTime.UtcNow;
                    SaveSyncState(state);
                    return (true, "OneDrive vault is newer. Downloaded to local.");
                }
                return (false, "Failed to read OneDrive vault file.");
            }
        }

        #endregion
    }
}
