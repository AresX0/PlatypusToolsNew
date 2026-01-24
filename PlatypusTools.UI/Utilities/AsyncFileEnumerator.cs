using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Utilities
{
    /// <summary>
    /// Provides async file enumeration to avoid blocking the UI thread.
    /// Use instead of Directory.GetFiles() for large directories.
    /// </summary>
    public static class AsyncFileEnumerator
    {
        /// <summary>
        /// Asynchronously enumerates files in a directory.
        /// </summary>
        /// <param name="path">The directory path to search.</param>
        /// <param name="searchPattern">The search pattern (e.g., "*.*").</param>
        /// <param name="searchOption">Whether to search subdirectories.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An async enumerable of file paths.</returns>
        public static async IAsyncEnumerable<string> EnumerateFilesAsync(
            string path,
            string searchPattern = "*.*",
            SearchOption searchOption = SearchOption.AllDirectories,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(path))
                yield break;

            // Use a channel or queue pattern for better performance on large directories
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };

            await Task.Yield(); // Ensure we're async from the start

            foreach (var file in Directory.EnumerateFiles(path, searchPattern, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }
        }

        /// <summary>
        /// Asynchronously gets all files matching a pattern, with optional extension filter.
        /// </summary>
        /// <param name="path">The directory path.</param>
        /// <param name="extensions">Optional array of extensions to include (e.g., ".pdf", ".docx").</param>
        /// <param name="searchOption">Whether to search subdirectories.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A list of matching file paths.</returns>
        public static async Task<List<string>> GetFilesAsync(
            string path,
            string[]? extensions = null,
            SearchOption searchOption = SearchOption.AllDirectories,
            CancellationToken cancellationToken = default)
        {
            var files = new List<string>();

            await foreach (var file in EnumerateFilesAsync(path, "*.*", searchOption, cancellationToken))
            {
                if (extensions == null || extensions.Length == 0)
                {
                    files.Add(file);
                }
                else
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    if (Array.Exists(extensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    {
                        files.Add(file);
                    }
                }
            }

            return files;
        }

        /// <summary>
        /// Asynchronously counts files in a directory.
        /// </summary>
        public static async Task<int> CountFilesAsync(
            string path,
            string searchPattern = "*.*",
            SearchOption searchOption = SearchOption.AllDirectories,
            CancellationToken cancellationToken = default)
        {
            int count = 0;
            await foreach (var _ in EnumerateFilesAsync(path, searchPattern, searchOption, cancellationToken))
            {
                count++;
            }
            return count;
        }

        /// <summary>
        /// Asynchronously enumerates directories.
        /// </summary>
        public static async IAsyncEnumerable<string> EnumerateDirectoriesAsync(
            string path,
            string searchPattern = "*",
            SearchOption searchOption = SearchOption.AllDirectories,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(path))
                yield break;

            var options = new EnumerationOptions
            {
                RecurseSubdirectories = searchOption == SearchOption.AllDirectories,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System
            };

            await Task.Yield();

            foreach (var dir in Directory.EnumerateDirectories(path, searchPattern, options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return dir;
            }
        }

        /// <summary>
        /// Gets file info asynchronously.
        /// </summary>
        public static async Task<FileInfo?> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return File.Exists(filePath) ? new FileInfo(filePath) : null;
            }, cancellationToken);
        }

        /// <summary>
        /// Checks if a path exists asynchronously.
        /// </summary>
        public static async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return File.Exists(path) || Directory.Exists(path);
            }, cancellationToken);
        }

        /// <summary>
        /// Gets total size of files in a directory asynchronously.
        /// </summary>
        public static async Task<long> GetDirectorySizeAsync(
            string path,
            SearchOption searchOption = SearchOption.AllDirectories,
            CancellationToken cancellationToken = default)
        {
            long totalSize = 0;

            await foreach (var file in EnumerateFilesAsync(path, "*.*", searchOption, cancellationToken))
            {
                try
                {
                    var info = new FileInfo(file);
                    totalSize += info.Length;
                }
                catch
                {
                    // Ignore inaccessible files
                }
            }

            return totalSize;
        }
    }
}
