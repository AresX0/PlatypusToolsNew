using System;
using System.IO;

namespace PlatypusTools.UI.Services.Scripting
{
    /// <summary>
    /// Phase 2.1 — generates the read-only `$Platypus` proxy preamble for PowerShell
    /// and Python scripts. The proxy talks to the local Remote.Server `/api/v1/*`
    /// surface (port 47392) using the auto-generated Bearer token at
    /// <c>%APPDATA%/PlatypusTools/api-token.txt</c>.
    ///
    /// Exposed sub-objects (read-only):
    ///   $Platypus.Audio    -> NowPlaying(), Queue()
    ///   $Platypus.Vault    -> Items()           (requires unlocked vault)
    ///   $Platypus.Forensics-> Iocs()            (read-only IOC list)
    ///   $Platypus.Info     -> Info()
    ///
    /// If Remote.Server is not running, calls throw with a clear message.
    /// </summary>
    public static class PlatypusScriptPrelude
    {
        public static string TokenPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PlatypusTools", "api-token.txt");

        public const string BaseUrl = "https://localhost:47392/api/v1";

        public static string LoadTokenOrEmpty()
        {
            try { return File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : ""; }
            catch { return ""; }
        }

        public static string PowerShell()
        {
            var token = LoadTokenOrEmpty();
            // Note: -SkipCertificateCheck only exists in PS7+. Older Windows PowerShell needs the
            // ServicePointManager callback escape hatch.
            return @"
# --- $Platypus prelude (read-only) ---
$Platypus_BaseUrl = '" + BaseUrl + @"'
$Platypus_Token   = '" + token.Replace("'", "''") + @"'
[System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
function _Platypus-Get($path) {
    if ([string]::IsNullOrEmpty($Platypus_Token)) {
        throw 'Remote.Server token missing. Start the Platypus Remote Server.'
    }
    $headers = @{ Authorization = ""Bearer $Platypus_Token"" }
    try {
        return Invoke-RestMethod -Uri ""$Platypus_BaseUrl$path"" -Headers $headers -ErrorAction Stop
    } catch {
        throw ""Platypus API call failed: $($_.Exception.Message). Is Remote.Server running on port 47392?""
    }
}
$Platypus = [PSCustomObject]@{
    Audio     = [PSCustomObject]@{
        NowPlaying = { _Platypus-Get '/audio/nowplaying' }
        Queue      = { _Platypus-Get '/audio/queue' }
    }
    Vault     = [PSCustomObject]@{
        Items      = { _Platypus-Get '/vault/items' }
    }
    Forensics = [PSCustomObject]@{
        Iocs       = { _Platypus-Get '/forensics/iocs' }
    }
    Info      = { _Platypus-Get '/info' }
}
# --- end prelude ---
";
        }

        public static string Python()
        {
            var token = LoadTokenOrEmpty();
            return @"
# --- $Platypus prelude (read-only) ---
import json as _json
import ssl as _ssl
import urllib.request as _urlreq

_PLATYPUS_BASE = '" + BaseUrl + @"'
_PLATYPUS_TOKEN = '" + token.Replace("'", "\\'") + @"'
_ssl_ctx = _ssl.create_default_context()
_ssl_ctx.check_hostname = False
_ssl_ctx.verify_mode = _ssl.CERT_NONE

def _platypus_get(path):
    if not _PLATYPUS_TOKEN:
        raise RuntimeError('Remote.Server token missing. Start the Platypus Remote Server.')
    req = _urlreq.Request(_PLATYPUS_BASE + path, headers={'Authorization': 'Bearer ' + _PLATYPUS_TOKEN})
    try:
        with _urlreq.urlopen(req, context=_ssl_ctx, timeout=10) as r:
            return _json.loads(r.read().decode('utf-8') or 'null')
    except Exception as e:
        raise RuntimeError('Platypus API call failed: ' + str(e) + '. Is Remote.Server running on port 47392?')

class _PlatypusAudio:
    def now_playing(self): return _platypus_get('/audio/nowplaying')
    def queue(self):       return _platypus_get('/audio/queue')
class _PlatypusVault:
    def items(self):       return _platypus_get('/vault/items')
class _PlatypusForensics:
    def iocs(self):        return _platypus_get('/forensics/iocs')
class _Platypus:
    def __init__(self):
        self.Audio = _PlatypusAudio()
        self.Vault = _PlatypusVault()
        self.Forensics = _PlatypusForensics()
    def info(self):        return _platypus_get('/info')
Platypus = _Platypus()
# --- end prelude ---
";
        }
    }
}
