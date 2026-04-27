using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace PlatypusTools.UI.Services.RemoteServer;

/// <summary>
/// Trust-on-first-use certificate pinning store for the Remote Desktop / Remote Server WebSocket client.
/// Stores SHA-256 thumbprints keyed by host:port in %APPDATA%\PlatypusTools\trusted_remote_certs.json.
/// Replaces the previous "accept-any-cert" policy that left the WebSocket open to MitM attacks.
/// </summary>
public sealed class CertificatePinService
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PlatypusTools",
        "trusted_remote_certs.json");

    private readonly object _sync = new();
    private Dictionary<string, string> _pins;

    public CertificatePinService()
    {
        _pins = LoadPins();
    }

    /// <summary>
    /// Returns the saved SHA-256 thumbprint (uppercase hex) for the given host, or null if not pinned.
    /// </summary>
    public string? GetPin(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return null;
        var key = host.Trim().ToLowerInvariant();
        lock (_sync)
        {
            return _pins.TryGetValue(key, out var v) ? v : null;
        }
    }

    /// <summary>
    /// Stores a trusted thumbprint for a host (overwriting any previous value).
    /// </summary>
    public void Pin(string host, string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(thumbprint)) return;
        var key = host.Trim().ToLowerInvariant();
        lock (_sync)
        {
            _pins[key] = thumbprint.ToUpperInvariant();
            SavePins();
        }
    }

    /// <summary>
    /// Removes a pin (e.g. user wants to re-trust on next connect).
    /// </summary>
    public void Unpin(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return;
        var key = host.Trim().ToLowerInvariant();
        lock (_sync)
        {
            if (_pins.Remove(key)) SavePins();
        }
    }

    /// <summary>
    /// Computes SHA-256 thumbprint of a certificate as uppercase hex (no colons).
    /// </summary>
    public static string ComputeThumbprint(X509Certificate cert)
    {
        ArgumentNullException.ThrowIfNull(cert);
        var raw = cert.GetRawCertData();
        var hash = SHA256.HashData(raw);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Returns true if the supplied cert thumbprint matches the pinned thumbprint for the host.
    /// </summary>
    public bool IsTrusted(string host, X509Certificate cert)
    {
        var pinned = GetPin(host);
        if (pinned is null) return false;
        var current = ComputeThumbprint(cert);
        return string.Equals(pinned, current, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<string, string> LoadPins()
    {
        try
        {
            if (!File.Exists(StorePath)) return new();
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>();
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private void SavePins()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_pins, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StorePath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CertificatePinService] SavePins failed: {ex.Message}");
        }
    }
}
