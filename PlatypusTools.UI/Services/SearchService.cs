using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Advanced file search service with filtering, regex support, and content search.
    /// </summary>
    public class SearchService
    {
        private static SearchService? _instance;
        public static SearchService Instance => _instance ??= new SearchService();

        public event EventHandler<SearchProgressEventArgs>? SearchProgress;
        public event EventHandler<SearchResultEventArgs>? ResultFound;

        /// <summary>
        /// Searches for files matching the specified criteria.
        /// </summary>
        public async Task<SearchResults> SearchAsync(SearchOptions options, CancellationToken ct = default)
        {
            var results = new SearchResults { Options = options, StartTime = DateTime.Now };
            var matchingFiles = new List<SearchResult>();

            await Task.Run(() =>
            {
                try
                {
                    var files = GetFilesToSearch(options, ct);
                    int total = files.Count;
                    int processed = 0;

                    Parallel.ForEach(files, new ParallelOptions 
                    { 
                        CancellationToken = ct,
                        MaxDegreeOfParallelism = Environment.ProcessorCount
                    }, file =>
                    {
                        ct.ThrowIfCancellationRequested();
                        
                        var result = EvaluateFile(file, options);
                        if (result.IsMatch)
                        {
                            lock (matchingFiles) matchingFiles.Add(result);
                            ResultFound?.Invoke(this, new SearchResultEventArgs { Result = result });
                        }

                        var current = Interlocked.Increment(ref processed);
                        if (current % 100 == 0 || current == total)
                        {
                            SearchProgress?.Invoke(this, new SearchProgressEventArgs
                            {
                                ProcessedFiles = current,
                                TotalFiles = total,
                                PercentComplete = (int)(current * 100.0 / total)
                            });
                        }
                    });
                }
                catch (OperationCanceledException) { results.WasCancelled = true; }
                catch (Exception ex) { results.Error = ex.Message; }
            }, ct);

            results.Results = matchingFiles;
            results.EndTime = DateTime.Now;
            return results;
        }

        private List<string> GetFilesToSearch(SearchOptions options, CancellationToken ct)
        {
            var files = new List<string>();
            var searchOption = options.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            foreach (var folder in options.SearchFolders)
            {
                ct.ThrowIfCancellationRequested();
                if (!Directory.Exists(folder)) continue;

                try
                {
                    var pattern = options.FilePattern ?? "*.*";
                    files.AddRange(Directory.EnumerateFiles(folder, pattern, searchOption));
                }
                catch (UnauthorizedAccessException) { }
                catch (DirectoryNotFoundException) { }
            }

            return files;
        }

        private SearchResult EvaluateFile(string filePath, SearchOptions options)
        {
            var result = new SearchResult { FilePath = filePath, FileName = Path.GetFileName(filePath) };

            try
            {
                var info = new FileInfo(filePath);
                result.Size = info.Length;
                result.Modified = info.LastWriteTime;
                result.Created = info.CreationTime;

                // Size filter
                if (options.MinSize.HasValue && info.Length < options.MinSize.Value) return result;
                if (options.MaxSize.HasValue && info.Length > options.MaxSize.Value) return result;

                // Date filter
                if (options.ModifiedAfter.HasValue && info.LastWriteTime < options.ModifiedAfter.Value) return result;
                if (options.ModifiedBefore.HasValue && info.LastWriteTime > options.ModifiedBefore.Value) return result;

                // Name filter
                if (!string.IsNullOrEmpty(options.NamePattern))
                {
                    if (options.UseRegex)
                    {
                        if (!Regex.IsMatch(info.Name, options.NamePattern, 
                            options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase))
                            return result;
                    }
                    else
                    {
                        var comparison = options.CaseSensitive 
                            ? StringComparison.Ordinal 
                            : StringComparison.OrdinalIgnoreCase;
                        if (!info.Name.Contains(options.NamePattern, comparison))
                            return result;
                    }
                }

                // Content search
                if (!string.IsNullOrEmpty(options.ContentPattern))
                {
                    if (info.Length > options.MaxContentSearchSize) return result;
                    
                    var content = File.ReadAllText(filePath);
                    if (options.UseRegex)
                    {
                        var matches = Regex.Matches(content, options.ContentPattern,
                            options.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
                        if (matches.Count == 0) return result;
                        result.ContentMatches = matches.Count;
                    }
                    else
                    {
                        var comparison = options.CaseSensitive 
                            ? StringComparison.Ordinal 
                            : StringComparison.OrdinalIgnoreCase;
                        if (!content.Contains(options.ContentPattern, comparison))
                            return result;
                        result.ContentMatches = CountOccurrences(content, options.ContentPattern, comparison);
                    }
                }

                result.IsMatch = true;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
            }

            return result;
        }

        private int CountOccurrences(string text, string pattern, StringComparison comparison)
        {
            int count = 0, index = 0;
            while ((index = text.IndexOf(pattern, index, comparison)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }
    }

    public class SearchOptions
    {
        public List<string> SearchFolders { get; set; } = new();
        public string? FilePattern { get; set; } = "*.*";
        public string? NamePattern { get; set; }
        public string? ContentPattern { get; set; }
        public bool UseRegex { get; set; }
        public bool CaseSensitive { get; set; }
        public bool IncludeSubfolders { get; set; } = true;
        public long? MinSize { get; set; }
        public long? MaxSize { get; set; }
        public DateTime? ModifiedAfter { get; set; }
        public DateTime? ModifiedBefore { get; set; }
        public long MaxContentSearchSize { get; set; } = 10 * 1024 * 1024; // 10MB
    }

    public class SearchResult
    {
        public string FilePath { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Size { get; set; }
        public DateTime Modified { get; set; }
        public DateTime Created { get; set; }
        public int ContentMatches { get; set; }
        public bool IsMatch { get; set; }
        public string? Error { get; set; }
    }

    public class SearchResults
    {
        public SearchOptions? Options { get; set; }
        public List<SearchResult> Results { get; set; } = new();
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public TimeSpan Duration => EndTime - StartTime;
        public int TotalMatches => Results.Count;
        public bool WasCancelled { get; set; }
        public string? Error { get; set; }
    }

    public class SearchProgressEventArgs : EventArgs
    {
        public int ProcessedFiles { get; set; }
        public int TotalFiles { get; set; }
        public int PercentComplete { get; set; }
    }

    public class SearchResultEventArgs : EventArgs
    {
        public SearchResult? Result { get; set; }
    }
}
