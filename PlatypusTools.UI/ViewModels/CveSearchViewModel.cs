using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Represents a single CVE search result.
    /// </summary>
    public class CveSearchResult : BindableBase
    {
        public string CveId { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Published { get; set; } = string.Empty;
        public string LastModified { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double? BaseScore { get; set; }
        public string Severity { get; set; } = string.Empty;
        public string CvssVector { get; set; } = string.Empty;
        public List<string> References { get; set; } = new();
        public List<string> AffectedProducts { get; set; } = new();

        /// <summary>Short description for DataGrid display (truncated to ~200 chars).</summary>
        public string ShortDescription => Description.Length > 200
            ? Description[..200] + "…"
            : Description;

        /// <summary>Score display with color hint.</summary>
        public string ScoreDisplay => BaseScore.HasValue
            ? $"{BaseScore.Value:F1} ({Severity})"
            : "N/A";

        /// <summary>References formatted as newline-separated list.</summary>
        public string ReferencesText => References.Count > 0
            ? string.Join("\n", References)
            : "None";

        /// <summary>Affected products formatted as newline-separated list.</summary>
        public string AffectedProductsText => AffectedProducts.Count > 0
            ? string.Join("\n", AffectedProducts)
            : "None";
    }

    /// <summary>
    /// ViewModel for the CVE Search tab.
    /// Uses the MITRE CVE AWG API for direct CVE ID lookups and the NVD API v2.0 for keyword searches.
    /// </summary>
    public class CveSearchViewModel : BindableBase
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private const string MitreCveApiBase = "https://cveawg.mitre.org/api/cve";
        private const string NvdApiBase = "https://services.nvd.nist.gov/rest/json/cves/2.0";

        private string _searchQuery = string.Empty;
        private int _selectedSearchType; // 0 = CVE ID, 1 = Keyword
        private bool _isSearching;
        private string _statusText = "Ready. Enter a CVE ID (e.g. CVE-2024-1234) or keyword to search.";
        private CveSearchResult? _selectedResult;
        private CancellationTokenSource? _searchCts;

        public CveSearchViewModel()
        {
            Results = new ObservableCollection<CveSearchResult>();

            SearchCommand = new AsyncRelayCommand(SearchAsync, () => !IsSearching && !string.IsNullOrWhiteSpace(SearchQuery));
            ClearCommand = new RelayCommand(_ => ClearResults());
            CopyIdCommand = new RelayCommand(_ => CopySelectedId(), _ => SelectedResult != null);
            CopyDescriptionCommand = new RelayCommand(_ => CopySelectedDescription(), _ => SelectedResult != null);
            ExportCommand = new AsyncRelayCommand(ExportResultsAsync, () => Results.Count > 0);
            OpenReferenceCommand = new RelayCommand(OpenReference);
            CancelCommand = new RelayCommand(_ => CancelSearch(), _ => IsSearching);
        }

        #region Properties

        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    // Auto-detect CVE ID pattern and switch search type
                    if (!string.IsNullOrWhiteSpace(value) && value.StartsWith("CVE-", StringComparison.OrdinalIgnoreCase))
                    {
                        SelectedSearchType = 0; // CVE ID
                    }
                    ((AsyncRelayCommand)SearchCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public int SelectedSearchType
        {
            get => _selectedSearchType;
            set => SetProperty(ref _selectedSearchType, value);
        }

        public bool IsSearching
        {
            get => _isSearching;
            set
            {
                if (SetProperty(ref _isSearching, value))
                {
                    ((AsyncRelayCommand)SearchCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
                    ((AsyncRelayCommand)ExportCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get => _statusText;
            set => SetProperty(ref _statusText, value);
        }

        public CveSearchResult? SelectedResult
        {
            get => _selectedResult;
            set
            {
                if (SetProperty(ref _selectedResult, value))
                {
                    ((RelayCommand)CopyIdCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CopyDescriptionCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public ObservableCollection<CveSearchResult> Results { get; }

        public string[] SearchTypes { get; } = { "CVE ID", "Keyword" };

        #endregion

        #region Commands

        public ICommand SearchCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand CopyIdCommand { get; }
        public ICommand CopyDescriptionCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand OpenReferenceCommand { get; }
        public ICommand CancelCommand { get; }

        #endregion

        #region Search Logic

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            IsSearching = true;
            StatusText = "Searching...";
            Results.Clear();
            SelectedResult = null;

            try
            {
                if (SelectedSearchType == 0)
                {
                    await SearchByCveIdAsync(SearchQuery.Trim(), ct);
                }
                else
                {
                    await SearchByKeywordAsync(SearchQuery.Trim(), ct);
                }

                StatusText = Results.Count == 0
                    ? "No results found."
                    : $"Found {Results.Count} result(s).";
            }
            catch (OperationCanceledException)
            {
                StatusText = "Search cancelled.";
            }
            catch (HttpRequestException ex)
            {
                StatusText = $"Network error: {ex.Message}";
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
            }
            finally
            {
                IsSearching = false;
            }
        }

        /// <summary>
        /// Lookup a specific CVE by ID using the MITRE CVE AWG API.
        /// Endpoint: GET https://cveawg.mitre.org/api/cve/{CVE-ID}
        /// </summary>
        private async Task SearchByCveIdAsync(string cveId, CancellationToken ct)
        {
            // Normalize the CVE ID
            cveId = cveId.Trim().ToUpperInvariant();
            if (!cveId.StartsWith("CVE-"))
                cveId = "CVE-" + cveId;

            StatusText = $"Looking up {cveId} via MITRE API...";

            var url = $"{MitreCveApiBase}/{cveId}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(request, ct);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                StatusText = $"{cveId} not found.";
                return;
            }

            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            var result = ParseMitreCveRecord(doc.RootElement);
            if (result != null)
            {
                Results.Add(result);
            }
        }

        /// <summary>
        /// Search for CVEs by keyword using the NVD API v2.0.
        /// Endpoint: GET https://services.nvd.nist.gov/rest/json/cves/2.0?keywordSearch={query}
        /// </summary>
        private async Task SearchByKeywordAsync(string keyword, CancellationToken ct)
        {
            StatusText = $"Searching NVD for \"{keyword}\"...";

            var url = $"{NvdApiBase}?keywordSearch={Uri.EscapeDataString(keyword)}&resultsPerPage=50";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");

            using var response = await _httpClient.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("vulnerabilities", out var vulns))
            {
                foreach (var vuln in vulns.EnumerateArray())
                {
                    if (ct.IsCancellationRequested) break;

                    if (vuln.TryGetProperty("cve", out var cve))
                    {
                        var result = ParseNvdCveRecord(cve);
                        if (result != null)
                        {
                            Results.Add(result);
                        }
                    }
                }
            }

            // Also show total results count from NVD
            if (doc.RootElement.TryGetProperty("totalResults", out var total))
            {
                var totalCount = total.GetInt32();
                if (totalCount > Results.Count)
                {
                    StatusText = $"Showing {Results.Count} of {totalCount} total results.";
                }
            }
        }

        #endregion

        #region JSON Parsing

        /// <summary>
        /// Parses a CVE Record from the MITRE CVE AWG API (CVE JSON 5.x format).
        /// </summary>
        private static CveSearchResult? ParseMitreCveRecord(JsonElement root)
        {
            var result = new CveSearchResult();

            // cveMetadata
            if (root.TryGetProperty("cveMetadata", out var meta))
            {
                result.CveId = meta.TryGetProperty("cveId", out var id) ? id.GetString() ?? "" : "";
                result.State = meta.TryGetProperty("state", out var state) ? state.GetString() ?? "" : "";
                result.Published = meta.TryGetProperty("datePublished", out var pub) ? FormatDate(pub.GetString()) : "";
                result.LastModified = meta.TryGetProperty("dateUpdated", out var upd) ? FormatDate(upd.GetString()) : "";
            }

            // containers.cna
            if (root.TryGetProperty("containers", out var containers) &&
                containers.TryGetProperty("cna", out var cna))
            {
                // Description
                if (cna.TryGetProperty("descriptions", out var descs))
                {
                    foreach (var desc in descs.EnumerateArray())
                    {
                        var lang = desc.TryGetProperty("lang", out var l) ? l.GetString() : "";
                        if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(result.Description))
                        {
                            result.Description = desc.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                            if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)) break;
                        }
                    }
                }

                // Metrics (CVSS)
                if (cna.TryGetProperty("metrics", out var metrics))
                {
                    foreach (var metric in metrics.EnumerateArray())
                    {
                        // Try cvssV3_1, cvssV3_0, cvssV4_0
                        foreach (var cvssKey in new[] { "cvssV4_0", "cvssV3_1", "cvssV3_0", "cvssV2_0" })
                        {
                            if (metric.TryGetProperty(cvssKey, out var cvss))
                            {
                                if (cvss.TryGetProperty("baseScore", out var score))
                                    result.BaseScore = score.GetDouble();
                                if (cvss.TryGetProperty("baseSeverity", out var sev))
                                    result.Severity = sev.GetString() ?? "";
                                if (cvss.TryGetProperty("vectorString", out var vec))
                                    result.CvssVector = vec.GetString() ?? "";
                                break;
                            }
                        }
                        if (result.BaseScore.HasValue) break;
                    }
                }

                // References
                if (cna.TryGetProperty("references", out var refs))
                {
                    foreach (var r in refs.EnumerateArray())
                    {
                        if (r.TryGetProperty("url", out var url))
                        {
                            result.References.Add(url.GetString() ?? "");
                        }
                    }
                }

                // Affected products
                if (cna.TryGetProperty("affected", out var affected))
                {
                    foreach (var a in affected.EnumerateArray())
                    {
                        var vendor = a.TryGetProperty("vendor", out var v) ? v.GetString() ?? "" : "";
                        var product = a.TryGetProperty("product", out var p) ? p.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(vendor) || !string.IsNullOrEmpty(product))
                        {
                            result.AffectedProducts.Add($"{vendor} / {product}");
                        }
                    }
                }
            }

            return string.IsNullOrEmpty(result.CveId) ? null : result;
        }

        /// <summary>
        /// Parses a CVE Record from the NVD API v2.0 response.
        /// </summary>
        private static CveSearchResult? ParseNvdCveRecord(JsonElement cve)
        {
            var result = new CveSearchResult();

            result.CveId = cve.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
            result.Published = cve.TryGetProperty("published", out var pub) ? FormatDate(pub.GetString()) : "";
            result.LastModified = cve.TryGetProperty("lastModified", out var mod) ? FormatDate(mod.GetString()) : "";
            result.State = cve.TryGetProperty("vulnStatus", out var status) ? status.GetString() ?? "" : "";

            // Description
            if (cve.TryGetProperty("descriptions", out var descs))
            {
                foreach (var desc in descs.EnumerateArray())
                {
                    var lang = desc.TryGetProperty("lang", out var l) ? l.GetString() : "";
                    if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(result.Description))
                    {
                        result.Description = desc.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                        if (string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)) break;
                    }
                }
            }

            // Metrics — try CVSS v3.1, v3.0, v2
            if (cve.TryGetProperty("metrics", out var metrics))
            {
                foreach (var metricKey in new[] { "cvssMetricV31", "cvssMetricV30", "cvssMetricV2" })
                {
                    if (metrics.TryGetProperty(metricKey, out var metricArray))
                    {
                        foreach (var m in metricArray.EnumerateArray())
                        {
                            if (m.TryGetProperty("cvssData", out var cvssData))
                            {
                                if (cvssData.TryGetProperty("baseScore", out var score))
                                    result.BaseScore = score.GetDouble();
                                if (cvssData.TryGetProperty("baseSeverity", out var sev))
                                    result.Severity = sev.GetString() ?? "";
                                if (cvssData.TryGetProperty("vectorString", out var vec))
                                    result.CvssVector = vec.GetString() ?? "";
                            }
                            break; // take first metric
                        }
                    }
                    if (result.BaseScore.HasValue) break;
                }
            }

            // References
            if (cve.TryGetProperty("references", out var refs))
            {
                foreach (var r in refs.EnumerateArray())
                {
                    if (r.TryGetProperty("url", out var url))
                    {
                        result.References.Add(url.GetString() ?? "");
                    }
                }
            }

            // Configurations → affected products (NVD format)
            if (cve.TryGetProperty("configurations", out var configs))
            {
                foreach (var config in configs.EnumerateArray())
                {
                    if (config.TryGetProperty("nodes", out var nodes))
                    {
                        foreach (var node in nodes.EnumerateArray())
                        {
                            if (node.TryGetProperty("cpeMatch", out var cpeMatches))
                            {
                                foreach (var cpe in cpeMatches.EnumerateArray())
                                {
                                    if (cpe.TryGetProperty("criteria", out var criteria))
                                    {
                                        var cpeStr = criteria.GetString() ?? "";
                                        // Parse CPE: cpe:2.3:a:vendor:product:version:...
                                        var parts = cpeStr.Split(':');
                                        if (parts.Length >= 5)
                                        {
                                            var vendor = parts[3];
                                            var product = parts[4];
                                            var entry = $"{vendor} / {product}";
                                            if (!result.AffectedProducts.Contains(entry))
                                                result.AffectedProducts.Add(entry);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return string.IsNullOrEmpty(result.CveId) ? null : result;
        }

        private static string FormatDate(string? isoDate)
        {
            if (string.IsNullOrEmpty(isoDate)) return "";
            if (DateTime.TryParse(isoDate, out var dt))
                return dt.ToString("yyyy-MM-dd HH:mm");
            return isoDate;
        }

        #endregion

        #region Actions

        private void ClearResults()
        {
            Results.Clear();
            SelectedResult = null;
            SearchQuery = string.Empty;
            StatusText = "Ready. Enter a CVE ID (e.g. CVE-2024-1234) or keyword to search.";
        }

        private void CopySelectedId()
        {
            if (SelectedResult != null)
            {
                Clipboard.SetText(SelectedResult.CveId);
                StatusText = $"Copied {SelectedResult.CveId} to clipboard.";
            }
        }

        private void CopySelectedDescription()
        {
            if (SelectedResult != null)
            {
                Clipboard.SetText(SelectedResult.Description);
                StatusText = $"Copied description for {SelectedResult.CveId} to clipboard.";
            }
        }

        private void CancelSearch()
        {
            _searchCts?.Cancel();
        }

        private void OpenReference(object? parameter)
        {
            if (parameter is string url && !string.IsNullOrWhiteSpace(url))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    StatusText = $"Could not open URL: {ex.Message}";
                }
            }
        }

        private async Task ExportResultsAsync()
        {
            if (Results.Count == 0) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|JSON Files (*.json)|*.json|Text Files (*.txt)|*.txt",
                DefaultExt = ".csv",
                FileName = $"CVE_Search_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var ext = System.IO.Path.GetExtension(dialog.FileName).ToLowerInvariant();
                    if (ext == ".json")
                    {
                        await ExportAsJsonAsync(dialog.FileName);
                    }
                    else if (ext == ".csv")
                    {
                        await ExportAsCsvAsync(dialog.FileName);
                    }
                    else
                    {
                        await ExportAsTextAsync(dialog.FileName);
                    }
                    StatusText = $"Exported {Results.Count} result(s) to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusText = $"Export error: {ex.Message}";
                }
            }
        }

        private async Task ExportAsCsvAsync(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("CVE ID,Published,Score,Severity,State,Description");
            foreach (var r in Results)
            {
                var desc = r.Description.Replace("\"", "\"\"");
                sb.AppendLine($"\"{r.CveId}\",\"{r.Published}\",\"{r.BaseScore?.ToString("F1") ?? "N/A"}\",\"{r.Severity}\",\"{r.State}\",\"{desc}\"");
            }
            await System.IO.File.WriteAllTextAsync(path, sb.ToString());
        }

        private async Task ExportAsJsonAsync(string path)
        {
            var data = Results.Select(r => new
            {
                r.CveId,
                r.Published,
                r.LastModified,
                r.BaseScore,
                r.Severity,
                r.CvssVector,
                r.State,
                r.Description,
                r.References,
                r.AffectedProducts
            });
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            await System.IO.File.WriteAllTextAsync(path, json);
        }

        private async Task ExportAsTextAsync(string path)
        {
            var sb = new StringBuilder();
            foreach (var r in Results)
            {
                sb.AppendLine($"=== {r.CveId} ===");
                sb.AppendLine($"Published: {r.Published}");
                sb.AppendLine($"Score: {r.ScoreDisplay}");
                sb.AppendLine($"State: {r.State}");
                sb.AppendLine($"Description: {r.Description}");
                sb.AppendLine($"References: {r.ReferencesText}");
                sb.AppendLine($"Affected: {r.AffectedProductsText}");
                sb.AppendLine();
            }
            await System.IO.File.WriteAllTextAsync(path, sb.ToString());
        }

        #endregion
    }
}
