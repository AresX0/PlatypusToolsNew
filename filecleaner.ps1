# Rename-WithMetadata-Tags-Final.ps1
param([switch]$NonInteractive)
. "$PSScriptRoot\Tools\NonInteractive.ps1"
Set-NonInteractive -Enable:$NonInteractive
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
Add-Type -AssemblyName System.Windows.Forms
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

# -----------------------
# robust script-root detection and tool paths
# -----------------------
try {
    if ($PSScriptRoot) {
        $ScriptDir = $PSScriptRoot
    } elseif ($MyInvocation -and $MyInvocation.MyCommand -and $MyInvocation.MyCommand.Definition) {
        $maybe = $MyInvocation.MyCommand.Definition
        if ([System.IO.Path]::IsPathRooted($maybe) -and (Test-Path $maybe)) {
            $ScriptDir = Split-Path -Parent $maybe
        } else {
            $ScriptDir = (Get-Location).ProviderPath
        }
    } else {
        $ScriptDir = (Get-Location).ProviderPath
    }
} catch {
    $ScriptDir = (Get-Location).ProviderPath
}

$ToolsDir = Join-Path $ScriptDir 'tools'
if (-not (Test-Path $ToolsDir)) {
    New-Item -Path $ToolsDir -ItemType Directory -Force | Out-Null
}

# download targets (edit if you prefer other providers)
$ExifToolZipUrl = 'https://exiftool.org/exiftool-12.59.zip'
$ExifToolExeLocal = Join-Path $ToolsDir 'exiftool.exe'
$FFmpegZipUrl = 'https://www.gyan.dev/ffmpeg/builds/ffmpeg-release-essentials.zip'
$FFProbeExeLocal = Join-Path $ToolsDir 'ffprobe.exe'

# -----------------------
# Preset extension groups
# -----------------------
$PresetGroups = @{
    Video = @('.mp4','.mkv','.avi','.mov','.wmv','.flv','.webm','.mpeg','.mpg','.m4v')
    Pictures = @('.jpg','.jpeg','.png','.gif','.bmp','.tiff','.webp')
    Documents = @('.pdf','.docx','.doc','.xlsx','.xls','.pptx','.ppt','.txt','.rtf','.odt')
    Audio = @('.mp3','.flac','.aac','.wav','.m4a','.ogg','.wma')
    Archives = @('.zip','.rar','.7z','.tar','.gz','.bz2','.xz')
}

# -----------------------
# XAML UI
# -----------------------
[xml]$xaml = @"
<Window xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
        xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
        Title='Batch File Renamer with Metadata' Height='980' Width='1220' WindowStartupLocation='CenterScreen' MinHeight='760' MinWidth='980'>
  <Grid Margin='10'>
    <Grid.RowDefinitions>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='*'/>
      <RowDefinition Height='Auto'/>
      <RowDefinition Height='Auto'/>
    </Grid.RowDefinitions>

    <StackPanel Orientation='Horizontal' Grid.Row='0' Margin='0,0,0,8'>
      <Label Content='Folder:' VerticalAlignment='Center'/>
      <TextBox Name='TxtFolder' Width='820' Margin='6,0,6,0' IsReadOnly='False'/>
      <Button Name='BtnBrowse' Width='90' Content='Browse'/>
      <CheckBox Name='ChkRecurse' Content='Include Subfolders' Margin='8,0,0,0' VerticalAlignment='Center'/>
    </StackPanel>

    <Border Grid.Row='1' BorderBrush='#DDD' BorderThickness='1' Padding='8' Margin='0,0,0,8' CornerRadius='4'>
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width='*'/>
          <ColumnDefinition Width='420'/>
        </Grid.ColumnDefinitions>

        <StackPanel Grid.Column='0' Orientation='Vertical'>
          <StackPanel Orientation='Horizontal' Margin='0,0,0,6'>
            <CheckBox Name='ChkChangePrefix' VerticalAlignment='Center'/>
            <Label Content='Change Prefix (Old -> New). Old required when checked; New optional' VerticalAlignment='Center' Margin='6,0,0,0'/>
          </StackPanel>

          <StackPanel Orientation='Horizontal' Margin='0,0,0,6'>
            <Label Content='Old Prefix' Width='80' VerticalAlignment='Center'/>
            <TextBox Name='TxtOldPrefix' Width='300' IsEnabled='False' Margin='6,0,12,0'/>
            <Label Content='New Prefix' Width='80' VerticalAlignment='Center'/>
            <TextBox Name='TxtNewPrefix' Width='300' IsEnabled='False' Margin='6,0,0,0'/>
            <CheckBox Name='ChkRequireExistingPrefix' Content='Only files that already have Old Prefix' Margin='12,0,0,0' VerticalAlignment='Center' IsEnabled='False'/>
          </StackPanel>

          <StackPanel Orientation='Horizontal' Margin='0,0,0,6'>
            <CheckBox Name='ChkAddSeason' VerticalAlignment='Center'/>
            <Label Content='Add Season' VerticalAlignment='Center' Margin='6,0,0,0'/>
            <TextBox Name='TxtSeason' Width='60' Text='1' Margin='6,0,0,0'/>
            <Label Content='Season digits' VerticalAlignment='Center' Margin='8,0,0,0'/>
            <ComboBox Name='CmbSeasonDigits' Width='60' SelectedIndex='1' Margin='6,0,0,0'>
              <ComboBoxItem>1</ComboBoxItem><ComboBoxItem>2</ComboBoxItem><ComboBoxItem>3</ComboBoxItem>
            </ComboBox>
            <CheckBox Name='ChkSeasonBeforeEpisode' Content='Use S##E### format (no separator)' Margin='12,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>

          <StackPanel Orientation='Horizontal' Margin='0,0,0,6'>
            <CheckBox Name='ChkAddEpisode' VerticalAlignment='Center'/>
            <Label Content='Add/Update Episode' VerticalAlignment='Center' Margin='6,0,0,0'/>
            <Label Content='Start' VerticalAlignment='Center' Margin='12,0,0,0'/>
            <TextBox Name='TxtStart' Width='60' Text='1' Margin='6,0,0,0'/>
            <Label Content='Episode digits' VerticalAlignment='Center' Margin='10,0,0,0'/>
            <ComboBox Name='CmbEpisodeDigits' Width='60' SelectedIndex='2' Margin='6,0,0,0'>
              <ComboBoxItem>1</ComboBoxItem><ComboBoxItem>2</ComboBoxItem><ComboBoxItem>3</ComboBoxItem><ComboBoxItem>4</ComboBoxItem>
            </ComboBox>
            <CheckBox Name='ChkRenumberAll' Content='Renumber All Alphabetically' Margin='12,0,0,0' VerticalAlignment='Center'/>
          </StackPanel>

          <StackPanel Orientation='Horizontal' Margin='0,6,0,6'>
            <CheckBox Name='ChkDryRun' Content='Dry-run (no files changed)' VerticalAlignment='Center'/>
            <Button Name='BtnScan' Content='Scan / Preview' Width='140' Margin='12,0,0,0'/>
            <Button Name='BtnScanMeta' Content='Scan Metadata' Width='140' Margin='8,0,0,0'/>
            <Button Name='BtnExportCsv' Content='Export Preview CSV' Width='160' Margin='8,0,0,0' IsEnabled='False'/>
          </StackPanel>

          <GroupBox Header='Clean filename tokens' Margin='0,6,0,0'>
            <StackPanel Orientation='Vertical' Margin='6'>
              <TextBlock Text='Preset tokens to remove (check to remove):' FontWeight='Bold' Margin='0,0,6,6'/>
              <WrapPanel>
                <CheckBox Name='Chk720p' Content='_720p / -720p / 720p' Margin='4'/>
                <CheckBox Name='Chk1080p' Content='_1080p / -1080p / 1080p' Margin='4'/>
                <CheckBox Name='Chk4k' Content='_4k / -4k / 4k' Margin='4'/>
                <CheckBox Name='Chk720' Content='_720 / -720 / 720' Margin='4'/>
                <CheckBox Name='Chk1080' Content='_1080 / -1080 / 1080' Margin='4'/>
                <CheckBox Name='ChkHD' Content='_hd / -hd / hd' Margin='4'/>
              </WrapPanel>
              <TextBlock Text='Custom tokens to remove (comma-separated, e.g. x264,WEBRip):' Margin='0,6,0,0'/>
              <TextBox Name='TxtCustomClean' Width='820' Height='24' Margin='0,4,0,0'/>
              <StackPanel Orientation='Horizontal' Margin='0,6,0,0'>
                <CheckBox Name='ChkCleanOnlyIfOldPrefix' Content='Only clean files matching Old Prefix' Margin='0,0,12,0' />
                <TextBlock Text='(requires Require Existing Prefix)' VerticalAlignment='Center' Foreground='Gray'/>
              </StackPanel>
            </StackPanel>
          </GroupBox>
        </StackPanel>

        <StackPanel Grid.Column='1' Orientation='Vertical' HorizontalAlignment='Right'>
          <TextBlock Text='File type selection' FontWeight='Bold' Margin='0,0,0,8'/>
          <WrapPanel>
            <CheckBox Name='ChkVideo' Content='Video' Margin='4'/>
            <CheckBox Name='ChkPictures' Content='Pictures' Margin='4'/>
            <CheckBox Name='ChkDocuments' Content='Documents' Margin='4'/>
            <CheckBox Name='ChkAudio' Content='Audio' Margin='4'/>
            <CheckBox Name='ChkArchives' Content='Archives' Margin='4'/>
          </WrapPanel>
          <TextBlock Text='Custom extensions (comma-separated)' Margin='0,8,0,4'/>
          <TextBox Name='TxtCustomExt' Width='380' Height='24' Margin='0,0,0,8'/>
          <TextBlock Text='Metadata options' FontWeight='Bold' Margin='0,6,0,8'/>
          <CheckBox Name='ChkUseAudioMetadata' Content='Use audio metadata (artist/album/title) in proposed name' Margin='0,0,6,0'/>
          <CheckBox Name='ChkUseVideoMetadata' Content='Use video metadata (show/title/season/episode) in proposed name' Margin='0,0,6,0'/>
          <TextBlock Text='Selection / Actions' FontWeight='Bold' Margin='0,6,0,8'/>
          <Button Name='BtnSelectAll' Content='Select All' Width='180' Margin='0,0,6,6'/>
          <Button Name='BtnSelectNone' Content='Select None' Width='180' Margin='0,0,0,6'/>
          <Button Name='BtnApply' Content='Apply Changes' Width='180' Margin='0,6,0,6' IsEnabled='False'/>
          <Button Name='BtnUndo' Content='Undo Last' Width='180' IsEnabled='False' Margin='0,0,0,6'/>
          <Button Name='BtnReset' Content='Reset All Options' Width='180' Margin='0,0,0,6'/>
          <Button Name='BtnExit' Content='Exit' Width='180' Margin='0,0,0,6'/>
          <TextBlock Text='Tools detected:' Margin='0,8,0,0'/>
          <TextBlock Name='TxtTools' Foreground='DarkBlue' Margin='0,2,0,0'/>
        </StackPanel>
      </Grid>
    </Border>

    <DataGrid Grid.Row='2' Name='DgPreview' AutoGenerateColumns='False' CanUserAddRows='False' SelectionMode='Extended' IsReadOnly='False'>
      <DataGrid.Columns>
        <DataGridCheckBoxColumn Header='Apply' Binding='{Binding Apply, Mode=TwoWay}' Width='70'/>
        <DataGridTextColumn Header='Original Name' Binding='{Binding Original}' IsReadOnly='True' Width='*'/>
        <DataGridTextColumn Header='Proposed Name' Binding='{Binding Proposed}' IsReadOnly='True' Width='*'/>
        <DataGridTextColumn Header='Status' Binding='{Binding Status}' IsReadOnly='True' Width='260'/>
        <DataGridTextColumn Header='Meta' Binding='{Binding MetaSummary}' IsReadOnly='True' Width='240'/>
      </DataGrid.Columns>
    </DataGrid>

    <DockPanel Grid.Row='3' LastChildFill='True' Margin='0,8,0,0'>
      <StatusBar DockPanel.Dock='Bottom'>
        <StatusBarItem>
          <TextBlock Name='TxtStatusBar' Text='Ready' />
        </StatusBarItem>
      </StatusBar>
    </DockPanel>

    <TextBlock Grid.Row='4' Foreground='Gray' Text='Tip: Scan Metadata reads tags if exiftool or ffprobe available. Use Dry-run first.' Margin='0,8,0,0'/>
  </Grid>
</Window>
"@

# -----------------------
# Load XAML and controls
# -----------------------
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

$TxtFolder = $window.FindName("TxtFolder"); $BtnBrowse = $window.FindName("BtnBrowse"); $ChkRecurse = $window.FindName("ChkRecurse")
$ChkChangePrefix = $window.FindName("ChkChangePrefix"); $TxtOldPrefix = $window.FindName("TxtOldPrefix"); $TxtNewPrefix = $window.FindName("TxtNewPrefix")
$ChkRequireExistingPrefix = $window.FindName("ChkRequireExistingPrefix")
$ChkAddSeason = $window.FindName("ChkAddSeason"); $TxtSeason = $window.FindName("TxtSeason"); $CmbSeasonDigits = $window.FindName("CmbSeasonDigits"); $ChkSeasonBeforeEpisode = $window.FindName("ChkSeasonBeforeEpisode")
$ChkAddEpisode = $window.FindName("ChkAddEpisode"); $TxtStart = $window.FindName("TxtStart"); $CmbEpisodeDigits = $window.FindName("CmbEpisodeDigits"); $ChkRenumberAll = $window.FindName("ChkRenumberAll")
$ChkDryRun = $window.FindName("ChkDryRun"); $BtnScan = $window.FindName("BtnScan"); $BtnScanMeta = $window.FindName("BtnScanMeta"); $BtnExportCsv = $window.FindName("BtnExportCsv")
$BtnSelectAll = $window.FindName("BtnSelectAll"); $BtnSelectNone = $window.FindName("BtnSelectNone"); $BtnApply = $window.FindName("BtnApply"); $BtnUndo = $window.FindName("BtnUndo")
$BtnReset = $window.FindName("BtnReset"); $BtnExit = $window.FindName("BtnExit")
$DgPreview = $window.FindName("DgPreview"); $TxtStatusBar = $window.FindName("TxtStatusBar"); $TxtTools = $window.FindName("TxtTools")

$Chk720p = $window.FindName("Chk720p"); $Chk1080p = $window.FindName("Chk1080p"); $Chk4k = $window.FindName("Chk4k")
$Chk720 = $window.FindName("Chk720"); $Chk1080 = $window.FindName("Chk1080"); $ChkHD = $window.FindName("ChkHD")
$TxtCustomClean = $window.FindName("TxtCustomClean"); $ChkCleanOnlyIfOldPrefix = $window.FindName("ChkCleanOnlyIfOldPrefix")

$ChkVideo = $window.FindName("ChkVideo"); $ChkPictures = $window.FindName("ChkPictures"); $ChkDocuments = $window.FindName("ChkDocuments")
$ChkAudio = $window.FindName("ChkAudio"); $ChkArchives = $window.FindName("ChkArchives"); $TxtCustomExt = $window.FindName("TxtCustomExt")
$ChkUseAudioMetadata = $window.FindName("ChkUseAudioMetadata"); $ChkUseVideoMetadata = $window.FindName("ChkUseVideoMetadata")

# -----------------------
# Tool download / ensure helpers
# -----------------------
function Set-Status($text) { $TxtStatusBar.Dispatcher.Invoke([action]{ $TxtStatusBar.Text = $text }) }

function Download-File($url, $dest) {
    try {
        Set-Status "Downloading $url ..."
        Invoke-WebRequest -Uri $url -OutFile $dest -UseBasicParsing -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Extract-Zip($zipPath, $outDir) {
    try {
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [System.IO.Compression.ZipFile]::ExtractToDirectory($zipPath, $outDir)
        return $true
    } catch {
        return $false
    }
}

function Ensure-ExifTool {
    if (Test-Path $ExifToolExeLocal) { return $ExifToolExeLocal }
    $cmd = Get-Command exiftool -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $tmpZip = Join-Path $env:TEMP ('exiftool_' + [System.Guid]::NewGuid().ToString() + '.zip')
    if (-not (Download-File -url $ExifToolZipUrl -dest $tmpZip)) { return $null }
    $tmpDir = Join-Path $env:TEMP ('exiftool_' + [System.Guid]::NewGuid().ToString())
    New-Item -Path $tmpDir -ItemType Directory | Out-Null
    if (-not (Extract-Zip -zipPath $tmpZip -outDir $tmpDir)) { return $null }
    $found = Get-ChildItem -Path $tmpDir -Recurse -Filter 'exiftool*.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        Copy-Item -Path $found.FullName -Destination $ExifToolExeLocal -Force
        Remove-Item -Path $tmpZip -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
        return $ExifToolExeLocal
    } else { return $null }
}

function Ensure-FFProbe {
    if (Test-Path $FFProbeExeLocal) { return $FFProbeExeLocal }
    $cmd = Get-Command ffprobe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $tmpZip = Join-Path $env:TEMP ('ffmpeg_' + [System.Guid]::NewGuid().ToString() + '.zip')
    if (-not (Download-File -url $FFmpegZipUrl -dest $tmpZip)) { return $null }
    $tmpDir = Join-Path $env:TEMP ('ffmpeg_' + [System.Guid]::NewGuid().ToString())
    New-Item -Path $tmpDir -ItemType Directory | Out-Null
    if (-not (Extract-Zip -zipPath $tmpZip -outDir $tmpDir)) { return $null }
    $found = Get-ChildItem -Path $tmpDir -Recurse -Filter 'ffprobe.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) {
        Copy-Item -Path $found.FullName -Destination $FFProbeExeLocal -Force
        Remove-Item -Path $tmpZip -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
        return $FFProbeExeLocal
    } else { return $null }
}

# Attempt to ensure tools (non-blocking)
$exiftoolPath = Ensure-ExifTool
$ffprobePath = Ensure-FFProbe

$toolsDetected = @()
if ($exiftoolPath) { $toolsDetected += "exiftool ($exiftoolPath)" }
if ($ffprobePath) { $toolsDetected += "ffprobe ($ffprobePath)" }
if ($toolsDetected.Count -eq 0) { $TxtTools.Text = "none (exiftool or ffprobe recommended)" } else { $TxtTools.Text = $toolsDetected -join ', ' }

# -----------------------
# Filesystem / filename helpers
# -----------------------
function Choose-Folder {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select folder to scan for files"
    if ($dlg.ShowDialog() -eq 'OK') { return $dlg.SelectedPath } else { return $null }
}

function Get-ExtensionsFromUI {
    $exts = @()
    if ($ChkVideo.IsChecked) { $exts += $PresetGroups.Video }
    if ($ChkPictures.IsChecked) { $exts += $PresetGroups.Pictures }
    if ($ChkDocuments.IsChecked) { $exts += $PresetGroups.Documents }
    if ($ChkAudio.IsChecked) { $exts += $PresetGroups.Audio }
    if ($ChkArchives.IsChecked) { $exts += $PresetGroups.Archives }
    if ($TxtCustomExt.Text.Trim() -ne '') {
        $custom = $TxtCustomExt.Text -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' } | ForEach-Object {
            if ($_ -notmatch '^\.') { '.' + $_ } else { $_ }
        }
        $exts += $custom
    }
    return ($exts | ForEach-Object { $_.ToLower() } | Select-Object -Unique)
}

function Get-FilesByExtensions {
    param($folder, $recurse, $exts)
    if ($recurse) {
        $items = Get-ChildItem -Path $folder -File -Recurse -ErrorAction SilentlyContinue
    } else {
        $items = Get-ChildItem -Path $folder -File -ErrorAction SilentlyContinue
    }
    return $items | Where-Object { $exts -contains $_.Extension.ToLower() }
}

function Pad-Number { param($num,$pad) return $num.ToString("D$pad") }
function Pad-Episode { param($num,$pad) return ("E" + (Pad-Number -num $num -pad $pad)) }
function Pad-Season { param($num,$pad) return ("S" + (Pad-Number -num $num -pad $pad)) }
function Get-DigitsFromCombo($combo) { return [int]($combo.SelectedItem.Content) }

# -----------------------
# Cleaning / normalization
# -----------------------
function Get-CleanTokens {
    param($use720p,$use1080p,$use4k,$use720,$use1080,$useHD,$custom)
    $tokens = @()
    if ($use720p) { $tokens += '720p' }
    if ($use1080p) { $tokens += '1080p' }
    if ($use4k) { $tokens += '4k' }
    if ($use720) { $tokens += '720' }
    if ($use1080) { $tokens += '1080' }
    if ($useHD) { $tokens += 'hd' }
    if ($custom) {
        $custom -split ',' | ForEach-Object { $t = $_.Trim(); if ($t) { $tokens += $t } }
    }
    return ($tokens | Select-Object -Unique)
}

function Clean-FilenameTokens {
    param([string]$base, [string[]]$tokens)
    if (-not $base) { return $base }
    $s = $base
    if ($tokens -and $tokens.Count -gt 0) {
        foreach ($t in $tokens) {
            $escaped = [regex]::Escape($t)
            $patterns = @(
                "(?i)([_\.\-\s])$escaped(?=(?:[_\.\-\s]|$))",
                "(?i)$escaped(?=(?:[_\.\-\s]|$))",
                "(?i)(?<=^)$escaped(?=(?:[_\.\-\s]|$))",
                "(?i)\($escaped\)",
                "(?i)\[$escaped\]"
            )
            foreach ($p in $patterns) { $s = [regex]::Replace($s, $p, ' ') }
        }
    }
    $s = $s -replace '\.{2,}', '.'
    $s = $s -replace '\s{2,}', ' '
    $s = $s -replace '_{2,}', '_'
    $s = $s -replace '-{2,}', '-'
    $s = [regex]::Replace($s, '([ _\-\.\s]){2,}', { param($m) $m.Value[0] })
    $s = $s.Trim(' ','_','-','.')
    return $s.Trim()
}

function Build-SortKeyForTitle {
    param($nameNoExt, $oldPrefix)
    $n = $nameNoExt
    if ($oldPrefix -and $oldPrefix -ne "") {
        $n = $n -replace ("^" + [regex]::Escape($oldPrefix) + "([_\-\. ]?)"), ''
    }
    $n = $n -replace '(?i)S\d{1,3}E\d{1,4}',''
    $n = $n -replace '(?i)S\d{1,3}\s*-\s*E\d{1,4}',''
    $n = $n -replace '(?i)E\d{1,4}',''
    return $n.Trim()
}

function Normalize-Proposed {
    param(
        [string]$coreBase,
        [string]$oldPrefix,
        [string]$newPrefix,
        [string]$seasonToken,
        [bool]$seasonBeforeEpisode,
        [string]$episodeToken,
        [string[]]$cleanTokens,
        [bool]$performClean
    )
    $s = $coreBase.Trim()
    if ($performClean -and $cleanTokens -and $cleanTokens.Count -gt 0) {
        $s = Clean-FilenameTokens -base $s -tokens $cleanTokens
    } else {
        $s = $s -replace '\s{2,}', ' '
        $s = $s -replace '_{2,}', '_'
        $s = $s -replace '-{2,}', '-'
        $s = [regex]::Replace($s, '([ _\-\.\s]){2,}', { param($m) $m.Value[0] })
        $s = $s.Trim(' ','_','-','.')
    }
    if ($oldPrefix -and $oldPrefix.Trim() -ne '') {
        $ep = [regex]::Escape($oldPrefix.Trim())
        $s = $s -replace "(?i)\b$ep\b",''
        $s = $s -replace "(?i)$ep(?=[_\-\. ]|$)",''
    }
    if ($newPrefix -and $newPrefix.Trim() -ne '') {
        $np = [regex]::Escape($newPrefix.Trim())
        $s = $s -replace "(?i)\b$np\b",''
        $s = $s -replace "(?i)$np(?=[_\-\. ]|$)",''
    }
    $s = $s.Trim(' ','_','-','.')
    $s = $s -replace '(?i)S\d{1,3}E\d{1,4}',''
    $s = $s -replace '(?i)S\d{1,3}\s*-\s*E\d{1,4}',''
    $s = $s -replace '(?i)E\d{1,4}',''
    $s = $s -replace '(?i)S\d{1,3}',''
    $s = $s.Trim()
    $combined = ''
    if ($seasonToken -and $episodeToken) {
        if ($seasonBeforeEpisode) { $combined = "$seasonToken$episodeToken" } else { $combined = "$seasonToken $episodeToken" }
    } elseif ($seasonToken) { $combined = $seasonToken } elseif ($episodeToken) { $combined = $episodeToken }
    $finalPrefix = ''
    if ($newPrefix -and $newPrefix.Trim() -ne '') { $finalPrefix = $newPrefix.Trim().Trim(' ','_','-','.') }
    elseif ($oldPrefix -and $oldPrefix.Trim() -ne '') { $finalPrefix = $oldPrefix.Trim().Trim(' ','_','-','.') }
    $parts = @()
    if ($finalPrefix -ne '') { $parts += $finalPrefix }
    if ($combined -ne '') { $parts += $combined }
    if ($s -ne '') { $parts += $s }
    $final = ($parts -join '-').Trim()
    $final = $final -replace '_{2,}', '_'
    $final = $final -replace '-{2,}', '-'
    $final = [regex]::Replace($final, '([ _\-\.\s]){2,}', { param($m) $m.Value[0] })
    $final = $final.Trim(' ','_','-','.')
    return $final
}

# -----------------------
# Metadata helpers
# -----------------------
function Get-MetaValue($meta, [string[]]$keys) {
    foreach ($k in $keys) {
        if ($null -ne $meta -and $meta.ContainsKey($k)) { return $meta[$k] }
    }
    return $null
}

function Read-Metadata($fullPath) {
    $meta = @{}
    $exifLocal = if (Test-Path $ExifToolExeLocal) { $ExifToolExeLocal } else {
        $cmd = Get-Command exiftool -ErrorAction SilentlyContinue
        if ($cmd) { $cmd.Source } else { $null }
    }
    if ($exifLocal) {
        try {
            $out = & $exifLocal -json -G -charset UTF8 -- "$fullPath" 2>$null
            if ($out) {
                $j = $out | ConvertFrom-Json
                if ($j -and $j.Count -gt 0) {
                    $props = $j[0].PSObject.Properties | Where-Object { $_.Name -ne 'SourceFile' }
                    foreach ($p in $props) { $meta[$p.Name] = $p.Value }
                }
            }
        } catch {}
        return $meta
    }

    $ffprobeLocal = if (Test-Path $FFProbeExeLocal) { $FFProbeExeLocal } else {
        $cmd = Get-Command ffprobe -ErrorAction SilentlyContinue
        if ($cmd) { $cmd.Source } else { $null }
    }
    if ($ffprobeLocal) {
        try {
            $out = & $ffprobeLocal -v quiet -print_format json -show_format -show_streams -- "$fullPath" 2>$null
            if ($out) {
                $j = $out | ConvertFrom-Json -ErrorAction SilentlyContinue
                if ($j.format.tags) {
                    foreach ($k in $j.format.tags.PSObject.Properties) { $meta[$k.Name] = $k.Value }
                }
                if ($j.streams) {
                    foreach ($s in $j.streams) {
                        if ($s.tags) {
                            foreach ($k in $s.tags.PSObject.Properties) { $meta[$k.Name] = $k.Value }
                        }
                    }
                }
            }
        } catch {}
    }
    return $meta
}

function Write-Metadata($fullPath, $tags) {
    if (-not $tags -or $tags.Count -eq 0) { return $false }
    $exifLocal = if (Test-Path $ExifToolExeLocal) { $ExifToolExeLocal } else {
        $cmd = Get-Command exiftool -ErrorAction SilentlyContinue
        if ($cmd) { $cmd.Source } else { $null }
    }
    if ($exifLocal) {
        try {
            $args = @()
            foreach ($k in $tags.Keys) {
                $v = $tags[$k].ToString()
                $args += ("-{0}={1}" -f $k, $v)
            }
            $args += '--'
            $args += "$fullPath"
            & $exifLocal @args | Out-Null
            try { Remove-Item ($fullPath + '_original') -ErrorAction SilentlyContinue } catch {}
            return $true
        } catch { return $false }
    }

    $cmd = Get-Command ffprobe -ErrorAction SilentlyContinue
    $ffprobeLocal = if ($cmd) { $cmd.Source } else { if (Test-Path $FFProbeExeLocal) { $FFProbeExeLocal } else { $null } }
    $cmd = Get-Command ffmpeg -ErrorAction SilentlyContinue
    $ffmpegLocal = if ($cmd) { $cmd.Source } else { $null }

    if ($ffmpegLocal) {
        try {
            $tmp = [System.IO.Path]::GetTempFileName()
            Remove-Item $tmp -ErrorAction SilentlyContinue
            $tmp += [System.IO.Path]::GetExtension($fullPath)
            $args = @('-y','-i', $fullPath)
            foreach ($k in $tags.Keys) {
                $v = $tags[$k].ToString()
                $args += ('-metadata'); $args += ("{0}={1}" -f $k, $v)
            }
            $args += $tmp
            & $ffmpegLocal @args 2>$null
            if (Test-Path $tmp) { Move-Item -LiteralPath $tmp -Destination $fullPath -Force; return $true }
            return $false
        } catch { return $false }
    }
    return $false
}

# -----------------------
# State and UI wiring (scan, preview, apply, undo)
# -----------------------
$PreviewList = New-Object System.Collections.ObjectModel.ObservableCollection[psobject]

$BtnBrowse.Add_Click({
    $folder = Choose-Folder
    if ($folder) { $TxtFolder.Text = $folder; Set-Status "Folder: $folder" }
})

$ChkChangePrefix.add_Checked({
    $TxtOldPrefix.IsEnabled = $true
    $TxtNewPrefix.IsEnabled = $true
    $ChkRequireExistingPrefix.IsEnabled = $true
})
$ChkChangePrefix.add_Unchecked({
    $TxtOldPrefix.IsEnabled = $false
    $TxtNewPrefix.IsEnabled = $false
    $ChkRequireExistingPrefix.IsEnabled = $false
})

$ChkCleanOnlyIfOldPrefix.add_Checked({
    if (-not $ChkChangePrefix.IsChecked) {
        $resp = [System.Windows.MessageBox]::Show("This option requires Change Prefix. Enable now?","Enable Change Prefix",[System.Windows.MessageBoxButton]::YesNo,[System.Windows.MessageBoxImage]::Question)
        if ($resp -eq [System.Windows.MessageBoxResult]::Yes) {
            $ChkChangePrefix.IsChecked = $true
            $TxtOldPrefix.IsEnabled = $true
            $TxtNewPrefix.IsEnabled = $true
            $ChkRequireExistingPrefix.IsEnabled = $true
            if ([string]::IsNullOrWhiteSpace($TxtOldPrefix.Text)) {
                try { $typed = Prompt-Or-Throw "Enter Old Prefix (leave blank to cancel)" } catch { $ChkCleanOnlyIfOldPrefix.IsChecked = $false; Set-Status "Cancelled (non-interactive)"; return }
                if ([string]::IsNullOrWhiteSpace($typed)) { $ChkCleanOnlyIfOldPrefix.IsChecked = $false; Set-Status "Cancelled"; return }
                $TxtOldPrefix.Text = $typed.Trim()
            }
        } else { $ChkCleanOnlyIfOldPrefix.IsChecked = $false; Set-Status "Cancelled"; return }
    } else {
        if ([string]::IsNullOrWhiteSpace($TxtOldPrefix.Text)) {
            $typed = Read-Host "Enter Old Prefix (leave blank to cancel)"
            if ([string]::IsNullOrWhiteSpace($typed)) { $ChkCleanOnlyIfOldPrefix.IsChecked = $false; Set-Status "Cancelled"; return }
            $TxtOldPrefix.Text = $typed.Trim()
        }
    }
})

$BtnReset.Add_Click({
    $TxtFolder.Text = ""
    $ChkRecurse.IsChecked = $false
    $ChkChangePrefix.IsChecked = $false
    $TxtOldPrefix.Text = ""
    $TxtNewPrefix.Text = ""
    $ChkRequireExistingPrefix.IsChecked = $false
    $ChkAddSeason.IsChecked = $false
    $TxtSeason.Text = "1"
    $CmbSeasonDigits.SelectedIndex = 1
    $ChkSeasonBeforeEpisode.IsChecked = $false
    $ChkAddEpisode.IsChecked = $false
    $TxtStart.Text = "1"
    $CmbEpisodeDigits.SelectedIndex = 2
    $ChkRenumberAll.IsChecked = $false
    $ChkDryRun.IsChecked = $false
    $Chk720p.IsChecked = $false
    $Chk1080p.IsChecked = $false
    $Chk4k.IsChecked = $false
    $Chk720.IsChecked = $false
    $Chk1080.IsChecked = $false
    $ChkHD.IsChecked = $false
    $TxtCustomClean.Text = ""
    $ChkCleanOnlyIfOldPrefix.IsChecked = $false
    $ChkVideo.IsChecked = $false
    $ChkPictures.IsChecked = $false
    $ChkDocuments.IsChecked = $false
    $ChkAudio.IsChecked = $false
    $ChkArchives.IsChecked = $false
    $TxtCustomExt.Text = ""
    $ChkUseAudioMetadata.IsChecked = $false
    $ChkUseVideoMetadata.IsChecked = $false
    $PreviewList.Clear()
    $DgPreview.ItemsSource = $null
    $BtnExportCsv.IsEnabled = $false
    $BtnApply.IsEnabled = $false
    Set-Status "Options reset to defaults."
})

$BtnExit.Add_Click({ $window.Close() })

# Metadata scan
$BtnScanMeta.Add_Click({
    Set-Status "Scanning metadata..."
    $folder = $TxtFolder.Text.Trim()
    if (-not $folder -or -not (Test-Path $folder)) { [System.Windows.MessageBox]::Show("Choose a valid folder.","Folder required",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; Set-Status "No folder."; return }

    $exts = Get-ExtensionsFromUI
    if (-not $exts -or $exts.Count -eq 0) { [System.Windows.MessageBox]::Show("Select file types or add custom extensions.","Extensions required",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; Set-Status "No extensions selected."; return }

    try {
        $files = Get-FilesByExtensions -folder $folder -recurse $ChkRecurse.IsChecked -exts $exts
        if (-not $files -or $files.Count -eq 0) { [System.Windows.MessageBox]::Show("No files found.","No files",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; Set-Status "No files found."; return }

        $PreviewList.Clear()
        foreach ($f in $files) {
            $meta = Read-Metadata $f.FullName
            $summary = ""
            if ($meta.Count -gt 0) {
                $album = Get-MetaValue -meta $meta -keys @('Album','album')
                $artist = Get-MetaValue -meta $meta -keys @('Artist','artist','Composer','composer','Author')
                $title = Get-MetaValue -meta $meta -keys @('Title','title')
                if ($album -or $artist -or $title) {
                    $summary = ("Audio: {0} / {1} / {2}" -f ($artist -as [string]), ($album -as [string]), ($title -as [string])).Trim(' ,/')
                } else {
                    $vtitle = Get-MetaValue -meta $meta -keys @('Title','title','show','show_title','tvshow','show_name','movie')
                    $season = Get-MetaValue -meta $meta -keys @('SeasonNumber','season_number','season')
                    $episode = Get-MetaValue -meta $meta -keys @('EpisodeNumber','episode_number','episode')
                    if ($vtitle -or $season -or $episode) {
                        $vtitleStr = if ($vtitle) { $vtitle.ToString() } else { '' }
                        $seasonStr = if ($season) { $season.ToString() } else { '' }
                        $episodeStr = if ($episode) { $episode.ToString() } else { '' }
                        $summary = ("Video: {0} S{1} E{2}" -f $vtitleStr, $seasonStr, $episodeStr).Trim()
                    } else {
                        $keys = $meta.Keys | Select-Object -First 3
                        $vals = $keys | ForEach-Object { $meta[$_] }
                        $summary = ("Tags: {0}" -f ($vals -join '; ')).Trim()
                    }
                }
            } else { $summary = 'No metadata' }

            $PreviewList.Add([PSCustomObject]@{
                Apply = $false
                Original = $f.Name
                Proposed = $f.Name
                Status = 'metadata scanned'
                FullPath = $f.FullName
                Directory = $f.DirectoryName
                MetaSummary = $summary
                _Metadata = $meta
            })
        }

        $DgPreview.ItemsSource = $PreviewList
        Set-Status "Metadata scan complete. $($PreviewList.Count) items."
    } catch {
        [System.Windows.MessageBox]::Show("Error scanning metadata: $($_.Exception.Message)","Error",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
        Set-Status "Error scanning metadata."
    }
})

# Scan / Preview (rename preview) - uses same logic as earlier versions with duplicate handling
$BtnScan.Add_Click({
    Set-Status "Scanning..."
    $PreviewList.Clear(); $DgPreview.ItemsSource = $null; $BtnApply.IsEnabled = $false; $BtnExportCsv.IsEnabled = $false; $BtnUndo.IsEnabled = $false

    $folder = $TxtFolder.Text.Trim()
    if (-not $folder -or -not (Test-Path $folder)) { [System.Windows.MessageBox]::Show("Please choose a valid folder.","Folder required",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; Set-Status "No folder selected."; return }

    try {
        $recurse = $ChkRecurse.IsChecked
        $exts = Get-ExtensionsFromUI
        if (-not $exts -or $exts.Count -eq 0) { [System.Windows.MessageBox]::Show("Please select at least one file type category or add custom extensions.","Extensions required",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; Set-Status "No extensions selected."; return }

        $files = Get-FilesByExtensions -folder $folder -recurse $recurse -exts $exts
        if (-not $files -or $files.Count -eq 0) { [System.Windows.MessageBox]::Show("No files found for selected extensions.","No files",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; Set-Status "No files found."; return }

        $changePrefix = $ChkChangePrefix.IsChecked
        $oldPrefix = if ($changePrefix) { $TxtOldPrefix.Text.Trim() } else { '' }
        $newPrefix = if ($changePrefix) { $TxtNewPrefix.Text.Trim() } else { '' }

        if ($ChkCleanOnlyIfOldPrefix.IsChecked -and -not $ChkChangePrefix.IsChecked) { [System.Windows.MessageBox]::Show("Clean-only requires Change Prefix.","Option conflict",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; Set-Status "Scan aborted."; return }

        if ($changePrefix -and [string]::IsNullOrWhiteSpace($oldPrefix)) {
            $first = $files | Select-Object -First 1
            $nameNoExt = [System.IO.Path]::GetFileNameWithoutExtension($first.Name)
            $m = [regex]::Match($nameNoExt, '^(.*?)(?:-|_| )')
            if ($m.Success) { $candidate = $m.Groups[1].Value } else { $candidate = if ($nameNoExt.Length -gt 12) { $nameNoExt.Substring(0,12) } else { $nameNoExt } }
            $msg = "Detected candidate old prefix:`n`n'$candidate'`n`nIs this your old prefix?"
            $res = [System.Windows.MessageBox]::Show($msg,"Confirm Old Prefix",[System.Windows.MessageBoxButton]::YesNoCancel,[System.Windows.MessageBoxImage]::Question)
            if ($res -eq [System.Windows.MessageBoxResult]::Yes) { $oldPrefix = $candidate; $TxtOldPrefix.Text = $oldPrefix }
            elseif ($res -eq [System.Windows.MessageBoxResult]::No) {
                try { $typed = Prompt-Or-Throw "Enter the old prefix to use (leave blank to cancel scan):" } catch { Set-Status "Scan cancelled (non-interactive)"; return }
                if ($null -eq $typed -or [string]::IsNullOrWhiteSpace($typed)) { Set-Status "Scan cancelled"; return }
                $oldPrefix = $typed.Trim(); $TxtOldPrefix.Text = $oldPrefix
            } else { Set-Status "Scan cancelled"; return }
        }

        if ($ChkCleanOnlyIfOldPrefix.IsChecked -and $ChkRequireExistingPrefix.IsChecked -and [string]::IsNullOrWhiteSpace($TxtOldPrefix.Text)) {
            try { $typed = Prompt-Or-Throw "You selected 'Only clean files matching Old Prefix'. Enter Old Prefix to use (leave blank to cancel):" } catch { $ChkCleanOnlyIfOldPrefix.IsChecked = $false; Set-Status "Clean-only cancelled (non-interactive)"; return }
            if ($null -eq $typed -or [string]::IsNullOrWhiteSpace($typed)) { $ChkCleanOnlyIfOldPrefix.IsChecked = $false; Set-Status "Clean-only cancelled"; return }
            $TxtOldPrefix.Text = $typed.Trim(); Set-Status "Cleaning limited to prefix: $($TxtOldPrefix.Text)"
        }

        if ($changePrefix -and [string]::IsNullOrWhiteSpace($oldPrefix)) { [System.Windows.MessageBox]::Show("Old Prefix required when Change Prefix is checked.","Prefix required",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; Set-Status "Old Prefix required."; return }

        $requireExistingPrefix = $ChkRequireExistingPrefix.IsChecked
        $addSeason = $ChkAddSeason.IsChecked
        $seasonNum = 0; if ($addSeason) { $seasonNum = [int]($TxtSeason.Text -as [int]); if (-not $seasonNum) { $seasonNum = 1 } }
        $seasonDigits = Get-DigitsFromCombo $CmbSeasonDigits
        $seasonBeforeEpisode = $ChkSeasonBeforeEpisode.IsChecked

        $addEpisode = $ChkAddEpisode.IsChecked
        $start = [int]($TxtStart.Text -as [int]); if (-not $start) { $start = 1 }
        $episodeDigits = Get-DigitsFromCombo $CmbEpisodeDigits
        $renumberAll = $ChkRenumberAll.IsChecked

        $cleanTokens = Get-CleanTokens -use720p $Chk720p.IsChecked -use1080p $Chk1080p.IsChecked -use4k $Chk4k.IsChecked -use720 $Chk720.IsChecked -use1080 $Chk1080.IsChecked -useHD $ChkHD.IsChecked -custom $TxtCustomClean.Text
        $performCleanGlobally = $true
        if ($ChkCleanOnlyIfOldPrefix.IsChecked -and $ChkRequireExistingPrefix.IsChecked) { $performCleanGlobally = $false }

        $items = @()
        foreach ($f in $files) {
            $items += [PSCustomObject]@{
                FullPath = $f.FullName
                Directory = $f.DirectoryName
                Original = $f.Name
                NameNoExt = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
                Extension = $f.Extension
            }
        }

        if ($requireExistingPrefix -and $changePrefix -and $oldPrefix) {
            $items = $items | Where-Object {
                $_.NameNoExt.StartsWith($oldPrefix) -or $_.NameNoExt.StartsWith("$oldPrefix-") -or $_.NameNoExt.StartsWith("$oldPrefix_")
            }
            if (-not $items -or $items.Count -eq 0) { [System.Windows.MessageBox]::Show("No files matched the Old Prefix filter.","No files",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; Set-Status "No files matched old prefix."; return }
        }

        foreach ($it in $items) { $it | Add-Member -NotePropertyName SortKey -NotePropertyValue (Build-SortKeyForTitle $it.NameNoExt $oldPrefix) }

        $assignWhenNewPrefixEmpty = $addEpisode -and ([string]::IsNullOrWhiteSpace($newPrefix))
        $epMap = @{}; $index = $start
        if ($addEpisode) {
            if ($renumberAll -or $assignWhenNewPrefixEmpty) {
                $ordered = $items | Sort-Object -Property SortKey, Original
                foreach ($o in $ordered) { $epMap[$o.Original] = $index; $index++ }
            } else {
                $ordered = $items | Sort-Object -Property SortKey, Original
                foreach ($o in $ordered) {
                    $hasEp = [regex]::IsMatch($o.NameNoExt, '(?i)E(\d{1,4})')
                    if (-not $hasEp) { $epMap[$o.Original] = $index; $index++ }
                }
            }
        }

        foreach ($it in $items) {
            $orig = $it.Original
            $baseNoExt = $it.NameNoExt
            $ext = $it.Extension
            $hasEpisode = [regex]::IsMatch($baseNoExt, '(?i)E(\d{1,4})')
            $hasSeason = [regex]::IsMatch($baseNoExt, '(?i)S(\d{1,3})')

            $performClean = $performCleanGlobally
            if ($ChkCleanOnlyIfOldPrefix.IsChecked -and $ChkRequireExistingPrefix.IsChecked -and $oldPrefix) {
                if ($it.NameNoExt.StartsWith($oldPrefix) -or $it.NameNoExt.StartsWith("$oldPrefix-") -or $it.NameNoExt.StartsWith("$oldPrefix_")) { $performClean = $true } else { $performClean = $false }
            }

            $core = $baseNoExt
            if ($changePrefix -and $oldPrefix) { $core = $core -replace ("^" + [regex]::Escape($oldPrefix) + "([_\-\. ]?)"), '' }

            $willAssign = $addEpisode -and ($renumberAll -or $assignWhenNewPrefixEmpty -or $epMap.ContainsKey($it.Original))
            if ($willAssign) {
                $core = $core -replace '(?i)S\d{1,3}E\d{1,4}',''
                $core = $core -replace '(?i)S\d{1,3}\s*-\s*E\d{1,4}',''
                $core = $core -replace '(?i)E\d{1,4}',''
                $core = $core.Trim()
            }

            $seasonToken = $null
            if ($ChkAddSeason.IsChecked -and -not $hasSeason) { $seasonToken = Pad-Season -num $seasonNum -pad $seasonDigits }

            $episodeToken = $null
            if ($addEpisode -and $epMap.ContainsKey($it.Original)) { $episodeToken = Pad-Episode -num $epMap[$it.Original] -pad $episodeDigits }

            $metaSummary = ''
            $metaTags = @{}
            if ($ChkUseAudioMetadata.IsChecked -or $ChkUseVideoMetadata.IsChecked) {
                $metaTags = Read-Metadata $it.FullPath
                if ($metaTags.Count -gt 0) {
                    $metaSummary = ($metaTags.GetEnumerator() | Select-Object -First 5 | ForEach-Object { "$($_.Key)=$($_.Value)" } ) -join '; '
                }
            }

            $proposedBase = $null
            $isAudio = ($PresetGroups.Audio -contains $ext.ToLower())
            $isVideo = ($PresetGroups.Video -contains $ext.ToLower())
            if ($isAudio -and $ChkUseAudioMetadata.IsChecked -and $metaTags.Count -gt 0) {
                $artist = Get-MetaValue -meta $metaTags -keys @('Artist','artist','composer','Composer','Author')
                $album = Get-MetaValue -meta $metaTags -keys @('Album','album')
                $title = Get-MetaValue -meta $metaTags -keys @('Title','title')
                $parts = @()
                if ($artist) { $parts += $artist }
                if ($album) { $parts += $album }
                if ($title) { $parts += $title } else { $parts += $core }
                $proposedBase = ($parts -join ' - ')
            } elseif ($isVideo -and $ChkUseVideoMetadata.IsChecked -and $metaTags.Count -gt 0) {
                $show = Get-MetaValue -meta $metaTags -keys @('show','tvshow','Show','Series')
                $title = Get-MetaValue -meta $metaTags -keys @('title','Title','movie','ShowTitle')
                $seasonMeta = Get-MetaValue -meta $metaTags -keys @('season_number','SeasonNumber','season')
                $episodeMeta = Get-MetaValue -meta $metaTags -keys @('episode_number','EpisodeNumber','episode','episode_id')
                $parts = @()
                if ($show) { $parts += $show }
                if ($seasonMeta -and $episodeMeta) {
                    $sTok = "S{0}" -f (Pad-Number -num ([int]$seasonMeta) -pad $seasonDigits)
                    $eTok = "E{0}" -f (Pad-Number -num ([int]$episodeMeta) -pad $episodeDigits)
                    if ($ChkSeasonBeforeEpisode.IsChecked) { $parts += ($sTok + $eTok) } else { $parts += ("{0} {1}" -f $sTok, $eTok) }
                }
                if ($title) { $parts += $title } else { $parts += $core }
                $proposedBase = ($parts -join '-')
            } else {
                $proposedBase = Normalize-Proposed -coreBase $core -oldPrefix $oldPrefix -newPrefix $newPrefix -seasonToken $seasonToken -seasonBeforeEpisode $seasonBeforeEpisode -episodeToken $episodeToken -cleanTokens $cleanTokens -performClean $performClean
            }

            if ($changePrefix -and [string]::IsNullOrWhiteSpace($newPrefix) -and -not [string]::IsNullOrWhiteSpace($oldPrefix)) {
                if ($proposedBase -notmatch "^(?i)$([regex]::Escape($oldPrefix))") { $proposedBase = "$oldPrefix-$proposedBase" }
            } elseif ($changePrefix -and -not [string]::IsNullOrWhiteSpace($newPrefix)) {
                if ($proposedBase -notmatch "^(?i)$([regex]::Escape($newPrefix))") { $proposedBase = "$newPrefix-$proposedBase" }
            }

            $proposedBase = $proposedBase -replace '_{2,}', '_'
            $proposedBase = $proposedBase -replace '-{2,}', '-'
            $proposedBase = [regex]::Replace($proposedBase, '([ _\-\.\s]){2,}', { param($m) $m.Value[0] })
            $proposedBase = $proposedBase.Trim(' ','_','-','.')

            $proposedName = $proposedBase + $ext
            $status = if ($proposedName -eq $orig) { "no modification needed" } else { "will be renamed" }

            $PreviewList.Add([PSCustomObject]@{
                Apply = ($status -ne "no modification needed")
                Original = $orig
                Proposed = $proposedName
                Status = $status
                FullPath = $it.FullPath
                Directory = $it.Directory
                MetaSummary = $metaSummary
                _Metadata = $metaTags
            })
        }

        # incremental duplicate core-title detection & suffixing
        $coreMap = @{}
        foreach ($entry in $PreviewList) {
            $proposed = $entry.Proposed
            $base = [System.IO.Path]::GetFileNameWithoutExtension($proposed)
            $core = $base -replace '^[^\-_\. ]+[_\-_\. ]+',''
            $core = $core -replace '(?i)S\d{1,3}E\d{1,4}',''
            $core = $core -replace '(?i)S\d{1,3}\s*-\s*E\d{1,4}',''
            $core = $core -replace '(?i)E\d{1,4}',''
            $core = $core -replace '(?i)S\d{1,3}',''
            $core = $core.Trim(' ','_','-','.')
            if (-not $coreMap.ContainsKey($core)) { $coreMap[$core] = @() }
            $coreMap[$core] += $entry
        }
        foreach ($kv in $coreMap.GetEnumerator()) {
            $list = $kv.Value
            if ($list.Count -gt 1) {
                $seen = @{}
                foreach ($e in $list) {
                    $origProposed = $e.Proposed
                    $ext = [System.IO.Path]::GetExtension($origProposed)
                    $base = [System.IO.Path]::GetFileNameWithoutExtension($origProposed)
                    $baseKey = $base -replace '(?i)-Version\d+$',''
                    if (-not $seen.ContainsKey($baseKey)) { $seen[$baseKey] = 1 } else {
                        $seen[$baseKey] = $seen[$baseKey] + 1
                        $ver = $seen[$baseKey]
                        $newBase = "$baseKey-Version$ver"
                        $e.Proposed = $newBase + $ext
                        $e.Apply = $true
                    }
                }
            }
        }

        $DgPreview.ItemsSource = $PreviewList
        $BtnExportCsv.IsEnabled = $PreviewList.Count -gt 0
        $BtnApply.IsEnabled = ($PreviewList | Where-Object { $_.Apply } | Measure-Object).Count -gt 0
        Set-Status "Preview ready. $($PreviewList.Count) items."
    } catch {
        [System.Windows.MessageBox]::Show("Error during scan: $($_.Exception.Message)","Error",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
        Set-Status "Error during scan."
    }
})

# Select all / none
$BtnSelectAll.Add_Click({ foreach ($i in $PreviewList) { $i.Apply = $true }; $DgPreview.Items.Refresh(); $BtnApply.IsEnabled = ($PreviewList | Where-Object { $_.Apply } | Measure-Object).Count -gt 0 })
$BtnSelectNone.Add_Click({ foreach ($i in $PreviewList) { $i.Apply = $false }; $DgPreview.Items.Refresh(); $BtnApply.IsEnabled = $false })

# Export CSV
$BtnExportCsv.Add_Click({
    if (-not $PreviewList -or $PreviewList.Count -eq 0) { return }
    $dlg = New-Object Microsoft.Win32.SaveFileDialog
    $dlg.FileName = "preview-" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".csv"
    $dlg.DefaultExt = ".csv"; $dlg.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
    $res = $dlg.ShowDialog(); if ($res -ne $true) { return }; $path = $dlg.FileName
    $PreviewList | ForEach-Object { [PSCustomObject]@{ Original=$_.Original; Proposed=$_.Proposed; Status=$_.Status; MetaSummary=$_.MetaSummary; Apply=$_.Apply } } | Export-Csv -Path $path -NoTypeInformation -Encoding UTF8
    Set-Status "Preview exported to $path"
})

# Apply / Undo
$BtnApply.Add_Click({
    if (-not $PreviewList -or $PreviewList.Count -eq 0) { return }
    $toApply = $PreviewList | Where-Object { $_.Apply }
    if ($toApply.Count -eq 0) { [System.Windows.MessageBox]::Show("No items selected to apply.","Nothing to do",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; return }

    $dry = $ChkDryRun.IsChecked
    $folder = $TxtFolder.Text.Trim()
    $timestamp = (Get-Date).ToString("yyyyMMdd-HHmmss")
    $logFile = Join-Path $folder "rename-log_$timestamp.csv"
    $undoFile = Join-Path $folder "undo-rename_$timestamp.json"
    $results = @()

    foreach ($item in $toApply) {
        $src = Join-Path $item.Directory $item.Original
        $dest = Join-Path $item.Directory $item.Proposed
        $status = ""; $newFull = $dest

        if ($src -eq $dest) {
            $status = "no modification needed"
        } else {
            if (Test-Path $dest) {
                $baseNoExt = [System.IO.Path]::GetFileNameWithoutExtension($dest)
                $ext = [System.IO.Path]::GetExtension($dest)
                $counter = 1
                do {
                    $candidate = "{0} ({1}){2}" -f $baseNoExt, $counter, $ext
                    $dest = Join-Path $item.Directory $candidate
                    $counter++
                } while (Test-Path $dest)
                $newFull = $dest
            }
            if ($dry) {
                $status = "DRY-RUN simulated rename"
            } else {
                try {
                    Rename-Item -LiteralPath $src -NewName ([System.IO.Path]::GetFileName($dest)) -ErrorAction Stop
                    $status = "Renamed"
                    try {
                        $adsName = "$newFull`:NewFileName"
                        $newFileNameValue = [System.IO.Path]::GetFileName($newFull)
                        Set-Content -LiteralPath $adsName -Value $newFileNameValue -Encoding UTF8 -ErrorAction SilentlyContinue
                    } catch {}

                    $extLower = [System.IO.Path]::GetExtension($newFull).ToLower()
                    $metaTags = $item._Metadata
                    if ($metaTags -and $metaTags.Count -gt 0) {
                        $tagsToWrite = @{}
                        if ($PresetGroups.Audio -contains $extLower -and $ChkUseAudioMetadata.IsChecked) {
                            $map = @{
                                Title = Get-MetaValue -meta $metaTags -keys @('Title','title','movie')
                                Artist = Get-MetaValue -meta $metaTags -keys @('Artist','artist','composer')
                                Album = Get-MetaValue -meta $metaTags -keys @('Album','album')
                                Track = Get-MetaValue -meta $metaTags -keys @('Track','track')
                            }
                            foreach ($k in $map.Keys) { if ($map[$k]) { $tagsToWrite[$k] = $map[$k] } }
                        } elseif ($PresetGroups.Video -contains $extLower -and $ChkUseVideoMetadata.IsChecked) {
                            $map = @{
                                Title = Get-MetaValue -meta $metaTags -keys @('title','Title','movie')
                                Show = Get-MetaValue -meta $metaTags -keys @('show','tvshow','Series')
                                Season = Get-MetaValue -meta $metaTags -keys @('season_number','SeasonNumber','season')
                                Episode = Get-MetaValue -meta $metaTags -keys @('episode_number','EpisodeNumber','episode')
                            }
                            foreach ($k in $map.Keys) { if ($map[$k]) { $tagsToWrite[$k] = $map[$k] } }
                        }
                        if ($tagsToWrite.Count -gt 0) {
                            $wrote = Write-Metadata -fullPath $newFull -tags $tagsToWrite
                            if ($wrote) { $status += " +metadata" } else { $status += " (metadata failed)" }
                        }
                    }

                } catch {
                    $status = "Error: $($_.Exception.Message)"
                }
            }
        }

        $results += [PSCustomObject]@{
            Time = (Get-Date).ToString("s")
            OriginalFullPath = $src
            OriginalName = $item.Original
            NewName = [System.IO.Path]::GetFileName($newFull)
            NewFullPath = $newFull
            Status = $status
        }
    }

    $results | Export-Csv -Path $logFile -NoTypeInformation -Encoding UTF8

    $undoEntries = $results | Where-Object { $_.Status -match 'Renamed' } | ForEach-Object { [PSCustomObject]@{ Old = $_.NewFullPath; New = $_.OriginalFullPath } }
    if ($undoEntries.Count -gt 0) { $undoEntries | ConvertTo-Json | Out-File -FilePath $undoFile -Encoding UTF8; $BtnUndo.IsEnabled = $true }

    foreach ($r in $PreviewList) {
        $match = $results | Where-Object { $_.OriginalName -eq $r.Original } | Select-Object -First 1
        if ($match) { $r.Status = $match.Status; $r.Proposed = $match.NewName }
    }
    $DgPreview.Items.Refresh()
    $BtnApply.IsEnabled = $false

    $msg = if ($dry) { "Dry-run complete. Log: $logFile" } else { "Rename complete. Log: $logFile" }
    Set-Status $msg
})

# Undo handler
$BtnUndo.Add_Click({
    $folder = $TxtFolder.Text.Trim()
    $undoFiles = Get-ChildItem -Path $folder -Filter "undo-rename_*.json" -File | Sort-Object LastWriteTime -Descending
    if (-not $undoFiles -or $undoFiles.Count -eq 0) { [System.Windows.MessageBox]::Show("No undo files found in folder.","Undo",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; return }
    $latest = $undoFiles[0].FullName
    try { $entries = Get-Content -Path $latest -Raw | ConvertFrom-Json } catch { [System.Windows.MessageBox]::Show("Undo file invalid.","Undo",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null; return }
    $log = @()
    foreach ($e in $entries) {
        $from = $e.Old; $to = $e.New
        if (-not (Test-Path $from)) { $log += [PSCustomObject]@{ Time=(Get-Date).ToString("s"); From=$from; To=$to; Status="Source not found" }; continue }
        try { Rename-Item -LiteralPath $from -NewName ([System.IO.Path]::GetFileName($to)) -ErrorAction Stop; $log += [PSCustomObject]@{ Time=(Get-Date).ToString("s"); From=$from; To=$to; Status="Reverted" } } catch { $log += [PSCustomObject]@{ Time=(Get-Date).ToString("s"); From=$from; To=$to; Status="Error: $($_.Exception.Message)" } }
    }
    $undoLogFile = Join-Path $folder ("undo-log_" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".csv")
    $log | Export-Csv -Path $undoLogFile -NoTypeInformation -Encoding UTF8
    [System.Windows.MessageBox]::Show("Undo complete. Log: $undoLogFile","Undo",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
    $BtnUndo.IsEnabled = $false
    Set-Status "Undo complete. Log: $undoLogFile"
})

# Show window
[void]$window.ShowDialog()