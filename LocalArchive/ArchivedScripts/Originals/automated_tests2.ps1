# More thorough non-destructive scenario for hidefolder.ps1
# - Loads functions from script (without calling Save-Config)
# - Creates an in-memory folder record (no persistent config write)
# - Sets a password record, exercises hide/unhide and ACL add/remove, then cleans up

$src = Get-Content -LiteralPath 'c:\Path\hidefolder.ps1' -Raw
$outPath = 'c:\Path\test_results2.txt'
Remove-Item -LiteralPath $outPath -ErrorAction SilentlyContinue
Add-Content -LiteralPath $outPath -Value ("Automated deep tests started: {0}" -f (Get-Date))

# Define functions by extracting blocks (same approach as before)
$pos = 0
$len = $src.Length
while ($pos -lt $len) {
    $m = [regex]::Match($src.Substring($pos), '(?ms)^\s*function\s+([A-Za-z0-9_-]+)\s*')
    if (-not $m.Success) { break }
    $start = $pos + $m.Index
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
    try { Invoke-Expression $fnText; Add-Content -LiteralPath $outPath -Value ("Defined function: {0}" -f $m.Groups[1].Value) } catch { Add-Content -LiteralPath $outPath -Value ("Failed to define {0}: {1}" -f $m.Groups[1].Value, $_.Exception.Message) }
    $pos = $end
}

# Prepare in-memory config (do not persist to disk)
if (Get-Command Get-DefaultConfig -ErrorAction SilentlyContinue) { $Global:Config = Get-DefaultConfig; Add-Content -LiteralPath $outPath -Value 'Loaded in-memory default config' } else { Add-Content -LiteralPath $outPath -Value 'Missing Get-DefaultConfig'; exit 1 }

# Create temp folder
$temp = Join-Path $env:TEMP ('fh_test_' + [guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $temp | Out-Null
Add-Content -LiteralPath $outPath -Value ("Created temp folder: {0}" -f $temp)

# Create in-memory folder record and add to $Global:Config (no Save-Config)
$rec = [PSCustomObject]@{
    FolderPath = $temp
    PasswordRecord = $null
    AclRestricted = $false
    EfsEnabled = $false
}
$Global:Config.Folders += $rec
Add-Content -LiteralPath $outPath -Value ("Added in-memory folder record for {0}" -f $temp)

# Set password for the record
$plain = 'AutoTest!234'
$earlyExit = $false
$hasConvert = Get-Command Convert-PlainToSecureString -ErrorAction SilentlyContinue
$hasNewPw = Get-Command New-PasswordRecord -ErrorAction SilentlyContinue
if ($hasConvert -and $hasNewPw) {
    $secure = Convert-PlainToSecureString -Plain $plain
    $pr = New-PasswordRecord -Password $secure
    $rec.PasswordRecord = $pr
    Add-Content -LiteralPath $outPath -Value ("Set password record: Iter={0} SaltLen={1} HashLen={2}" -f $pr.Iterations, $pr.Salt.Length, $pr.Hash.Length)
} else {
    Add-Content -LiteralPath $outPath -Value 'Password helpers missing'
    $earlyExit = $true
}

# Simulate hide flow: Set-Hidden, optionally apply ACL
try {
    if (-not (Test-Path -LiteralPath $temp)) { throw "Temp path vanished before Set-Hidden." }
    Set-Hidden -Path $temp -ErrorAction Stop
    if (Test-Path -LiteralPath $temp) {
        $isHidden = Get-HiddenState -Path $temp
        Add-Content -LiteralPath $outPath -Value ("After Set-Hidden, Get-HiddenState: {0}" -f $isHidden)
    }
} catch { Add-Content -LiteralPath $outPath -Value ("Set-Hidden warning: {0}" -f $_.Exception.Message) }

# Apply ACL restriction (if supported) and then restore
$aclOk = $false
$hasSetAcl = Get-Command Set-AclRestriction -ErrorAction SilentlyContinue
$hasRestoreAcl = Get-Command Restore-ACL -ErrorAction SilentlyContinue
if ($hasSetAcl -and $hasRestoreAcl) {
    try {
        if (Test-Path -LiteralPath $temp) {
            $aclOk = Set-AclRestriction -Path $temp
            Add-Content -LiteralPath $outPath -Value ("Set-AclRestriction returned: {0}" -f $aclOk)
        } else {
            Add-Content -LiteralPath $outPath -Value "Set-AclRestriction skipped: temp path missing"
        }
    } catch { Add-Content -LiteralPath $outPath -Value ("Set-AclRestriction warning: {0}" -f $_.Exception.Message) }
    try {
        if (Test-Path -LiteralPath $temp) {
            $rest = Restore-ACL -Path $temp
            Add-Content -LiteralPath $outPath -Value ("Restore-ACL returned: {0}" -f $rest)
        } else {
            Add-Content -LiteralPath $outPath -Value "Restore-ACL skipped: temp path missing"
        }
    } catch { Add-Content -LiteralPath $outPath -Value ("Restore-ACL warning: {0}" -f $_.Exception.Message) }
} else { Add-Content -LiteralPath $outPath -Value 'ACL helpers missing' }

# Simulate unhide flow: check password then Clear-Hidden
try {
    $entered = Convert-PlainToSecureString -Plain $plain
    $ok = Test-Password -Password $entered -PasswordRecord $rec.PasswordRecord
    Add-Content -LiteralPath $outPath -Value ("Test-Password (correct) returned: {0}" -f $ok)
    if ($ok -and (Test-Path -LiteralPath $temp)) {
        Clear-Hidden -Path $temp -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temp) {
            $isHidden2 = Get-HiddenState -Path $temp
            Add-Content -LiteralPath $outPath -Value ("After Clear-Hidden, Get-HiddenState: {0}" -f $isHidden2)
        }
    }
    $wrong = Convert-PlainToSecureString -Plain 'nope'
    $ok2 = Test-Password -Password $wrong -PasswordRecord $rec.PasswordRecord
    Add-Content -LiteralPath $outPath -Value ("Test-Password (wrong) returned: {0}" -f $ok2)
} catch { Add-Content -LiteralPath $outPath -Value ("Unhide flow warning: {0}" -f $_.Exception.Message) }

if (Get-Command Test-EFSAvailable -ErrorAction SilentlyContinue) { $efs = Test-EFSAvailable; Add-Content -LiteralPath $outPath -Value ("Test-EFSAvailable: {0}" -f $efs) } else { Add-Content -LiteralPath $outPath -Value 'Test-EFSAvailable missing' }
if (Get-Command Test-DriveNTFS -ErrorAction SilentlyContinue) { $ntfs = Test-DriveNTFS -Path $temp; Add-Content -LiteralPath $outPath -Value ("Test-DriveNTFS for {0}: {1}" -f $temp, $ntfs) } else { Add-Content -LiteralPath $outPath -Value 'Test-DriveNTFS missing' }

# --- FileCleaner single-file renumber test (headless, no UI) ---
Add-Content -LiteralPath $outPath -Value 'Starting FileCleaner single-file renumber test'

# Load FileCleaner functions only (avoid UI) by extracting function blocks
$fcSrc = Get-Content -LiteralPath 'c:\Path\filecleaner1.32.ps1' -Raw
$pos2 = 0
$len2 = $fcSrc.Length
while ($pos2 -lt $len2) {
    $m2 = [regex]::Match($fcSrc.Substring($pos2), '(?ms)^\s*function\s+([A-Za-z0-9_-]+)\s*')
    if (-not $m2.Success) { break }
    $start2 = $pos2 + $m2.Index
    $openIndex2 = $fcSrc.IndexOf('{', $start2)
    if ($openIndex2 -lt 0) { break }
    $i2 = $openIndex2
    $depth2 = 0
    do {
        $c2 = $fcSrc[$i2]
        if ($c2 -eq '{') { $depth2++ }
        elseif ($c2 -eq '}') { $depth2-- }
        $i2++
        if ($i2 -ge $len2) { break }
    } while ($depth2 -gt 0)
    $end2 = $i2
    if ($end2 -le $start2) { break }
    $fnText2 = $fcSrc.Substring($start2, $end2 - $start2)
    try {
        Invoke-Expression $fnText2
        Add-Content -LiteralPath $outPath -Value ("Defined FileCleaner function: {0}" -f $m2.Groups[1].Value)
    } catch {
        Add-Content -LiteralPath $outPath -Value ("Failed to define FileCleaner function {0}: {1}" -f $m2.Groups[1].Value, $_.Exception.Message)
    }
    $pos2 = $end2
}

# Stub UI controls with needed properties
$ChkRenumberAll       = [pscustomobject]@{ IsChecked = $true }
$ChkAddEpisode        = [pscustomobject]@{ IsChecked = $false }
$ChkAddSeason         = [pscustomobject]@{ IsChecked = $false }
$ChkAddPrefixAll      = [pscustomobject]@{ IsChecked = $true }
$ChkChangePrefix      = [pscustomobject]@{ IsChecked = $false }
$ChkOnlyIfOldPrefix   = [pscustomobject]@{ IsChecked = $false }
$ChkRecurse           = [pscustomobject]@{ IsChecked = $false }
$ChkSeasonBeforeEpisode = [pscustomobject]@{ IsChecked = $false }
$Chk720p = $Chk1080p = $Chk4k = $ChkHD = [pscustomobject]@{ IsChecked = $false }
$ChkVideo      = [pscustomobject]@{ IsChecked = $true }
$ChkPictures   = [pscustomobject]@{ IsChecked = $false }
$ChkDocuments  = [pscustomobject]@{ IsChecked = $false }
$ChkAudio      = [pscustomobject]@{ IsChecked = $false }
$ChkArchives   = [pscustomobject]@{ IsChecked = $false }
$ChkUseAudioMetadata = [pscustomobject]@{ IsChecked = $false }
$ChkUseVideoMetadata = [pscustomobject]@{ IsChecked = $false }
$TxtOldPrefix   = [pscustomobject]@{ Text = '' }
$TxtNewPrefix   = [pscustomobject]@{ Text = 'Show' }
$TxtDetectedPrefix = [pscustomobject]@{ Text = '' }
$TxtSeason      = [pscustomobject]@{ Text = '' }
$TxtStart       = [pscustomobject]@{ Text = '1' }
$TxtCustomClean = [pscustomobject]@{ Text = '' }
$TxtCustomExt   = [pscustomobject]@{ Text = '' }
$CmbEpisodeDigits = [pscustomobject]@{ SelectedItem = [pscustomobject]@{ Content = '3' } }
$CmbSeasonDigits  = [pscustomobject]@{ SelectedItem = [pscustomobject]@{ Content = '2' } }

# Create single test file
$fcTemp = Join-Path $env:TEMP ('fc_single_' + [guid]::NewGuid().ToString())
New-Item -ItemType Directory -Path $fcTemp | Out-Null
$fcFile = Join-Path $fcTemp 'Alpha.mkv'
Set-Content -LiteralPath $fcFile -Value ''

# Run preview logic for single file renumber
$startNumFC = 1
$filesFC = Get-FilteredFiles -Path $fcTemp -Recurse:$ChkRecurse.IsChecked
if (-not $filesFC) { Add-Content -LiteralPath $outPath -Value 'FileCleaner test failed: no files found'; } else {
    $previewFC = @()
    $episodeCounterFC = $startNumFC
    $hasEpisodeOpsFC = $ChkRenumberAll.IsChecked -or $ChkAddEpisode.IsChecked

    foreach ($f in $filesFC) {
        if ($ChkRenumberAll.IsChecked) {
            $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounterFC -ForceEpisode
            $episodeCounterFC++
        }
        elseif ($ChkAddEpisode.IsChecked) {
            $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounterFC
            $episodeCounterFC++
        }
        else {
            $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounterFC
        }

        $status = if (-not $hasEpisodeOpsFC -and $proposed -eq $f.Name) { "No change" } else { "Pending" }

        $previewFC += [pscustomobject]@{
            Apply     = ($status -ne 'No change')
            Original  = $f.Name
            Proposed  = $proposed
            Status    = $status
            Directory = $f.DirectoryName
        }
    }

    $hasPendingFC = (@($previewFC | Where-Object { $_.Status -ne 'No change' })).Count -gt 0
    $applyEnabledFC = ($previewFC.Count -gt 0 -and $hasPendingFC)
    Add-Content -LiteralPath $outPath -Value ("FileCleaner proposed: {0}" -f $previewFC[0].Proposed)
    Add-Content -LiteralPath $outPath -Value ("FileCleaner ApplyEnabled (expect True): {0}" -f $applyEnabledFC)
}

# Clean up temp file
try { Remove-Item -LiteralPath $fcTemp -Recurse -Force -ErrorAction Stop } catch { Add-Content -LiteralPath $outPath -Value ("FileCleaner temp cleanup warning: {0}" -f $_.Exception.Message) }

# Final cleanup (always run)
try {
    if (Test-Path -LiteralPath $temp) {
        # Ensure ACL is restored before deletion to avoid Deny rules blocking cleanup
        if (Get-Command Restore-ACL -ErrorAction SilentlyContinue) { Restore-ACL -Path $temp | Out-Null }
        try {
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction Stop
        } catch {
            # Reset permissions and retry once if access was denied
            $me = $env:USERNAME
            $null = & icacls "$temp" /inheritance:e /grant:r "${me}:(OI)(CI)F" /T /C 2>$null
            Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction Stop
        }
    }
    Add-Content -LiteralPath $outPath -Value ("Removed temp folder {0}" -f $temp)
} catch {
    Add-Content -LiteralPath $outPath -Value ("Temp cleanup failed: {0}" -f $_.Exception.Message)
}
Add-Content -LiteralPath $outPath -Value ("Automated deep tests finished: {0}" -f (Get-Date))
Write-Output ("Wrote deep test results to {0}" -f $outPath)

if ($earlyExit) { exit 0 }
