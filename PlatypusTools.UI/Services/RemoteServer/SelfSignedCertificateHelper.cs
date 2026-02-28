using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Generates and caches a self-signed X.509 certificate for the Platypus Remote Server HTTPS endpoint.
/// This eliminates the dependency on 'dotnet dev-certs https' which requires the .NET SDK.
/// The certificate is stored in %APPDATA%\PlatypusTools\ and reused until it expires.
/// </summary>
public static class SelfSignedCertificateHelper
{
    private static readonly string CertDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlatypusTools");

    private static readonly string CertPath = Path.Combine(CertDirectory, "server-cert.pfx");

    // Certificate is valid for 2 years
    private const int CertValidityDays = 730;

    // Renew when less than 30 days remain
    private const int CertRenewThresholdDays = 30;

    /// <summary>
    /// Gets or creates a self-signed certificate for the HTTPS server.
    /// The certificate is cached on disk and reused until near expiry.
    /// </summary>
    public static X509Certificate2 GetOrCreateCertificate()
    {
        Directory.CreateDirectory(CertDirectory);

        // Try to load existing certificate
        if (File.Exists(CertPath))
        {
            try
            {
                var existing = new X509Certificate2(CertPath, (string?)null, X509KeyStorageFlags.Exportable);
                if (existing.NotAfter > DateTime.UtcNow.AddDays(CertRenewThresholdDays))
                {
                    return existing;
                }
                existing.Dispose();
            }
            catch
            {
                // Corrupted or unreadable — regenerate
            }
        }

        // Generate new self-signed certificate
        var cert = CreateSelfSignedCertificate();
        SaveCertificate(cert);
        return cert;
    }

    private static X509Certificate2 CreateSelfSignedCertificate()
    {
        var subjectName = new X500DistinguishedName("CN=PlatypusTools Remote Server");

        using var rsa = RSA.Create(2048);

        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Key usage: Digital Signature, Key Encipherment
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                critical: true));

        // Enhanced key usage: Server Authentication
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new("1.3.6.1.5.5.7.3.1") }, // serverAuth
                critical: false));

        // Subject Alternative Names — localhost + common LAN patterns
        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("localhost");
        sanBuilder.AddDnsName(Environment.MachineName);
        sanBuilder.AddIpAddress(System.Net.IPAddress.Loopback);       // 127.0.0.1
        sanBuilder.AddIpAddress(System.Net.IPAddress.IPv6Loopback);   // ::1
        request.CertificateExtensions.Add(sanBuilder.Build());

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddDays(CertValidityDays);

        var cert = request.CreateSelfSigned(notBefore, notAfter);

        // On Windows, export and re-import to make the private key persist properly
        return new X509Certificate2(
            cert.Export(X509ContentType.Pfx),
            (string?)null,
            X509KeyStorageFlags.Exportable);
    }

    private static void SaveCertificate(X509Certificate2 cert)
    {
        try
        {
            var pfxBytes = cert.Export(X509ContentType.Pfx);
            File.WriteAllBytes(CertPath, pfxBytes);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save certificate: {ex.Message}");
        }
    }

    /// <summary>
    /// Deletes any cached certificate, forcing regeneration on next use.
    /// </summary>
    public static void DeleteCertificate()
    {
        try
        {
            if (File.Exists(CertPath))
                File.Delete(CertPath);
        }
        catch { }
    }
}
