$files = @("ArchivedScripts\PlatypusTools.ps1","ArchivedScripts\Originals\PlatypusTools.ps1")
foreach ($file in $files) {
    if (-not (Test-Path $file)) { Write-Warning "Missing: $file"; continue }
    $text = Get-Content $file -Raw
    $pattern = '(?ms)param\(\s*(.*?)\s*\r?\n\s*\[switch\]\$NonInteractive\s*\)'
    $new = [regex]::Replace($text, $pattern, { param($m) "[switch]$NonInteractive`r`nparam($($m.Groups[1].Value))" })
    if ($new -ne $text) {
        Set-Content -Path $file -Value $new -Force
        Write-Output "Fixed param insert in $file"
    } else { Write-Output "No change for $file" }
}
Write-Output "Done."