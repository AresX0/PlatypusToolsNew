using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class CveSearchViewModel : ObservableObject
{
    [ObservableProperty] private string _query = "";
    [ObservableProperty] private string _status = "Search NVD by keyword (e.g. \"openssl\") or CVE id.";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<CveRow> Results { get; } = new();

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query)) { Status = "Type a keyword or CVE id"; return; }
        IsBusy = true; Results.Clear(); Status = "Querying NVD…";
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PlatypusTools/1.0");
            var q = Uri.EscapeDataString(Query.Trim());
            var url = Query.Trim().StartsWith("CVE-", StringComparison.OrdinalIgnoreCase)
                ? $"https://services.nvd.nist.gov/rest/json/cves/2.0?cveId={q}"
                : $"https://services.nvd.nist.gov/rest/json/cves/2.0?keywordSearch={q}&resultsPerPage=40";
            var json = await http.GetStringAsync(url);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("vulnerabilities", out var vulns))
            {
                foreach (var v in vulns.EnumerateArray())
                {
                    var cve = v.GetProperty("cve");
                    string id = cve.GetProperty("id").GetString() ?? "";
                    string published = cve.TryGetProperty("published", out var pub) ? (pub.GetString() ?? "") : "";
                    string desc = "";
                    if (cve.TryGetProperty("descriptions", out var descs))
                        foreach (var d in descs.EnumerateArray())
                            if (d.GetProperty("lang").GetString() == "en") { desc = d.GetProperty("value").GetString() ?? ""; break; }
                    string sev = "?", score = "?";
                    if (cve.TryGetProperty("metrics", out var m))
                    {
                        foreach (var key in new[] { "cvssMetricV31", "cvssMetricV30", "cvssMetricV2" })
                            if (m.TryGetProperty(key, out var arr) && arr.GetArrayLength() > 0)
                            {
                                var first = arr[0].GetProperty("cvssData");
                                if (first.TryGetProperty("baseScore", out var bs)) score = bs.GetDouble().ToString("0.0");
                                if (first.TryGetProperty("baseSeverity", out var bsev)) sev = bsev.GetString() ?? sev;
                                break;
                            }
                    }
                    Results.Add(new CveRow { Id = id, Severity = sev, Score = score, Published = published, Description = desc });
                }
            }
            Status = $"{Results.Count} results";
        }
        catch (Exception ex) { Status = "Failed: " + ex.Message; }
        IsBusy = false;
    }
}

public sealed class CveRow
{
    public string Id { get; set; } = "";
    public string Severity { get; set; } = "";
    public string Score { get; set; } = "";
    public string Published { get; set; } = "";
    public string Description { get; set; } = "";
}
