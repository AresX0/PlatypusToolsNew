using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class DependenciesViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "Click Refresh to probe each dependency.";
    public ObservableCollection<DependencyRow> Rows { get; } = new();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Rows.Clear();
        async Task Add(string name, string exe, string installHint)
        {
            var path = Resolve(exe);
            if (path is null)
            {
                Rows.Add(new DependencyRow { Name = name, Path = "(not found)", IsAvailable = false, InstallHint = installHint });
                return;
            }
            string ver = "";
            try { var r = await ShellHelper.RunAsync(path, "--version"); ver = (r.StdOut + " " + r.StdErr).Split('\n', 2)[0].Trim(); }
            catch { }
            Rows.Add(new DependencyRow { Name = name, Path = path, IsAvailable = true, Version = ver, InstallHint = installHint });
        }

        await Add("ffmpeg", ShellHelper.IsWindows ? "ffmpeg.exe" : "ffmpeg",
            ShellHelper.IsWindows ? "scoop install ffmpeg" : ShellHelper.IsMac ? "brew install ffmpeg" : "apt install ffmpeg");
        await Add("ffprobe", ShellHelper.IsWindows ? "ffprobe.exe" : "ffprobe", "(comes with ffmpeg)");
        await Add("qpdf", ShellHelper.IsWindows ? "qpdf.exe" : "qpdf",
            ShellHelper.IsWindows ? "scoop install qpdf" : ShellHelper.IsMac ? "brew install qpdf" : "apt install qpdf");
        await Add("exiftool", ShellHelper.IsWindows ? "exiftool.exe" : "exiftool",
            ShellHelper.IsWindows ? "Download from exiftool.org" : ShellHelper.IsMac ? "brew install exiftool" : "apt install libimage-exiftool-perl");
        await Add("rsync", ShellHelper.IsWindows ? "rsync.exe" : "rsync",
            ShellHelper.IsWindows ? "(use built-in robocopy instead)" : ShellHelper.IsMac ? "preinstalled" : "apt install rsync");
        await Add("ssh-keygen", ShellHelper.IsWindows ? "ssh-keygen.exe" : "ssh-keygen", "(part of OpenSSH)");
        await Add("curl", ShellHelper.IsWindows ? "curl.exe" : "curl", "(usually preinstalled)");
        await Add("wget", ShellHelper.IsWindows ? "wget.exe" : "wget",
            ShellHelper.IsWindows ? "scoop install wget" : ShellHelper.IsMac ? "brew install wget" : "apt install wget");

        Status = $"{Rows.Count} dependencies checked";
    }

    private static string? Resolve(string exe)
    {
        var pathSep = ShellHelper.IsWindows ? ';' : ':';
        var paths = (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(pathSep);
        foreach (var dir in paths)
        {
            try
            {
                var full = Path.Combine(dir, exe);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        // Common macOS bundle paths
        if (ShellHelper.IsMac)
            foreach (var d in new[] { "/opt/homebrew/bin", "/usr/local/bin", "/opt/local/bin" })
                if (File.Exists(Path.Combine(d, exe))) return Path.Combine(d, exe);
        return null;
    }
}

public sealed class DependencyRow
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Version { get; set; } = "";
    public bool IsAvailable { get; set; }
    public string InstallHint { get; set; } = "";
    public string Mark => IsAvailable ? "✅" : "❌";
}
