
# --- Part 1: Load WPF assemblies and define XAML ---
Add-Type -AssemblyName PresentationCore,PresentationFramework

[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="FileCleaner" Height="720" Width="1000"
        WindowStartupLocation="CenterScreen">

  <DockPanel Margin="10">

    <!-- Left panel: Options -->
    <ScrollViewer DockPanel.Dock="Left" Width="420" VerticalScrollBarVisibility="Auto">
      <StackPanel Orientation="Vertical" Margin="0,0,10,0">

        <!-- Folder Selection -->
        <GroupBox Header="Folder Selection" Margin="0,0,0,10">
          <StackPanel Margin="8">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
              <TextBox Name="TxtFolder" Width="300" Margin="0,0,6,0"/>
              <Button Name="BtnBrowse" Content="Browse..." Width="80"/>
            </StackPanel>
            <CheckBox Name="ChkRecurse" Content="Include Subfolders"/>
          </StackPanel>
        </GroupBox>

        <!-- Prefix Options -->
        <GroupBox Header="Prefix Options" Margin="0,0,0,10">
          <StackPanel Margin="8" Orientation="Vertical">

            <CheckBox Name="ChkChangePrefix" Content="Change Prefix (Old → New)" Margin="0,0,0,6"/>

            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="200"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="200"/>
              </Grid.ColumnDefinitions>
              <Label Content="Old Prefix:" Grid.Column="0" VerticalAlignment="Center"/>
              <TextBox Name="TxtOldPrefix" Grid.Column="1" Margin="4,0"/>
              <Label Content="New Prefix:" Grid.Column="2" VerticalAlignment="Center"/>
              <TextBox Name="TxtNewPrefix" Grid.Column="3" Margin="4,0"/>
            </Grid>

            <!-- Detected Prefix row -->
            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="400"/>
              </Grid.ColumnDefinitions>
              <Label Content="Detected Prefix:" Grid.Column="0" VerticalAlignment="Center"/>
              <TextBox Name="TxtDetectedPrefix" Grid.Column="1" Margin="4,0" IsReadOnly="False"/>
            </Grid>

            <CheckBox Name="ChkAddPrefixAll" Content="Add Prefix to All Files" Margin="0,0,0,4"/>
            <CheckBox Name="ChkOnlyIfOldPrefix" Content="Only process files with Old Prefix" Margin="0,0,0,4"/>
            <CheckBox Name="ChkDryRun" Content="Dry-run (simulate without renaming)" Margin="0,0,0,4"/>
          </StackPanel>
        </GroupBox>

        <!-- Season / Episode Options -->
        <GroupBox Header="Season / Episode Options" Margin="0,0,0,10">
          <StackPanel Margin="8" Orientation="Vertical">

            <CheckBox Name="ChkAddSeason" Content="Add Season" Margin="0,0,0,6"/>
            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="100"/>
              </Grid.ColumnDefinitions>
              <Label Content="Season #:" Grid.Column="0" VerticalAlignment="Center"/>
              <TextBox Name="TxtSeason" Grid.Column="1" Margin="4,0" Width="60"/>
              <Label Content="Digits:" Grid.Column="2" VerticalAlignment="Center"/>
              <ComboBox Name="CmbSeasonDigits" Grid.Column="3" Margin="4,0" Width="60">
                <ComboBoxItem Content="2" IsSelected="True"/>
                <ComboBoxItem Content="3"/>
              </ComboBox>
            </Grid>

            <CheckBox Name="ChkAddEpisode" Content="Add / Update Episode" Margin="0,0,0,6"/>
            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="100"/>
              </Grid.ColumnDefinitions>
              <Label Content="Start #:" Grid.Column="0" VerticalAlignment="Center"/>
              <TextBox Name="TxtStart" Grid.Column="1" Margin="4,0" Width="60"/>
              <Label Content="Digits:" Grid.Column="2" VerticalAlignment="Center"/>
              <ComboBox Name="CmbEpisodeDigits" Grid.Column="3" Margin="4,0" Width="60">
                <ComboBoxItem Content="2" IsSelected="True"/>
                <ComboBoxItem Content="3"/>
                <ComboBoxItem Content="4"/>
              </ComboBox>
            </Grid>

            <CheckBox Name="ChkRenumberAll" Content="Renumber all files alphabetically" Margin="0,0,0,4"/>
            <CheckBox Name="ChkSeasonBeforeEpisode" Content="Place Season before Episode (S01E01)" Margin="0,0,0,4"/>
          </StackPanel>
        </GroupBox>

        <!-- Cleaning Options -->
        <GroupBox Header="Cleaning Options" Margin="0,0,0,10">
          <StackPanel Margin="8" Orientation="Vertical">

            <TextBlock Text="Remove common tokens:" Margin="0,0,0,6" FontWeight="Bold"/>
            <WrapPanel Margin="0,0,0,6">
              <CheckBox Name="Chk720p" Content="720p" Margin="4,0"/>
              <CheckBox Name="Chk1080p" Content="1080p" Margin="4,0"/>
              <CheckBox Name="Chk4k" Content="4K" Margin="4,0"/>
              <CheckBox Name="ChkHD" Content="HD" Margin="4,0"/>
            </WrapPanel>

            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <Label Content="Custom Tokens:" Grid.Column="0" VerticalAlignment="Center"/>
              <TextBox Name="TxtCustomClean" Grid.Column="1" Margin="4,0"
                       ToolTip="Enter comma-separated tokens to remove (e.g. 'WEBRip, x264, AC3')"/>
            </Grid>

            <TextBlock Text="Use metadata (optional):" Margin="0,4,0,6" FontWeight="Bold"/>
            <StackPanel Orientation="Vertical" Margin="0,0,0,4">
              <CheckBox Name="ChkUseAudioMetadata" Content="Use Audio Metadata (Artist/Album/Title)" Margin="0,0,0,4"/>
              <CheckBox Name="ChkUseVideoMetadata" Content="Use Video Metadata (Show/Title/Season/Episode)" Margin="0,0,0,4"/>
            </StackPanel>
          </StackPanel>
        </GroupBox>

        <!-- File Type Filters -->
        <GroupBox Header="File Type Filters" Margin="0,0,0,10">
          <StackPanel Margin="8" Orientation="Vertical">
            <WrapPanel Margin="0,0,0,6">
              <CheckBox Name="ChkVideo" Content="Video" Margin="4,0" IsChecked="True"/>
              <CheckBox Name="ChkPictures" Content="Pictures" Margin="4,0"/>
              <CheckBox Name="ChkDocuments" Content="Documents" Margin="4,0"/>
              <CheckBox Name="ChkAudio" Content="Audio" Margin="4,0"/>
              <CheckBox Name="ChkArchives" Content="Archives" Margin="4,0"/>
            </WrapPanel>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <Label Content="Custom Extensions:" Grid.Column="0" VerticalAlignment="Center"/>
              <TextBox Name="TxtCustomExt" Grid.Column="1" Margin="4,0"
                       ToolTip="Enter comma-separated extensions (e.g. .mkv, .mp4, .avi)"/>
            </Grid>
          </StackPanel>
        </GroupBox>

      </StackPanel>
    </ScrollViewer>

    <!-- Right panel: Preview grid -->
    <DockPanel>
      <DataGrid Name="DgPreview" AutoGenerateColumns="False" CanUserAddRows="False"
                DockPanel.Dock="Top" Height="500" Margin="0,0,0,10">

                        <DataGrid.Columns>
          <DataGridCheckBoxColumn Header="Apply" Binding="{Binding Apply}" Width="60"/>
          <DataGridTextColumn Header="Original" Binding="{Binding Original}" Width="*"/>
          <DataGridTextColumn Header="Proposed" Binding="{Binding Proposed}" Width="*"/>
          <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="120"/>
          <DataGridTextColumn Header="Directory" Binding="{Binding Directory}" Width="200"/>
          <DataGridTextColumn Header="Meta" Binding="{Binding MetaSummary}" Width="200"/>
        </DataGrid.Columns>
      </DataGrid>

      <!-- Action buttons -->
      <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,10,0,0">
        <Button Name="BtnSelectAll" Content="Select All" Width="90" Margin="4"/>
        <Button Name="BtnSelectNone" Content="Select None" Width="90" Margin="4"/>
        <Button Name="BtnScan" Content="Scan / Preview" Width="110" Margin="4"/>
        <Button Name="BtnApply" Content="Apply Changes" Width="110" Margin="4" IsEnabled="False"/>
        <Button Name="BtnUndo" Content="Undo Last" Width="90" Margin="4"/>
        <Button Name="BtnExportCsv" Content="Export CSV" Width="90" Margin="4" IsEnabled="False"/>
        <Button Name="BtnReset" Content="Reset" Width="90" Margin="4"/>
        <Button Name="BtnExit" Content="Exit" Width="90" Margin="4"/>
      </StackPanel>

      <!-- Status bar -->
      <StatusBar DockPanel.Dock="Bottom" Margin="0,10,0,0">
        <StatusBarItem>
          <TextBlock Name="TxtStatus" Text="Ready."/>
        </StatusBarItem>
      </StatusBar>
    </DockPanel>

  </DockPanel>
</Window>
"@



#part 3

# Load XAML and controls
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

# Folder and scan controls
$TxtFolder   = $window.FindName("TxtFolder")
$BtnBrowse   = $window.FindName("BtnBrowse")
$ChkRecurse  = $window.FindName("ChkRecurse")

# Prefix controls
$ChkChangePrefix   = $window.FindName("ChkChangePrefix")
$TxtOldPrefix      = $window.FindName("TxtOldPrefix")
$TxtNewPrefix      = $window.FindName("TxtNewPrefix")
$ChkAddPrefixAll   = $window.FindName("ChkAddPrefixAll")
$ChkOnlyIfOldPrefix= $window.FindName("ChkOnlyIfOldPrefix")
$ChkDryRun         = $window.FindName("ChkDryRun")

# Season/Episode
$ChkAddSeason           = $window.FindName("ChkAddSeason")
$TxtSeason              = $window.FindName("TxtSeason")
$CmbSeasonDigits        = $window.FindName("CmbSeasonDigits")
$ChkSeasonBeforeEpisode = $window.FindName("ChkSeasonBeforeEpisode")
$ChkAddEpisode          = $window.FindName("ChkAddEpisode")
$TxtStart               = $window.FindName("TxtStart")
$CmbEpisodeDigits       = $window.FindName("CmbEpisodeDigits")
$ChkRenumberAll         = $window.FindName("ChkRenumberAll")

# Cleaning
$Chk720p        = $window.FindName("Chk720p")
$Chk1080p       = $window.FindName("Chk1080p")
$Chk4k          = $window.FindName("Chk4k")
$ChkHD          = $window.FindName("ChkHD")
$TxtCustomClean = $window.FindName("TxtCustomClean")

# File type filters
$ChkVideo     = $window.FindName("ChkVideo")
$ChkPictures  = $window.FindName("ChkPictures")
$ChkDocuments = $window.FindName("ChkDocuments")
$ChkAudio     = $window.FindName("ChkAudio")
$ChkArchives  = $window.FindName("ChkArchives")
$TxtCustomExt = $window.FindName("TxtCustomExt")

# Metadata options
$ChkUseAudioMetadata = $window.FindName("ChkUseAudioMetadata")
$ChkUseVideoMetadata = $window.FindName("ChkUseVideoMetadata")

# Actions, preview, status
$BtnSelectAll  = $window.FindName("BtnSelectAll")
$BtnSelectNone = $window.FindName("BtnSelectNone")
$BtnScan       = $window.FindName("BtnScan")
$BtnScanMeta   = $window.FindName("BtnScanMeta")
$BtnApply      = $window.FindName("BtnApply")
$BtnUndo       = $window.FindName("BtnUndo")
$BtnExportCsv  = $window.FindName("BtnExportCsv")
$BtnReset      = $window.FindName("BtnReset")
$BtnExit       = $window.FindName("BtnExit")

$DgPreview    = $window.FindName("DgPreview")
$TxtStatusBar = $window.FindName("TxtStatusBar")
$TxtTools     = $window.FindName("TxtTools")

# Status helper
function Set-Status($text) {
    $TxtStatusBar.Dispatcher.Invoke([action]{ $TxtStatusBar.Text = $text })
}

# Browse
$BtnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select a folder to scan"
    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
        $TxtFolder.Text = $dialog.SelectedPath
        Set-Status "Folder selected: $($dialog.SelectedPath)"
    }
})

# Tool detection
function Ensure-ExifTool {
    if (Test-Path $ExifToolExeLocal) { return $ExifToolExeLocal }
    $cmd = Get-Command exiftool -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}
function Ensure-FFProbe {
    if (Test-Path $FFProbeExeLocal) { return $FFProbeExeLocal }
    $cmd = Get-Command ffprobe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$exiftoolPath = Ensure-ExifTool
$ffprobePath  = Ensure-FFProbe
$toolsDetected = @()
if ($exiftoolPath) { $toolsDetected += "exiftool" }
if ($ffprobePath)  { $toolsDetected += "ffprobe" }
$TxtTools.Text = if ($toolsDetected.Count -eq 0) { "none (exiftool or ffprobe recommended)" } else { $toolsDetected -join ', ' }

# Simple select all/none
$BtnSelectAll.Add_Click({ $DgPreview.Items | ForEach-Object { $_.Apply = $true }; $DgPreview.Items.Refresh() })
$BtnSelectNone.Add_Click({ $DgPreview.Items | ForEach-Object { $_.Apply = $false }; $DgPreview.Items.Refresh() })

#part 4

function Pad-Number {
    param($num,$pad)
    return $num.ToString("D$pad")
}
function Pad-Episode {
    param($num,$pad)
    return ("E" + (Pad-Number -num $num -pad $pad))
}
function Pad-Season {
    param($num,$pad)
    return ("S" + (Pad-Number -num $num -pad $pad))
}
function Get-DigitsFromCombo($combo) {
    return [int]($combo.SelectedItem.Content)
}
function Get-PostEpisodeSortKey {
    param($nameNoExt)
    $n = $nameNoExt
    $n = $n -replace '(?i)^.*?(S\d{1,3}E\d{1,4}|E\d{1,4})[_\-\.\s]*', ''
    return $n.Trim()
}
function Get-CleanTokens {
    param($use720p,$use1080p,$use4k,$useHD,$custom)
    $tokens = @()
    if ($use720p)  { $tokens += '720p' }
    if ($use1080p) { $tokens += '1080p' }
    if ($use4k)    { $tokens += '4k' }
    if ($useHD)    { $tokens += 'hd' }
    if ($custom) {
        $custom -split ',' | ForEach-Object {
            $t = $_.Trim()
            if ($t) { $tokens += $t }
        }
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
function Normalize-Filename {
    param([string]$base,[string[]]$cleanTokens,[bool]$performClean)
    if ($performClean) {
        return Clean-FilenameTokens -base $base -tokens $cleanTokens
    } else {
        $s = $base -replace '\s{2,}', ' '
        $s = $s -replace '_{2,}', '_'
        $s = $s -replace '-{2,}', '-'
        $s = [regex]::Replace($s, '([ _\-\.\s]){2,}', { param($m) $m.Value[0] })
        return $s.Trim(' ','_','-','.')
    }
}

#part 5

function Generate-ProposedName {
    param(
        $file,
        $ext,
        $oldPrefix,
        $newPrefix,
        $seasonToken,
        $episodeToken,
        $seasonBeforeEpisode,
        $cleanTokens,
        $performClean,
        $useAudioMeta,
        $useVideoMeta
    )

    # Base name cleanup
    $base = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    $base = Normalize-Filename -base $base -cleanTokens $cleanTokens -performClean $performClean

    # Episode token handling
    if ($ChkAddEpisode.IsChecked) {
        # Strip existing episode tokens before re-adding
        $base = $base -replace '(?i)\bS\d{1,3}E\d{1,4}\b', ''
        $base = $base -replace '(?i)\bE\d{1,4}\b', ''
        $base = $base.Trim(' ','_','-','.')
    }
    # else: leave existing episode numbers intact

    # Prefix handling
    $prefix = ''
    # If no Old Prefix provided, fall back to detected prefix
    if (-not $oldPrefix -and $TxtDetectedPrefix.Text.Trim()) {
        $oldPrefix = $TxtDetectedPrefix.Text.Trim()
    }

    $hasOld = ($oldPrefix -and $file.BaseName -match "^$([regex]::Escape($oldPrefix))([_\-\. ]?)")

    if ($oldPrefix) {
        if ($ChkChangePrefix.IsChecked -and $newPrefix -and $hasOld) {
            # Remove old prefix completely, replace with new
            $base = $base -replace "^(?i)$([regex]::Escape($oldPrefix))([_\-\. ]?)", ''
            $base = $base.Trim(' ','_','-','.')
            $prefix = $newPrefix
        }
        elseif ($ChkAddPrefixAll.IsChecked -and $newPrefix) {
            $prefix = $newPrefix
        }
        elseif ($ChkOnlyIfOldPrefix.IsChecked -and $hasOld) {
            $prefix = $oldPrefix
        }
        elseif ($hasOld) {
            $prefix = $oldPrefix
        }
    }

    # Build season/episode token
    $combined = ''
    if ($seasonToken -and $episodeToken) {
        $combined = if ($seasonBeforeEpisode) { "$seasonToken$episodeToken" } else { "$seasonToken $episodeToken" }
    } elseif ($seasonToken) { $combined = $seasonToken }
    elseif ($episodeToken) { $combined = $episodeToken }

    # Assemble parts
    $parts = @()
    if ($prefix)   { $parts += $prefix }
    if ($combined) { $parts += $combined }
    if ($base)     { $parts += $base }

    $final = ($parts -join '-').Trim(' ','_','-','.')
    $final = $final -replace '_{2,}', '_'
    $final = $final -replace '-{2,}', '-'
    $final = [regex]::Replace($final, '([ _\-\.\s]){2,}', { param($m) $m.Value[0] })
    $final = $final.Trim(' ','_','-','.')

    return $final + $ext
}

#Part 6

function Get-MostCommonPrefix {
    param($files)
    $prefixes = $files | ForEach-Object {
        if ($_.BaseName -match '^[^ \-_.]+') { $matches[0] }
    }
    $grouped = $prefixes | Group-Object | Sort-Object Count -Descending
    if ($grouped.Count -gt 0) { return $grouped[0].Name } else { return $null }
}

$BtnScan.Add_Click({
    $PreviewList.Clear()
    $DgPreview.ItemsSource = $null
    $BtnApply.IsEnabled = $false
    $BtnExportCsv.IsEnabled = $false
    Set-Status "Scanning..."

    $folder = $TxtFolder.Text.Trim()
    if (-not $folder -or -not (Test-Path $folder)) {
        [System.Windows.MessageBox]::Show("Please select a valid folder.","Folder Required",
            [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
        Set-Status "No folder selected."
        return
    }

    $exts = Get-ExtensionsFromUI
    if (-not $exts -or $exts.Count -eq 0) {
        [System.Windows.MessageBox]::Show("Please select at least one file type or enter custom extensions.","Extensions Required",
            [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
        Set-Status "No extensions selected."
        return
    }

    $files = Get-FilesByExtensions -folder $folder -recurse $ChkRecurse.IsChecked -exts $exts
    if (-not $files -or $files.Count -eq 0) {
        [System.Windows.MessageBox]::Show("No matching files found.","No Files",
            [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
        Set-Status "No files found."
        return
    }

    # Gather options
    $oldPrefix = $TxtOldPrefix.Text.Trim()
    $newPrefix = $TxtNewPrefix.Text.Trim()

    # Auto-detect prefix if none provided
    if (-not $oldPrefix) {
        $detected = Get-MostCommonPrefix $files
        if ($detected) {
            $TxtDetectedPrefix.Text = $detected
            [System.Windows.MessageBox]::Show("Detected prefix: $detected. Please confirm or edit in the 'Detected Prefix' field.","Prefix Detection",
                [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
        }
    }

    $addSeason        = $ChkAddSeason.IsChecked
    $seasonNum        = [int]($TxtSeason.Text -as [int]); if (-not $seasonNum) { $seasonNum = 1 }
    $seasonDigits     = Get-DigitsFromCombo $CmbSeasonDigits
    $seasonToken      = if ($addSeason) { Pad-Season -num $seasonNum -pad $seasonDigits } else { $null }

    $addEpisode       = $ChkAddEpisode.IsChecked
    $start            = [int]($TxtStart.Text -as [int]); if (-not $start) { $start = 1 }
    $episodeDigits    = Get-DigitsFromCombo $CmbEpisodeDigits
    $renumberAll      = $ChkRenumberAll.IsChecked

    $cleanTokens      = Get-CleanTokens -use720p $Chk720p.IsChecked -use1080p $Chk1080p.IsChecked -use4k $Chk4k.IsChecked -useHD $ChkHD.IsChecked -custom $TxtCustomClean.Text
    $performClean     = $true

    $useAudioMeta     = $ChkUseAudioMetadata.IsChecked
    $useVideoMeta     = $ChkUseVideoMetadata.IsChecked
    $seasonBeforeEpisode = $ChkSeasonBeforeEpisode.IsChecked

    if ($renumberAll) {
        $files = $files | Sort-Object { Get-PostEpisodeSortKey $_.BaseName }
    }

    $index = $start
    $counter = 0
    $total = $files.Count

    foreach ($file in $files) {
        $counter++
        Set-Status ("Scanning file {0} of {1}: {2}" -f $counter, $total, $file.Name)

        if ($ChkOnlyIfOldPrefix.IsChecked -and $oldPrefix) {
            if ($file.BaseName -notmatch "^$([regex]::Escape($oldPrefix))([_\-\. ]?)") {
                continue
            }
        }

        $ext = $file.Extension
        $meta = @{} # placeholder

        $episodeToken = $null
        if ($addEpisode) {
            $episodeToken = Pad-Episode -num $index -pad $episodeDigits
            $index++
        }

        $proposed = Generate-ProposedName -file $file -ext $ext `
            -oldPrefix $oldPrefix -newPrefix $newPrefix `
            -seasonToken $seasonToken -episodeToken $episodeToken -seasonBeforeEpisode $seasonBeforeEpisode `
            -cleanTokens $cleanTokens -performClean $performClean `
            -useAudioMeta $useAudioMeta -useVideoMeta $useVideoMeta

                $status = if ($proposed -eq $file.Name) { "no change" } else { "will be renamed" }

        $PreviewList.Add([PSCustomObject]@{
            Apply       = ($status -ne "no change")
            Original    = $file.Name
            Proposed    = $proposed
            Status      = $status
            FullPath    = $file.FullName
            Directory   = $file.DirectoryName
            MetaSummary = ""
            _Metadata   = $meta
        })
    }

    $DgPreview.ItemsSource = $PreviewList
    $BtnApply.IsEnabled    = ($PreviewList | Where-Object { $_.Apply } | Measure-Object).Count -gt 0
    $BtnExportCsv.IsEnabled = $true
    Set-Status ("{0} files scanned." -f $PreviewList.Count)
})

#part 7

$BtnApply.Add_Click({
    $toApply = $PreviewList | Where-Object { $_.Apply }
    if (-not $toApply -or $toApply.Count -eq 0) {
        [System.Windows.MessageBox]::Show("No files selected for renaming.","Nothing to Apply",
            [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
        return
    }

    $folder = $TxtFolder.Text.Trim()
    $timestamp = (Get-Date).ToString("yyyyMMdd-HHmmss")
    $logFile = Join-Path $folder "rename-log_$timestamp.csv"
    $undoFile = Join-Path $folder "undo-rename_$timestamp.json"
    $results = @()
    $dry = $ChkDryRun.IsChecked

    $counter = 0
    $total = $toApply.Count

    foreach ($item in $toApply) {
        $counter++
        $action = if ($dry) { "Simulating" } else { "Renaming" }
        Set-Status ("{0} file {1} of {2}: {3}" -f $action, $counter, $total, $item.Original)

        $src = Join-Path $item.Directory $item.Original
        $dest = Join-Path $item.Directory $item.Proposed
        $status = ""; $newFull = $dest

        if ($src -eq $dest) {
            $status = "no change"
        } else {
            if (Test-Path $dest) {
                # Collision handling
                $baseNoExt = [System.IO.Path]::GetFileNameWithoutExtension($dest)
                $ext = [System.IO.Path]::GetExtension($dest)
                $counter2 = 1
                do {
                    $candidate = "{0} ({1}){2}" -f $baseNoExt, $counter2, $ext
                    $dest = Join-Path $item.Directory $candidate
                    $counter2++
                } while (Test-Path $dest)
                $newFull = $dest
            }
            if ($dry) {
                $status = "DRY-RUN simulated rename"
            } else {
                try {
                    Rename-Item -LiteralPath $src -NewName ([System.IO.Path]::GetFileName($dest)) -ErrorAction Stop
                    $status = "Renamed"
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

    if (-not $dry) {
        $undoEntries = $results | Where-Object { $_.Status -eq 'Renamed' } | ForEach-Object {
            [PSCustomObject]@{ Old = $_.NewFullPath; New = $_.OriginalFullPath }
        }
        if ($undoEntries.Count -gt 0) {
            $undoEntries | ConvertTo-Json | Out-File -FilePath $undoFile -Encoding UTF8
            $BtnUndo.IsEnabled = $true
        }
    }

    foreach ($r in $PreviewList) {
        $match = $results | Where-Object { $_.OriginalName -eq $r.Original } | Select-Object -First 1
        if ($match) {
            $r.Status = $match.Status
            $r.Proposed = $match.NewName
        }
    }

    $DgPreview.Items.Refresh()
    $BtnApply.IsEnabled = $false
    $action = if ($dry) { "Dry-run" } else { "Rename" }
    Set-Status ("{0} complete. Log: {1}" -f $action, $logFile)
})

$BtnUndo.Add_Click({
    $folder = $TxtFolder.Text.Trim()
    $undoFiles = Get-ChildItem -Path $folder -Filter "undo-rename_*.json" -File | Sort-Object LastWriteTime -Descending
    if (-not $undoFiles -or $undoFiles.Count -eq 0) {
        [System.Windows.MessageBox]::Show("No undo files found.","Undo",
            [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
        return
    }

    $latest = $undoFiles[0].FullName
    try {
        $entries = Get-Content -Path $latest -Raw | ConvertFrom-Json
    } catch {
        [System.Windows.MessageBox]::Show("Undo file invalid.","Undo",
            [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
        return
    }

    $log = @()
    $counter = 0
    $total = $entries.Count
    foreach ($e in $entries) {
        $counter++
        Set-Status ("Undo rename {0} of {1}" -f $counter, $total)

        $from = $e.Old; $to = $e.New
        if (-not (Test-Path $from)) {
            $log += [PSCustomObject]@{ Time=(Get-Date).ToString("s"); From=$from; To=$to; Status="Source not found" }
            continue
        }
        try {
            Rename-Item -LiteralPath $from -NewName ([System.IO.Path]::GetFileName($to)) -ErrorAction Stop
            $log += [PSCustomObject]@{ Time=(Get-Date).ToString("s"); From=$from; To=$to; Status="Reverted" }
        } catch {
            $log += [PSCustomObject]@{ Time=(Get-Date).ToString("s"); From=$from; To=$to; Status="Error: $($_.Exception.Message)" }
        }
    }

    $undoLogFile = Join-Path $folder ("undo-log_" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".csv")
    $log | Export-Csv -Path $undoLogFile -NoTypeInformation -Encoding UTF8
    [System.Windows.MessageBox]::Show("Undo complete. Log: $undoLogFile","Undo",
        [System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
    $BtnUndo.IsEnabled = $false
    Set-Status "Undo complete. Log: $undoLogFile"
})

$BtnExportCsv.Add_Click({
    if (-not $PreviewList -or $PreviewList.Count -eq 0) { return }
    $dlg = New-Object Microsoft.Win32.SaveFileDialog
    $dlg.FileName = "preview-" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".csv"
    $dlg.DefaultExt = ".csv"
    $dlg.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
    $res = $dlg.ShowDialog()
    if ($res -ne $true) { return }
    $path = $dlg.FileName
    $PreviewList | ForEach-Object {
        [PSCustomObject]@{
            Original = $_.Original
            Proposed = $_.Proposed
            Status = $_.Status
            MetaSummary = $_.MetaSummary
            Apply = $_.Apply
        }
    } | Export-Csv -Path $path -NoTypeInformation -Encoding UTF8
    Set-Status "Preview exported to $path"
})

$BtnReset.Add_Click({
    $TxtFolder.Text = ""
    $ChkRecurse.IsChecked = $false
    $ChkChangePrefix.IsChecked = $false
    $TxtOldPrefix.Text = ""
    $TxtNewPrefix.Text = ""
    $ChkAddPrefixAll.IsChecked = $false
    $ChkOnlyIfOldPrefix.IsChecked = $false
    $ChkDryRun.IsChecked = $false

    $ChkAddSeason.IsChecked = $false
    $TxtSeason.Text = "1"
    $CmbSeasonDigits.SelectedIndex = 1
    $ChkSeasonBeforeEpisode.IsChecked = $false
    $ChkAddEpisode.IsChecked = $false
    $TxtStart.Text = "1"
    $CmbEpisodeDigits.SelectedIndex = 2
    $ChkRenumberAll.IsChecked = $false

    $Chk720p.IsChecked = $false
    $Chk1080p.IsChecked = $false
    $Chk4k.IsChecked = $false
    $ChkHD.IsChecked = $false
    $TxtCustomClean.Text = ""

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
    $BtnUndo.IsEnabled = $false
    Set-Status "Options reset."
})

$BtnExit.Add_Click({ $window.Close() })

#part 8
[void]$window.ShowDialog()




