$root = (Get-Location).Path
$errors = @()
Get-Content files_to_remove.txt | ForEach-Object {
    $src = $_.Trim()
    if (-not (Test-Path $src)) { Write-Warning "Missing: $src"; return }
    if ($src -match '\\ArchivedScripts\\') { Write-Warning "Skipping already archived: $src"; return }
    try { $rel = [System.IO.Path]::GetRelativePath($root, $src) } catch { if ($src.StartsWith($root)) { $rel = $src.Substring($root.Length + 1) } else { $rel = Split-Path $src -Leaf } }
    $dest = Join-Path $root (Join-Path 'ArchivedScripts\Originals' $rel)
    $destDir = Split-Path $dest -Parent
    if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }
    if (Test-Path $dest) {
        $i = 1
        $base = [IO.Path]::GetFileNameWithoutExtension($dest)
        $ext = [IO.Path]::GetExtension($dest)
        $origDest = $dest
        while (Test-Path $dest) {
            $dest = Join-Path $destDir ($base + ".orig$i" + $ext)
            $i++
        }
        Write-Warning "Destination exists, will move to $dest"
    }
    Write-Output "GIT-MV: '$src' -> '$dest'"
    git mv -- "$src" "$dest" 2>&1 | ForEach-Object { Write-Output $_ }
    if ($LASTEXITCODE -ne 0) { $errors += $src }
}
if ($errors.Count -gt 0) { Write-Error "Errors moving files: $($errors -join ', ')"; exit 1 }
Write-Output "Move complete."