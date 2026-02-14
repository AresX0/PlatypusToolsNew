using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Utilities
{
    /// <summary>
    /// Provides safe file and directory enumeration that gracefully handles
    /// PathTooLongException, UnauthorizedAccessException, and other I/O errors
    /// by using EnumerationOptions.IgnoreInaccessible and catching exceptions
    /// during iteration.
    /// </summary>
    public static class SafeFileEnumerator
    {
        /// <summary>
        /// Default enumeration options that skip inaccessible files/directories (including long paths).
        /// </summary>
        public static EnumerationOptions SafeOptions(bool recurse = false) => new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = recurse,
            AttributesToSkip = FileAttributes.System,
            ReturnSpecialDirectories = false
        };

        /// <summary>
        /// Safely enumerate files in a directory. Skips files that cause
        /// PathTooLongException or UnauthorizedAccessException.
        /// </summary>
        /// <param name="path">Root directory path.</param>
        /// <param name="pattern">Search pattern (default: "*.*").</param>
        /// <param name="recurse">Whether to recurse subdirectories.</param>
        /// <returns>Enumerable of accessible file paths.</returns>
        public static IEnumerable<string> EnumerateFiles(string path, string pattern = "*.*", bool recurse = false)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                yield break;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(path, pattern, SafeOptions(recurse));
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                System.Diagnostics.Debug.WriteLine($"[SafeFileEnumerator] Cannot enumerate files in '{path}': {ex.GetType().Name}");
                yield break;
            }

            foreach (var file in files)
            {
                string result;
                try
                {
                    result = file;
                }
                catch (Exception ex) when (IsIgnorableException(ex))
                {
                    continue;
                }
                yield return result;
            }
        }

        /// <summary>
        /// Safely enumerate directories. Skips directories that cause
        /// PathTooLongException or UnauthorizedAccessException.
        /// </summary>
        public static IEnumerable<string> EnumerateDirectories(string path, string pattern = "*", bool recurse = false)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                yield break;

            IEnumerable<string> dirs;
            try
            {
                dirs = Directory.EnumerateDirectories(path, pattern, SafeOptions(recurse));
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                System.Diagnostics.Debug.WriteLine($"[SafeFileEnumerator] Cannot enumerate dirs in '{path}': {ex.GetType().Name}");
                yield break;
            }

            foreach (var dir in dirs)
            {
                string result;
                try
                {
                    result = dir;
                }
                catch (Exception ex) when (IsIgnorableException(ex))
                {
                    continue;
                }
                yield return result;
            }
        }

        /// <summary>
        /// Safely get file count in a directory without crashing on long paths.
        /// </summary>
        public static int CountFiles(string path, string pattern = "*.*", bool recurse = false)
        {
            try
            {
                return Directory.EnumerateFiles(path, pattern, SafeOptions(recurse)).Count();
            }
            catch (Exception ex) when (IsIgnorableException(ex))
            {
                return 0;
            }
        }

        /// <summary>
        /// Safely calculate total file size in a directory.
        /// </summary>
        public static long CalculateTotalSize(string path, string pattern = "*.*", bool recurse = false)
        {
            long total = 0;
            foreach (var file in EnumerateFiles(path, pattern, recurse))
            {
                try
                {
                    total += new FileInfo(file).Length;
                }
                catch (Exception ex) when (IsIgnorableException(ex))
                {
                    // Skip files we can't stat
                }
            }
            return total;
        }

        /// <summary>
        /// Safely enumerate FileInfo objects.
        /// </summary>
        public static IEnumerable<FileInfo> EnumerateFileInfos(string path, string pattern = "*.*", bool recurse = false)
        {
            foreach (var file in EnumerateFiles(path, pattern, recurse))
            {
                FileInfo? fi = null;
                try
                {
                    fi = new FileInfo(file);
                }
                catch (Exception ex) when (IsIgnorableException(ex))
                {
                    // Skip
                }
                if (fi != null)
                    yield return fi;
            }
        }

        /// <summary>
        /// Safely enumerate DirectoryInfo objects.
        /// </summary>
        public static IEnumerable<DirectoryInfo> EnumerateDirectoryInfos(string path, string pattern = "*", bool recurse = false)
        {
            foreach (var dir in EnumerateDirectories(path, pattern, recurse))
            {
                DirectoryInfo? di = null;
                try
                {
                    di = new DirectoryInfo(dir);
                }
                catch (Exception ex) when (IsIgnorableException(ex))
                {
                    // Skip
                }
                if (di != null)
                    yield return di;
            }
        }

        /// <summary>
        /// Determines if an exception is one we can safely ignore during enumeration.
        /// </summary>
        private static bool IsIgnorableException(Exception ex)
        {
            return ex is PathTooLongException
                || ex is UnauthorizedAccessException
                || ex is DirectoryNotFoundException
                || ex is IOException;
        }
    }
}
