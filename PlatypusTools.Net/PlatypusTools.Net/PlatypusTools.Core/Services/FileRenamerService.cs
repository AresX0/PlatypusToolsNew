using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PlatypusTools.Core.Models;

namespace PlatypusTools.Core.Services
{
    public interface IFileRenamerService
    {
        List<RenameOperation> ScanFolder(string folderPath, bool includeSubfolders, FileTypeFilter fileTypeFilter);
        void ApplyPrefixRules(List<RenameOperation> operations, string? newPrefix, string? oldPrefix, string? ignorePrefix, bool normalizeCasing, bool onlyFilesWithOldPrefix, bool addPrefixToAll);
        void ApplySeasonEpisodeNumbering(List<RenameOperation> operations, int? seasonNumber, int seasonLeadingZeros, int startEpisodeNumber, int episodeLeadingZeros, bool renumberAlphabetically, bool includeSeasonInFormat);
        void ApplyCleaningRules(List<RenameOperation> operations, bool removeCommonTokens, bool remove1080p, bool remove4k, bool removeHD, string[]? customTokens);
        void ApplyNormalization(List<RenameOperation> operations, NormalizationPreset preset);
        List<(string oldPath, string newPath)> ApplyChanges(List<RenameOperation> operations, bool dryRun = false);
        void UndoChanges(List<(string oldPath, string newPath)> backupList);
        string DetectCommonPrefix(List<RenameOperation> operations);
        FilenameComponents ParseFilenameComponents(string filename, string? knownPrefix = null);
        string GetCoreNameForSorting(string filename, string? detectedPrefix = null, string? oldPrefix = null, string? newPrefix = null);
    }

    public class FileRenamerService : IFileRenamerService
    {
        private static readonly Dictionary<FileTypeFilter, string[]> FileExtensions = new()
        {
            { FileTypeFilter.Video, new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts" } },
            { FileTypeFilter.Picture, new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tif", ".tiff", ".webp", ".svg", ".ico", ".heic", ".heif" } },
            { FileTypeFilter.Document, new[] { ".pdf", ".doc", ".docx", ".txt", ".rtf", ".odt", ".xls", ".xlsx", ".ppt", ".pptx", ".md" } },
            { FileTypeFilter.Audio, new[] { ".mp3", ".wav", ".flac", ".m4a", ".aac", ".ogg", ".wma", ".opus" } },
            { FileTypeFilter.Archive, new[] { ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz" } }
        };

        private static readonly string[] CommonTokens = new[] { "720", "720p", "1080", "1080p", "4k", "8k", "4K", "8K", "hd", "HD", "HDTV", "WEBRip", "BluRay", "BRRip" };

        public List<RenameOperation> ScanFolder(
            string folderPath,
            bool includeSubfolders,
            FileTypeFilter fileTypeFilter)
        {
            var operations = new List<RenameOperation>();
            var searchOption = includeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            if (!Directory.Exists(folderPath))
                return operations;

            var files = Directory.GetFiles(folderPath, "*.*", searchOption);

            foreach (var filePath in files)
            {
                if (ShouldIncludeFile(filePath, fileTypeFilter))
                {
                    var fileName = Path.GetFileName(filePath);
                    var directory = Path.GetDirectoryName(filePath) ?? string.Empty;
                    var coreNameForSorting = GetCoreNameForSorting(fileName, null, null, null);

                    operations.Add(new RenameOperation
                    {
                        OriginalPath = filePath,
                        ProposedPath = filePath,
                        Directory = directory,
                        OriginalFileName = fileName,
                        ProposedFileName = fileName,
                        OriginalFileNameForSorting = coreNameForSorting,
                        Status = RenameStatus.Pending,
                        IsSelected = true
                    });
                }
            }

            return operations;
        }

        public void ApplyPrefixRules(
            List<RenameOperation> operations,
            string? newPrefix,
            string? oldPrefix,
            string? ignorePrefix,
            bool normalizeCasing,
            bool onlyFilesWithOldPrefix,
            bool addPrefixToAll)
        {
            foreach (var op in operations)
            {
                var fileName = op.ProposedFileName;
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);

                // Skip if file has ignore prefix
                if (!string.IsNullOrEmpty(ignorePrefix) && 
                    nameWithoutExt.StartsWith(ignorePrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip if only processing files with old prefix and this doesn't have it
                if (onlyFilesWithOldPrefix && !string.IsNullOrEmpty(oldPrefix))
                {
                    if (!nameWithoutExt.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                // Remove old prefix if specified
                bool hadOldPrefix = false;
                if (!string.IsNullOrEmpty(oldPrefix) && 
                    nameWithoutExt.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    hadOldPrefix = true;
                    nameWithoutExt = nameWithoutExt.Substring(oldPrefix.Length);
                    // Trim separators that were after the prefix
                    if (nameWithoutExt.StartsWith("-") || nameWithoutExt.StartsWith("_") || 
                        nameWithoutExt.StartsWith(" ") || nameWithoutExt.StartsWith("."))
                    {
                        nameWithoutExt = nameWithoutExt.Substring(1);
                    }
                }

                // Add new prefix if specified
                if (!string.IsNullOrEmpty(newPrefix))
                {
                    // Determine if we should add the prefix:
                    // - If both oldPrefix and newPrefix are specified, ALWAYS replace (remove old, add new)
                    // - If addPrefixToAll is true, always add
                    // - If we removed an old prefix (hadOldPrefix), add new one
                    // - If onlyFilesWithOldPrefix is false and no oldPrefix constraint, add to all
                    bool shouldAddPrefix = (!string.IsNullOrEmpty(oldPrefix) && !string.IsNullOrEmpty(newPrefix)) ||
                                          addPrefixToAll || hadOldPrefix || 
                                          (!onlyFilesWithOldPrefix && string.IsNullOrEmpty(oldPrefix));
                    
                    if (shouldAddPrefix)
                    {
                        // Check if already has this prefix
                        if (!nameWithoutExt.StartsWith(newPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            // Add prefix with separator dash
                            nameWithoutExt = newPrefix + "-" + nameWithoutExt;
                        }
                        else if (normalizeCasing)
                        {
                            // Force correct casing
                            var afterPrefix = nameWithoutExt.Substring(newPrefix.Length);
                            nameWithoutExt = newPrefix + afterPrefix;
                            op.RequiresCaseOnlyRename = true;
                        }
                    }
                }

                op.ProposedFileName = nameWithoutExt + ext;
                op.ProposedPath = Path.Combine(op.Directory, op.ProposedFileName);
            }
        }

        public void ApplySeasonEpisodeNumbering(
            List<RenameOperation> operations,
            int? seasonNumber,
            int seasonLeadingZeros,
            int startEpisodeNumber,
            int episodeLeadingZeros,
            bool renumberAlphabetically,
            bool includeSeasonInFormat)
        {
            if (renumberAlphabetically)
            {
                // Group by directory and sort alphabetically by ORIGINAL core filename
                var grouped = operations.GroupBy(o => o.Directory).ToList();
                
                foreach (var group in grouped)
                {
                    // Sort by original filename (ignoring current prefix/episode modifications)
                    var sorted = group.OrderBy(o => string.IsNullOrEmpty(o.OriginalFileNameForSorting) 
                        ? Path.GetFileNameWithoutExtension(o.OriginalFileName) 
                        : o.OriginalFileNameForSorting).ToList();
                    int episodeNum = startEpisodeNumber;

                    foreach (var op in sorted)
                    {
                        var fileName = op.ProposedFileName;
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        var ext = Path.GetExtension(fileName);

                        // AGGRESSIVELY remove ALL existing season/episode patterns
                        // Patterns: S01, S1, E01, E1, S01E01, etc.
                        // Remove patterns that follow prefix (Prefix-E01-) or standalone (E01-)
                        nameWithoutExt = Regex.Replace(nameWithoutExt, @"-?S\d+", "", RegexOptions.IgnoreCase);
                        nameWithoutExt = Regex.Replace(nameWithoutExt, @"-?E\d+", "", RegexOptions.IgnoreCase);
                        nameWithoutExt = Regex.Replace(nameWithoutExt, @"^S\d+", "", RegexOptions.IgnoreCase);
                        nameWithoutExt = Regex.Replace(nameWithoutExt, @"^E\d+", "", RegexOptions.IgnoreCase);
                        nameWithoutExt = nameWithoutExt.Trim('-', ' ');

                        // Build season/episode string with correct leading zeros
                        string seasonEpisodeStr;
                        if (includeSeasonInFormat && seasonNumber.HasValue)
                        {
                            var seasonFormat = new string('0', seasonLeadingZeros);
                            var episodeFormat = new string('0', episodeLeadingZeros);
                            seasonEpisodeStr = $"S{seasonNumber.Value.ToString(seasonFormat)}E{episodeNum.ToString(episodeFormat)}";
                        }
                        else
                        {
                            var episodeFormat = new string('0', episodeLeadingZeros);
                            seasonEpisodeStr = $"E{episodeNum.ToString(episodeFormat)}";
                        }

                        // Insert S##E## right after prefix (before first dash)
                        // Format: Prefix-S##E##-RestOfName or E##-RestOfName
                        var firstDashIndex = nameWithoutExt.IndexOf('-');
                        if (firstDashIndex > 0)
                        {
                            // Has prefix with dash separator
                            var prefix = nameWithoutExt.Substring(0, firstDashIndex);
                            var rest = nameWithoutExt.Substring(firstDashIndex + 1).TrimStart('-');
                            
                            // Only add dash before rest if rest is not empty
                            if (!string.IsNullOrEmpty(rest))
                                nameWithoutExt = $"{prefix}-{seasonEpisodeStr}-{rest}";
                            else
                                nameWithoutExt = $"{prefix}-{seasonEpisodeStr}";
                        }
                        else
                        {
                            // No prefix or dash, prepend S##E##
                            nameWithoutExt = $"{seasonEpisodeStr}-{nameWithoutExt}";
                        }

                        op.ProposedFileName = nameWithoutExt + ext;
                        op.ProposedPath = Path.Combine(op.Directory, op.ProposedFileName);
                        episodeNum++;
                    }
                }
            }
        }

        public void ApplyCleaningRules(
            List<RenameOperation> operations,
            bool removeCommonTokens,
            bool remove1080p,
            bool remove4k,
            bool removeHD,
            string[]? customTokens)
        {
            var tokensToRemove = new List<string>();
            if (removeCommonTokens) tokensToRemove.AddRange(CommonTokens);
            if (remove1080p) tokensToRemove.AddRange(new[] { "1080", "1080p" });
            if (remove4k) tokensToRemove.AddRange(new[] { "4k", "4K" });
            if (removeHD) tokensToRemove.AddRange(new[] { "hd", "HD", "HDTV" });
            if (customTokens != null && customTokens.Length > 0)
                tokensToRemove.AddRange(customTokens);

            foreach (var op in operations)
            {
                var fileName = op.ProposedFileName;
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);

                foreach (var token in tokensToRemove)
                {
                    // Remove token (case insensitive)
                    nameWithoutExt = Regex.Replace(nameWithoutExt, Regex.Escape(token), "", RegexOptions.IgnoreCase);
                }

                // Clean up multiple spaces/dashes/underscores
                nameWithoutExt = Regex.Replace(nameWithoutExt, @"\s+", " ");
                nameWithoutExt = Regex.Replace(nameWithoutExt, @"-+", "-");
                nameWithoutExt = Regex.Replace(nameWithoutExt, @"_+", "_");
                nameWithoutExt = nameWithoutExt.Trim(' ', '-', '_', '.');

                op.ProposedFileName = nameWithoutExt + ext;
                op.ProposedPath = Path.Combine(op.Directory, op.ProposedFileName);
            }
        }

        public void ApplyNormalization(
            List<RenameOperation> operations,
            NormalizationPreset preset)
        {
            foreach (var op in operations)
            {
                var fileName = op.ProposedFileName;
                var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                var ext = Path.GetExtension(fileName);

                switch (preset)
                {
                    case NormalizationPreset.SpacesToDashes:
                        nameWithoutExt = nameWithoutExt.Replace(' ', '-');
                        break;
                    case NormalizationPreset.SpacesToUnderscores:
                        nameWithoutExt = nameWithoutExt.Replace(' ', '_');
                        break;
                    case NormalizationPreset.RemoveSpaces:
                        nameWithoutExt = nameWithoutExt.Replace(" ", "");
                        break;
                    case NormalizationPreset.DashesToSpaces:
                        nameWithoutExt = nameWithoutExt.Replace('-', ' ');
                        break;
                    case NormalizationPreset.UnderscoresToSpaces:
                        nameWithoutExt = nameWithoutExt.Replace('_', ' ');
                        break;
                    case NormalizationPreset.DashesToUnderscores:
                        nameWithoutExt = nameWithoutExt.Replace('-', '_');
                        break;
                    case NormalizationPreset.UnderscoresToDashes:
                        nameWithoutExt = nameWithoutExt.Replace('_', '-');
                        break;
                }

                // Clean up multiple consecutive separators
                nameWithoutExt = Regex.Replace(nameWithoutExt, @"\s+", " ");
                nameWithoutExt = Regex.Replace(nameWithoutExt, @"-+", "-");
                nameWithoutExt = Regex.Replace(nameWithoutExt, @"_+", "_");
                nameWithoutExt = nameWithoutExt.Trim(' ', '-', '_', '.');

                op.ProposedFileName = nameWithoutExt + ext;
                op.ProposedPath = Path.Combine(op.Directory, op.ProposedFileName);
            }
        }

        public string DetectCommonPrefix(List<RenameOperation> operations)
        {
            if (operations == null || operations.Count == 0)
                return string.Empty;

            var filenames = operations.Select(o => Path.GetFileNameWithoutExtension(o.OriginalFileName)).ToList();
            if (filenames.Count == 0)
                return string.Empty;

            var firstFile = filenames[0];
            var commonPrefix = string.Empty;

            for (int i = 1; i <= firstFile.Length; i++)
            {
                var prefix = firstFile.Substring(0, i);
                if (filenames.All(f => f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                {
                    commonPrefix = prefix;
                }
                else
                {
                    break;
                }
            }

            // Trim to last separator
            var lastDash = commonPrefix.LastIndexOf('-');
            var lastUnderscore = commonPrefix.LastIndexOf('_');
            var lastSpace = commonPrefix.LastIndexOf(' ');
            var lastSeparator = Math.Max(lastDash, Math.Max(lastUnderscore, lastSpace));

            if (lastSeparator > 0)
                commonPrefix = commonPrefix.Substring(0, lastSeparator);

            return commonPrefix.Trim();
        }

        public FilenameComponents ParseFilenameComponents(string filename, string? knownPrefix = null)
        {
            var components = new FilenameComponents
            {
                OriginalFileName = filename,
                Extension = Path.GetExtension(filename)
            };

            var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);

            // Extract prefix if known
            if (!string.IsNullOrEmpty(knownPrefix) && 
                nameWithoutExt.StartsWith(knownPrefix, StringComparison.OrdinalIgnoreCase))
            {
                components.Prefix = knownPrefix;
                nameWithoutExt = nameWithoutExt.Substring(knownPrefix.Length).TrimStart('-', '_', ' ', '.');
            }

            // Extract season/episode patterns
            var seasonEpisodeMatch = Regex.Match(nameWithoutExt, @"(?i)S(\d{1,4})E(\d{1,4})", RegexOptions.IgnoreCase);
            if (seasonEpisodeMatch.Success)
            {
                components.Season = seasonEpisodeMatch.Groups[0].Value;
                components.SeasonNum = int.Parse(seasonEpisodeMatch.Groups[1].Value);
                components.Episode = seasonEpisodeMatch.Groups[0].Value;
                components.EpisodeNum = int.Parse(seasonEpisodeMatch.Groups[2].Value);
                nameWithoutExt = nameWithoutExt.Replace(seasonEpisodeMatch.Groups[0].Value, "").Trim('-', '_', ' ', '.');
            }
            else
            {
                // Try episode only
                var episodeMatch = Regex.Match(nameWithoutExt, @"(?i)E(\d{1,4})", RegexOptions.IgnoreCase);
                if (episodeMatch.Success)
                {
                    components.Episode = episodeMatch.Groups[0].Value;
                    components.EpisodeNum = int.Parse(episodeMatch.Groups[1].Value);
                    nameWithoutExt = nameWithoutExt.Replace(episodeMatch.Groups[0].Value, "").Trim('-', '_', ' ', '.');
                }

                // Try season only
                var seasonMatch = Regex.Match(nameWithoutExt, @"(?i)S(\d{1,4})", RegexOptions.IgnoreCase);
                if (seasonMatch.Success)
                {
                    components.Season = seasonMatch.Groups[0].Value;
                    components.SeasonNum = int.Parse(seasonMatch.Groups[1].Value);
                    nameWithoutExt = nameWithoutExt.Replace(seasonMatch.Groups[0].Value, "").Trim('-', '_', ' ', '.');
                }
            }

            components.CoreName = nameWithoutExt.Trim();
            return components;
        }

        public string GetCoreNameForSorting(string filename, string? detectedPrefix = null, string? oldPrefix = null, string? newPrefix = null)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);

            // Remove prefixes
            foreach (var prefix in new[] { detectedPrefix, oldPrefix, newPrefix }.Where(p => !string.IsNullOrEmpty(p)))
            {
                if (nameWithoutExt.StartsWith(prefix!, StringComparison.OrdinalIgnoreCase))
                {
                    nameWithoutExt = nameWithoutExt.Substring(prefix!.Length).TrimStart('-', '_', ' ', '.');
                    break;
                }
            }

            // Remove season/episode patterns
            nameWithoutExt = Regex.Replace(nameWithoutExt, @"(?i)S\d{1,4}E\d{1,4}", "", RegexOptions.IgnoreCase);
            nameWithoutExt = Regex.Replace(nameWithoutExt, @"(?i)S\d{1,4}", "", RegexOptions.IgnoreCase);
            nameWithoutExt = Regex.Replace(nameWithoutExt, @"(?i)E\d{1,4}", "", RegexOptions.IgnoreCase);
            nameWithoutExt = Regex.Replace(nameWithoutExt, @"(?i)Season\s*\d{1,4}", "", RegexOptions.IgnoreCase);
            nameWithoutExt = Regex.Replace(nameWithoutExt, @"(?i)Episode\s*\d{1,4}", "", RegexOptions.IgnoreCase);

            // Remove common quality tokens
            foreach (var token in CommonTokens)
            {
                nameWithoutExt = Regex.Replace(nameWithoutExt, Regex.Escape(token), "", RegexOptions.IgnoreCase);
            }

            nameWithoutExt = nameWithoutExt.Trim(' ', '-', '_', '.');
            return nameWithoutExt;
        }

        public List<(string oldPath, string newPath)> ApplyChanges(List<RenameOperation> operations, bool dryRun = false)
        {
            var backupList = new List<(string oldPath, string newPath)>();

            foreach (var op in operations.Where(o => o.IsSelected))
            {
                // Check if no change needed
                if (op.ProposedPath.Equals(op.OriginalPath, StringComparison.OrdinalIgnoreCase) &&
                    !op.RequiresCaseOnlyRename)
                {
                    op.Status = RenameStatus.NoChange;
                    continue;
                }

                if (dryRun)
                {
                    op.Status = RenameStatus.DryRun;
                    continue;
                }

                try
                {
                    if (!File.Exists(op.OriginalPath))
                    {
                        op.Status = RenameStatus.Failed;
                        op.ErrorMessage = "Source file not found";
                        continue;
                    }

                    // Check if destination already exists (and it's not just case difference)
                    if (File.Exists(op.ProposedPath) && 
                        !op.ProposedPath.Equals(op.OriginalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        op.Status = RenameStatus.Failed;
                        op.ErrorMessage = "Destination file already exists";
                        continue;
                    }

                    // Handle case-only rename (Windows workaround)
                    if (op.RequiresCaseOnlyRename || 
                        op.ProposedPath.Equals(op.OriginalPath, StringComparison.OrdinalIgnoreCase))
                    {
                        var tempPath = Path.Combine(op.Directory, Guid.NewGuid().ToString() + Path.GetExtension(op.OriginalPath));
                        File.Move(op.OriginalPath, tempPath);
                        File.Move(tempPath, op.ProposedPath);
                    }
                    else
                    {
                        File.Move(op.OriginalPath, op.ProposedPath);
                    }

                    backupList.Add((op.OriginalPath, op.ProposedPath));
                    op.Status = RenameStatus.Success;
                    op.OriginalPath = op.ProposedPath;
                    op.OriginalFileName = op.ProposedFileName;
                }
                catch (Exception ex)
                {
                    op.Status = RenameStatus.Failed;
                    op.ErrorMessage = ex.Message;
                }
            }

            return backupList;
        }

        public void UndoChanges(List<(string oldPath, string newPath)> backupList)
        {
            foreach (var (oldPath, newPath) in backupList.AsEnumerable().Reverse())
            {
                try
                {
                    if (File.Exists(newPath))
                    {
                        File.Move(newPath, oldPath);
                    }
                }
                catch
                {
                    // Log error but continue undoing others
                }
            }
        }

        private bool ShouldIncludeFile(string filePath, FileTypeFilter filter)
        {
            if (filter == FileTypeFilter.All)
                return true;

            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            return FileExtensions.TryGetValue(filter, out var extensions) && extensions.Contains(ext);
        }
    }
}
