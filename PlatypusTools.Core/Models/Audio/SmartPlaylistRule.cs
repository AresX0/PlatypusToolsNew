using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatypusTools.Core.Models.Audio
{
    /// <summary>
    /// A single rule for smart playlist filtering.
    /// </summary>
    public class SmartPlaylistRule
    {
        public string Field { get; set; } = "Title";
        public string Operator { get; set; } = "Contains";
        public string Value { get; set; } = string.Empty;

        /// <summary>
        /// Available fields for smart playlist rules.
        /// </summary>
        public static readonly string[] AvailableFields = new[]
        {
            "Title", "Artist", "Album", "AlbumArtist", "Genre", "Year",
            "Rating", "PlayCount", "Duration", "Bitrate", "DateAdded",
            "LastPlayed", "IsFavorite", "FileSize", "TrackNumber"
        };

        /// <summary>
        /// Gets operators applicable to a given field.
        /// </summary>
        public static string[] GetOperatorsForField(string field)
        {
            return field switch
            {
                "Title" or "Artist" or "Album" or "AlbumArtist" or "Genre" =>
                    new[] { "Contains", "Does Not Contain", "Is", "Is Not", "Starts With", "Ends With" },
                "Year" or "Rating" or "PlayCount" or "TrackNumber" or "Bitrate" =>
                    new[] { "Is", "Is Not", "Greater Than", "Less Than", "Between" },
                "Duration" or "FileSize" =>
                    new[] { "Greater Than", "Less Than", "Between" },
                "DateAdded" or "LastPlayed" =>
                    new[] { "In Last Days", "Before", "After", "Between" },
                "IsFavorite" =>
                    new[] { "Is" },
                _ => new[] { "Contains", "Is", "Is Not" }
            };
        }
    }

    /// <summary>
    /// A group of rules with AND/OR logic.
    /// </summary>
    public class SmartPlaylistRuleSet
    {
        public string MatchMode { get; set; } = "All"; // "All" = AND, "Any" = OR
        public List<SmartPlaylistRule> Rules { get; set; } = new();
        public int? MaxResults { get; set; }
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; }

        /// <summary>
        /// Serialize to JSON string for storage in Playlist.SmartPlaylistQuery.
        /// </summary>
        public string ToJson()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = false });
        }

        /// <summary>
        /// Deserialize from JSON string.
        /// </summary>
        public static SmartPlaylistRuleSet? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<SmartPlaylistRuleSet>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
