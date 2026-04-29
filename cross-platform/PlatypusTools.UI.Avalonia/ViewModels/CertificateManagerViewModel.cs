using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class CertificateManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "Lists trusted CA certs from common file stores. (Read-only)";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private CertEntry? _selected;
    [ObservableProperty] private string _details = "";
    public ObservableCollection<CertEntry> Entries { get; } = new();

    partial void OnSelectedChanged(CertEntry? value)
    {
        if (value is null) { Details = ""; return; }
        try
        {
            using var c = X509CertificateLoader.LoadCertificateFromFile(value.Path);
            Details = $"Subject : {c.Subject}\nIssuer  : {c.Issuer}\nSerial  : {c.SerialNumber}\nFrom    : {c.NotBefore}\nUntil   : {c.NotAfter}\nThumbpr : {c.Thumbprint}\n";
        }
        catch (Exception ex) { Details = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsBusy = true; Entries.Clear();
        await Task.Run(() =>
        {
            string[] roots;
            if (ShellHelper.IsWindows)
            {
                // System cert stores aren't easily file-based; show machine root via X509Store
                try
                {
                    using var st = new X509Store(StoreName.Root, StoreLocation.LocalMachine);
                    st.Open(OpenFlags.ReadOnly);
                    foreach (var c in st.Certificates)
                        Entries.Add(new CertEntry { Path = "[LocalMachine\\Root]", Subject = c.Subject, NotAfter = c.NotAfter });
                }
                catch { }
                return;
            }
            if (ShellHelper.IsMac)
                roots = new[] { "/etc/ssl/cert.pem", "/etc/ssl/certs", "/usr/local/etc/openssl@3/cert.pem" };
            else
                roots = new[] { "/etc/ssl/certs", "/etc/pki/tls/certs", "/etc/ca-certificates" };

            foreach (var root in roots)
            {
                if (File.Exists(root))
                    Entries.Add(new CertEntry { Path = root, Subject = "(bundle)", NotAfter = null });
                else if (Directory.Exists(root))
                {
                    foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.TopDirectoryOnly).Take(500))
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext != ".pem" && ext != ".crt" && ext != ".cer") continue;
                        try
                        {
                            using var c = X509CertificateLoader.LoadCertificateFromFile(f);
                            Entries.Add(new CertEntry { Path = f, Subject = c.Subject, NotAfter = c.NotAfter });
                        }
                        catch
                        {
                            Entries.Add(new CertEntry { Path = f, Subject = "(unreadable)", NotAfter = null });
                        }
                    }
                }
            }
        });
        Status = $"Found {Entries.Count} entries";
        IsBusy = false;
    }
}

public sealed class CertEntry
{
    public string Path { get; set; } = "";
    public string Subject { get; set; } = "";
    public DateTime? NotAfter { get; set; }
}
