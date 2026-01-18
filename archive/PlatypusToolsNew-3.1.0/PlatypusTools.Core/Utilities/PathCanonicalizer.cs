using System;
using System.Globalization;
using System.Text;

namespace PlatypusTools.Core.Utilities
{
    /// <summary>
    /// Utility for canonicalizing file paths to enable reliable deduplication.
    /// Handles case normalization, Unicode normalization, and path format standardization.
    /// </summary>
    public static class PathCanonicalizer
    {
        /// <summary>
        /// Canonicalize a file path for consistent comparison and deduplication.
        /// </summary>
        /// <param name="filePath">The file path to canonicalize.</param>
        /// <returns>Canonicalized path (NFC normalized, lowercase on Windows).</returns>
        public static string Canonicalize(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            // Normalize Unicode to NFC (composed form)
            var nfc = filePath.Normalize(NormalizationForm.FormC);

            // Normalize path separators to forward slash (but Windows paths can use backslash too)
            // Keep original separators for compatibility with System.IO.Path
            var normalized = nfc.Replace('\\', System.IO.Path.DirectorySeparatorChar);

            // On Windows (case-insensitive filesystem), convert to lowercase
            if (IsWindowsPath(normalized))
                normalized = normalized.ToLowerInvariant();

            // Remove any redundant dots or double slashes
            normalized = System.IO.Path.GetFullPath(normalized);

            return normalized;
        }

        /// <summary>
        /// Get a deduplication key for a file path.
        /// Two files with the same deduplication key are considered duplicates.
        /// </summary>
        public static string GetDeduplicationKey(string filePath)
        {
            return Canonicalize(filePath).ToLowerInvariant();
        }

        /// <summary>
        /// Compare two paths for equality (handles case-insensitivity on Windows).
        /// </summary>
        public static bool PathsEqual(string path1, string path2)
        {
            return GetDeduplicationKey(path1) == GetDeduplicationKey(path2);
        }

        /// <summary>
        /// Detect if a path is a Windows path.
        /// </summary>
        private static bool IsWindowsPath(string path)
        {
            return Environment.OSVersion.Platform == PlatformID.Win32NT ||
                   path.Contains("\\") ||
                   (path.Length > 1 && char.IsLetter(path[0]) && path[1] == ':');
        }

        /// <summary>
        /// Normalize a path to the current OS conventions.
        /// </summary>
        public static string NormalizeToOS(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return string.Empty;

            // Replace forward slashes with OS-specific separator
            var separator = System.IO.Path.DirectorySeparatorChar;
            return filePath.Replace('/', separator).Replace('\\', separator);
        }

        /// <summary>
        /// Extract the file name from a canonicalized path.
        /// </summary>
        public static string GetFileName(string canonicalPath)
        {
            return System.IO.Path.GetFileName(canonicalPath);
        }

        /// <summary>
        /// Extract the directory path from a canonicalized path.
        /// </summary>
        public static string GetDirectory(string canonicalPath)
        {
            return System.IO.Path.GetDirectoryName(canonicalPath) ?? string.Empty;
        }

        /// <summary>
        /// Get file extension (e.g., ".mp3").
        /// </summary>
        public static string GetExtension(string canonicalPath)
        {
            return System.IO.Path.GetExtension(canonicalPath).ToLowerInvariant();
        }

        /// <summary>
        /// Combine multiple path segments and canonicalize the result.
        /// </summary>
        public static string Combine(params string[] pathSegments)
        {
            var combined = System.IO.Path.Combine(pathSegments);
            return Canonicalize(combined);
        }

        /// <summary>
        /// Check if a path appears to be valid (basic validation).
        /// </summary>
        public static bool IsValidPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                // Try to parse as a full path
                var fullPath = System.IO.Path.GetFullPath(path);
                return !string.IsNullOrWhiteSpace(fullPath);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Check if two paths refer to the same file (case-insensitive comparison).
        /// </summary>
        public static bool IsSameFile(string path1, string path2)
        {
            try
            {
                var canonical1 = Canonicalize(System.IO.Path.GetFullPath(path1));
                var canonical2 = Canonicalize(System.IO.Path.GetFullPath(path2));
                return canonical1 == canonical2;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Relativize a path relative to a base directory.
        /// </summary>
        public static string MakeRelative(string basePath, string targetPath)
        {
            try
            {
                var baseUri = new Uri(Canonicalize(System.IO.Path.GetFullPath(basePath)) + System.IO.Path.DirectorySeparatorChar);
                var targetUri = new Uri(Canonicalize(System.IO.Path.GetFullPath(targetPath)));

                var relativeUri = baseUri.MakeRelativeUri(targetUri);
                return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', System.IO.Path.DirectorySeparatorChar));
            }
            catch
            {
                return targetPath;
            }
        }
    }
}
