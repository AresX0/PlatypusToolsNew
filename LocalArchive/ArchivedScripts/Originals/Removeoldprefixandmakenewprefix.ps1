Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Create main form
$form = New-Object Windows.Forms.Form
$form.Text = "Batch Video Renamer"
$form.Size = New-Object Drawing.Size(800, 650)
$form.StartPosition = "CenterScreen"
$form.AllowDrop = $true

# Drag-and-drop folder support
$form.Add_DragEnter({ $_.Effect = 'Copy' })
$form.Add_DragDrop({
    $folderBox.Text = $_.Data.GetData("FileDrop")[0]
})

# Folder input
$folderLabel = New-Object Windows.Forms.Label
$folderLabel.Text = "Target Folder:"
$folderLabel.Location = New-Object Drawing.Point(10, 20)
$form.Controls.Add($folderLabel)

$folderBox = New-Object Windows.Forms.TextBox
$folderBox.Location = New-Object Drawing.Point(110, 18)
$folderBox.Size = New-Object Drawing.Size(550, 20)
$form.Controls.Add($folderBox)

$browseButton = New-Object Windows.Forms.Button
$browseButton.Text = "Browse"
$browseButton.Location = New-Object Drawing.Point(670, 16)
$browseButton.Add_Click({
    $dialog = New-Object Windows.Forms.FolderBrowserDialog
    if ($dialog.ShowDialog() -eq "OK") {
        $folderBox.Text = $dialog.SelectedPath
    }
})
$form.Controls.Add($browseButton)

# Prefix inputs
$removeLabel = New-Object Windows.Forms.Label
$removeLabel.Text = "Prefix to Remove:"
$removeLabel.Location = New-Object Drawing.Point(10, 60)
$form.Controls.Add($removeLabel)

$removeBox = New-Object Windows.Forms.TextBox
$removeBox.Location = New-Object Drawing.Point(130, 58)
$removeBox.Size = New-Object Drawing.Size(200, 20)
$form.Controls.Add($removeBox)

$addLabel = New-Object Windows.Forms.Label
$addLabel.Text = "Prefix to Add:"
$addLabel.Location = New-Object Drawing.Point(350, 60)
$form.Controls.Add($addLabel)

$addBox = New-Object Windows.Forms.TextBox
$addBox.Location = New-Object Drawing.Point(450, 58)
$addBox.Size = New-Object Drawing.Size(200, 20)
$form.Controls.Add($addBox)

# Mode dropdown
$modeLabel = New-Object Windows.Forms.Label
$modeLabel.Text = "Mode:"
$modeLabel.Location = New-Object Drawing.Point(10, 90)
$form.Controls.Add($modeLabel)

$modeBox = New-Object Windows.Forms.ComboBox
$modeBox.Items.AddRange(@("WhatIf", "Run"))
$modeBox.SelectedIndex = 0
$modeBox.Location = New-Object Drawing.Point(130, 88)
$modeBox.Size = New-Object Drawing.Size(200, 20)
$form.Controls.Add($modeBox)

# Buttons
$previewButton = New-Object Windows.Forms.Button
$previewButton.Text = "Preview Renames"
$previewButton.Location = New-Object Drawing.Point(350, 88)
$form.Controls.Add($previewButton)

$renameButton = New-Object Windows.Forms.Button
$renameButton.Text = "Execute Rename"
$renameButton.Location = New-Object Drawing.Point(500, 88)
$form.Controls.Add($renameButton)

$rerunButton = New-Object Windows.Forms.Button
$rerunButton.Text = "Rerun"
$rerunButton.Location = New-Object Drawing.Point(630, 88)
$rerunButton.Add_Click({
    $listView.Items.Clear()
    $logBox.Clear()
    $global:renamePlan = @()
    $folderBox.Text = ""
    $removeBox.Text = ""
    $addBox.Text = ""
    $modeBox.SelectedIndex = 0
    $progressBar.Value = 0
})
$form.Controls.Add($rerunButton)

$exitButton = New-Object Windows.Forms.Button
$exitButton.Text = "Exit"
$exitButton.Location = New-Object Drawing.Point(700, 600)
$exitButton.Add_Click({ $form.Close() })
$form.Controls.Add($exitButton)

$savePlanButton = New-Object Windows.Forms.Button
$savePlanButton.Text = "Save Plan"
$savePlanButton.Location = New-Object Drawing.Point(10, 560)
$savePlanButton.Add_Click({
    $planPath = Join-Path $folderBox.Text "rename_plan.csv"
    $global:renamePlan | Select-Object OriginalName, NewName | Export-Csv -Path $planPath -NoTypeInformation
    $logBox.AppendText("Rename plan saved to: $planPath`r`n")
})
$form.Controls.Add($savePlanButton)

# Preview grid
$listView = New-Object Windows.Forms.ListView
$listView.View = 'Details'
$listView.CheckBoxes = $true
$listView.FullRowSelect = $true
$listView.Location = New-Object Drawing.Point(10, 130)
$listView.Size = New-Object Drawing.Size(760, 250)
$listView.Columns.Add("Original Name", 370)
$listView.Columns.Add("New Name", 370)
$form.Controls.Add($listView)

# Log viewer
$logBox = New-Object Windows.Forms.TextBox
$logBox.Multiline = $true
$logBox.ScrollBars = "Vertical"
$logBox.Location = New-Object Drawing.Point(10, 400)
$logBox.Size = New-Object Drawing.Size(760, 150)
$form.Controls.Add($logBox)

# Progress bar
$progressBar = New-Object Windows.Forms.ProgressBar
$progressBar.Location = New-Object Drawing.Point(10, 360)
$progressBar.Size = New-Object Drawing.Size(760, 20)
$form.Controls.Add($progressBar)

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
        $episodeTag = "$prefixToAdd" + "E{0:D3}" -f $episodeNumber
        $newName = "$episodeTag $cleanName"
        $newName = ($newName -replace '\s{2,}', ' ').Trim()
        $newPath = Join-Path $file.DirectoryName $newName

        if ($file.Name -eq $newName) {
            $logBox.AppendText("Skipping '$($file.Name)' — already correctly named.`r`n")
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

# Rename execution logic
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

# Show the form
[void]$form.ShowDialog()