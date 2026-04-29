using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace PlatypusTools.UI.Avalonia.ViewModels;

// =====================================================================
// Service Manager — cross-platform service listing + start/stop/restart
// Linux:  systemctl
// Mac:    launchctl
// Win:    sc query
// =====================================================================
public sealed partial class ServiceManagerViewModel : ObservableObject
{
    public ObservableCollection<string> Services { get; } = new();
    [ObservableProperty] private string? _selected;
    [ObservableProperty] private string _status = "Idle";
    [ObservableProperty] private string _filter = "";

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Services.Clear();
        Status = "Loading…";
        try
        {
            string file, args;
            if (ShellHelper.IsLinux)        { file = "systemctl"; args = "list-units --type=service --no-pager --no-legend"; }
            else if (ShellHelper.IsMac)     { file = "launchctl"; args = "list"; }
            else                            { file = "sc";        args = "query state= all"; }

            var r = await ShellHelper.RunAsync(file, args);
            var f = (Filter ?? "").Trim();
            foreach (var line in r.StdOut.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0))
            {
                if (f.Length == 0 || line.Contains(f, StringComparison.OrdinalIgnoreCase))
                    Services.Add(line);
            }
            Status = $"{Services.Count} services";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand] private Task StartAsync()   => ActAsync("start");
    [RelayCommand] private Task StopAsync()    => ActAsync("stop");
    [RelayCommand] private Task RestartAsync() => ActAsync("restart");

    private async Task ActAsync(string verb)
    {
        if (string.IsNullOrWhiteSpace(Selected)) { Status = "Select a service first"; return; }
        // Pull the first whitespace-delimited token as the unit/service name.
        var name = Selected.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(name)) { Status = "Cannot parse service name"; return; }

        try
        {
            string file, args;
            if (ShellHelper.IsLinux)
            {
                file = "systemctl"; args = $"{verb} {name}";
            }
            else if (ShellHelper.IsMac)
            {
                // launchctl has no 'restart' — stop+start.
                if (verb == "restart")
                {
                    await ShellHelper.RunAsync("launchctl", $"stop {name}");
                    await ShellHelper.RunAsync("launchctl", $"start {name}");
                    Status = $"Restarted {name}"; return;
                }
                file = "launchctl"; args = $"{verb} {name}";
            }
            else
            {
                if (verb == "restart")
                {
                    await ShellHelper.RunAsync("sc", $"stop {name}");
                    await ShellHelper.RunAsync("sc", $"start {name}");
                    Status = $"Restarted {name}"; return;
                }
                file = "sc"; args = $"{verb} {name}";
            }
            var r = await ShellHelper.RunAsync(file, args);
            Status = r.ExitCode == 0 ? $"{verb} {name} OK" : $"{verb} {name} failed: {r.StdErr.Trim()}";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }
}

// =====================================================================
// Directory / LDAP Security Analyzer — cross-platform via ldapsearch.
// Targets ANY LDAP server (AD, OpenLDAP, FreeIPA). No Windows assumption.
// =====================================================================
public sealed partial class DirectorySecurityAnalyzerViewModel : ObservableObject
{
    [ObservableProperty] private string _host = "ldap://example.local";
    [ObservableProperty] private string _baseDn = "DC=example,DC=local";
    [ObservableProperty] private string _bindDn = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _filter = "(objectClass=user)";
    [ObservableProperty] private string _attributes = "cn,sAMAccountName,userAccountControl,memberOf";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Idle";

    public string[] Presets { get; } =
    {
        "(objectClass=user)",
        "(&(objectCategory=person)(objectClass=user)(!(userAccountControl:1.2.840.113556.1.4.803:=2)))",     // enabled users
        "(&(objectCategory=person)(objectClass=user)(userAccountControl:1.2.840.113556.1.4.803:=65536))",   // password never expires
        "(&(objectCategory=group)(adminCount=1))",                                                          // admin groups
        "(&(objectClass=computer)(operatingSystem=*Server*))",                                              // servers
        "(memberOf=CN=Domain Admins,CN=Users,DC=example,DC=local)",                                         // domain admins
    };

    [ObservableProperty] private string _selectedPreset = "";

    partial void OnSelectedPresetChanged(string value)
    {
        if (!string.IsNullOrEmpty(value)) Filter = value;
    }

    [RelayCommand]
    private async Task QueryAsync()
    {
        Output = "";
        Status = "Querying…";
        try
        {
            var args = $"-H {Host} -x -b \"{BaseDn}\" \"{Filter}\" {Attributes.Replace(',', ' ')}";
            if (!string.IsNullOrWhiteSpace(BindDn))
                args = $"-D \"{BindDn}\" -w \"{Password}\" " + args;
            // -LLL = LDIF, no comments, no version.
            args = "-LLL " + args;
            var r = await ShellHelper.RunAsync("ldapsearch", args);
            Output = r.StdOut;
            Status = r.ExitCode == 0 ? "OK" : $"ldapsearch exit {r.ExitCode}: {r.StdErr.Trim()}";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message} (install openldap-clients / ldap-utils)"; }
    }
}
