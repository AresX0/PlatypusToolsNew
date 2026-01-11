# MetadataCleaner-Final-Fixes.ps1
# Fixed: OrderedDictionary uses .Contains not .ContainsKey. Full GUI with Load, Scan (subfolders), per-file view, edit/delete/export, Reset, Exit.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
if (-not ([AppDomain]::CurrentDomain.GetAssemblies() | Where-Object { $_.GetName().Name -eq 'Microsoft.VisualBasic' })) { Add-Type -AssemblyName Microsoft.VisualBasic }

function Get-ShellMetadata {
    param([string]$FullPath, [int[]]$indices = $null)
    if (-not $FullPath -or -not (Test-Path $FullPath)) { return $null }
    try {
        $shell = New-Object -ComObject Shell.Application
        $folder = $shell.Namespace((Split-Path $FullPath -Parent))
        $file = $folder.ParseName((Split-Path $FullPath -Leaf))
        $meta = [ordered]@{}
        if (-not $indices) { $indices = 0..300 }
        foreach ($i in $indices) {
            $k = "Index:$i"
            $v = $folder.GetDetailsOf($file, $i)
            if ($v -ne '') { $meta[$k] = $v }
        }
        $meta['FullPath'] = $FullPath
        return $meta
    } catch { return $null }
}

function Get-ShellFolderFieldNames {
    param([string]$AnyFilePath)
    if (-not $AnyFilePath -or -not (Test-Path $AnyFilePath)) { return @() }
    $shell = New-Object -ComObject Shell.Application
    $folder = $shell.Namespace((Split-Path $AnyFilePath -Parent))
    $names = @()
    for ($i=0; $i -le 300; $i++) {
        $n = $folder.GetDetailsOf($folder.Items, $i)
        if ($n -and $n -ne '') { $names += [PSCustomObject]@{ Index=$i; Name=$n } }
    }
    return $names
}

function Choose-File([string]$title="Select file") {
    $ofd = New-Object System.Windows.Forms.OpenFileDialog
    $ofd.Title = $title
    $ofd.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
    if ($ofd.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return $null }
    return $ofd.FileName
}
function Choose-Folder([string]$title="Select folder") {
    $fd = New-Object System.Windows.Forms.FolderBrowserDialog
    $fd.Description = $title
    if ($fd.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $fd.SelectedPath }
    return $null
}

function Build-ScanEditUI {
    param()
    $state = [ordered]@{ Items=@(); CsvPath=$null; BasePath=$null; IncludeSubfolders=$true }

    $form = New-Object System.Windows.Forms.Form
    $form.Text = "Metadata Cleaner — Scan & View"
    $form.Size = New-Object System.Drawing.Size(1200,820)
    $form.StartPosition = 'CenterScreen'
    $form.MaximizeBox = $true

    $list = New-Object System.Windows.Forms.ListView
    $list.View = 'Details'; $list.FullRowSelect = $true; $list.MultiSelect = $true; $list.GridLines = $true
    $list.Height = 520; $list.Width = 1160; $list.Left = 10; $list.Top = 10; $list.HideSelection = $false
    $cols = @('Original','Renamed','Path','Title','Artist','Size','Duration')
    $widths = @{ 'Original'=160; 'Renamed'=300; 'Path'=380; 'Title'=140; 'Artist'=140; 'Size'=80; 'Duration'=80 }
    foreach ($c in $cols) { $null = $list.Columns.Add($c, $widths[$c]) }

    $btnLoadCsv = New-Object System.Windows.Forms.Button -Property @{ Text='Load CSV'; Left=10; Top=540; Width=100 }
    $txtCsv = New-Object System.Windows.Forms.TextBox -Property @{ Left=120; Top=542; Width=570; ReadOnly=$true }
    $btnBrowseCsv = New-Object System.Windows.Forms.Button -Property @{ Text='Browse CSV'; Left=700; Top=540; Width=100 }

    $btnChooseBase = New-Object System.Windows.Forms.Button -Property @{ Text='Choose Folder to Scan'; Left=820; Top=540; Width=150 }
    $txtBase = New-Object System.Windows.Forms.TextBox -Property @{ Left=980; Top=542; Width=190; ReadOnly=$true }

    $chkSub = New-Object System.Windows.Forms.CheckBox -Property @{ Text='Include subfolders'; Left=10; Top=580; Checked = $true }
    $btnScanFiles = New-Object System.Windows.Forms.Button -Property @{ Text='Scan files (in Base)'; Left=160; Top=576; Width=140 }
    $btnScanSelectedFolders = New-Object System.Windows.Forms.Button -Property @{ Text='Scan selected folders'; Left=320; Top=576; Width=200 }

    $btnViewMeta = New-Object System.Windows.Forms.Button -Property @{ Text='View Metadata (selected)'; Left=540; Top=576; Width=160 }
    $btnViewAllFields = New-Object System.Windows.Forms.Button -Property @{ Text='Show All Shell Fields (folder)'; Left=720; Top=576; Width=220 }

    $chkDry = New-Object System.Windows.Forms.CheckBox -Property @{ Text='Dry-run (no edits/deletes)'; Left=960; Top=576; Checked=$true }

    $btnEdit = New-Object System.Windows.Forms.Button -Property @{ Text='Edit Metadata (selected)'; Left=10; Top=616; Width=170 }
    $btnDelete = New-Object System.Windows.Forms.Button -Property @{ Text='Delete Files (selected)'; Left=200; Top=616; Width=160 }
    $btnRefresh = New-Object System.Windows.Forms.Button -Property @{ Text='Refresh Metadata'; Left=380; Top=616; Width=140 }
    $btnExport = New-Object System.Windows.Forms.Button -Property @{ Text='Export Results CSV'; Left=540; Top=616; Width=160 }

    $btnReset = New-Object System.Windows.Forms.Button -Property @{ Text='Reset'; Left=720; Top=616; Width=100 }
    $btnExit = New-Object System.Windows.Forms.Button -Property @{ Text='Exit'; Left=840; Top=616; Width=100 }

    $lblStatus = New-Object System.Windows.Forms.Label -Property @{ Text='Ready'; Left=10; Top=660; Width=1160; Height=24 }

    $refreshList = {
        $list.BeginUpdate()
        $list.Items.Clear()
        foreach ($i in $state.Items) {
            $origText = [string]($i.Original -or '')
            $lvi = [System.Windows.Forms.ListViewItem]::new($origText)
            $lvi.SubItems.Add([string]($i.Renamed -or '')) | Out-Null
            $lvi.SubItems.Add([string]($i.FullPath -or '')) | Out-Null
            $title = if ($i.Metadata -and $i.Metadata.Contains('Index:21')) { $i.Metadata['Index:21'] } else { '' }
            $artist = if ($i.Metadata -and $i.Metadata.Contains('Index:13')) { $i.Metadata['Index:13'] } else { '' }
            $size = if ($i.Metadata -and $i.Metadata.Contains('Index:1')) { $i.Metadata['Index:1'] } else { '' }
            $duration = if ($i.Metadata -and $i.Metadata.Contains('Index:27')) { $i.Metadata['Index:27'] } else { '' }
            $lvi.SubItems.Add([string]$title) | Out-Null
            $lvi.SubItems.Add([string]$artist) | Out-Null
            $lvi.SubItems.Add([string]$size) | Out-Null
            $lvi.SubItems.Add([string]$duration) | Out-Null
            $lvi.Tag = $i
            $list.Items.Add($lvi) | Out-Null
        }
        $list.EndUpdate()
    }

    $btnLoadCsv.Add_Click({
        $file = Choose-File "Select undo_rename_log.csv"
        if (-not $file) { return }
        $txtCsv.Text = $file; $state.CsvPath = $file
        $lblStatus.Text = "CSV loaded: $file"
    })
    $btnBrowseCsv.Add_Click({
        $f = Choose-File "Select undo_rename_log.csv"
        if ($f) { $txtCsv.Text = $f; $state.CsvPath = $f; $lblStatus.Text = "CSV selected: $f" }
    })

    $btnChooseBase.Add_Click({
        $b = Choose-Folder "Select base folder to scan"
        if ($b) { $txtBase.Text = $b; $state.BasePath = $b; $lblStatus.Text = "Base: $b" }
    })

    $btnScanFiles.Add_Click({
        if (-not $state.BasePath -or -not (Test-Path $state.BasePath)) { [System.Windows.Forms.MessageBox]::Show("Select a valid base folder first.","Error"); return }
        $include = $chkSub.Checked
        $lblStatus.Text = "Scanning folder: $($state.BasePath) (subfolders: $include)..."
        $form.Refresh()
        $exts = @('*.mp4','*.mkv','*.mp3','*.m4v','*.avi','*.mov','*.wmv')
        $found = @()
        foreach ($e in $exts) { $found += Get-ChildItem -Path $state.BasePath -Recurse:($include) -File -Filter $e -ErrorAction SilentlyContinue }
        $state.Items = @()
        foreach ($f in $found) {
            $meta = Get-ShellMetadata -FullPath $f.FullName
            $state.Items += [PSCustomObject]@{ Original = $f.Name; Renamed = $f.Name; FullPath = $f.FullName; Metadata = $meta }
        }
        & $refreshList
        $lblStatus.Text = "Scan complete. Files found: $($state.Items.Count)"
    })

    $btnScanFromCsv = {
        if (-not $state.CsvPath) { [System.Windows.Forms.MessageBox]::Show("Load a CSV first or use Scan files.","Info"); return }
        try { $entries = Import-Csv -Path $state.CsvPath } catch { [System.Windows.Forms.MessageBox]::Show("CSV load failed.","Error"); return }
        $state.Items = @()
        $base = if ($state.BasePath) { $state.BasePath } else { 'F:\' }
        $include = $chkSub.Checked
        $lblStatus.Text = "Building list from CSV (searching base: $base, subfolders: $include)..."; $form.Refresh()
        foreach ($e in $entries) {
            $orig = $null; $ren = $null
            if ($e.PSObject.Properties.Match('Original')) { $orig = $e.Original } elseif ($e.PSObject.Properties.Match('OriginalName')) { $orig = $e.OriginalName }
            if ($e.PSObject.Properties.Match('Renamed')) { $ren = $e.Renamed } elseif ($e.PSObject.Properties.Match('NewName')) { $ren = $e.NewName }
            if (-not $orig -or -not $ren) { $state.Items += [PSCustomObject]@{ Original=$orig; Renamed=$ren; FullPath=$null; Metadata=$null }; continue }
            $candidate = Join-Path -Path $base -ChildPath $ren
            if (Test-Path $candidate) { $fp = (Resolve-Path $candidate).ProviderPath } else {
                $found = Get-ChildItem -Path $base -Recurse:($include) -File -Filter $ren -ErrorAction SilentlyContinue | Select-Object -First 1
                $fp = if ($found) { $found.FullName } else { $null }
            }
            $meta = if ($fp) { Get-ShellMetadata -FullPath $fp } else { $null }
            $state.Items += [PSCustomObject]@{ Original=$orig; Renamed=$ren; FullPath=$fp; Metadata=$meta }
        }
        & $refreshList
        $lblStatus.Text = "CSV-derived build complete. Items: $($state.Items.Count)"
    }

    $btnBuildCsv = New-Object System.Windows.Forms.Button -Property @{ Text='Build from CSV'; Left=900; Top=540; Width=120 }
    $btnBuildCsv.Add_Click({ & $btnScanFromCsv.Invoke() })

    $btnScanSelectedFolders.Add_Click({
        if ($list.SelectedItems.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("Select one or more rows to scan their folders.","Info"); return }
        $include = $chkSub.Checked
        $selectedFolders = @()
        foreach ($s in $list.SelectedItems) { $obj=$s.Tag; if ($obj.FullPath) { $selectedFolders += (Split-Path $obj.FullPath -Parent) } }
        $selectedFolders = $selectedFolders | Select-Object -Unique
        $state.Items = @()
        foreach ($folder in $selectedFolders) {
            $found = Get-ChildItem -Path $folder -Recurse:($include) -File -ErrorAction SilentlyContinue
            foreach ($f in $found) {
                $meta = Get-ShellMetadata -FullPath $f.FullName
                $state.Items += [PSCustomObject]@{ Original=$f.Name; Renamed=$f.Name; FullPath=$f.FullName; Metadata=$meta }
            }
        }
        & $refreshList
        $lblStatus.Text = "Scanned folders: $($selectedFolders.Count) — files: $($state.Items.Count)"
    })

    $btnViewMeta.Add_Click({
        if ($list.SelectedItems.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("Select one or more files to view metadata.","Info"); return }
        foreach ($s in $list.SelectedItems) {
            $obj = $s.Tag
            if (-not $obj.FullPath) { continue }
            $meta = Get-ShellMetadata -FullPath $obj.FullPath
            $vw = New-Object System.Windows.Forms.Form
            $vw.Text = "Metadata — $([IO.Path]::GetFileName($obj.FullPath))"
            $vw.Size = New-Object System.Drawing.Size(900,700); $vw.StartPosition='CenterParent'
            $rt = New-Object System.Windows.Forms.RichTextBox -Property @{ Dock='Fill'; ReadOnly=$true; Font = 'Consolas,10' }
            $lines = @("FullPath: $($obj.FullPath)","")
            foreach ($k in $meta.Keys) { $lines += ("{0,-12} : {1}" -f $k, $meta[$k]) }
            $rt.Text = ($lines -join "`r`n")
            $vw.Controls.Add($rt); $vw.Show()
        }
    })

    $btnViewAllFields.Add_Click({
        $samplePath = $null
        if ($list.SelectedItems.Count -gt 0) { $samplePath = $list.SelectedItems[0].Tag.FullPath }
        elseif ($state.BasePath) { $sampleFile = Get-ChildItem -Path $state.BasePath -Recurse:$false -File -ErrorAction SilentlyContinue | Select-Object -First 1; if ($sampleFile) { $samplePath = $sampleFile.FullName } }
        if (-not $samplePath) { [System.Windows.Forms.MessageBox]::Show("No sample file found. Load CSV or set base and scan to get a sample.","Info"); return }
        $fields = Get-ShellFolderFieldNames -AnyFilePath $samplePath
        $vf = New-Object System.Windows.Forms.Form
        $vf.Text = "Shell field indices and names (sample folder)"
        $vf.Size = New-Object System.Drawing.Size(600,700); $vf.StartPosition='CenterParent'
        $lv = New-Object System.Windows.Forms.ListView
        $lv.View='Details'; $lv.FullRowSelect=$true; $lv.Dock='Fill'; $lv.Columns.Add('Index',80) | Out-Null; $lv.Columns.Add('Name',450) | Out-Null
        foreach ($f in $fields) { $it = [System.Windows.Forms.ListViewItem]::new([string]$f.Index); $it.SubItems.Add([string]$f.Name) | Out-Null; $lv.Items.Add($it) | Out-Null }
        $vf.Controls.Add($lv); $vf.ShowDialog()
    })

    $btnEdit.Add_Click({
        if ($list.SelectedItems.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("Select files to edit.","Info"); return }
        $selected = @(); foreach ($s in $list.SelectedItems) { $selected += $s.Tag }
        $titles = $selected | ForEach-Object { if ($_.Metadata -and $_.Metadata.Contains('Index:21')) { $_.Metadata['Index:21'] } else { $null } } | Where-Object { $_ } | Select-Object -Unique
        $artists = $selected | ForEach-Object { if ($_.Metadata -and $_.Metadata.Contains('Index:13')) { $_.Metadata['Index:13'] } else { $null } } | Where-Object { $_ } | Select-Object -Unique
        $titleVal = if ($titles.Count -eq 1) { $titles[0] } else { '' }; $artistVal = if ($artists.Count -eq 1) { $artists[0] } else { '' }

        $ef = New-Object System.Windows.Forms.Form; $ef.Text="Edit metadata"; $ef.Size=New-Object System.Drawing.Size(480,220); $ef.StartPosition='CenterParent'
        $lbl1=New-Object System.Windows.Forms.Label -Property @{Text='Title';Left=10;Top=15;Width=80}; $txtTitle=New-Object System.Windows.Forms.TextBox -Property @{Left=100;Top=12;Width=350;Text=$titleVal}
        $lbl2=New-Object System.Windows.Forms.Label -Property @{Text='Artist';Left=10;Top=55;Width=80}; $txtArtist=New-Object System.Windows.Forms.TextBox -Property @{Left=100;Top=52;Width=350;Text=$artistVal}
        $btnOk=New-Object System.Windows.Forms.Button -Property @{Text='Apply';Left=140;Top=110;Width=100}; $btnCancel=New-Object System.Windows.Forms.Button -Property @{Text='Cancel';Left=260;Top=110;Width=100}
        $btnOk.Add_Click({
            foreach ($sel in $selected) {
                if (-not $sel.FullPath) { continue }
                if ($chkDry.Checked) { Write-Host "DRY: Would set Title='$($txtTitle.Text)' Artist='$($txtArtist.Text)' on $($sel.FullPath)"; continue }
                try {
                    $shell = New-Object -ComObject Shell.Application
                    $folder = $shell.Namespace((Split-Path $sel.FullPath -Parent)); $fileItem = $folder.ParseName((Split-Path $sel.FullPath -Leaf))
                    if ($txtTitle.Text -ne '') { $folder.SetDetailsOf($fileItem,21,$txtTitle.Text) }
                    if ($txtArtist.Text -ne '') { $folder.SetDetailsOf($fileItem,13,$txtArtist.Text) }
                } catch { Write-Host "Update failed: $_" }
            }
            foreach ($lv in $list.SelectedItems) { $o=$lv.Tag; if ($o.FullPath) { $o.Metadata = Get-ShellMetadata -FullPath $o.FullPath; $lv.SubItems[3].Text = $o.Metadata['Index:21'] -or ''; $lv.SubItems[4].Text = $o.Metadata['Index:13'] -or '' } }
            $ef.Close()
        })
        $btnCancel.Add_Click({ $ef.Close() }); $ef.Controls.AddRange(@($lbl1,$txtTitle,$lbl2,$txtArtist,$btnOk,$btnCancel)); $ef.ShowDialog()
    })

    $btnDelete.Add_Click({
        if ($list.SelectedItems.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("Select files to delete.","Info"); return }
        $confirm = [System.Windows.Forms.MessageBox]::Show("Delete selected files? This cannot be undone.","Confirm Delete",[System.Windows.Forms.MessageBoxButtons]::YesNo,[System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) { return }
        foreach ($s in @($list.SelectedItems)) {
            $o = $s.Tag
            if (-not $o.FullPath) { Write-Host "Skipping missing"; continue }
            if ($chkDry.Checked) { Write-Host "DRY: Would delete $($o.FullPath)"; continue }
            try { Remove-Item -LiteralPath $o.FullPath -Force; $list.Items.Remove($s) | Out-Null } catch { Write-Host "Delete failed: $_" }
        }
    })

    $btnRefresh.Add_Click({
        foreach ($lvi in $list.Items) { $o=$lvi.Tag; if ($o.FullPath) { $o.Metadata = Get-ShellMetadata -FullPath $o.FullPath; $lvi.SubItems[3].Text = $o.Metadata['Index:21'] -or ''; $lvi.SubItems[4].Text = $o.Metadata['Index:13'] -or ''; $lvi.SubItems[5].Text = $o.Metadata['Index:1'] -or '' } }
        $lblStatus.Text = "Metadata refreshed at $(Get-Date -Format 'HH:mm:ss')"
    })

    $btnExport.Add_Click({
        $sfd = New-Object System.Windows.Forms.SaveFileDialog; $sfd.Filter="CSV (*.csv)|*.csv|All|*.*"; $sfd.FileName="metadata_results.csv"
        if ($sfd.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { return }
        $out = foreach ($lvi in $list.Items) { $o=$lvi.Tag; [PSCustomObject]@{ Original=$o.Original; Renamed=$o.Renamed; FullPath=$o.FullPath; Title=($o.Metadata['Index:21'] -or ''); Artist=($o.Metadata['Index:13'] -or ''); Size=($o.Metadata['Index:1'] -or '') } }
        try { $out | Export-Csv -Path $sfd.FileName -NoTypeInformation -Encoding UTF8; $lblStatus.Text = "Exported to $($sfd.FileName)" } catch { [System.Windows.Forms.MessageBox]::Show("Export failed: $_","Error") }
    })

    $btnReset.Add_Click({
        $state.Items = @(); $state.CsvPath = $null; $state.BasePath = $null
        $txtCsv.Text = ''; $txtBase.Text = ''; $lblStatus.Text = 'Reset done'
        & $refreshList
    })

    $btnExit.Add_Click({ $form.Close() })

    $form.Controls.AddRange(@($list,$btnLoadCsv,$txtCsv,$btnBrowseCsv,$btnChooseBase,$txtBase,$btnScanFiles,$btnBuildCsv,$btnScanSelectedFolders,$chkSub,$btnViewMeta,$btnViewAllFields,$chkDry,$btnEdit,$btnDelete,$btnRefresh,$btnExport,$btnReset,$btnExit,$lblStatus))
    $form.Add_Shown({ $form.Activate() })
    [void]$form.ShowDialog()
}

if ([System.Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    $scriptPath = $MyInvocation.MyCommand.Definition
    if (-not $scriptPath) { Write-Host "Run with: powershell.exe -STA -File <script.ps1>"; Build-ScanEditUI; return }
    $psExe = (Get-Command powershell).Source
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $psExe
    $psi.Arguments = "-NoProfile -STA -ExecutionPolicy Bypass -File `"$scriptPath`" --open-ui"
    $psi.UseShellExecute = $false
    [System.Diagnostics.Process]::Start($psi) | Out-Null
    return
}

if ($args -contains '--open-ui' -or [System.Threading.Thread]::CurrentThread.ApartmentState -eq 'STA') {
    try { Build-ScanEditUI } catch { Write-Host "UI failed: $_"; Add-Content -Path "$env:TEMP\MetadataCleaner_error.log" -Value ("[{0}] ERROR: {1}" -f (Get-Date), $_) }
}