using Microsoft.Web.WebView2.Core;
using System;
using System.IO;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Manages a shared WebView2 environment to prevent initialization conflicts.
    /// WebView2 can only use one environment per process.
    /// </summary>
    public static class WebView2EnvironmentService
    {
        private static CoreWebView2Environment? _sharedEnvironment;
        private static readonly object _lock = new();
        private static Task<CoreWebView2Environment>? _initTask;

        /// <summary>
        /// Gets the shared WebView2 environment, creating it if necessary.
        /// Thread-safe and ensures only one environment is created per process.
        /// </summary>
        public static async Task<CoreWebView2Environment> GetSharedEnvironmentAsync()
        {
            if (_sharedEnvironment != null)
                return _sharedEnvironment;

            lock (_lock)
            {
                if (_initTask == null)
                {
                    _initTask = InitializeEnvironmentAsync();
                }
            }

            return await _initTask;
        }

        private static async Task<CoreWebView2Environment> InitializeEnvironmentAsync()
        {
            try
            {
                var userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PlatypusTools", "WebView2Data");

                Directory.CreateDirectory(userDataFolder);

                _sharedEnvironment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                return _sharedEnvironment;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize WebView2 environment: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Checks if WebView2 Runtime is installed.
        /// </summary>
        public static bool IsWebView2Available()
        {
            try
            {
                var version = CoreWebView2Environment.GetAvailableBrowserVersionString();
                return !string.IsNullOrEmpty(version);
            }
            catch
            {
                return false;
            }
        }
    }
}
