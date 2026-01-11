Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Ensure C:\convert exists
$targetDir = "C:\convert"
if (-not (Test-Path -LiteralPath $targetDir)) {
    try {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }
    catch {
        [System.Windows.Forms.MessageBox]::Show("Failed to create directory $targetDir. Error: $($_.Exception.Message)")
        exit
    }
}

# Check for ffmpeg.exe and ffprobe.exe in C:\convert
$ffmpegPath  = Join-Path $targetDir "ffmpeg.exe"
$ffprobePath = Join-Path $targetDir "ffprobe.exe"
if (-not (Test-Path -LiteralPath $ffmpegPath) -or -not (Test-Path -LiteralPath $ffprobePath)) {
    $result = [System.Windows.Forms.MessageBox]::Show(
        "FFmpeg/ffprobe not found in $targetDir. Would you like to download it now?",
        "FFmpeg Missing",
        [System.Windows.Forms.MessageBoxButtons]::YesNo,
        [System.Windows.Forms.MessageBoxIcon]::Warning
    )
    if ($result -eq [System.Windows.Forms.DialogResult]::Yes) {
        Start-Process "https://www.gyan.dev/ffmpeg/builds/"
    }
    exit
}

function Get-VideoInfo($file) {
    $args = @(
        "-v","error",
        "-select_streams","v:0",
        "-show_entries","stream=codec_name,width,height,r_frame_rate,format=duration",
        "-of","default=noprint_wrappers=1",
        "$file"
    )
    $infoLines = & $ffprobePath @args
    return $infoLines
}

function Compare-Encodings($files) {
    $details = @()
    foreach ($f in $files) {
        $info = Get-VideoInfo $f
        $codec    = ($info | Select-String "codec_name="     | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $width    = ($info | Select-String "width="          | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $height   = ($info | Select-String "height="         | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $fps      = ($info | Select-String "r_frame_rate="   | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $duration = ($info | Select-String "duration="       | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $details += [PSCustomObject]@{
            File     = [System.IO.Path]::GetFileName($f)
            FullPath = $f
            Codec    = $codec
            Width    = $width
            Height   = $height
            FPS      = $fps
            Duration = $duration
        }
    }
    return $details
}

# GUI Setup (modernized layout and docking)
$defaultFont = New-Object System.Drawing.Font("Segoe UI",10)

$form = New-Object System.Windows.Forms.Form
$form.Text = "Video Combiner"
$form.Size = New-Object System.Drawing.Size(1100,720)
$form.StartPosition = "CenterScreen"
$form.Font = $defaultFont

$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = "Fill"
$layout.RowCount = 3
$layout.ColumnCount = 1
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize)))
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent,100)))
$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize)))
$layout.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent,100)))

$commandBar = New-Object System.Windows.Forms.FlowLayoutPanel
$commandBar.Dock = "Top"
$commandBar.AutoSize = $true
$commandBar.WrapContents = $true  # allow the mode selector to stay visible on smaller widths
$commandBar.AutoScroll = $true    # enable horizontal/vertical scroll if it still overflows
$commandBar.Padding = New-Object System.Windows.Forms.Padding(8)
$commandBar.FlowDirection = "LeftToRight"

$btnBrowse   = New-Object System.Windows.Forms.Button
$btnBrowse.Text = "Browse"
$btnBrowse.AutoSize = $true
$btnBrowse.FlatStyle = "System"

$btnPreview  = New-Object System.Windows.Forms.Button
$btnPreview.Text = "Preview"
$btnPreview.AutoSize = $true
$btnPreview.FlatStyle = "System"

$btnFix      = New-Object System.Windows.Forms.Button
$btnFix.Text = "Normalize"
$btnFix.AutoSize = $true
$btnFix.FlatStyle = "System"

$btnCombine  = New-Object System.Windows.Forms.Button
$btnCombine.Text = "Combine"
$btnCombine.AutoSize = $true
$btnCombine.FlatStyle = "System"

$btnExit     = New-Object System.Windows.Forms.Button
$btnExit.Text = "Exit"
$btnExit.AutoSize = $true
$btnExit.FlatStyle = "System"

$lblMode = New-Object System.Windows.Forms.Label
$lblMode.Text = "Combine mode:"
$lblMode.AutoSize = $true
$lblMode.Padding = New-Object System.Windows.Forms.Padding(12,6,4,0)

$cmbMode = New-Object System.Windows.Forms.ComboBox
$cmbMode.DropDownStyle = "DropDownList"
$cmbMode.Items.AddRange(@("Auto","Copy (fast)","Transcode (safe)"))
$cmbMode.SelectedIndex = 0
$cmbMode.Width = 140

$commandBar.Controls.AddRange(@($btnBrowse,$btnPreview,$btnFix,$btnCombine,$lblMode,$cmbMode,$btnExit))

$mainSplit = New-Object System.Windows.Forms.SplitContainer
$mainSplit.Dock = "Fill"
$mainSplit.Orientation = "Horizontal"
$mainSplit.SplitterDistance = 420

# Top panel: file list and ordering
$listPanel = New-Object System.Windows.Forms.TableLayoutPanel
$listPanel.Dock = "Fill"
$listPanel.RowCount = 3
$listPanel.ColumnCount = 1
$listPanel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize)))
$listPanel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::Percent,100)))
$listPanel.RowStyles.Add((New-Object System.Windows.Forms.RowStyle([System.Windows.Forms.SizeType]::AutoSize)))
$listPanel.ColumnStyles.Add((New-Object System.Windows.Forms.ColumnStyle([System.Windows.Forms.SizeType]::Percent,100)))

$listHeader = New-Object System.Windows.Forms.Label
$listHeader.Text = "Checked videos will be used (drag or buttons to reorder)"
$listHeader.AutoSize = $true
$listHeader.Padding = New-Object System.Windows.Forms.Padding(4,6,4,6)

$listBox     = New-Object System.Windows.Forms.CheckedListBox
$listBox.Dock = "Fill"
$listBox.CheckOnClick = $true
$listBox.IntegralHeight = $false

$listActions = New-Object System.Windows.Forms.FlowLayoutPanel
$listActions.Dock = "Top"
$listActions.FlowDirection = "LeftToRight"
$listActions.AutoSize = $true
$listActions.Padding = New-Object System.Windows.Forms.Padding(4,4,4,4)

$btnSelectAll = New-Object System.Windows.Forms.Button
$btnSelectAll.Text = "Select All"
$btnSelectAll.AutoSize = $true

$btnSelectNone = New-Object System.Windows.Forms.Button
$btnSelectNone.Text = "Select None"
$btnSelectNone.AutoSize = $true

$btnUp       = New-Object System.Windows.Forms.Button
$btnUp.Text = "Move Up"
$btnUp.AutoSize = $true

$btnDown     = New-Object System.Windows.Forms.Button
$btnDown.Text = "Move Down"
$btnDown.AutoSize = $true

$listActions.Controls.AddRange(@($btnSelectAll,$btnSelectNone,$btnUp,$btnDown))

$listPanel.Controls.Add($listHeader,0,0)
$listPanel.Controls.Add($listBox,0,1)
$listPanel.Controls.Add($listActions,0,2)

$mainSplit.Panel1.Controls.Add($listPanel)

# Bottom panel: preview/output
$previewPane = New-Object System.Windows.Forms.TextBox
$previewPane.Dock = "Fill"
$previewPane.Multiline = $true
$previewPane.ScrollBars = "Vertical"
$previewPane.ReadOnly = $true
$previewPane.Font = New-Object System.Drawing.Font("Consolas",9)
$previewPane.BackColor = [System.Drawing.Color]::FromArgb(248,248,248)
$previewPane.BorderStyle = "FixedSingle"

$mainSplit.Panel2.Controls.Add($previewPane)

$statusPanel = New-Object System.Windows.Forms.Panel
$statusPanel.Dock = "Bottom"
$statusPanel.Height = 32
$statusPanel.Padding = New-Object System.Windows.Forms.Padding(8,6,8,6)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Dock = "Fill"
$statusLabel.AutoEllipsis = $true
$statusLabel.Text = "Ready"

$statusPanel.Controls.Add($statusLabel)

$layout.Controls.Add($commandBar,0,0)
$layout.Controls.Add($mainSplit,0,1)
$layout.Controls.Add($statusPanel,0,2)
$form.Controls.Add($layout)

# Logic
$videoFiles = @()
$supportedExt = @(".mp4",".mkv",".avi",".mov",".wmv",".webm",".flv")
$Global:PreviewNeedsTranscode = $false

function Update-Status {
    param([string]$Message)
    $statusLabel.Text = $Message
}

function Get-CheckedFiles {
    param($ListControl)
    return @($ListControl.CheckedItems | ForEach-Object { [string]$_ })
}

function Load-VideoFiles {
    param([string]$Folder)
    $videoFiles = Get-ChildItem -Path $Folder -File |
        Where-Object { $supportedExt -contains $_.Extension.ToLower() } |
        ForEach-Object { $_.FullName }

    $listBox.Items.Clear()
    foreach ($f in $videoFiles) { [void]$listBox.Items.Add($f,$false) }
    Update-Status "Loaded $($videoFiles.Count) video files"
    $previewPane.Text = "Loaded $($videoFiles.Count) video files from $Folder"
}

function Move-ItemInList {
    param([int]$Offset)
    $i = $listBox.SelectedIndex
    if ($i -lt 0) { return }
    $target = $i + $Offset
    if ($target -lt 0 -or $target -ge $listBox.Items.Count) { return }

    $item = $listBox.Items[$i]
    $isChecked = $listBox.GetItemChecked($i)

    $listBox.Items.RemoveAt($i)
    $listBox.Items.Insert($target, $item)
    $listBox.SetItemChecked($target, $isChecked)
    $listBox.SelectedIndex = $target
}

$btnBrowse.Add_Click({
    $folderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($folderBrowser.ShowDialog() -eq "OK") {
        Load-VideoFiles -Folder $folderBrowser.SelectedPath
    }
})

$btnUp.Add_Click({
    Move-ItemInList -Offset -1
})

$btnDown.Add_Click({
    Move-ItemInList -Offset 1
})

$btnSelectAll.Add_Click({
    for ($i=0; $i -lt $listBox.Items.Count; $i++) { $listBox.SetItemChecked($i,$true) }
})

$btnSelectNone.Add_Click({
    for ($i=0; $i -lt $listBox.Items.Count; $i++) { $listBox.SetItemChecked($i,$false) }
})

$btnPreview.Add_Click({
    $checkedFiles = Get-CheckedFiles $listBox
    if ($checkedFiles.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("No videos checked.") ; return }

    $previewPane.Clear()
    Update-Status "Reading metadata with ffprobe..."
    $details = Compare-Encodings $checkedFiles

    foreach ($d in $details) {
        $previewPane.AppendText(("{0,-40} | {1,-8} | {2}x{3} | FPS:{4,-10} | Dur:{5}" -f $d.File,$d.Codec,$d.Width,$d.Height,$d.FPS,$d.Duration) + "`r`n")
    }

    $codecMismatch = ($details.Codec | Select-Object -Unique).Count -gt 1
    $resMismatch   = ($details.Width | Select-Object -Unique).Count -gt 1 -or ($details.Height | Select-Object -Unique).Count -gt 1
    $fpsMismatch   = ($details.FPS | Select-Object -Unique).Count -gt 1

    $Global:PreviewNeedsTranscode = ($codecMismatch -or $resMismatch -or $fpsMismatch)

    if ($Global:PreviewNeedsTranscode) {
        $previewPane.AppendText("Differences detected → re-encode recommended.`r`n")
        Update-Status "Differences detected; consider Normalize before combining"
        $cmbMode.SelectedItem = "Transcode (safe)"
    } else {
        $previewPane.AppendText("Encodings compatible → safe to combine.`r`n")
        Update-Status "Encodings look compatible"
        if ($cmbMode.SelectedItem -eq "Auto") { $cmbMode.SelectedItem = "Copy (fast)" }
    }

    $recommended = if ($Global:PreviewNeedsTranscode) { "Transcode (safe)" } else { "Copy (fast)" }
    $previewPane.AppendText("Recommended combine mode: $recommended`r`n")
})

$btnFix.Add_Click({
    $checkedFiles = Get-CheckedFiles $listBox
    if ($checkedFiles.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("No videos checked.") ; return }

    $previewPane.Clear()
    $previewPane.AppendText("Re-encoding to H.264/AAC MP4 in $targetDir ...`r`n")
    Update-Status "Normalizing files (may take a while)"

    $fixedFiles = @()
    foreach ($f in $checkedFiles) {
        $outFile = Join-Path $targetDir ("fixed_" + [System.IO.Path]::GetFileNameWithoutExtension($f) + ".mp4")
        $args = @(
            "-y","-i",$f,
            "-c:v","libx264","-preset","fast","-crf","23",
            "-c:a","aac","-b:a","192k",
            $outFile
        )
        $previewPane.AppendText("→ $([System.IO.Path]::GetFileName($f)) → $([System.IO.Path]::GetFileName($outFile))`r`n")
        & $ffmpegPath @args
        $fixedFiles += $outFile
    }

    # Replace list with fixed files, checked by default, preserving order
    $listBox.Items.Clear()
    foreach ($f in $fixedFiles) { [void]$listBox.Items.Add($f,$true) }
    $previewPane.AppendText("Done. Files normalized for concat in $targetDir.`r`n")
    Update-Status "Normalization finished"
})

$btnCombine.Add_Click({
    $checkedFiles = Get-CheckedFiles $listBox
    if ($checkedFiles.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("No videos checked.") ; return }

    # Pick output directory
    $folderBrowser = New-Object System.Windows.Forms.FolderBrowserDialog
    $folderBrowser.Description = "Choose output directory for combined video"
    if ($folderBrowser.ShowDialog() -ne "OK") { return }
    $outputDir = $folderBrowser.SelectedPath

    # Pick output file name
    $saveDialog = New-Object System.Windows.Forms.SaveFileDialog
    $saveDialog.InitialDirectory = $outputDir
    $saveDialog.Filter = "MP4 files|*.mp4"
    $saveDialog.FileName = "combined.mp4"
    if ($saveDialog.ShowDialog() -ne "OK") { return }

    # Build concat list file (UTF-8 without BOM, safely escaped paths)
    $listFile = Join-Path $outputDir "videolist.txt"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $lines = @()
    foreach ($f in $checkedFiles) {
        $normalized = [System.IO.Path]::GetFullPath($f)
        $escaped = ($normalized -replace "'", "'\''")
        $lines += "file '$escaped'"
    }
    [System.IO.File]::WriteAllLines($listFile, $lines, $utf8NoBom)

    # Preview destination and order
    $previewPane.Clear()
    $previewPane.AppendText("Combining into: $($saveDialog.FileName)`r`n")
    $previewPane.AppendText("List file: $listFile`r`n")
    $previewPane.AppendText("Order:`r`n")
    $idx = 1
    foreach ($f in $checkedFiles) {
        $previewPane.AppendText(("  {0}. {1}" -f $idx, [System.IO.Path]::GetFileName($f)) + "`r`n")
        $idx++
    }

    # Decide combine mode
    $selectedMode = $cmbMode.SelectedItem
    if ($selectedMode -eq "Auto") {
        $selectedMode = if ($Global:PreviewNeedsTranscode) { "Transcode (safe)" } else { "Copy (fast)" }
    }
    $useTranscode = ($selectedMode -eq "Transcode (safe)")
    $previewPane.AppendText("Combine mode: $selectedMode`r`n")

    # Run ffmpeg concat; regenerate PTS to avoid non-monotonic DTS warnings
    Update-Status "Combining with ffmpeg..."

    $ffmpegArgsCopy = @(
        "-y",
        "-f","concat","-safe","0","-i",$listFile,
        "-fflags","+genpts",
        "-reset_timestamps","1",
        "-avoid_negative_ts","make_zero",
        "-c","copy",
        "-movflags","+faststart",
        $saveDialog.FileName
    )

    $ffmpegArgsTranscode = @(
        "-y",
        "-f","concat","-safe","0","-i",$listFile,
        "-fflags","+genpts",
        "-reset_timestamps","1",
        "-vsync","2",
        "-c:v","libx264","-preset","fast","-crf","20",
        "-c:a","aac","-b:a","192k",
        "-af","aresample=async=1",
        "-movflags","+faststart",
        $saveDialog.FileName
    )

    $copySucceeded = $false
    if (-not $useTranscode) {
        try {
            & $ffmpegPath @ffmpegArgsCopy
            if ($LASTEXITCODE -eq 0) { $copySucceeded = $true }
            else {
                $previewPane.AppendText("Direct stream copy failed (exit $LASTEXITCODE), retrying with re-encode...`r`n")
            }
        }
        catch {
            $previewPane.AppendText("Direct stream copy threw: $($_.Exception.Message)`r`nRetrying with re-encode...`r`n")
        }
    }

    if ($useTranscode -or -not $copySucceeded) {
        & $ffmpegPath @ffmpegArgsTranscode
        if ($LASTEXITCODE -ne 0) {
            Remove-Item $listFile -Force
            [System.Windows.Forms.MessageBox]::Show("Combine failed (exit $LASTEXITCODE). Check Preview log for details.")
            Update-Status "Combine failed"
            return
        }
    }

    # Cleanup and notify
    Remove-Item $listFile -Force
    [System.Windows.Forms.MessageBox]::Show("Videos combined successfully into $($saveDialog.FileName)")
    Update-Status "Combine complete"
})

$btnExit.Add_Click({ $form.Close() })

$form.ShowDialog()