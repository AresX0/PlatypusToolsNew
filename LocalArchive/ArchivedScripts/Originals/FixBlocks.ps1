Add-Type -AssemblyName System.Windows.Forms

# === Configuration ===
$EnableDryRun = $false                # set to $true to preview actions without renaming
$ActionLogPath = Join-Path $env:TEMP "rename_action_log.txt"

function Log-Action {
    param($line)
    try { Add-Content -LiteralPath $ActionLogPath -Value $line -ErrorAction SilentlyContinue } catch {}
}

# === Scanners ===
function Get-BlockedFiles($path, $recurse) {
    Get-ChildItem -Path $path -File -Recurse:$recurse | Where-Object {
        Get-Item -LiteralPath $_.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue
    }
}

function Get-InvalidNameFiles($path, $recurse) {
    $invalidChars = [System.IO.Path]::GetInvalidFileNameChars() -join ''
    $reservedNames = 'CON','PRN','AUX','NUL'
    $reservedNames += (1..9 | ForEach-Object { "COM$_" })
    $reservedNames += (1..9 | ForEach-Object { "LPT$_" })
    Get-ChildItem -Path $path -File -Recurse:$recurse | Where-Object {
        ($_.BaseName -match "[$invalidChars]") -or ($reservedNames -contains $_.BaseName.ToUpper())
    }
}

function Get-WildcardSensitiveFiles($path, $recurse) {
    Get-ChildItem -Path $path -File -Recurse:$recurse | Where-Object {
        $_.Name -match '[\*\?\[\]]'
    }
}

function Suggest-Rename($file) {
    $safeBase = ($file.BaseName -replace '[<>:"/\\|?*\[\]]', '_')
    if ($safeBase -match '^(?i:(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]))$') {
        $safeBase = "_$safeBase"
    }
    return "$safeBase$($file.Extension)"
}

# === GUI setup ===
$form = New-Object System.Windows.Forms.Form
$form.Text = "Blocked and Invalid Filename Scanner"
$form.Size = '760,580'
$form.StartPosition = "CenterScreen"

$label = New-Object System.Windows.Forms.Label -Property @{
    Text = "Select folder:"
    Location = '10,20'
    Size = '100,20'
}
$form.Controls.Add($label)

$textBox = New-Object System.Windows.Forms.TextBox -Property @{
    Location = '120,20'
    Size = '500,20'
}
$form.Controls.Add($textBox)

$browseButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Browse"
    Location = '630,18'
    Size = '80,24'
}
$folderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
$browseButton.Add_Click({
    if ($folderBrowser.ShowDialog() -eq 'OK') { $textBox.Text = $folderBrowser.SelectedPath }
})
$form.Controls.Add($browseButton)

$checkBox = New-Object System.Windows.Forms.CheckBox -Property @{
    Text = "Include subdirectories"
    Location = '120,50'
    Size = '200,20'
}
$form.Controls.Add($checkBox)

$dryRunCheckbox = New-Object System.Windows.Forms.CheckBox -Property @{
    Text = "Dry-run (no changes)"
    Location = '340,50'
    Size = '200,20'
}
$form.Controls.Add($dryRunCheckbox)

$scanButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Scan"
    Location = '120,80'
    Size = '100,30'
}
$form.Controls.Add($scanButton)

$listBox = New-Object System.Windows.Forms.ListBox -Property @{
    Location = '10,130'
    Size = '720,320'
    HorizontalScrollbar = $true
}
$form.Controls.Add($listBox)

$unblockButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Unblock All"
    Location = '10,470'
    Size = '100,30'
    Enabled = $false
}
$previewRenameButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Preview Renames"
    Location = '120,470'
    Size = '120,30'
    Enabled = $false
}
$applyRenameButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Rename Selected"
    Location = '250,470'
    Size = '120,30'
    Enabled = $false
}
$renameAllButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Rename All Now"
    Location = '380,470'
    Size = '120,30'
    Enabled = $false
}
$exportCsvButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Export CSV"
    Location = '510,470'
    Size = '80,30'
    Enabled = $false
}
$resetButton = New-Object System.Windows.Forms.Button -Property @{
    Text = "Reset"
    Location = '600,470'
    Size = '80,30'
}

$form.Controls.AddRange(@($unblockButton,$previewRenameButton,$applyRenameButton,$renameAllButton,$exportCsvButton,$resetButton))

# === Helpers ===
function Parse-Entry($entry) {
    # returns a hashtable: @{Tag='[INVALID]'; Original='C:\...'; Suggested='name.ext'}
    if (-not ($entry -is [string])) { return $null }
    if ($entry.StartsWith('[BLOCKED]')) {
        $orig = $entry -replace '^\[BLOCKED\]\s*',''
        return @{ Tag='[BLOCKED]'; Original=$orig; Suggested=$null }
    }
    if ($entry -match '^\[(INVALID|WILDCARD)\]\s*(.+?)\s*->\s*(.+)$') {
        return @{ Tag="[$($matches[1])]"; Original=$matches[2]; Suggested=$matches[3] }
    }
    return $null
}

# === Scan logic ===
$scanButton.Add_Click({
    $listBox.Items.Clear()
    $unblockButton.Enabled = $false
    $previewRenameButton.Enabled = $false
    $applyRenameButton.Enabled = $false
    $renameAllButton.Enabled = $false
    $exportCsvButton.Enabled = $false

    $path = $textBox.Text.Trim()
    $recurse = $checkBox.Checked
    $EnableDryRun = $dryRunCheckbox.Checked

    if (-not (Test-Path -LiteralPath $path)) {
        [System.Windows.Forms.MessageBox]::Show("Invalid path.","Error",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    # clear/create log
    try { Remove-Item -LiteralPath $ActionLogPath -ErrorAction SilentlyContinue } catch {}
    if (-not $EnableDryRun) { "$((Get-Date).ToString()) Starting renamer run" | Out-File -LiteralPath $ActionLogPath -Encoding UTF8 -Force }

    $blocked  = Get-BlockedFiles -path $path -recurse $recurse
    $invalid  = Get-InvalidNameFiles -path $path -recurse $recurse
    $wildcard = Get-WildcardSensitiveFiles -path $path -recurse $recurse

    foreach ($file in $blocked) {
        $entry = "[BLOCKED] $($file.FullName)"
        $listBox.Items.Add($entry)
    }

    foreach ($file in $invalid) {
        $safeName = Suggest-Rename $file
        $entry = "[INVALID] $($file.FullName) -> $safeName"
        $listBox.Items.Add($entry)
    }

    foreach ($file in $wildcard) {
        $safeName = Suggest-Rename $file
        $entry = "[WILDCARD] $($file.FullName) -> $safeName"
        $listBox.Items.Add($entry)
    }

    if ($listBox.Items.Count -gt 0) {
        # enable controls based on content
        if ($blocked.Count -gt 0) { $unblockButton.Enabled = $true }
        if ($invalid.Count -gt 0 -or $wildcard.Count -gt 0) {
            $previewRenameButton.Enabled = $true
            $applyRenameButton.Enabled = $true
            $renameAllButton.Enabled = $true
            $exportCsvButton.Enabled = $true
        }
    } else {
        [System.Windows.Forms.MessageBox]::Show("No issues found.","Scan Complete",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information)
    }
})

# === Unblock all logic ===
$unblockButton.Add_Click({
    $choice = [System.Windows.Forms.MessageBox]::Show("Unblock all listed files?","Confirm",[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Question)
    if ($choice -eq [System.Windows.Forms.DialogResult]::Yes) {
        foreach ($item in $listBox.Items) {
            if ($item -is [string] -and $item.StartsWith('[BLOCKED]')) {
                $path = $item -replace '^\[BLOCKED\]\s*', ''
                try {
                    Unblock-File -LiteralPath $path -ErrorAction Stop
                    Log-Action "Unblocked: $path"
                } catch {
                    Log-Action "Unblock failed: $path - $($_.Exception.Message)"
                }
            }
        }
        [System.Windows.Forms.MessageBox]::Show("Blocked files unblocked.","Done",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information)
    }
})

# === Preview renames (Write-Host) ===
$previewRenameButton.Add_Click({
    foreach ($item in $listBox.Items) {
        if ($item -is [string] -and ($item.StartsWith('[INVALID]') -or $item.StartsWith('[WILDCARD]'))) {
            $parts = $item -split ' -> '
            if ($parts.Count -ge 2) {
                $orig = $parts[0] -replace '^\[(INVALID|WILDCARD)\]\s*', ''
                $suggest = $parts[1]
                Write-Host "Original: $orig" -ForegroundColor Yellow
                Write-Host "Suggested: $suggest" -ForegroundColor Cyan
            }
        }
    }
})

# === Export CSV ===
$exportCsvButton.Add_Click({
    $saveDlg = New-Object System.Windows.Forms.SaveFileDialog
    $saveDlg.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
    $saveDlg.FileName = "rename_preview.csv"
    if ($saveDlg.ShowDialog() -ne 'OK') { return }
    $out = @()
    foreach ($item in $listBox.Items) {
        $p = Parse-Entry $item
        if ($p) {
            $obj = [PSCustomObject]@{
                Tag = $p.Tag
                Original = $p.Original
                Suggested = $p.Suggested
            }
            $out += $obj
        }
    }
    $out | Export-Csv -LiteralPath $saveDlg.FileName -NoTypeInformation -Encoding UTF8
    [System.Windows.Forms.MessageBox]::Show("Exported to $($saveDlg.FileName)","Export Complete",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information)
})

# === Rename selected ===
$applyRenameButton.Add_Click({
    $selected = $listBox.SelectedItem
    if ($null -eq $selected) { return }
    $p = Parse-Entry $selected
    if (-not $p -or -not $p.Suggested) { return }

    $originalPath = $p.Original
    $newName = $p.Suggested

    if (-not (Test-Path -LiteralPath $originalPath)) {
        [System.Windows.Forms.MessageBox]::Show("Original file not found: $originalPath","Error",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error)
        return
    }

    $dir = Split-Path -LiteralPath $originalPath
    $newPath = Join-Path $dir $newName

    if (Test-Path -LiteralPath $newPath) {
        [System.Windows.Forms.MessageBox]::Show("Target already exists: $newPath","Conflict",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning)
        return
    }

    if ($EnableDryRun) {
        Log-Action "Dry-run: Would rename `"$originalPath`" -> `"$newName`""
        [System.Windows.Forms.MessageBox]::Show("Dry-run: no change made.","Dry-run",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information)
        return
    }

    try {
        Rename-Item -LiteralPath $originalPath -NewName $newName -ErrorAction Stop
        Log-Action "Renamed: `"$originalPath`" -> `"$newName`""
        [System.Windows.Forms.MessageBox]::Show("Renamed selected file.","Done",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information)
    } catch {
        Log-Action "Rename failed: `"$originalPath`" -> `"$newName`" - $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show("Failed to rename.`n$($_.Exception.Message)","Error",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error)
    }
})

# === Rename all ===
$renameAllButton.Add_Click({
    $EnableDryRun = $dryRunCheckbox.Checked
    $renamed = 0
    $skipped = 0
    foreach ($item in $listBox.Items) {
        if ($item -is [string] -and ($item.StartsWith('[INVALID]') -or $item.StartsWith('[WILDCARD]'))) {
            $parts = $item -split ' -> '
            if ($parts.Count -lt 2) { continue }
            $originalPath = $parts[0] -replace '^\[(INVALID|WILDCARD)\]\s*', ''
            $newName = $parts[1]

            if (-not (Test-Path -LiteralPath $originalPath)) {
                Log-Action "Original not found: $originalPath"
                continue
            }

            $dir = Split-Path -LiteralPath $originalPath
            $newPath = Join-Path $dir $newName

            if (Test-Path -LiteralPath $newPath) {
                Log-Action "Skipped (exists): $newPath"
                $skipped++
                continue
            }

            if ($EnableDryRun) {
                Log-Action "Dry-run: Would rename `"$originalPath`" -> `"$newName`""
                continue
            }

            try {
                Rename-Item -LiteralPath $originalPath -NewName $newName -ErrorAction Stop
                Log-Action "Renamed: `"$originalPath`" -> `"$newName`""
                $renamed++
            } catch {
                Log-Action "Rename failed: `"$originalPath`" -> `"$newName`" - $($_.Exception.Message)"
            }
        }
    }

    [System.Windows.Forms.MessageBox]::Show(
        "Renamed $renamed file(s). Skipped $skipped due to conflicts.",
        "Rename Complete",
        [System.Windows.Forms.MessageBoxButtons]::OK,
        [System.Windows.Forms.MessageBoxIcon]::Information
    )
})

# === Reset ===
$resetButton.Add_Click({
    $listBox.Items.Clear()
    $textBox.Text = ""
    $checkBox.Checked = $false
    $dryRunCheckbox.Checked = $false
    $unblockButton.Enabled = $false
    $previewRenameButton.Enabled = $false
    $applyRenameButton.Enabled = $false
    $renameAllButton.Enabled = $false
    $exportCsvButton.Enabled = $false
})

# === Finalize and show form ===
$form.Topmost = $true
$form.Add_Shown({ $form.Activate() })
[void]$form.ShowDialog()