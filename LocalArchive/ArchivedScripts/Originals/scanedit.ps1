# ScanEditMediaMeta.ps1
# PowerShell WPF GUI to scan, view, export, and batch-edit media metadata.
# Run from an STA PowerShell process: powershell -STA -File .\ScanEditMediaMeta.ps1
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Drawing

# Determine script directory safely (works when run from file or interactively)
try { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition } catch { $scriptDir = $null }
if ([string]::IsNullOrWhiteSpace($scriptDir)) { $scriptDir = (Get-Location).Path }

# Attempt to load TagLib# (optional for write support)
$taglibPathCandidates = @(
    Join-Path $scriptDir 'TagLibSharp.dll'
    Join-Path $scriptDir 'taglib-sharp.dll'
)
$TagLibLoaded = $false
foreach ($p in $taglibPathCandidates) {
    if (-not [string]::IsNullOrWhiteSpace($p) -and (Test-Path $p)) {
        try { [Reflection.Assembly]::LoadFrom($p) > $null; $TagLibLoaded = $true; break } catch {}
    }
}

# Supported extensions (expand as needed)
$MediaExts = @(
    '.mp3','.flac','.m4a','.mp4','.wav','.wma','.ogg','.opus',
    '.jpg','.jpeg','.png','.gif','.tiff','.bmp',
    '.avi','.mkv','.mov','.wmv'
)

# Shell property reader (read-only)
function Get-ShellPropertiesForFile {
    param([string]$filePath)
    $props = @{}
    try {
        $shell = New-Object -ComObject Shell.Application
        $ns = $shell.Namespace((Split-Path $filePath))
        if ($ns -eq $null) { return $props }
        $file = $ns.ParseName((Split-Path $filePath -Leaf))
        for ($i = 0; $i -lt 400; $i++) {
            $label = $ns.GetDetailsOf($null, $i)
            if ([string]::IsNullOrWhiteSpace($label)) { continue }
            $value = $ns.GetDetailsOf($file, $i)
            if ($value -ne $null -and $value -ne '') { $props[$label] = $value }
        }
    } catch {}
    return $props
}

# TagLib# readers/writers (only used when TagLib is loaded)
function Read-TaglibMetadata {
    param([string]$path)
    $meta = @{}
    try {
        $tfile = [TagLib.File]::Create($path)
        if ($tfile -ne $null) {
            if ($tfile.Tag.Title) { $meta['Title'] = $tfile.Tag.Title }
            if ($tfile.Tag.Album) { $meta['Album'] = $tfile.Tag.Album }
            if ($tfile.Tag.Performers -and $tfile.Tag.Performers.Count -gt 0) { $meta['Artist'] = ($tfile.Tag.Performers -join '; ') }
            if ($tfile.Tag.Genres -and $tfile.Tag.Genres.Count -gt 0) { $meta['Genre'] = ($tfile.Tag.Genres -join '; ') }
            if ($tfile.Tag.Year -ne 0) { $meta['Year'] = $tfile.Tag.Year.ToString() }
            if ($tfile.Tag.Track -ne 0) { $meta['Track'] = $tfile.Tag.Track.ToString() }
            if ($tfile.Tag.Disc -ne 0) { $meta['Disc'] = $tfile.Tag.Disc.ToString() }
            if ($tfile.Tag.Comment) { $meta['Comment'] = $tfile.Tag.Comment }
            try {
                $props = $tfile.Properties
                if ($props -ne $null -and $props.Duration) { $meta['Duration'] = $props.Duration.ToString() }
                if ($props -ne $null -and $props.AudioBitrate -ne $null) { $meta['AudioBitrate'] = $props.AudioBitrate.ToString() }
            } catch {}
        }
    } catch {}
    return $meta
}

function Write-TaglibMetadata {
    param([string]$path, [string]$field, [string]$value)
    try {
        $tfile = [TagLib.File]::Create($path)
        switch ($field.ToLower()) {
            'title'    { $tfile.Tag.Title = $value; break }
            'album'    { $tfile.Tag.Album = $value; break }
            'artist'   { $tfile.Tag.Performers = $value -split ';' | ForEach-Object { $_.Trim() }; break }
            'genre'    { $tfile.Tag.Genres = $value -split ';' | ForEach-Object { $_.Trim() }; break }
            'year'     { [int]::TryParse($value,[ref]$n) | Out-Null; $tfile.Tag.Year = $n; break }
            'track'    { [uint]::TryParse($value,[ref]$n) | Out-Null; $tfile.Tag.Track = $n; break }
            'disc'     { [uint]::TryParse($value,[ref]$n) | Out-Null; $tfile.Tag.Disc = $n; break }
            'comment'  { $tfile.Tag.Comment = $value; break }
            default {
                try { $p = $tfile.Tag.GetType().GetProperty($field); if ($p) { $p.SetValue($tfile.Tag, $value, $null) } } catch {}
            }
        }
        $tfile.Save()
        return $true
    } catch { return $false }
}

function Delete-TaglibField {
    param([string]$path, [string]$field)
    try {
        $tfile = [TagLib.File]::Create($path)
        switch ($field.ToLower()) {
            'title'    { $tfile.Tag.Title = $null; break }
            'album'    { $tfile.Tag.Album = $null; break }
            'artist'   { $tfile.Tag.Performers = @(); break }
            'genre'    { $tfile.Tag.Genres = @(); break }
            'year'     { $tfile.Tag.Year = 0; break }
            'track'    { $tfile.Tag.Track = 0; break }
            'disc'     { $tfile.Tag.Disc = 0; break }
            'comment'  { $tfile.Tag.Comment = $null; break }
            default {}
        }
        $tfile.Save()
        return $true
    } catch { return $false }
}

# XAML as single-quoted here-string. Note escaped ampersand in Title using &amp;
$XamlString = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        Title="Media Metadata Scanner &amp; Editor" Height="720" Width="1100" WindowStartupLocation="CenterScreen">
  <Grid Margin="8">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="160"/>
    </Grid.RowDefinitions>

    <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,8">
      <Button Name="btnPick" Width="120" Margin="0,0,8,0">Pick Directory</Button>
      <TextBox Name="txtDir" Width="480" IsReadOnly="True" />
      <CheckBox Name="chkSub" Margin="8,0,8,0" VerticalAlignment="Center">Include subfolders</CheckBox>
      <Button Name="btnScan" Width="100" Margin="0,0,8,0">Scan</Button>
      <Button Name="btnExport" Width="120" Margin="0,0,8,0">Export CSV</Button>
      <Button Name="btnChooseOut" Width="140">Choose Output Folder</Button>
      <TextBlock Name="lblOut" Margin="8,4,0,0" VerticalAlignment="Center" />
    </StackPanel>

    <Grid Grid.Row="1">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="3*"/>
        <ColumnDefinition Width="2*"/>
      </Grid.ColumnDefinitions>

      <!-- left: file grid -->
      <DataGrid Name="dgFiles" Grid.Column="0" AutoGenerateColumns="True" IsReadOnly="True" Margin="0,0,8,0" />

      <!-- right: metadata fields and actions -->
      <StackPanel Grid.Column="1">
        <TextBlock FontWeight="Bold" Margin="0,0,0,6">Fields found in scanned files</TextBlock>
        <ListBox Name="lstFields" Height="220" />
        <CheckBox Name="chkShowNoMeta" Margin="0,6,0,6">Show files without metadata in separate window</CheckBox>
        <StackPanel Orientation="Horizontal" Margin="0,6,0,6">
          <ComboBox Name="cbFields" Width="240" Margin="0,0,8,0" />
        </StackPanel>
        <TextBox Name="txtNewValue" Width="360" Height="26" Margin="0,6,0,6" />
        <StackPanel Orientation="Horizontal" Margin="0,6,0,6">
          <Button Name="btnApply" Width="120" Margin="0,0,8,0">Apply to all files</Button>
          <Button Name="btnDeleteField" Width="140">Delete field from all files</Button>
        </StackPanel>
        <TextBlock Name="lblStatus" TextWrapping="Wrap" Margin="0,8,0,0" />
        <TextBlock FontStyle="Italic" Margin="0,12,0,0">TagLib# write support: <Run Name="runTaglib" /></TextBlock>
      </StackPanel>
    </Grid>

    <TextBox Name="txtLog" Grid.Row="2" IsReadOnly="True" VerticalScrollBarVisibility="Auto" TextWrapping="Wrap" />
  </Grid>
</Window>
'@

# Parse XAML using XmlReader from StringReader (avoids [xml] cast and entity problems)
try {
    $stringReader = New-Object System.IO.StringReader($XamlString)
    $xmlReader = [System.Xml.XmlReader]::Create($stringReader)
    $window = [Windows.Markup.XamlReader]::Load($xmlReader)
} catch {
    Write-Error "Failed to parse XAML UI: $_"
    return
}

# Controls
$btnPick = $window.FindName('btnPick')
$txtDir = $window.FindName('txtDir')
$chkSub = $window.FindName('chkSub')
$btnScan = $window.FindName('btnScan')
$dgFiles = $window.FindName('dgFiles')
$lstFields = $window.FindName('lstFields')
$cbFields = $window.FindName('cbFields')
$txtNewValue = $window.FindName('txtNewValue')
$btnApply = $window.FindName('btnApply')
$btnDeleteField = $window.FindName('btnDeleteField')
$chkShowNoMeta = $window.FindName('chkShowNoMeta')
$txtLog = $window.FindName('txtLog')
$btnExport = $window.FindName('btnExport')
$btnChooseOut = $window.FindName('btnChooseOut')
$lblOut = $window.FindName('lblOut')
$runTaglib = $window.FindName('runTaglib')

if ($runTaglib -ne $null) { if ($TagLibLoaded) { $runTaglib.Text = 'Available' } else { $runTaglib.Text = 'Not found (read-only scan only)' } }
$Global:ScanResults = @()
$Global:OutputFolder = $scriptDir
if ($lblOut -ne $null) { $lblOut.Text = "Output: $Global:OutputFolder" }

function Log($s) {
    $ts = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    if ($txtLog -ne $null) { $txtLog.Text = "$ts`t$s`n" + $txtLog.Text } else { Write-Host "$ts $s" }
}

function Choose-FolderDialog([string]$initial) {
    Add-Type -AssemblyName System.Windows.Forms
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.SelectedPath = $initial
    $dlg.ShowNewFolderButton = $true
    if ($dlg.ShowDialog() -eq 'OK') { return $dlg.SelectedPath } else { return $null }
}

# Wire up buttons (check for null to avoid errors if UI failed)
if ($btnPick -ne $null) {
    $btnPick.Add_Click({
        $d = Choose-FolderDialog -initial (Get-Location).Path
        if ($d) { $txtDir.Text = $d }
    })
}

if ($btnChooseOut -ne $null) {
    $btnChooseOut.Add_Click({
        $d = Choose-FolderDialog -initial $Global:OutputFolder
        if ($d) {
            $Global:OutputFolder = $d
            if ($lblOut -ne $null) { $lblOut.Text = "Output: $Global:OutputFolder" }
            Log "Output folder set to $d"
        }
    })
}

# Scan
if ($btnScan -ne $null) {
    $btnScan.Add_Click({
        $root = $txtDir.Text
        if ([string]::IsNullOrWhiteSpace($root) -or -not (Test-Path $root)) {
            [System.Windows.MessageBox]::Show("Please pick an existing directory first.","Pick Directory",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
            return
        }
        $includeSub = $false
        if ($chkSub -ne $null) { $includeSub = $chkSub.IsChecked }
        Log "Scanning $root (Include subfolders: $includeSub)"
        $Global:ScanResults = @()
        $searchScope = if ($includeSub) { Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue } else { Get-ChildItem -Path $root -File -ErrorAction SilentlyContinue }
        $files = $searchScope | Where-Object { $MediaExts -contains ([IO.Path]::GetExtension($_.FullName).ToLower()) }
        foreach ($f in $files) {
            $meta = @{}
            if ($TagLibLoaded) { $meta = Read-TaglibMetadata -path $f.FullName } else { $meta = Get-ShellPropertiesForFile -filePath $f.FullName }
            $entry = @{ FullName = $f.FullName; FileName = $f.Name; Metadata = $meta }
            $Global:ScanResults += $entry
        }
        $rows = foreach ($e in $Global:ScanResults) {
            [PSCustomObject]@{
                FileName = $e.FileName
                FullName = $e.FullName
                MetaSummary = ($e.Metadata.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)" } ) -join ' ; '
            }
        }
        if ($dgFiles -ne $null) { $dgFiles.ItemsSource = $rows }
        $fieldSet = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($e in $Global:ScanResults) { foreach ($k in $e.Metadata.Keys) { $null = $fieldSet.Add($k) } }
        $fields = $fieldSet | Sort-Object
        if ($lstFields -ne $null) { $lstFields.ItemsSource = $fields }
        if ($cbFields -ne $null) { $cbFields.ItemsSource = $fields }
        if ($fields.Count -eq 0) { Log "No metadata fields found in scanned files." } else { Log "Found fields: $($fields -join ', ')" }
        [int]$noMetaCount = ($Global:ScanResults | Where-Object { $_.Metadata.Count -eq 0 }).Count
        Log "Total files scanned: $($Global:ScanResults.Count); files without metadata: $noMetaCount"
        if ($chkShowNoMeta -ne $null -and $chkShowNoMeta.IsChecked -and $noMetaCount -gt 0) {
            $noMetaFiles = $Global:ScanResults | Where-Object { $_.Metadata.Count -eq 0 } | Select-Object -ExpandProperty FullName
            $msg = "Files without metadata:`n" + ($noMetaFiles -join "`n")
            [System.Windows.MessageBox]::Show($msg, "Files without metadata", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Information) | Out-Null
        }
    })
}

# Export CSV
if ($btnExport -ne $null) {
    $btnExport.Add_Click({
        if ($Global:ScanResults.Count -eq 0) { [System.Windows.MessageBox]::Show("No scan results to export. Please scan a folder first.","No Data",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; return }
        $fieldSet = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($e in $Global:ScanResults) { foreach ($k in $e.Metadata.Keys) { $null = $fieldSet.Add($k) } }
        $fields = $fieldSet | Sort-Object
        $csvCols = @('FileName','FullName') + $fields
        $outName = "MediaMetadataExport_{0:yyyyMMdd_HHmmss}.csv" -f (Get-Date)
        $outPath = Join-Path $Global:OutputFolder $outName
        $lines = @()
        $lines += ($csvCols -join ',')
        foreach ($e in $Global:ScanResults) {
            $vals = @()
            $vals += ('"' + ($e.FileName -replace '"','""') + '"')
            $vals += ('"' + ($e.FullName -replace '"','""') + '"')
            foreach ($col in $fields) {
                $v = if ($e.Metadata.ContainsKey($col)) { $e.Metadata[$col] } else { '' }
                $vals += ('"' + ($v -replace '"','""') + '"')
            }
            $lines += ($vals -join ',')
        }
        try {
            $lines | Out-File -FilePath $outPath -Encoding UTF8
            Log "Exported CSV to $outPath"
            [System.Windows.MessageBox]::Show("Export complete:`n$outPath","Export Complete",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
        } catch {
            Log "Failed to write CSV: $_"
            [System.Windows.MessageBox]::Show("Failed to write CSV. Check permissions.","Export Failed",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
        }
    })
}

# Apply and Delete (require TagLib#)
if ($btnApply -ne $null) {
    $btnApply.Add_Click({
        if (-not $TagLibLoaded) {
            [System.Windows.MessageBox]::Show("Editing requires TagLib# (TagLibSharp.dll) in script folder. Editing disabled.","Edit Not Available",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
            return
        }
        $field = $cbFields.SelectedItem
        if (-not $field) { [System.Windows.MessageBox]::Show("Select a field from the Fields list first.","Select Field",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; return }
        $value = $txtNewValue.Text
        if ($null -eq $value) { $value = '' }
        $confirm = [System.Windows.MessageBox]::Show("This will write the value to the field '$field' for all scanned files. Continue?","Confirm Write",[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Question)
        if ($confirm -ne 'Yes') { return }
        $success = 0; $total = 0
        foreach ($e in $Global:ScanResults) {
            $total++
            $res = Write-TaglibMetadata -path $e.FullName -field $field -value $value
            if ($res) { $success++ } else { Log "Failed to write $field on $($e.FullName)" }
        }
        Log "Write complete. Success: $success / $total"
        [System.Windows.MessageBox]::Show("Write complete. Success: $success / $total","Write Complete",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
    })
}

if ($btnDeleteField -ne $null) {
    $btnDeleteField.Add_Click({
        if (-not $TagLibLoaded) {
            [System.Windows.MessageBox]::Show("Deleting requires TagLib# (TagLibSharp.dll) in script folder. Deleting disabled.","Delete Not Available",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
            return
        }
        $field = $cbFields.SelectedItem
        if (-not $field) { [System.Windows.MessageBox]::Show("Select a field from the Fields list first.","Select Field",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; return }
        $confirm = [System.Windows.MessageBox]::Show("This will attempt to delete the field '$field' from all scanned files. This cannot be undone. Continue?","Confirm Delete",[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Warning)
        if ($confirm -ne 'Yes') { return }
        $success = 0; $total = 0
        foreach ($e in $Global:ScanResults) {
            $total++
            $res = Delete-TaglibField -path $e.FullName -field $field
            if ($res) { $success++ } else { Log "Failed to delete $field on $($e.FullName)" }
        }
        Log "Delete complete. Success: $success / $total"
        [System.Windows.MessageBox]::Show("Delete complete. Success: $success / $total","Delete Complete",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
    })
}

# Double-click to show metadata details
if ($dgFiles -ne $null) {
    $dgFiles.MouseDoubleClick.Add({
        $sel = $dgFiles.SelectedItem
        if ($sel) {
            $full = $sel.FullName
            $metaEntry = $Global:ScanResults | Where-Object { $_.FullName -eq $full }
            if ($metaEntry) {
                $pairs = $metaEntry.Metadata.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)" }
                $body = "File:`n$($metaEntry.FullName)`n`nMetadata entries:`n" + ($pairs -join "`n")
                [System.Windows.MessageBox]::Show($body, "File metadata", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Information) | Out-Null
            }
        }
    })
}

# Show window
$window.ShowDialog() | Out-Null