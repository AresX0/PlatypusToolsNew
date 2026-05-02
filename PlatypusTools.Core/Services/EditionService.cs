using System;
using System.IO;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Identifies which installation edition is running.
    /// </summary>
    public enum AppEdition
    {
        /// <summary>The full PlatypusTools suite (all features available).</summary>
        Full,
        /// <summary>Multimedia &amp; Streaming only edition.</summary>
        Media
    }

    /// <summary>
    /// Resolves the current product edition. Resolution order:
    /// 1. Environment variable PLATYPUS_EDITION (Full|Media)
    /// 2. edition.txt sidecar next to the executable
    /// 3. Registry HKLM\Software\PlatypusTools!Edition (Windows only)
    /// 4. Default: Full
    /// </summary>
    public static class EditionService
    {
        private static AppEdition? _cached;
        private const string EnvVar = "PLATYPUS_EDITION";
        private const string SidecarFile = "edition.txt";

        /// <summary>
        /// The detected edition. Cached after first read.
        /// </summary>
        public static AppEdition Current
        {
            get
            {
                if (_cached.HasValue) return _cached.Value;
                _cached = Resolve();
                return _cached.Value;
            }
        }

        /// <summary>
        /// Forces a specific edition (used by command-line --edition=).
        /// Pass null to clear the override and re-resolve on next access.
        /// </summary>
        public static void Override(AppEdition? edition)
        {
            _cached = edition;
        }

        /// <summary>
        /// Whether a tab key (e.g. "Multimedia.Audio.AudioPlayer") is permitted
        /// in the current edition. Saved user visibility preferences still apply
        /// on top of this.
        /// </summary>
        public static bool IsTabAllowed(string? tabKey)
        {
            if (Current == AppEdition.Full) return true;
            if (string.IsNullOrEmpty(tabKey)) return true;

            // Media edition: keep multimedia + streaming/remote dashboard +
            // a small set of always-on roots. Everything else is hidden.
            if (tabKey.StartsWith("Multimedia", StringComparison.Ordinal)) return true;

            // Streaming server / remote dashboard
            if (tabKey == "System.RemoteDashboard") return true;
            if (tabKey == "System.RemoteDesktop") return true;
            if (tabKey == "Tools.PlexBackup") return true;

            // Always-available utilities for the media edition
            if (tabKey == "Tools.NotificationCenter") return true;
            if (tabKey == "Tools.QrCode") return true;
            if (tabKey == "Tools.ColorPicker") return true;

            return false;
        }

        private static AppEdition Resolve()
        {
            // 1. Environment variable
            try
            {
                var env = Environment.GetEnvironmentVariable(EnvVar);
                if (TryParse(env, out var fromEnv)) return fromEnv;
            }
            catch { /* ignore */ }

            // 2. edition.txt next to entry assembly
            try
            {
                var baseDir = AppContext.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir))
                {
                    var path = Path.Combine(baseDir, SidecarFile);
                    if (File.Exists(path))
                    {
                        var text = File.ReadAllText(path).Trim();
                        if (TryParse(text, out var fromFile)) return fromFile;
                    }
                }
            }
            catch { /* ignore */ }

            // 3. Registry (Windows only)
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var fromReg = ReadRegistryEdition();
                    if (fromReg.HasValue) return fromReg.Value;
                }
                catch { /* ignore */ }
            }

            return AppEdition.Full;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static AppEdition? ReadRegistryEdition()
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"Software\PlatypusTools");
            var value = key?.GetValue("Edition") as string;
            if (TryParse(value, out var fromReg)) return fromReg;
            return null;
        }

        private static bool TryParse(string? value, out AppEdition edition)
        {
            edition = AppEdition.Full;
            if (string.IsNullOrWhiteSpace(value)) return false;
            var v = value.Trim();
            if (string.Equals(v, "Media", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "Multimedia", StringComparison.OrdinalIgnoreCase))
            {
                edition = AppEdition.Media;
                return true;
            }
            if (string.Equals(v, "Full", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "Suite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(v, "Default", StringComparison.OrdinalIgnoreCase))
            {
                edition = AppEdition.Full;
                return true;
            }
            return false;
        }
    }
}
