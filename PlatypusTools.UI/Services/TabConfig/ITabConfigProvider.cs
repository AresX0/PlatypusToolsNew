using System.Collections.Generic;

namespace PlatypusTools.UI.Services.TabConfig
{
    /// <summary>
    /// Phase 1.4 — Per-tab configuration import/export/reset contract.
    /// Tabs that want their own config bundle implement this interface; the optional
    /// <see cref="Controls.TabActionMenuButton"/> control surfaces a "⋯" menu wired to it.
    /// </summary>
    public interface ITabConfigProvider
    {
        /// <summary>Stable key (e.g. "System.WallpaperRotator") — used as the bundle's "tab" field.</summary>
        string TabKey { get; }

        /// <summary>Display label shown in dialogs (e.g. "Wallpaper Rotator").</summary>
        string TabDisplayName { get; }

        /// <summary>
        /// Returns a key/value snapshot of the tab's user-facing settings.
        /// Use simple types only (string, number, bool, arrays, dictionaries).
        /// </summary>
        IDictionary<string, object?> ExportConfig();

        /// <summary>Restore settings from a previously-exported snapshot. Unknown keys MUST be ignored.</summary>
        void ImportConfig(IDictionary<string, object?> config);

        /// <summary>Restore the tab to defaults.</summary>
        void ResetConfig();
    }
}
