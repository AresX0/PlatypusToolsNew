# Recovers a truncated enhanced_audio_library.json by truncating at the last
# fully-completed top-level array element (each element is one AudioTrack).
[CmdletBinding()]
param(
    [string]$Source = "$env:APPDATA\PlatypusTools\enhanced_audio_library.json.corrupted",
    [string]$Destination = "$env:APPDATA\PlatypusTools\enhanced_audio_library.json"
)

$ErrorActionPreference = 'Stop'

Get-Process PlatypusTools.UI -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

$cs = @'
using System;
using System.IO;
using System.Text.Json;
public static class JsonRecover {
    public static int FindSafeTruncation(byte[] data) {
        int lastGoodEnd = -1;
        int depth = 0;
        var reader = new Utf8JsonReader(data, isFinalBlock: true,
            state: new JsonReaderState(new JsonReaderOptions {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            }));
        try {
            while (reader.Read()) {
                var t = reader.TokenType;
                if (t == JsonTokenType.StartArray || t == JsonTokenType.StartObject) depth++;
                else if (t == JsonTokenType.EndArray || t == JsonTokenType.EndObject) {
                    depth--;
                    if (t == JsonTokenType.EndObject && depth == 1) {
                        lastGoodEnd = (int)reader.BytesConsumed;
                    }
                }
            }
        } catch (JsonException) { }
        return lastGoodEnd;
    }
}
'@

if (-not ([System.Management.Automation.PSTypeName]'JsonRecover').Type) {
    Add-Type -TypeDefinition $cs -Language CSharp
}

if (Test-Path $Destination) {
    Copy-Item $Destination "$Destination.empty.bak" -Force
}

Write-Host "Reading $Source..."
$bytes = [System.IO.File]::ReadAllBytes($Source)
Write-Host "Read $($bytes.Length) bytes"

$cut = [JsonRecover]::FindSafeTruncation($bytes)
Write-Host "Safe truncation point: byte $cut"
if ($cut -le 0) { throw "Could not find a safe truncation point." }

$out = New-Object byte[] ($cut + 1)
[Array]::Copy($bytes, 0, $out, 0, $cut)
$out[$cut] = 0x5D  # ']'

[System.IO.File]::WriteAllBytes($Destination, $out)
Remove-Item "$Destination.sha256" -ErrorAction SilentlyContinue
Write-Host "Wrote $((Get-Item $Destination).Length) bytes to $Destination"

# Validate via real JSON parser
$json = [System.IO.File]::ReadAllText($Destination)
$opts = [System.Text.Json.JsonDocumentOptions]::new()
$opts.AllowTrailingCommas = $true
try {
    $doc = [System.Text.Json.JsonDocument]::Parse($json, $opts)
    $count = $doc.RootElement.GetArrayLength()
    Write-Host "VALID JSON: $count tracks recovered" -ForegroundColor Green
    $doc.Dispose()
} catch {
    Write-Host "VALIDATION FAILED: $_" -ForegroundColor Red
    throw
}
