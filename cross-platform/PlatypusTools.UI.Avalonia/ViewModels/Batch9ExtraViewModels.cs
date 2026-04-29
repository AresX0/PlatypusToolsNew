using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

// === Advanced Forensics: entropy + magic-byte sniff ===
public partial class AdvancedForensicsViewModel : ObservableObject
{
    [ObservableProperty] private string _file = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task AnalyzeAsync()
    {
        if (!System.IO.File.Exists(File)) { Result = "File missing"; return; }
        IsBusy = true;
        await Task.Run(() =>
        {
            try
            {
                var bytes = System.IO.File.ReadAllBytes(File);
                var freq = new int[256];
                foreach (var b in bytes) freq[b]++;
                double H = 0; foreach (var c in freq) if (c > 0) { var p = c / (double)bytes.Length; H -= p * Math.Log2(p); }
                string magic = bytes.Length < 4 ? "<short>" : Convert.ToHexString(bytes, 0, Math.Min(8, bytes.Length));
                string sig = magic switch
                {
                    var m when m.StartsWith("4D5A") => "PE / EXE",
                    var m when m.StartsWith("7F454C46") => "ELF",
                    var m when m.StartsWith("CFFAEDFE") || m.StartsWith("FEEDFACE") || m.StartsWith("CAFEBABE") => "Mach-O",
                    var m when m.StartsWith("504B0304") => "ZIP/Office/JAR",
                    var m when m.StartsWith("25504446") => "PDF",
                    _ => "Unknown"
                };
                Result = $"Size: {bytes.Length:N0}\nEntropy: {H:F4} (max 8)\nMagic: {magic}\nGuess: {sig}";
            }
            catch (Exception ex) { Result = ex.Message; }
        });
        IsBusy = false;
    }
}

// === Mail Client (IMAP via MailKit) ===
public partial class MailClientViewModel : ObservableObject
{
    [ObservableProperty] private string _server = "imap.gmail.com";
    [ObservableProperty] private int _port = 993;
    [ObservableProperty] private string _username = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _status = "IMAP read-only inbox preview. Use an app password.";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<MailItem> Messages { get; } = new();

    [RelayCommand]
    private async Task FetchAsync()
    {
        IsBusy = true; Messages.Clear();
        try
        {
            using var c = new MailKit.Net.Imap.ImapClient();
            await c.ConnectAsync(Server, Port, MailKit.Security.SecureSocketOptions.SslOnConnect);
            await c.AuthenticateAsync(Username, Password);
            var inbox = c.Inbox!; await inbox.OpenAsync(MailKit.FolderAccess.ReadOnly);
            int n = Math.Min(25, inbox.Count);
            for (int i = inbox.Count - 1; i >= inbox.Count - n && i >= 0; i--)
            {
                var m = await inbox.GetMessageAsync(i);
                Messages.Add(new MailItem { From = m.From.ToString(), Subject = m.Subject ?? "", Date = m.Date.LocalDateTime });
            }
            await c.DisconnectAsync(true);
            Status = $"{Messages.Count} messages";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
public sealed class MailItem { public string From { get; set; } = ""; public string Subject { get; set; } = ""; public DateTime Date { get; set; } }

// === Reboot Analyzer ===
public partial class RebootAnalyzerViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        ShellHelper.Result r;
        if (ShellHelper.IsWindows) r = await ShellHelper.RunAsync("wevtutil.exe", "qe System \"/q:*[System[(EventID=6005 or EventID=6006 or EventID=1074 or EventID=41)]]\" /f:text /c:50 /rd:true");
        else if (ShellHelper.IsMac) r = await ShellHelper.RunAsync("last", "reboot");
        else r = await ShellHelper.RunAsync("/bin/sh", "-c \"who -b; echo ---; last -x reboot shutdown | head -n 30\"");
        Output = r.ExitCode == 0 ? r.StdOut : r.StdErr;
        IsBusy = false;
    }
}

// === Recent Cleanup ===
public partial class RecentCleanupViewModel : ObservableObject
{
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _dryRun = true;

    [RelayCommand]
    private void Clean()
    {
        var sb = new StringBuilder();
        var paths = ShellHelper.IsWindows ? new[] {
            Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Windows\Recent"),
            Environment.ExpandEnvironmentVariables(@"%APPDATA%\Microsoft\Office\Recent"),
        } : ShellHelper.IsMac ? new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/com.apple.sharedfilelist"),
        } : new[] {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/recently-used.xbel"),
        };
        foreach (var p in paths)
        {
            try
            {
                if (Directory.Exists(p))
                {
                    var files = Directory.GetFiles(p);
                    sb.AppendLine($"{p}: {files.Length} files");
                    if (!DryRun) foreach (var f in files) try { System.IO.File.Delete(f); } catch { }
                }
                else if (System.IO.File.Exists(p))
                {
                    sb.AppendLine($"{p}: file");
                    if (!DryRun) try { System.IO.File.Delete(p); } catch { }
                }
                else sb.AppendLine($"{p}: <missing>");
            }
            catch (Exception ex) { sb.AppendLine(p + ": " + ex.Message); }
        }
        Result = sb.ToString() + (DryRun ? "\n(dry run — uncheck to delete)" : "\nDeleted.");
    }
}

// === File Cleaner ===
public partial class FileCleanerViewModel : ObservableObject
{
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _patterns = "*.tmp;*.bak;Thumbs.db;.DS_Store";
    [ObservableProperty] private bool _dryRun = true;
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task RunAsync()
    {
        if (!Directory.Exists(Root)) { Result = "Pick a root"; return; }
        IsBusy = true;
        await Task.Run(() =>
        {
            var sb = new StringBuilder();
            var pats = Patterns.Split(';', StringSplitOptions.RemoveEmptyEntries);
            int n = 0; long bytes = 0;
            foreach (var pat in pats)
            {
                try
                {
                    foreach (var f in Directory.EnumerateFiles(Root, pat, SearchOption.AllDirectories))
                    {
                        try { var fi = new FileInfo(f); bytes += fi.Length; n++; sb.AppendLine(f); if (!DryRun) System.IO.File.Delete(f); }
                        catch { }
                    }
                }
                catch { }
            }
            Result = sb.ToString() + $"\n{n} files, {bytes / 1024.0 / 1024.0:F1} MB" + (DryRun ? " (dry run)" : " (deleted)");
        });
        IsBusy = false;
    }
}

// === System Hardening ===
public partial class SystemHardeningViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task AuditAsync()
    {
        IsBusy = true;
        var sb = new StringBuilder();
        if (ShellHelper.IsWindows)
        {
            sb.AppendLine("# Firewall");  sb.AppendLine((await ShellHelper.RunAsync("netsh", "advfirewall show allprofiles state")).StdOut);
            sb.AppendLine("# Defender"); sb.AppendLine((await ShellHelper.RunAsync("powershell", "-NoProfile -Command \"Get-MpPreference | Select-Object DisableRealtimeMonitoring, DisableBehaviorMonitoring | Format-List\"")).StdOut);
        }
        else if (ShellHelper.IsMac)
        {
            sb.AppendLine("# Firewall"); sb.AppendLine((await ShellHelper.RunAsync("/usr/libexec/ApplicationFirewall/socketfilterfw", "--getglobalstate")).StdOut);
            sb.AppendLine("# Gatekeeper"); sb.AppendLine((await ShellHelper.RunAsync("spctl", "--status")).StdOut);
            sb.AppendLine("# SIP"); sb.AppendLine((await ShellHelper.RunAsync("csrutil", "status")).StdOut);
        }
        else
        {
            sb.AppendLine("# UFW"); sb.AppendLine((await ShellHelper.RunAsync("/bin/sh", "-c \"sudo -n ufw status 2>/dev/null || ufw status\"")).StdOut);
            sb.AppendLine("# SSH"); sb.AppendLine((await ShellHelper.RunAsync("/bin/sh", "-c \"grep -E '^(PermitRootLogin|PasswordAuthentication)' /etc/ssh/sshd_config\"")).StdOut);
            sb.AppendLine("# Sudoers (NOPASSWD?)"); sb.AppendLine((await ShellHelper.RunAsync("/bin/sh", "-c \"sudo -n grep -r NOPASSWD /etc/sudoers /etc/sudoers.d 2>/dev/null\"")).StdOut);
        }
        Output = sb.ToString();
        IsBusy = false;
    }
}

// === System Restore (snapshot listing) ===
public partial class SystemRestoreViewModel : ObservableObject
{
    [ObservableProperty] private string _output = "";

    [RelayCommand]
    private async Task ListAsync()
    {
        if (ShellHelper.IsWindows)
            Output = (await ShellHelper.RunAsync("powershell", "-NoProfile -Command \"Get-ComputerRestorePoint | Format-Table -AutoSize | Out-String -Width 200\"")).StdOut;
        else if (ShellHelper.IsMac)
            Output = (await ShellHelper.RunAsync("tmutil", "listbackups")).StdOut;
        else
            Output = (await ShellHelper.RunAsync("/bin/sh", "-c \"ls -1 /var/lib/snapper/configs 2>/dev/null; ls -1 /.snapshots 2>/dev/null; timeshift --list 2>/dev/null\"")).StdOut;
    }
}

// === Plex Backup ===
public partial class PlexBackupViewModel : ObservableObject
{
    [ObservableProperty] private string _plexConfig = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private bool _isBusy;

    public PlexBackupViewModel()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (ShellHelper.IsWindows) PlexConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plex Media Server");
        else if (ShellHelper.IsMac) PlexConfig = Path.Combine(home, "Library/Application Support/Plex Media Server");
        else PlexConfig = Path.Combine(home, ".config/plex/Library/Application Support/Plex Media Server");
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        if (!Directory.Exists(PlexConfig) || string.IsNullOrEmpty(Output)) { Status = "Pick a valid Plex config + zip output"; return; }
        IsBusy = true;
        try
        {
            await Task.Run(() => System.IO.Compression.ZipFile.CreateFromDirectory(PlexConfig, Output, System.IO.Compression.CompressionLevel.Fastest, true));
            Status = "Done → " + Output;
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }
}

// === Upscaler (FFmpeg lanczos) ===
public partial class UpscalerViewModel : ObservableObject
{
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private int _scale = 2;
    [ObservableProperty] private string _status = "Bicubic/lanczos upscale via FFmpeg.";
    [ObservableProperty] private bool _isBusy;

    public int[] Scales { get; } = { 2, 3, 4 };

    [RelayCommand]
    private async Task UpscaleAsync()
    {
        if (string.IsNullOrEmpty(Input) || string.IsNullOrEmpty(Output)) { Status = "Pick in/out"; return; }
        IsBusy = true;
        var args = $"-y -i \"{Input}\" -vf \"scale=iw*{Scale}:ih*{Scale}:flags=lanczos\" \"{Output}\"";
        var r = await ShellHelper.RunAsync(FFmpegRunner.FFmpegPath, args);
        Status = r.ExitCode == 0 ? "Done" : "Failed";
        IsBusy = false;
    }
}

// === Theme Builder ===
public partial class ThemeBuilderViewModel : ObservableObject
{
    [ObservableProperty] private string _accent = "#4CAF50";
    [ObservableProperty] private string _background = "#1E1E1E";
    [ObservableProperty] private string _foreground = "#FFFFFF";
    [ObservableProperty] private string _output = "";
    [ObservableProperty] private string _status = "Generates an Avalonia ResourceDictionary.";

    [RelayCommand]
    private void Generate()
    {
        Output = $"<ResourceDictionary xmlns=\"https://github.com/avaloniaui\">\n" +
                 $"  <Color x:Key=\"AccentColor\">{Accent}</Color>\n" +
                 $"  <SolidColorBrush x:Key=\"AccentBrush\" Color=\"{Accent}\"/>\n" +
                 $"  <SolidColorBrush x:Key=\"BackgroundBrush\" Color=\"{Background}\"/>\n" +
                 $"  <SolidColorBrush x:Key=\"ForegroundBrush\" Color=\"{Foreground}\"/>\n" +
                 $"</ResourceDictionary>\n";
        Status = "Generated";
    }
}

// === Encrypted Clipboard ===
public partial class EncryptedClipboardViewModel : ObservableObject
{
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _result = "";

    [RelayCommand]
    private void Encrypt()
    {
        try { Result = Convert.ToBase64String(Aes(Encoding.UTF8.GetBytes(Text), Password, true)); }
        catch (Exception ex) { Result = "Error: " + ex.Message; }
    }
    [RelayCommand]
    private void Decrypt()
    {
        try { Result = Encoding.UTF8.GetString(Aes(Convert.FromBase64String(Text), Password, false)); }
        catch (Exception ex) { Result = "Error: " + ex.Message; }
    }

    private static byte[] Aes(byte[] data, string pass, bool encrypt)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var key = sha.ComputeHash(Encoding.UTF8.GetBytes(pass));
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Key = key; aes.Mode = System.Security.Cryptography.CipherMode.CBC; aes.Padding = System.Security.Cryptography.PaddingMode.PKCS7;
        if (encrypt)
        {
            aes.GenerateIV();
            using var enc = aes.CreateEncryptor();
            var ct = enc.TransformFinalBlock(data, 0, data.Length);
            var output = new byte[16 + ct.Length]; Buffer.BlockCopy(aes.IV, 0, output, 0, 16); Buffer.BlockCopy(ct, 0, output, 16, ct.Length);
            return output;
        }
        else
        {
            var iv = new byte[16]; Buffer.BlockCopy(data, 0, iv, 0, 16);
            aes.IV = iv;
            using var dec = aes.CreateDecryptor();
            return dec.TransformFinalBlock(data, 16, data.Length - 16);
        }
    }
}

// === Export Queue ===
public partial class ExportQueueViewModel : ObservableObject
{
    public ObservableCollection<ExportJob> Jobs { get; } = new();
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newCmd = "";
    [ObservableProperty] private string _status = "Queue arbitrary export shell commands. Run sequentially.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand] private void Add() { if (!string.IsNullOrEmpty(NewName)) { Jobs.Add(new ExportJob { Name = NewName, Command = NewCmd, State = "Pending" }); NewName = ""; NewCmd = ""; } }
    [RelayCommand] private void Remove(ExportJob? j) { if (j is not null) Jobs.Remove(j); }
    [RelayCommand]
    private async Task RunAsync()
    {
        IsBusy = true;
        foreach (var j in Jobs)
        {
            if (j.State == "Done") continue;
            j.State = "Running";
            var shell = ShellHelper.IsWindows ? "cmd.exe" : "/bin/sh";
            var argFlag = ShellHelper.IsWindows ? "/c " : "-c ";
            var r = await ShellHelper.RunAsync(shell, argFlag + "\"" + j.Command.Replace("\"", "\\\"") + "\"");
            j.State = r.ExitCode == 0 ? "Done" : "Failed";
        }
        Status = "Run complete";
        IsBusy = false;
    }
}
public sealed partial class ExportJob : ObservableObject
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _command = "";
    [ObservableProperty] private string _state = "Pending";
}

// === AI Assistant (OpenAI-compatible) ===
public partial class AIAssistantViewModel : ObservableObject
{
    [ObservableProperty] private string _endpoint = "https://api.openai.com/v1/chat/completions";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _model = "gpt-4o-mini";
    [ObservableProperty] private string _prompt = "";
    [ObservableProperty] private string _response = "";
    [ObservableProperty] private bool _isBusy;

    public AIAssistantViewModel()
    {
        var s = AppSettings.Load();
        Endpoint = s.AiEndpoint ?? Endpoint;
        ApiKey = s.AiApiKey ?? "";
        Model = s.AiModel ?? Model;
    }

    [RelayCommand]
    private void Save()
    {
        var s = AppSettings.Load();
        s.AiEndpoint = Endpoint; s.AiApiKey = ApiKey; s.AiModel = Model; s.Save();
        Response = "Saved";
    }

    [RelayCommand]
    private async Task AskAsync()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Prompt)) { Response = "Enter API key + prompt"; return; }
        IsBusy = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
            var payload = new { model = Model, messages = new[] { new { role = "user", content = Prompt } } };
            var resp = await http.PostAsJsonAsync(Endpoint, payload);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode) { Response = "HTTP " + (int)resp.StatusCode + ": " + body; }
            else
            {
                using var doc = JsonDocument.Parse(body);
                Response = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? body;
            }
        }
        catch (Exception ex) { Response = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }
}

// === Workflow Designer (JSON node list) ===
public partial class WorkflowDesignerViewModel : ObservableObject
{
    public ObservableCollection<WorkflowStep> Steps { get; } = new();
    [ObservableProperty] private string _newKind = "shell";
    [ObservableProperty] private string _newArg = "";
    [ObservableProperty] private string _status = "Linear workflow — each step's command runs in order.";
    [ObservableProperty] private bool _isBusy;
    public string[] Kinds { get; } = { "shell", "delay", "echo" };

    [RelayCommand] private void Add() => Steps.Add(new WorkflowStep { Kind = NewKind, Arg = NewArg });
    [RelayCommand] private void Remove(WorkflowStep? s) { if (s is not null) Steps.Remove(s); }
    [RelayCommand]
    private async Task RunAsync()
    {
        IsBusy = true;
        foreach (var s in Steps)
        {
            s.State = "Running";
            try
            {
                if (s.Kind == "delay" && int.TryParse(s.Arg, out var ms)) await Task.Delay(ms);
                else if (s.Kind == "echo") { Status = s.Arg; }
                else
                {
                    var shell = ShellHelper.IsWindows ? "cmd.exe" : "/bin/sh";
                    var argFlag = ShellHelper.IsWindows ? "/c " : "-c ";
                    var r = await ShellHelper.RunAsync(shell, argFlag + "\"" + s.Arg.Replace("\"", "\\\"") + "\"");
                    s.State = r.ExitCode == 0 ? "Done" : "Failed"; continue;
                }
                s.State = "Done";
            }
            catch (Exception ex) { s.State = "Failed: " + ex.Message; }
        }
        IsBusy = false;
    }
}
public sealed partial class WorkflowStep : ObservableObject
{
    [ObservableProperty] private string _kind = "shell";
    [ObservableProperty] private string _arg = "";
    [ObservableProperty] private string _state = "Pending";
}

// === Fleet View (host list with SSH ping) ===
public partial class FleetViewModel : ObservableObject
{
    public ObservableCollection<FleetHost> Hosts { get; } = new();
    [ObservableProperty] private string _newHost = "";
    [ObservableProperty] private string _newUser = "";
    [ObservableProperty] private string _status = "List hosts. Ping checks reachability; SSH probe runs `uptime` over ssh.";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand] private void Add() { if (!string.IsNullOrEmpty(NewHost)) { Hosts.Add(new FleetHost { Host = NewHost, User = NewUser }); NewHost = ""; NewUser = ""; } }
    [RelayCommand] private void Remove(FleetHost? h) { if (h is not null) Hosts.Remove(h); }
    [RelayCommand]
    private async Task ProbeAsync()
    {
        IsBusy = true;
        foreach (var h in Hosts)
        {
            var ping = await ShellHelper.RunAsync(ShellHelper.IsWindows ? "ping" : "ping", (ShellHelper.IsWindows ? "-n 1 -w 1500 " : "-c 1 -W 2 ") + h.Host);
            h.Reachable = ping.ExitCode == 0;
            if (!h.Reachable) { h.Uptime = ""; continue; }
            var target = string.IsNullOrEmpty(h.User) ? h.Host : $"{h.User}@{h.Host}";
            var ssh = await ShellHelper.RunAsync("ssh", $"-o BatchMode=yes -o ConnectTimeout=4 {target} uptime");
            h.Uptime = ssh.ExitCode == 0 ? ssh.StdOut.Trim() : "ssh fail";
        }
        IsBusy = false;
    }
}
public sealed partial class FleetHost : ObservableObject
{
    [ObservableProperty] private string _host = "";
    [ObservableProperty] private string _user = "";
    [ObservableProperty] private bool _reachable;
    [ObservableProperty] private string _uptime = "";
}

// === Remote Dashboard (subset of Fleet view as status grid) ===
public partial class RemoteDashboardViewModel : FleetViewModel { }

// === Plugin Marketplace ===
public partial class PluginMarketplaceViewModel : ObservableObject
{
    [ObservableProperty] private string _registryUrl = "https://example.com/plugins.json";
    [ObservableProperty] private string _status = "Fetches a JSON registry of {name, version, url, description}.";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<MarketPlugin> Plugins { get; } = new();

    [RelayCommand]
    private async Task FetchAsync()
    {
        IsBusy = true; Plugins.Clear();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            var json = await http.GetStringAsync(RegistryUrl);
            using var doc = JsonDocument.Parse(json);
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                Plugins.Add(new MarketPlugin
                {
                    Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                    Version = e.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "",
                    Url = e.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "",
                    Description = e.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "",
                });
            }
            Status = $"{Plugins.Count} plugins";
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
public sealed class MarketPlugin
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Description { get; set; } = "";
}

// === Global Search (recursive grep) ===
public partial class GlobalSearchViewModel : ObservableObject
{
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _pattern = "";
    [ObservableProperty] private string _result = "";
    [ObservableProperty] private bool _isBusy;

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!Directory.Exists(Root) || string.IsNullOrEmpty(Pattern)) { Result = "Pick root + pattern"; return; }
        IsBusy = true;
        await Task.Run(() =>
        {
            var sb = new StringBuilder(); int n = 0;
            try
            {
                foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        int ln = 0;
                        foreach (var line in System.IO.File.ReadLines(f))
                        {
                            ln++;
                            if (line.Contains(Pattern, StringComparison.OrdinalIgnoreCase))
                            {
                                sb.AppendLine($"{f}:{ln}: {line.Trim()}");
                                if (++n >= 1000) goto done;
                            }
                        }
                    }
                    catch { }
                }
                done:;
            }
            catch (Exception ex) { sb.AppendLine(ex.Message); }
            Result = sb.ToString() + $"\n{n} matches";
        });
        IsBusy = false;
    }
}

// === Update Checker (GitHub releases) ===
public partial class UpdateCheckerViewModel : ObservableObject
{
    [ObservableProperty] private string _repo;
    [ObservableProperty] private string _status = "Checks the latest GitHub release.";
    [ObservableProperty] private string _latestTag = "";
    [ObservableProperty] private string _latestUrl = "";
    [ObservableProperty] private bool _isBusy;

    public UpdateCheckerViewModel()
    {
        var s = AppSettings.Load();
        _repo = s.UpdateRepo ?? "Codename-Reborn/PlatypusTools";
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        IsBusy = true;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PlatypusTools-Avalonia");
            var json = await http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
            using var doc = JsonDocument.Parse(json);
            LatestTag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            LatestUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "";
            Status = "Latest: " + LatestTag;
        }
        catch (Exception ex) { Status = "Error: " + ex.Message; }
        finally { IsBusy = false; }
    }
}
