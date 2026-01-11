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

# Tab: Preview
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

# Tab: Logs
$tabLogs = New-Object Windows.Forms.TabPage
$tabLogs.Text = "Logs"

$logBox = New-Object Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = "Vertical"
$logBox.Dock = 'Fill'
$tabLogs.Controls.Add($logBox)

# Tab: Settings (placeholder)
$tabSettings = New-Object Windows.Forms.TabPage
$tabSettings.Text = "Settings"
$tabSettings.Controls.Add((New-Object Windows.Forms.Label -Property @{ Text = "Future settings go here."; AutoSize = $true; Location = [Drawing.Point]::new(10,10) }))

$tabs.TabPages.AddRange(@($tabPreview, $tabLogs, $tabSettings))
$form.Controls.Add($tabs)

# Folder input
$folderBox = New-Object Windows.Forms.TextBox
$folderBox.Location = [Drawing.Point]::new(10, 520)
$folderBox.Size = [Drawing.Size]::new(500, 20)
$form.Controls.Add($folderBox)

$browseButton = New-Object Windows.Forms.Button
$browseButton.Text = "Browse"
$browseButton.Location = [Drawing.Point]::new(520, 518)
$browseButton.Size = [Drawing.Size]::new(80, 24)
$browseButton.Add_Click({
    $dialog = New-Object Windows.Forms.FolderBrowserDialog
    if ($dialog.ShowDialog() -eq "OK") { $folderBox.Text = $dialog.SelectedPath }
})
$form.Controls.Add($browseButton)

$form.Add_DragEnter({ $_.Effect = 'Copy' })
$form.Add_DragDrop({ $folderBox.Text = $_.Data.GetData("FileDrop")[0] })

# Labels for prefix inputs with spacing
$removeLabel = New-Object Windows.Forms.Label
$removeLabel.Text = "Prefix to Remove:"
$removeLabel.Location = [Drawing.Point]::new(10, 540)
$removeLabel.Size = [Drawing.Size]::new(150, 15)
$removeLabel.Font = New-Object Drawing.Font("Segoe UI", 9, [Drawing.FontStyle]::Bold)
$form.Controls.Add($removeLabel)

$addLabel = New-Object Windows.Forms.Label
$addLabel.Text = "Prefix to Add:"
$addLabel.Location = [Drawing.Point]::new(170, 540)
$addLabel.Size = [Drawing.Size]::new(150, 15)
$addLabel.Font = New-Object Drawing.Font("Segoe UI", 9, [Drawing.FontStyle]::Bold)
$form.Controls.Add($addLabel)

# Prefix input boxes with spacing
$removeBox = New-Object Windows.Forms.TextBox
$removeBox.Location = [Drawing.Point]::new(10, 560)
$removeBox.Size = [Drawing.Size]::new(150, 20)

$addBox = New-Object Windows.Forms.TextBox
$addBox.Location = [Drawing.Point]::new(170, 560)
$addBox.Size = [Drawing.Size]::new(150, 20)

$form.Controls.AddRange(@($removeBox, $addBox))

# Tooltips
$tooltip = New-Object Windows.Forms.ToolTip
$tooltip.SetToolTip($removeBox, "Enter any prefix you want stripped from filenames.")
$tooltip.SetToolTip($addBox, "Enter the prefix you'd like added to each renamed file.")

# Mode dropdown
$modeBox = New-Object Windows.Forms.ComboBox
$modeBox.Location = [Drawing.Point]::new(330, 560)
$modeBox.Size = [Drawing.Size]::new(100, 20)
$modeBox.Items.AddRange(@("WhatIf", "Run"))
$modeBox.SelectedIndex = 0
$form.Controls.Add($modeBox)

# Progress bar
$progressBar = New-Object Windows.Forms.ProgressBar
$progressBar.Location = [Drawing.Point]::new(10, 590)
$progressBar.Size = [Drawing.Size]::new(760, 15)
$form.Controls.Add($progressBar)

# Button panel
$buttonPanel = New-Object Windows.Forms.Panel
$buttonPanel.Location = [Drawing.Point]::new(10, 610)
$buttonPanel.Size = [Drawing.Size]::new(760, 40)

$previewButton = New-Object Windows.Forms.Button -Property @{ Text = "Preview"; Size = [Drawing.Size]::new(100, 30) }
$renameButton = New-Object Windows.Forms.Button -Property @{ Text = "Rename"; Size = [Drawing.Size]::new(100, 30) }
$savePlanButton = New-Object Windows.Forms.Button -Property @{ Text = "Save Plan"; Size = [Drawing.Size]::new(100, 30) }
$rerunButton = New-Object Windows.Forms.Button -Property @{ Text = "Rerun"; Size = [Drawing.Size]::new(100, 30) }
$exitButton = New-Object Windows.Forms.Button -Property @{ Text = "Exit"; Size = [Drawing.Size]::new(100, 30) }
$saveLogButton = New-Object Windows.Forms.Button -Property @{ Text = "Save Logs & Exit"; Size = [Drawing.Size]::new(140, 30) }

$buttons = @($previewButton, $renameButton, $savePlanButton, $rerunButton, $exitButton, $saveLogButton)
for ($i = 0; $i -lt $buttons.Count; $i++) {
    $buttons[$i].Location = [Drawing.Point]::new(5 + ($i * 125), 5)
    $buttonPanel.Controls.Add($buttons[$i])
}
$form.Controls.Add($buttonPanel)

# Preview logic
$previewButton.Add_Click({
    $listView.Items.Clear()
    $logBox.Clear()
    $progressBar.Value = 0
    $folderPath = $folderBox.Text
    $prefixToRemove = $removeBox.Text
    $prefixToAdd = $addBox.Text
    $mode = $modeBox.SelectedItem

    $videoExtensions = @(".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm")
    $resolutionTags = @("_720", "_1080", "_4k", "-hd")
    $files = Get-ChildItem -Path $folderPath -Recurse -File | Where-Object { $videoExtensions -contains $_.Extension.ToLower() }

    $cleanedFiles = foreach ($file in $files) {
        $name = $file.Name
        $name = $name -replace [Regex]::Escape($prefixToRemove), ''
        $name = $name -replace [Regex]::Escape($prefixToAdd), ''
        $name = $name -replace "E\d{3}", ''
        foreach ($tag in $resolutionTags) {
            $name = $name -replace [Regex]::Escape($tag), ''
        }
        $name = ($name -replace '\s{2,}', ' ').Trim()
        [PSCustomObject]@{ File = $file; CleanName = $name }
    }

    $sortedFiles = $cleanedFiles | Sort-Object CleanName
    $global:renamePlan = @()
    $episodeNumber = 1

    foreach ($entry in $sortedFiles) {
        $file = $entry.File
        $cleanName = $entry.CleanName
        $episodeTag = "E{0:D3}" -f $episodeNumber
        $newName = "$prefixToAdd $episodeTag $cleanName"
        $newName = ($newName -replace '\s{2,}', ' ').Trim()
        $newPath = Join-Path $file.DirectoryName $newName

$episodePattern = "E\d{3}"
$hasCorrectPrefix = $file.Name.StartsWith($prefixToAdd)
$hasEpisodeTag = $file.Name -match $episodePattern

if ($hasCorrectPrefix -and $hasEpisodeTag) {
    $logBox.AppendText("Skipping '$($file.Name)' — already has correct prefix and episode tag.`r`n")
    continue
}

          if (Test-Path $newPath) {
            $base = [System.IO.Path]::GetFileNameWithoutExtension($newName)
            $ext = [System.IO.Path]::GetExtension($newName)
            $newName = "$base-duplicate$ext"
            $newPath = Join-Path $file.DirectoryName $newName
            $logBox.AppendText("Duplicate detected. Renaming to '$newName'`r`n")
        }

        $item = New-Object Windows.Forms.ListViewItem($file.Name)
        $item.SubItems.Add($newName)
        $item.Checked = $true
        $listView.Items.Add($item)

        $global:renamePlan += [PSCustomObject]@{
            File = $file
            NewName = $newName
            NewPath = $newPath
            OriginalName = $file.Name
        }

        $episodeNumber++
    }
})

# Rename logic
$renameButton.Add_Click({
    $backup = @()
    $undoLines = @("# Undo Rename Script", "# Run this to revert the file names", "")
    $mode = $modeBox.SelectedItem
    $folderPath = $folderBox.Text
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

        $backup += [PSCustomObject]@{
            OriginalName = $originalName
            NewName      = $newName
            FullPath     = $file.FullName
        }
        $undoLines += "Rename-Item -Path `"$newPath`" -NewName `"$originalName`""

        if ($mode -eq 'Run') {
            Rename-Item -Path $file.FullName -NewName $newName
            $logBox.AppendText("Renamed: $originalName -> $newName`r`n")
        } else {
            Rename-Item -Path $file.FullName -NewName $newName -WhatIf
            $logBox.AppendText("Simulated: $originalName -> $newName`r`n")
        }

        $progressBar.Value = [Math]::Min($progressBar.Value + (100 / $listView.Items.Count), 100)
    }

    $backup | Export-Csv -Path $backupFile -NoTypeInformation
    $undoLines | Set-Content -Path $undoScript -Encoding UTF8
    $logBox.AppendText("`r`nBackup saved to: $backupFile`r`n")
    $logBox.AppendText("Undo script saved to: $undoScript`r`n")
    [System.Windows.Forms.MessageBox]::Show("Rename process complete.", "Done", [System.Windows.Forms.MessageBoxButtons]::OK)
})

# Save logs and exit
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