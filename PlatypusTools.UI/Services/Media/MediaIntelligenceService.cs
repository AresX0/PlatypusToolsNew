using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.UI.Services.AI;
using PlatypusTools.UI.Services.Files;

namespace PlatypusTools.UI.Services.Media
{
    /// <summary>
    /// Phase 3.2 — Media-aware smart-rename / auto-tag wrapper around
    /// SmartRenameService and LocalLlmService. Provides media-specific hint
    /// generation (extension class, ID3-ish guesses from filename, known
    /// codec hints) so audio/video/image batches get more relevant
    /// suggestions than the generic SmartRenameService.
    /// </summary>
    public sealed class MediaIntelligenceService
    {
        private static readonly Lazy<MediaIntelligenceService> _instance = new(() => new MediaIntelligenceService());
        public static MediaIntelligenceService Instance => _instance.Value;

        private readonly SmartRenameService _renamer = new();

        private static readonly HashSet<string> AudioExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp3", ".flac", ".wav", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".alac", ".ape" };
        private static readonly HashSet<string> VideoExt = new(StringComparer.OrdinalIgnoreCase)
        { ".mp4", ".mkv", ".mov", ".avi", ".wmv", ".webm", ".flv", ".m4v", ".mpg", ".mpeg", ".ts" };
        private static readonly HashSet<string> ImageExt = new(StringComparer.OrdinalIgnoreCase)
        { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".heic", ".heif", ".raw", ".cr2", ".nef", ".arw" };

        public enum MediaKind { Unknown, Audio, Video, Image }

        public static MediaKind Classify(string path)
        {
            var ext = Path.GetExtension(path);
            if (AudioExt.Contains(ext)) return MediaKind.Audio;
            if (VideoExt.Contains(ext)) return MediaKind.Video;
            if (ImageExt.Contains(ext)) return MediaKind.Image;
            return MediaKind.Unknown;
        }

        public Task<List<SmartRenameSuggestion>> SuggestForMediaAsync(
            IEnumerable<string> files, string? extraHint = null, CancellationToken ct = default)
        {
            var paths = files.Where(File.Exists).ToList();
            // Group by kind so we can issue per-kind hints (the underlying
            // LLM sees the hint per file, but a per-kind hint is cheaper to
            // construct and gives more consistent results).
            var grouped = paths.GroupBy(Classify).ToList();
            var allSuggestions = new List<SmartRenameSuggestion>();

            return RunAsync();

            async Task<List<SmartRenameSuggestion>> RunAsync()
            {
                foreach (var grp in grouped)
                {
                    var kindHint = grp.Key switch
                    {
                        MediaKind.Audio => "Audio file. Prefer artist_track_title style. Strip track-numbers and bitrate noise.",
                        MediaKind.Video => "Video file. Prefer show_title_sNNeNN_episode_title or movie_title_year style.",
                        MediaKind.Image => "Image file. Prefer subject_or_event_yyyymmdd style. Drop camera serials.",
                        _ => "Generic file. Use short descriptive snake_case."
                    };
                    var hint = string.IsNullOrWhiteSpace(extraHint) ? kindHint : kindHint + " " + extraHint;
                    var batch = await _renamer.SuggestAsync(grp.Select(p => p), hint, ct).ConfigureAwait(false);
                    allSuggestions.AddRange(batch);
                }
                return allSuggestions;
            }
        }

        public int Apply(IEnumerable<SmartRenameSuggestion> suggestions) => _renamer.Apply(suggestions);

        /// <summary>
        /// Quick local "auto-tag" that derives candidate tag tokens from the
        /// filename without contacting the LLM. Useful for grids that want to
        /// show suggestions instantly while a background LLM call refines them.
        /// </summary>
        public IEnumerable<string> QuickTagsFromName(string path)
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(name)) yield break;
            // Split on common separators
            foreach (var token in name.Split(new[] { ' ', '_', '-', '.', '(', ')', '[', ']' },
                StringSplitOptions.RemoveEmptyEntries))
            {
                var t = token.Trim().ToLowerInvariant();
                if (t.Length < 3) continue;
                if (int.TryParse(t, out _)) continue;
                yield return t;
            }
        }
    }
}
