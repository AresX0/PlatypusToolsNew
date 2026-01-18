using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace PlatypusTools.Core.Utilities
{
    /// <summary>
    /// Utility for atomic file write operations.
    /// Uses the temp-write-replace pattern to ensure data integrity:
    /// 1. Write to temporary file
    /// 2. Flush to disk
    /// 3. Replace original with atomic swap
    /// 4. Keep backup of original for recovery
    /// </summary>
    public class AtomicFileWriter
    {
        /// <summary>
        /// Default backup suffix.
        /// </summary>
        public const string BackupSuffix = ".backup";

        /// <summary>
        /// Write content to file atomically.
        /// </summary>
        /// <param name="filePath">Target file path.</param>
        /// <param name="content">Content to write.</param>
        /// <param name="encoding">Text encoding (default: UTF-8).</param>
        /// <param name="keepBackup">Whether to keep backup of original.</param>
        /// <returns>True if successful, false if failed.</returns>
        public static async Task<bool> WriteTextAtomicAsync(
            string filePath,
            string content,
            Encoding encoding = null,
            bool keepBackup = true)
        {
            encoding ??= Encoding.UTF8;

            try
            {
                // Step 1: Write to temporary file
                var tempFilePath = filePath + ".tmp";
                var directory = Path.GetDirectoryName(filePath);

                // Ensure directory exists
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                // Write to temp file
                await File.WriteAllTextAsync(tempFilePath, content, encoding);

                // Step 2: Flush to ensure data is on disk
                using (var file = File.Open(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    file.Flush();
                }

                // Step 3: Create backup if original exists and requested
                var backupPath = filePath + BackupSuffix;
                if (keepBackup && File.Exists(filePath))
                {
                    // Remove old backup if it exists
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    // Copy original to backup
                    File.Copy(filePath, backupPath, overwrite: true);
                }

                // Step 4: Atomic replace
                if (File.Exists(filePath))
                    File.Delete(filePath);

                File.Move(tempFilePath, filePath, overwrite: true);

                return true;
            }
            catch (Exception ex)
            {
                // Log error (you might want to inject ILogger here)
                System.Diagnostics.Debug.WriteLine($"AtomicFileWriter error: {ex.Message}");

                // Clean up temp file if it exists
                try
                {
                    var tempPath = filePath + ".tmp";
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// Write binary content to file atomically.
        /// </summary>
        /// <param name="filePath">Target file path.</param>
        /// <param name="content">Binary content to write.</param>
        /// <param name="keepBackup">Whether to keep backup of original.</param>
        /// <returns>True if successful, false if failed.</returns>
        public static async Task<bool> WriteBinaryAtomicAsync(
            string filePath,
            byte[] content,
            bool keepBackup = true)
        {
            try
            {
                // Step 1: Write to temporary file
                var tempFilePath = filePath + ".tmp";
                var directory = Path.GetDirectoryName(filePath);

                // Ensure directory exists
                if (!Directory.Exists(directory))
                    Directory.CreateDirectory(directory);

                // Write to temp file
                await File.WriteAllBytesAsync(tempFilePath, content);

                // Step 2: Flush to ensure data is on disk
                using (var file = File.Open(tempFilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    file.Flush();
                }

                // Step 3: Create backup if original exists and requested
                var backupPath = filePath + BackupSuffix;
                if (keepBackup && File.Exists(filePath))
                {
                    // Remove old backup if it exists
                    if (File.Exists(backupPath))
                        File.Delete(backupPath);

                    // Copy original to backup
                    File.Copy(filePath, backupPath, overwrite: true);
                }

                // Step 4: Atomic replace
                if (File.Exists(filePath))
                    File.Delete(filePath);

                File.Move(tempFilePath, filePath, overwrite: true);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AtomicFileWriter error: {ex.Message}");

                // Clean up temp file if it exists
                try
                {
                    var tempPath = filePath + ".tmp";
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch { }

                return false;
            }
        }

        /// <summary>
        /// Restore file from backup.
        /// </summary>
        /// <param name="filePath">Original file path.</param>
        /// <returns>True if backup was restored, false if no backup exists.</returns>
        public static bool RestoreFromBackup(string filePath)
        {
            var backupPath = filePath + BackupSuffix;

            if (!File.Exists(backupPath))
                return false;

            try
            {
                if (File.Exists(filePath))
                    File.Delete(filePath);

                File.Copy(backupPath, filePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AtomicFileWriter restore error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Delete backup file.
        /// </summary>
        /// <param name="filePath">Original file path.</param>
        /// <returns>True if backup was deleted, false if no backup exists.</returns>
        public static bool DeleteBackup(string filePath)
        {
            var backupPath = filePath + BackupSuffix;

            if (!File.Exists(backupPath))
                return false;

            try
            {
                File.Delete(backupPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AtomicFileWriter delete backup error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Check if backup exists for a file.
        /// </summary>
        public static bool BackupExists(string filePath)
        {
            return File.Exists(filePath + BackupSuffix);
        }

        /// <summary>
        /// Get path to backup file.
        /// </summary>
        public static string GetBackupPath(string filePath)
        {
            return filePath + BackupSuffix;
        }
    }
}
