using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class IntuneBackupViewModel : ObservableObject
{
    [ObservableProperty] private string _accessToken = "";
    [ObservableProperty] private string _outputFolder = "";
    [ObservableProperty] private string _status = "Paste a Microsoft Graph access token (Intune Read scope), pick an output folder, click Backup. Works on any OS — pure HTTP.";
    [ObservableProperty] private bool _isBusy;

    private static readonly string[] Endpoints =
    {
        "deviceAppManagement/mobileApps",
        "deviceAppManagement/mobileAppConfigurations",
        "deviceAppManagement/managedAppPolicies",
        "deviceManagement/deviceConfigurations",
        "deviceManagement/deviceCompliancePolicies",
        "deviceManagement/configurationPolicies",
        "deviceManagement/managedDevices",
        "deviceManagement/roleAssignments",
        "deviceManagement/deviceEnrollmentConfigurations",
        "groups",
    };

    [RelayCommand]
    private async Task BackupAsync()
    {
        if (string.IsNullOrWhiteSpace(AccessToken) || string.IsNullOrWhiteSpace(OutputFolder)) { Status = "Token + folder required"; return; }
        IsBusy = true;
        try
        {
            Directory.CreateDirectory(OutputFolder);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken.Trim());
            int total = 0;
            foreach (var ep in Endpoints)
            {
                Status = "GET " + ep;
                var url = $"https://graph.microsoft.com/v1.0/{ep}";
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[");
                bool first = true;
                while (!string.IsNullOrEmpty(url))
                {
                    using var resp = await http.GetAsync(url);
                    var body = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode) { sb.AppendLine($"  {{\"error\": {body}}}"); break; }
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("value", out var val) && val.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var item in val.EnumerateArray())
                        {
                            if (!first) sb.AppendLine(","); first = false;
                            sb.Append("  ").Append(item.GetRawText());
                            total++;
                        }
                    }
                    url = doc.RootElement.TryGetProperty("@odata.nextLink", out var nl) ? nl.GetString() ?? "" : "";
                }
                sb.AppendLine();
                sb.AppendLine("]");
                var safeName = ep.Replace('/', '_') + ".json";
                File.WriteAllText(Path.Combine(OutputFolder, safeName), sb.ToString());
            }
            Status = $"Done — {total} items across {Endpoints.Length} endpoints → {OutputFolder}";
        }
        catch (System.Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
