using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class SshKeyManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _sshDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh");
    [ObservableProperty] private string _newKeyName = "id_ed25519_new";
    [ObservableProperty] private string _newKeyType = "ed25519";
    [ObservableProperty] private string _newKeyComment = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private SshKeyEntry? _selected;
    [ObservableProperty] private string _output = "";

    public ObservableCollection<SshKeyEntry> Keys { get; } = new();
    public string[] KeyTypes { get; } = new[] { "ed25519", "rsa", "ecdsa" };

    public SshKeyManagerViewModel() { Refresh(); }

    [RelayCommand]
    private void Refresh()
    {
        Keys.Clear();
        try
        {
            if (!Directory.Exists(SshDir)) { Status = $"~/.ssh not found at {SshDir}"; return; }
            foreach (var f in Directory.EnumerateFiles(SshDir).OrderBy(f => f))
            {
                if (f.EndsWith(".pub", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(f).StartsWith("id_"))
                {
                    var fi = new FileInfo(f);
                    Keys.Add(new SshKeyEntry { Path = f, Name = fi.Name, IsPublic = f.EndsWith(".pub"), Size = fi.Length });
                }
            }
            Status = $"{Keys.Count} key file(s)";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        try
        {
            Directory.CreateDirectory(SshDir);
            var path = Path.Combine(SshDir, NewKeyName);
            if (File.Exists(path)) { Status = "Key already exists"; return; }
            var args = $"-t {NewKeyType} -f \"{path}\" -N \"\"";
            if (!string.IsNullOrWhiteSpace(NewKeyComment)) args += $" -C \"{NewKeyComment}\"";
            Output = $"$ ssh-keygen {args}\n";
            var r = await ShellHelper.RunAsync("ssh-keygen", args, default,
                line => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => Output += line + "\n"));
            Status = r.ExitCode == 0 ? "Key generated" : $"Failed (exit {r.ExitCode})";
            Refresh();
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void ShowPublic()
    {
        if (Selected is null) return;
        try
        {
            var pub = Selected.IsPublic ? Selected.Path : Selected.Path + ".pub";
            Output = File.Exists(pub) ? File.ReadAllText(pub) : "(public key not found)";
            Status = $"Showing {Path.GetFileName(pub)}";
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }

    [RelayCommand]
    private void Delete()
    {
        if (Selected is null) return;
        try
        {
            File.Delete(Selected.Path);
            var pub = Selected.Path + ".pub";
            if (File.Exists(pub)) File.Delete(pub);
            Status = $"Deleted {Selected.Name}";
            Refresh();
        }
        catch (Exception ex) { Status = $"Error: {ex.Message}"; }
    }
}

public sealed class SshKeyEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public bool IsPublic { get; set; }
    public long Size { get; set; }
    public string Kind => IsPublic ? "Public" : "Private";
}
