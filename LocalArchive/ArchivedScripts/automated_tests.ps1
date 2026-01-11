# Automated non-destructive smoke tests for hidefolder.ps1
# - Extracts function blocks from the script and defines them in this session
# - Runs: Get-DefaultConfig, New-PasswordRecord/Test-Password, Set-Hidden/Get-HiddenState/Clear-Hidden on temp folder,
#   Test-EFSAvailable, Test-DriveNTFS

$src = Get-Content -LiteralPath 'c:\Path\hidefolder.ps1' -Raw
$outPath = 'c:\Path\test_results.txt'
Remove-Item -LiteralPath $outPath -ErrorAction SilentlyContinue
Add-Content -LiteralPath $outPath -Value "Automated tests started: $(Get-Date)"

# Simple function extractor: finds 'function NAME' and copies balanced-brace block
$pos = 0
$len = $src.Length
while ($pos -lt $len) {
    $m = [regex]::Match($src.Substring($pos), '(?ms)^\s*function\s+([A-Za-z0-9_-]+)\s*')
    if (-not $m.Success) { break }
    $start = $pos + $m.Index
    # find first '{' after the match
    $openIndex = $src.IndexOf('{', $start)
    if ($openIndex -lt 0) { break }
    $i = $openIndex
    $depth = 0
    do {
        $c = $src[$i]
        if ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') { $depth-- }
        $i++
        if ($i -ge $len) { break }
    } while ($depth -gt 0)
    $end = $i
    if ($end -le $start) { break }
    $fnText = $src.Substring($start, $end - $start)
    try {
        Invoke-Expression $fnText
        Add-Content -LiteralPath $outPath -Value "Defined function: $($m.Groups[1].Value)"
    } catch {
        Add-Content -LiteralPath $outPath -Value "Failed to define $($m.Groups[1].Value): $($_.Exception.Message)"
    }
    $pos = $end
}

# Ensure default config exists
if (Get-Command Get-DefaultConfig -ErrorAction SilentlyContinue) {
    $Global:Config = Get-DefaultConfig
    Add-Content -LiteralPath $outPath -Value "Loaded default config"
} else {
    Add-Content -LiteralPath $outPath -Value "Get-DefaultConfig missing"
}

# Test password hashing
if (Get-Command New-PasswordRecord -ErrorAction SilentlyContinue -CommandType Function) {
    $pw = Convert-PlainToSecureString -Plain 'TestP@ssw0rd!'
    $rec = New-PasswordRecord -Password $pw
    Add-Content -LiteralPath $outPath -Value "New-PasswordRecord produced: Iterations=$($rec.Iterations) SaltLen=$($rec.Salt.Length) HashLen=$($rec.Hash.Length)"
    $ok = Test-Password -Password $pw -PasswordRecord $rec
    Add-Content -LiteralPath $outPath -Value "Test-Password with correct password: $ok"
    $wrong = Convert-PlainToSecureString -Plain 'wrong'
    $ok2 = Test-Password -Password $wrong -PasswordRecord $rec
    Add-Content -LiteralPath $outPath -Value "Test-Password with wrong password: $ok2"
} else { Add-Content -LiteralPath $outPath -Value 'Password functions missing' }

# Create temp folder and test hidden attribute toggling (skip safely if missing)
$temp = Join-Path $env:TEMP ('fh_test_' + [guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $temp | Out-Null
Add-Content -LiteralPath $outPath -Value "Created temp folder: $temp"
if (Get-Command Set-Hidden -ErrorAction SilentlyContinue) {
    try {
        if (-not (Test-Path -LiteralPath $temp)) { throw "Temp path vanished before test." }
        Set-Hidden -Path $temp -ErrorAction Stop
        if (Test-Path -LiteralPath $temp) {
            $isHidden = Get-HiddenState -Path $temp
            Add-Content -LiteralPath $outPath -Value "After Set-Hidden, Get-HiddenState: $isHidden"
        } else {
            Add-Content -LiteralPath $outPath -Value "After Set-Hidden, path missing (skipped state check)."
        }

        if (Test-Path -LiteralPath $temp) {
            Clear-Hidden -Path $temp -ErrorAction Stop
            $isHidden2 = Get-HiddenState -Path $temp
            Add-Content -LiteralPath $outPath -Value "After Clear-Hidden, Get-HiddenState: $isHidden2"
        }
    } catch {
        Add-Content -LiteralPath $outPath -Value "Hidden attribute test warning: $($_.Exception.Message)"
    }
} else { Add-Content -LiteralPath $outPath -Value 'Hidden attribute functions missing' }

# EFS / Drive checks (non-destructive)
if (Get-Command Test-EFSAvailable -ErrorAction SilentlyContinue) {
    $efs = Test-EFSAvailable
    Add-Content -LiteralPath $outPath -Value "Test-EFSAvailable: $efs"
} else { Add-Content -LiteralPath $outPath -Value 'Test-EFSAvailable missing' }
if (Get-Command Test-DriveNTFS -ErrorAction SilentlyContinue) {
    $root = [System.IO.Path]::GetPathRoot($temp)
    $ntfs = Test-DriveNTFS -Path $temp
    Add-Content -LiteralPath $outPath -Value ("Test-DriveNTFS for {0}: {1}" -f $root, $ntfs)
} else { Add-Content -LiteralPath $outPath -Value 'Test-DriveNTFS missing' }

# Clean up temp
try {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Recurse -Force }
    Add-Content -LiteralPath $outPath -Value "Removed temp folder"
} catch { Add-Content -LiteralPath $outPath -Value "Temp cleanup failed: $($_.Exception.Message)" }

Add-Content -LiteralPath $outPath -Value "Automated tests finished: $(Get-Date)"
Write-Output "Wrote test results to $outPath"