using System;

namespace PlatypusTools.Core.Models
{
    /// <summary>
    /// Represents the parsed components of a filename for renaming operations
    /// </summary>
    public class FilenameComponents
    {
        /// <summary>
        /// The detected or specified prefix (e.g., "SHOW", "PREFIX")
        /// </summary>
        public string Prefix { get; set; } = string.Empty;

        /// <summary>
        /// Season tag with formatting (e.g., "S01", "S001")
        /// </summary>
        public string Season { get; set; } = string.Empty;

        /// <summary>
        /// Season number as integer
        /// </summary>
        public int SeasonNum { get; set; }

        /// <summary>
        /// Episode tag with formatting (e.g., "E01", "E001", "E0001")
        /// </summary>
        public string Episode { get; set; } = string.Empty;

        /// <summary>
        /// Episode number as integer
        /// </summary>
        public int EpisodeNum { get; set; }

        /// <summary>
        /// The core filename after stripping prefix, season, episode, and common tokens
        /// </summary>
        public string CoreName { get; set; } = string.Empty;

        /// <summary>
        /// The file extension including the dot
        /// </summary>
        public string Extension { get; set; } = string.Empty;

        /// <summary>
        /// The full original filename
        /// </summary>
        public string OriginalFileName { get; set; } = string.Empty;
    }

    /// <summary>
    /// Options for space handling in filename normalization
    /// </summary>
    public enum SpaceHandling
    {
        None,
        SpacesToDashes,
        SpacesToUnderscores,
        RemoveSpaces,
        DashesToSpaces,
        UnderscoresToSpaces
    }

    /// <summary>
    /// Options for symbol conversion in filename normalization
    /// </summary>
    public enum SymbolConversion
    {
        None,
        DashesToSpaces,
        UnderscoresToSpaces,
        DashesToUnderscores,
        UnderscoresToDashes
    }

    /// <summary>
    /// Preset normalization patterns that combine space handling and symbol conversion
    /// </summary>
    public enum NormalizationPreset
    {
        None,
        SpacesToDashes,        // Spaces → -
        SpacesToUnderscores,   // Spaces → _
        RemoveSpaces,          // Remove all spaces
        DashesToSpaces,        // - → Spaces
        UnderscoresToSpaces,   // _ → Spaces
        DashesToUnderscores,   // - → _
        UnderscoresToDashes    // _ → -
    }
}
