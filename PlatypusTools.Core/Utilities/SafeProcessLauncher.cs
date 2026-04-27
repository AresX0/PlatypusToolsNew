using System;
using System.Diagnostics;
using System.IO;

namespace PlatypusTools.Core.Utilities
{
    /// <summary>
    /// Safe wrapper around <see cref="Process.Start"/> for opening URLs and local paths via the OS shell.
    /// Validates inputs to prevent arbitrary scheme handler invocation (javascript:, vbscript:, file: with embedded args, etc.)
    /// </summary>
    public static class SafeProcessLauncher
    {
        /// <summary>
        /// Opens an HTTP(S) URL in the user's default browser. Rejects any non-http(s) URI.
        /// Returns true on success, false if URL is invalid or launch failed.
        /// </summary>
        public static bool OpenUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = uri.AbsoluteUri,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SafeProcessLauncher] OpenUrl failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Opens a local file or folder via the OS shell. Rejects URIs (http://, file://, etc.)
        /// and verifies the path exists on disk.
        /// </summary>
        public static bool OpenLocalPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;

            // Reject URIs — only on-disk paths are allowed.
            if (path.Contains("://", StringComparison.Ordinal)) return false;

            // Path must exist (file or directory).
            if (!File.Exists(path) && !Directory.Exists(path)) return false;

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SafeProcessLauncher] OpenLocalPath failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reveals a file/folder in Windows Explorer using /select.
        /// Validates the path exists and uses ArgumentList to prevent argument injection.
        /// </summary>
        public static bool RevealInExplorer(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            if (!File.Exists(path) && !Directory.Exists(path)) return false;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    UseShellExecute = false
                };
                psi.ArgumentList.Add("/select,");
                psi.ArgumentList.Add(path);
                Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SafeProcessLauncher] RevealInExplorer failed: {ex.Message}");
                return false;
            }
        }
    }
}
