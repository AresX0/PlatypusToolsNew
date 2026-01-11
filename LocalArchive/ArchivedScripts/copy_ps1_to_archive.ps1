# Copy all .ps1 files in the repo (excluding the archive itself) to ArchivedScripts/ preserving relative paths
$repoRoot = (Get-Location).ProviderPath
$arch = Join-Path $repoRoot 'ArchivedScripts'
if (-not (Test-Path $arch)) { New-Item -ItemType Directory -Path $arch | Out-Null }
$ps1 = Get-ChildItem -Path $repoRoot -Filter '*.ps1' -Recurse | Where-Object { $_.FullName -notmatch '\\ArchivedScripts\\' }
foreach ($f in $ps1) {
    $rel = $f.FullName.Substring($repoRoot.Length).TrimStart('\\','/')
    $dest = Join-Path $arch $rel
    $destDir = Split-Path -Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    Copy-Item -Path $f.FullName -Destination $dest -Force
    Write-Output "Copied: $($f.FullName) -> $dest"
}
Write-Output "Done. Copied $($ps1.Count) .ps1 files to $arch"