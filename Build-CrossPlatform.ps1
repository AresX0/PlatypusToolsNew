# Cross-platform portable builds (Full + Media editions).
# Mirrors cross-platform/build-portable.sh and build-linux.sh logic but runnable from Windows.
[CmdletBinding()]
param(
    [string[]]$Rids = @('linux-x64','linux-arm64','osx-x64','osx-arm64','win-x64','win-arm64'),
    [string]$Version = '4.0.4.9'
)

$ErrorActionPreference = 'Stop'
$ProjectRoot = $PSScriptRoot
$AvaloniaProj = Join-Path $ProjectRoot 'cross-platform\PlatypusTools.UI.Avalonia\PlatypusTools.UI.Avalonia.csproj'
$DistRoot = Join-Path $ProjectRoot 'cross-platform\dist'
$ArtifactsRoot = Join-Path $ProjectRoot 'releases'
New-Item -ItemType Directory -Force -Path $ArtifactsRoot | Out-Null

function Publish-Variant {
    param([string]$Rid, [string]$Edition, [string]$Suffix)

    $out = Join-Path $DistRoot "portable-$Rid$Suffix"
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    Write-Host "==> Publishing $Rid ($Edition)" -ForegroundColor Cyan
    $args = @(
        'publish', $AvaloniaProj,
        '-c','Release',
        '-r',$Rid,
        '--self-contained','true',
        '/p:PublishSingleFile=true',
        '/p:IncludeNativeLibrariesForSelfExtract=true',
        '/p:DebugType=None',
        '/p:DebugSymbols=false',
        '-o',$out,
        '--nologo','/v:m'
    )
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Rid $Edition" }
    Set-Content -Path (Join-Path $out 'edition.txt') -Value $Edition -NoNewline

    # Package into archive
    $archiveBase = "PlatypusTools-$Edition-$Rid-v$Version"
    if ($Rid -like 'win-*') {
        $zipPath = Join-Path $ArtifactsRoot "$archiveBase.zip"
        if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
        Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zipPath -CompressionLevel Optimal
        Write-Host "    -> $zipPath" -ForegroundColor Green
    } else {
        $tarPath = Join-Path $ArtifactsRoot "$archiveBase.tar.gz"
        if (Test-Path $tarPath) { Remove-Item $tarPath -Force }
        Push-Location $DistRoot
        try {
            $rel = Split-Path -Leaf $out
            tar -czf $tarPath $rel
        } finally { Pop-Location }
        Write-Host "    -> $tarPath" -ForegroundColor Green
    }
}

foreach ($rid in $Rids) {
    Publish-Variant -Rid $rid -Edition 'Full' -Suffix ''
    Publish-Variant -Rid $rid -Edition 'Media' -Suffix '-media'
}

Write-Host "`nAll cross-platform artifacts written to: $ArtifactsRoot" -ForegroundColor Cyan
Get-ChildItem $ArtifactsRoot -Filter 'PlatypusTools-*' | Format-Table Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}}
