$root = (Get-Location).Path
$planned = "planned_moves.txt"
if (Test-Path $planned) { Remove-Item $planned -Force }
Get-Content files_to_remove.txt | ForEach-Object {
    $src = $_.Trim()
    if (-not (Test-Path $src)) { Write-Output "SKIP (missing): '$src'"; return }
    try {
        $rel = [System.IO.Path]::GetRelativePath($root, $src)
    } catch {
        if ($src.StartsWith($root)) {
            $rel = $src.Substring($root.Length + 1)
        } else {
            $rel = Split-Path $src -Leaf
        }
    }
    $dest = Join-Path $root (Join-Path 'ArchivedScripts\Originals' $rel)
    $line = "MOVE: '$src' -> '$dest'"
    Write-Output $line
    Add-Content -Path $planned -Value $line
}
Write-Output "WROTE: $planned -> " + (Get-Content $planned | Measure-Object -Line).Lines