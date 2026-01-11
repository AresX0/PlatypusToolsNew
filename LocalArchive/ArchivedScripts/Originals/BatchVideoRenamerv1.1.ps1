Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Create main form
$form = New-Object Windows.Forms.Form
$form.Text = "Batch Video Renamer"
$form.Size = New-Object Drawing.Size(800, 700)
$form.StartPosition = "CenterScreen"
$form.AllowDrop = $true

# Tab control
$tabs = New-Object Windows.Forms.TabControl
$tabs.Size = New-Object Drawing.Size(760, 500)
$tabs.Location = New-Object Drawing.Point(10, 10)

# Preview tab
$tabPreview = New-Object Windows.Forms.TabPage
$tabPreview.Text = "Preview"

$listView = New-Object Windows.Forms.ListView
$listView.View = 'Details'
$listView.CheckBoxes = $true
$listView.FullRowSelect = $true
$listView.Dock = 'Fill'
$listView.Columns.Add("Original Name", 370)
$listView.Columns.Add("New Name", 370)
$tabPreview.Controls.Add($listView)

# Logs tab
$tabLogs = New-Object Windows.Forms.TabPage
$tabLogs.Text = "Logs"

$logBox = New-Object Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = "Vertical"
$logBox.Dock = 'Fill'
$tabLogs.Controls.Add($logBox)

# Settings tab
$tabSettings = New-Object Windows.Forms.TabPage
$tabSettings.Text = "Settings"
$tabSettings.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = "Future settings go here."; AutoSize = $true; Location = [Drawing.Point]::new(10,10) }))
$tabs.TabPages.AddRange(@($tabPreview, $tabLogs, $tabSettings))
$form.Controls.Add($tabs)

# Folder input
$folderBox = New-Object Windows.Forms.TextBox -Property @{ Location = [Drawing.Point]::new(10, 520); Size = [Drawing.Size]::new(500, 20) }
$form.Controls.Add($folderBox)

$browseButton = New-Object Windows.Forms.Button -Property @{ Text = "Browse"; Location = [Drawing.Point]::new(520, 518); Size = [Drawing.Size]::new(80, 24) }
$browseButton.Add_Click({
    $dialog = New-Object Windows.Forms.FolderBrowserDialog
    if ($dialog.ShowDialog() -eq "OK") { $folderBox.Text = $dialog.SelectedPath }
})
$form.Controls.Add($browseButton)

$form.Add_DragEnter({ $_.Effect = 'Copy' })
$form.Add_DragDrop({ $folderBox.Text = $_.Data.GetData("FileDrop")[0] })

# Labels and input boxes
$removeLabel = New-Object Windows.Forms.Label -Property @{ Text = "Prefix to Remove:"; Location = [Drawing.Point]::new(10, 540); Size = [Drawing.Size]::new(150, 15); Font = New-Object Drawing.Font("Segoe UI", 9, [Drawing.FontStyle]::Bold) }
$addLabel = New-Object Windows.Forms.Label -Property @{ Text = "Prefix to Add:"; Location = [Drawing.Point]::new(170, 540); Size = [Drawing.Size]::new(150, 15); Font = New-Object Drawing.Font("Segoe UI", 9, [Drawing.FontStyle]::Bold) }
$form.Controls.AddRange(@($removeLabel, $addLabel))

$removeBox = New-Object Windows.Forms.TextBox -Property @{ Location = [Drawing.Point]::new(10, 560); Size = [Drawing.Size]::new(150, 20) }
$addBox = New-Object Windows.Forms.TextBox -Property @{ Location = [Drawing.Point]::new(170, 560); Size = [Drawing.Size]::new(150, 20) }
$form.Controls.AddRange(@($removeBox, $addBox))

$tooltip = New-Object Windows.Forms.ToolTip
$tooltip.SetToolTip($removeBox, "Enter any prefix you want stripped from filenames.")
$tooltip.SetToolTip($addBox, "Enter the prefix you'd like added to each renamed file.")

# Mode dropdown
$modeBox = New-Object Windows.Forms.ComboBox -Property @{ Location = [Drawing.Point]::new(330, 560); Size = [Drawing.Size]::new(100, 20) }
$modeBox.Items.AddRange(@("WhatIf", "Run"))
$modeBox.SelectedIndex = 0
$form.Controls.Add($modeBox)

# Force checkboxes
$forceRenameCheck = New-Object Windows.Forms.CheckBox -Property @{ Text = "Force Rename"; Location = [Drawing.Point]::new(440, 560); Size = [Drawing.Size]::new(120, 20) }
$forceRenumberCheck = New-Object Windows.Forms.CheckBox -Property @{ Text = "Force Renumber"; Location = [Drawing.Point]::new(560, 560); Size = [Drawing.Size]::new(120, 20) }
$form.Controls.AddRange(@($forceRenameCheck, $forceRenumberCheck))

# Progress bar
$progressBar = New-Object Windows.Forms.ProgressBar -Property @{ Location = [Drawing.Point]::new(10, 590); Size = [Drawing.Size]::new(760, 15) }
$form.Controls.Add($progressBar)

# Buttons
$buttonPanel = New-Object Windows.Forms.Panel -Property @{ Location = [Drawing.Point]::new(10, 610); Size = [Drawing.Size]::new(760, 40) }

$previewButton = New-Object Windows.Forms.Button -Property @{ Text = "Preview"; Size = [Drawing.Size]::new(100, 30) }
$renameButton = New-Object Windows.Forms.Button -Property @{ Text = "Rename"; Size = [Drawing.Size]::new(100, 30) }
$renumberButton = New-Object Windows.Forms.Button -Property @{ Text = "Renumber"; Size = [Drawing.Size]::new(100, 30) }
$exitButton = New-Object Windows.Forms.Button -Property @{ Text = "Exit"; Size = [Drawing.Size]::new(100, 30) }
$saveLogButton = New-Object Windows.Forms.Button -Property @{ Text = "Save Logs & Exit"; Size = [Drawing.Size]::new(140, 30) }

$buttons = @($previewButton, $renameButton, $renumberButton, $exitButton, $saveLogButton)
for ($i = 0; $i -lt $buttons.Count; $i++) {
    $buttons[$i].Location = [Drawing.Point]::new(5 + ($i * 150), 5)
    $buttonPanel.Controls.Add($buttons[$i])
}
$form.Controls.Add($buttonPanel)

# Preview logic (shared by all modes)
$previewButton.Add_Click({
    $listView.Items.Clear()
    $logBox.Clear()
    $progressBar.Value = 0
    $folderPath = $folderBox.Text
    $prefixToRemove = $removeBox.Text
    $prefixToAdd = $addBox.Text
    $forceRename = $forceRenameCheck.Checked
    $forceRenumber = $forceRenumberCheck.Checked
    $videoExtensions = @(".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm")
    $episodePattern = "E\d{3}"

    if (-not (Test-Path $folderPath)) {
        [System.Windows.Forms.MessageBox]::Show("Please select a valid folder.", "Error", [System.Windows.Forms.MessageBoxButtons]::OK)
        return
    }

    $files = Get-ChildItem -Path $folderPath -Recurse -File | Where-Object { $videoExtensions -contains $_.Extension.ToLower() }
    $sortedFiles = $files | Sort-Object Name
    $global:renamePlan = @()
    $episodeNumber = 1

    foreach ($file in $sortedFiles) {
        $originalName = $file.Name
        $baseName = $originalName

        if ($forceRename -or $forceRenumber) {
            $baseName = $baseName -replace [Regex]::Escape($prefixToRemove), ''
            $baseName = $baseName -replace [Regex]::Escape($prefixToAdd), ''
            $baseName = $baseName -replace $episodePattern, ''
        } elseif ($originalName.StartsWith($prefixToAdd) -and ($originalName -match $episodePattern)) {
            $logBox.AppendText("Skipping '$originalName' — already correctly tagged.`r`n")
            continue
        }

        $baseName = ($baseName -replace '\s{2,}', ' ').Trim()
        $episodeTag = "E{0:D3}" -f $episodeNumber
        $newName = "$prefixToAdd $episodeTag $baseName"
        $newName = ($newName -replace '\s{2,}', ' ').Trim()
        $newPath = Join-Path $file.DirectoryName $newName

        if (Test-Path $newPath) {
            $base = [System.IO.Path]::GetFileNameWithoutExtension($newName)
            $ext = [System.IO.Path]::GetExtension($newName)
            $newName = "$base-duplicate$ext"
            $newPath = Join-Path $file.DirectoryName $newName
            $logBox.AppendText("Duplicate detected. Renaming to '$newName'`r`n")
        }

        $item = New-Object Windows.Forms.ListViewItem($originalName)
        $item.SubItems.Add($newName)
        $item.Checked = $true
        $listView.Items.Add($item)

        $global:renamePlan += [PSCustomObject]@{
            File = $file
            NewName = $newName
            NewPath = $newPath
            OriginalName = $originalName
        }

        $episodeNumber++
    }

    [System.Windows.Forms.MessageBox]::Show("Preview complete. Review changes in the Preview tab.", "Preview Done", [System.Windows.Forms.MessageBoxButtons]::OK)
})
# Rename logic
$renameButton.Add_Click({
    $mode = $modeBox.SelectedItem
    $folderPath = $folderBox.Text
    $backup = @()
    $undoLines = @("# Undo Rename Script", "# Run this to revert the file names", "")
    $backupFile = Join-Path $folderPath "rename_backup.csv"
    $undoScript = Join-Path $folderPath "undo_rename.ps1"

    for ($i = 0; $i -lt $listView.Items.Count; $i++) {
        $item = $listView.Items[$i]
        if (-not $item.Checked) {
            $logBox.AppendText("Skipped: $($item.Text)`r`n")
            continue
        }

        $plan = $global:renamePlan[$i]
        $file = $plan.File
        $newName = $plan.NewName
        $newPath = $plan.NewPath
        $originalName = $plan.OriginalName

        if ($mode -eq 'Run') {
            Rename-Item -Path $file.FullName -NewName $newName
            $logBox.AppendText("Renamed: $originalName -> $newName`r`n")
        } else {
            Rename-Item -Path $file.FullName -NewName $newName -WhatIf
            $logBox.AppendText("Simulated: $originalName -> $newName`r`n")
        }

        $backup += [PSCustomObject]@{
            OriginalName = $originalName
            NewName      = $newName
            FullPath     = $file.FullName
        }
        $undoLines += "Rename-Item -Path `"$newPath`" -NewName `"$originalName`""
        $progressBar.Value = [Math]::Min($progressBar.Value + (100 / $listView.Items.Count), 100)
    }

    $backup | Export-Csv -Path $backupFile -NoTypeInformation
    $undoLines | Set-Content -Path $undoScript -Encoding UTF8
    $logBox.AppendText("`r`nBackup saved to: $backupFile`r`n")
    $logBox.AppendText("Undo script saved to: $undoScript`r`n")
    [System.Windows.Forms.MessageBox]::Show("Rename process complete.", "Done", [System.Windows.Forms.MessageBoxButtons]::OK)
})

# Renumber logic
$renumberButton.Add_Click({
    $folderPath = $folderBox.Text
    $prefixToAdd = $addBox.Text
    $prefixToRemove = $removeBox.Text
    $force = $forceRenumberCheck.Checked
    $episodePattern = "E\d{3}"
    $videoExtensions = @(".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm")

    if (-not (Test-Path $folderPath)) {
        [System.Windows.Forms.MessageBox]::Show("Please select a valid folder before renumbering.", "Error", [System.Windows.Forms.MessageBoxButtons]::OK)
        return
    }

    $files = Get-ChildItem -Path $folderPath -Recurse -File | Where-Object { $videoExtensions -contains $_.Extension.ToLower() }

    if (-not $force -and [string]::IsNullOrWhiteSpace($prefixToRemove)) {
        $validFiles = foreach ($file in $files) {
            if ($file.Name.StartsWith($prefixToAdd) -and ($file.Name -match $episodePattern)) { $file }
        }
        if ($validFiles.Count -eq $files.Count) {
            [System.Windows.Forms.MessageBox]::Show("All files already have correct prefix and episode tag. Renumbering skipped.", "No Action Needed", [System.Windows.Forms.MessageBoxButtons]::OK)
            return
        }
    }

    $sortedFiles = $files | Sort-Object Name
    $episodeNumber = 1
    $backup = @()
    $undoLines = @("# Undo Renumber Script", "# Run this to revert the episode numbers", "")
    $logBox.AppendText("Renumbering episodes:`r`n")

    foreach ($file in $sortedFiles) {
        $originalName = $file.Name
        $baseName = $originalName

        if (-not [string]::IsNullOrWhiteSpace($prefixToRemove)) {
            $baseName = $baseName -replace [Regex]::Escape($prefixToRemove), ''
        }
        $baseName = $baseName -replace [Regex]::Escape($prefixToAdd), ''
        $baseName = $baseName -replace $episodePattern, ''
        $baseName = ($baseName -replace '\s{2,}', ' ').Trim()

        $episodeTag = "E{0:D3}" -f $episodeNumber
        $newName = "$prefixToAdd $episodeTag $baseName"
        $newName = ($newName -replace '\s{2,}', ' ').Trim()
        $newPath = Join-Path $file.DirectoryName $newName

        if (-not $force -and $file.Name -eq $newName) {
            $logBox.AppendText("Skipped: $newName — already correctly numbered.`r`n")
        } else {
            Rename-Item -Path $file.FullName -NewName $newName
            $logBox.AppendText("Renamed: $($file.Name) -> $newName`r`n")
            $backup += [PSCustomObject]@{
                OriginalName = $file.Name
                NewName      = $newName
                FullPath     = $file.FullName
            }
            $undoLines += "Rename-Item -Path `"$newPath`" -NewName `"$($file.Name)`""
        }

        $episodeNumber++
    }

    $backupFile = Join-Path $folderPath "renumber_backup.csv"
    $undoScript = Join-Path $folderPath "undo_renumber.ps1"
    $backup | Export-Csv -Path $backupFile -NoTypeInformation
    $undoLines | Set-Content -Path $undoScript -Encoding UTF8

    $logBox.AppendText("`r`nRenumber backup saved to: $backupFile`r`n")
    $logBox.AppendText("Undo script saved to: $undoScript`r`n")
    [System.Windows.Forms.MessageBoxButtons]::Show("Renumbering complete.", "Done", [System.Windows.Forms.MessageBoxButtons]::OK)
})
# Save Logs & Exit
$saveLogButton.Add_Click({
    $folderPath = $folderBox.Text
    if (-not (Test-Path $folderPath)) {
        [System.Windows.Forms.MessageBox]::Show("Please select a valid folder before saving logs.", "Error", [System.Windows.Forms.MessageBoxButtons]::OK)
        return
    }

    $logPath = Join-Path $folderPath "rename_log.txt"
    $logBox.Text | Set-Content -Path $logPath -Encoding UTF8
    [System.Windows.Forms.MessageBox]::Show("Logs saved to: $logPath", "Saved", [System.Windows.Forms.MessageBoxButtons]::OK)
    $form.Close()
})

# Exit button
$exitButton.Add_Click({ $form.Close() })

# Show the form
[void]$form.ShowDialog()

