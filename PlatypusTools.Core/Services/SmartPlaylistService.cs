using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using PlatypusTools.Core.Models.Audio;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Evaluates smart playlist rules against AudioTrack collections.
    /// </summary>
    public class SmartPlaylistService
    {
        private static readonly Lazy<SmartPlaylistService> _instance = new(() => new SmartPlaylistService());
        public static SmartPlaylistService Instance => _instance.Value;

        /// <summary>
        /// Evaluate a rule set against a track collection, returning matching tracks.
        /// </summary>
        public List<AudioTrack> EvaluateRules(SmartPlaylistRuleSet ruleSet, IEnumerable<AudioTrack> tracks)
        {
            if (ruleSet.Rules.Count == 0)
                return tracks.ToList();

            var filtered = tracks.Where(track =>
            {
                if (ruleSet.MatchMode == "All")
                    return ruleSet.Rules.All(r => EvaluateRule(r, track));
                else
                    return ruleSet.Rules.Any(r => EvaluateRule(r, track));
            });

            // Sort
            if (!string.IsNullOrEmpty(ruleSet.SortBy))
            {
                filtered = ruleSet.SortDescending
                    ? filtered.OrderByDescending(t => GetFieldValue(t, ruleSet.SortBy))
                    : filtered.OrderBy(t => GetFieldValue(t, ruleSet.SortBy));
            }

            // Limit
            if (ruleSet.MaxResults.HasValue && ruleSet.MaxResults.Value > 0)
                filtered = filtered.Take(ruleSet.MaxResults.Value);

            return filtered.ToList();
        }

        /// <summary>
        /// Evaluate a single rule against a track.
        /// </summary>
        public bool EvaluateRule(SmartPlaylistRule rule, AudioTrack track)
        {
            var fieldValue = GetFieldValue(track, rule.Field);

            return rule.Field switch
            {
                "Title" or "Artist" or "Album" or "AlbumArtist" or "Genre" =>
                    EvaluateStringRule(fieldValue?.ToString() ?? "", rule.Operator, rule.Value),

                "Year" or "Rating" or "PlayCount" or "TrackNumber" or "Bitrate" =>
                    EvaluateNumericRule(ToDouble(fieldValue), rule.Operator, rule.Value),

                "Duration" =>
                    EvaluateTimeSpanRule(fieldValue is TimeSpan ts ? ts : TimeSpan.Zero, rule.Operator, rule.Value),

                "FileSize" =>
                    EvaluateNumericRule(ToDouble(fieldValue), rule.Operator, rule.Value),

                "DateAdded" or "LastPlayed" =>
                    EvaluateDateRule(ToDateTime(fieldValue), rule.Operator, rule.Value),

                "IsFavorite" =>
                    (fieldValue is bool b && b) == (rule.Value.Equals("true", StringComparison.OrdinalIgnoreCase) || rule.Value == "1"),

                _ => false
            };
        }

        private object? GetFieldValue(AudioTrack track, string field)
        {
            return field switch
            {
                "Title" => track.Title,
                "Artist" => track.Artist,
                "Album" => track.Album,
                "AlbumArtist" => track.AlbumArtist,
                "Genre" => track.Genre,
                "Year" => track.Year,
                "Rating" => track.Rating,
                "PlayCount" => track.PlayCount,
                "Duration" => track.Duration,
                "Bitrate" => track.Bitrate,
                "DateAdded" => track.DateAdded,
                "LastPlayed" => track.LastPlayed,
                "IsFavorite" => track.IsFavorite,
                "FileSize" => track.FileSize,
                "TrackNumber" => track.TrackNumber,
                _ => null
            };
        }

        private static bool EvaluateStringRule(string fieldValue, string op, string ruleValue)
        {
            return op switch
            {
                "Contains" => fieldValue.Contains(ruleValue, StringComparison.OrdinalIgnoreCase),
                "Does Not Contain" => !fieldValue.Contains(ruleValue, StringComparison.OrdinalIgnoreCase),
                "Is" => fieldValue.Equals(ruleValue, StringComparison.OrdinalIgnoreCase),
                "Is Not" => !fieldValue.Equals(ruleValue, StringComparison.OrdinalIgnoreCase),
                "Starts With" => fieldValue.StartsWith(ruleValue, StringComparison.OrdinalIgnoreCase),
                "Ends With" => fieldValue.EndsWith(ruleValue, StringComparison.OrdinalIgnoreCase),
                _ => false
            };
        }

        private static bool EvaluateNumericRule(double fieldValue, string op, string ruleValue)
        {
            if (!double.TryParse(ruleValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var target))
                return false;

            return op switch
            {
                "Is" => Math.Abs(fieldValue - target) < 0.001,
                "Is Not" => Math.Abs(fieldValue - target) >= 0.001,
                "Greater Than" => fieldValue > target,
                "Less Than" => fieldValue < target,
                "Between" => EvaluateBetween(fieldValue, ruleValue),
                _ => false
            };
        }

        private static bool EvaluateTimeSpanRule(TimeSpan fieldValue, string op, string ruleValue)
        {
            // Value in seconds
            if (!double.TryParse(ruleValue, NumberStyles.Any, CultureInfo.InvariantCulture, out var targetSeconds))
                return false;

            return op switch
            {
                "Greater Than" => fieldValue.TotalSeconds > targetSeconds,
                "Less Than" => fieldValue.TotalSeconds < targetSeconds,
                "Between" => EvaluateBetween(fieldValue.TotalSeconds, ruleValue),
                _ => false
            };
        }

        private static bool EvaluateDateRule(DateTime fieldValue, string op, string ruleValue)
        {
            return op switch
            {
                "In Last Days" => int.TryParse(ruleValue, out var days) && fieldValue >= DateTime.Now.AddDays(-days),
                "Before" => DateTime.TryParse(ruleValue, out var before) && fieldValue < before,
                "After" => DateTime.TryParse(ruleValue, out var after) && fieldValue > after,
                "Between" => EvaluateDateBetween(fieldValue, ruleValue),
                _ => false
            };
        }

        private static bool EvaluateBetween(double value, string ruleValue)
        {
            var parts = ruleValue.Split('-', ',');
            if (parts.Length != 2) return false;
            if (!double.TryParse(parts[0].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var low)) return false;
            if (!double.TryParse(parts[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var high)) return false;
            return value >= low && value <= high;
        }

        private static bool EvaluateDateBetween(DateTime value, string ruleValue)
        {
            var parts = ruleValue.Split(',');
            if (parts.Length != 2) return false;
            if (!DateTime.TryParse(parts[0].Trim(), out var start)) return false;
            if (!DateTime.TryParse(parts[1].Trim(), out var end)) return false;
            return value >= start && value <= end;
        }

        private static double ToDouble(object? value)
        {
            if (value == null) return 0;
            if (value is int i) return i;
            if (value is long l) return l;
            if (value is double d) return d;
            if (value is float f) return f;
            if (value is TimeSpan ts) return ts.TotalSeconds;
            if (double.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var result)) return result;
            return 0;
        }

        private static DateTime ToDateTime(object? value)
        {
            if (value is DateTime dt) return dt;
            if (value == null) return DateTime.MinValue;
            if (DateTime.TryParse(value.ToString(), out var parsed)) return parsed;
            return DateTime.MinValue;
        }
    }
}
