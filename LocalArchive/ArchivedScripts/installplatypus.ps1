param(
    [string]$SourceRoot = (Split-Path -Parent $MyInvocation.MyCommand.Path),
    [string]$DestinationRoot = 'C:\ProgramFiles\PlatypusUtils'
)

# Require admin to write under C:\ProgramFiles
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Error 'Please run installplatypus.ps1 as Administrator to write under C:\ProgramFiles.'
    exit 1
}

function Ensure-Dir {
    [switch]$NonInteractive
    param([Parameter(Mandatory)][string]$Path)
. "$PSScriptRoot\\Tools\\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Require-Parameter 'Path' 
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path -Force | Out-Null
    }
}

$paths = [ordered]@{
    Base          = $DestinationRoot
    Assets        = Join-Path $DestinationRoot 'Assets'
    DataSystem    = Join-Path $DestinationRoot 'Data\SystemCleaner'
    DataSystemRun = Join-Path $DestinationRoot 'Data\SystemCleaner\RecentCleaner'
    DataSystemHid = Join-Path $DestinationRoot 'Data\SystemCleaner\Hider'
    DataVideo     = Join-Path $DestinationRoot 'Data\VideoEditor'
    Logs          = Join-Path $DestinationRoot 'Logs'
    LogsSystem    = Join-Path $DestinationRoot 'Logs\SystemCleaner'
    LogsVideo     = Join-Path $DestinationRoot 'Logs\VideoEditor'
    Tools         = Join-Path $DestinationRoot 'Tools'
}

foreach ($p in $paths.Values) { Ensure-Dir -Path $p }

$filesToCopy = @(
    @{ Source = 'SystemCleaner.ps1'; Dest = Join-Path $paths.Base 'SystemCleaner.ps1' },
    @{ Source = 'VideoEditor2.ps1'; Dest = Join-Path $paths.Base 'VideoEditor2.ps1' },
    @{ Source = 'PlatypusTools.ps1'; Dest = Join-Path $paths.Base 'PlatypusTools.ps1' }
)

# Asset candidates
$assetCandidates = @('platypus.png','platypus.ico','platypus - Copy.png','assets\platypus.png','assets\platypus.ico')
foreach ($asset in $assetCandidates) {
    $src = Join-Path $SourceRoot $asset
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination (Join-Path $paths.Assets ([System.IO.Path]::GetFileName($src))) -Force
    }
}

foreach ($file in $filesToCopy) {
    $src = Join-Path $SourceRoot $file.Source
    if (-not (Test-Path -LiteralPath $src)) {
        Write-Warning "Missing source file: $($file.Source)"
        continue
    }
    Copy-Item -LiteralPath $src -Destination $file.Dest -Force
}

Write-Host "Platypus utils installed to $DestinationRoot"
Write-Host "Assets placed in $($paths.Assets)"
Write-Host "Data folders prepared: $($paths.DataSystem), $($paths.DataVideo)"
Write-Host "Logs folders prepared: $($paths.LogsSystem), $($paths.LogsVideo)"
Write-Host "You can place ffmpeg/exiftool binaries under $($paths.Tools) for VideoEditor."

