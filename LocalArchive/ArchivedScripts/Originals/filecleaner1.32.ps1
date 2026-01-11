# --- Part 1: Setup and XAML Definition ---
Add-Type -AssemblyName PresentationCore,PresentationFramework
Add-Type -AssemblyName System.Windows.Forms

# Ensure STA for WPF; relaunch in STA if needed
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
  $target = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Definition }
  $pwsh   = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
  if (Test-Path $pwsh -PathType Leaf -and $target) {
    Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-STA','-File',"`"$target`"") -WorkingDirectory (Get-Location)
    exit
  }
}

[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    Title="FileCleaner" Height="720" Width="1200"
    MinHeight="600" MinWidth="900"
    ResizeMode="CanResizeWithGrip"
    WindowStartupLocation="CenterScreen">

  <ScrollViewer VerticalScrollBarVisibility="Auto"
        HorizontalScrollBarVisibility="Auto">
    <Grid Margin="10">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="420" MinWidth="300"/>
      <ColumnDefinition Width="5"/>
      <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>

    <!-- Left panel -->
    <ScrollViewer Grid.Column="0"
            VerticalScrollBarVisibility="Auto"
            HorizontalScrollBarVisibility="Auto">
      <StackPanel Orientation="Vertical" Margin="0,0,10,0">

        <!-- Folder Selection -->
        <GroupBox Header="Folder Selection" Margin="0,0,0,10">
          <StackPanel Margin="8">
            <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
              <TextBox Name="TxtFolder" Width="320" Margin="0,0,6,0"/>
              <Button Name="BtnBrowse" Content="Browse..." Width="90"/>
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
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="200"/>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="200"/>
              </Grid.ColumnDefinitions>
              <Label Content="Old Prefix:" Grid.Column="0"/>
              <TextBox Name="TxtOldPrefix" Grid.Column="1" Margin="4,0"/>
              <Label Content="New Prefix:" Grid.Column="2"/>
              <TextBox Name="TxtNewPrefix" Grid.Column="3" Margin="4,0"/>
            </Grid>
            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <Label Content="Detected Prefix:" Grid.Column="0"/>
              <TextBox Name="TxtDetectedPrefix" Grid.Column="1" Margin="4,0"/>
            </Grid>
            <CheckBox Name="ChkAddPrefixAll" Content="Add Prefix to All Files"/>
            <CheckBox Name="ChkOnlyIfOldPrefix" Content="Only process files with Old Prefix"/>
            <CheckBox Name="ChkDryRun" Content="Dry-run (simulate without renaming)"/>
          </StackPanel>
        </GroupBox>

        <!-- Season / Episode Options -->
        <GroupBox Header="Season / Episode Options" Margin="0,0,0,10">
          <StackPanel Margin="8">
            <CheckBox Name="ChkAddSeason" Content="Add Season"/>
            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="100"/>
              </Grid.ColumnDefinitions>
              <Label Content="Season #:" Grid.Column="0"/>
              <TextBox Name="TxtSeason" Grid.Column="1" Width="80"/>
              <Label Content="Digits:" Grid.Column="2"/>
              <ComboBox Name="CmbSeasonDigits" Grid.Column="3" Width="80">
                <ComboBoxItem Content="2" IsSelected="True"/>
                <ComboBoxItem Content="3"/>
              </ComboBox>
            </Grid>
            <CheckBox Name="ChkAddEpisode" Content="Add / Update Episode"/>
            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="100"/>
                <ColumnDefinition Width="120"/>
                <ColumnDefinition Width="100"/>
              </Grid.ColumnDefinitions>
              <Label Content="Start #:" Grid.Column="0"/>
              <TextBox Name="TxtStart" Grid.Column="1" Width="80"/>
              <Label Content="Digits:" Grid.Column="2"/>
              <ComboBox Name="CmbEpisodeDigits" Grid.Column="3" Width="80">
                <ComboBoxItem Content="2" IsSelected="True"/>
                <ComboBoxItem Content="3"/>
                <ComboBoxItem Content="4"/>
              </ComboBox>
            </Grid>
            <CheckBox Name="ChkRenumberAll" Content="Renumber all files alphabetically"/>
            <CheckBox Name="ChkSeasonBeforeEpisode" Content="Place Season before Episode (S01E01)"/>
          </StackPanel>
        </GroupBox>

        <!-- Cleaning Options -->
        <GroupBox Header="Cleaning Options" Margin="0,0,0,10">
          <StackPanel Margin="8">
            <TextBlock Text="Remove common tokens:" FontWeight="Bold"/>
            <WrapPanel>
              <CheckBox Name="Chk720p" Content="720p"/>
              <CheckBox Name="Chk1080p" Content="1080p"/>
              <CheckBox Name="Chk4k" Content="4K"/>
              <CheckBox Name="ChkHD" Content="HD"/>
            </WrapPanel>
            <Grid Margin="0,0,0,6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="140"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <Label Content="Custom Tokens:" Grid.Column="0"/>
              <TextBox Name="TxtCustomClean" Grid.Column="1"/>
            </Grid>
            <TextBlock Text="Use metadata (optional):" FontWeight="Bold"/>
            <CheckBox Name="ChkUseAudioMetadata" Content="Use Audio Metadata (Artist/Album/Title)"/>
            <CheckBox Name="ChkUseVideoMetadata" Content="Use Video Metadata (Show/Title/Season/Episode)"/>
          </StackPanel>
        </GroupBox>

        <!-- File Type Filters -->
        <GroupBox Header="File Type Filters" Margin="0,0,0,10">
          <StackPanel Margin="8">
            <WrapPanel>
              <CheckBox Name="ChkVideo" Content="Video" IsChecked="True"/>
              <CheckBox Name="ChkPictures" Content="Pictures"/>
              <CheckBox Name="ChkDocuments" Content="Documents"/>
              <CheckBox Name="ChkAudio" Content="Audio"/>
              <CheckBox Name="ChkArchives" Content="Archives"/>
            </WrapPanel>
            <Grid>
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="160"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <Label Content="Custom Extensions:" Grid.Column="0"/>
              <TextBox Name="TxtCustomExt" Grid.Column="1"/>
            </Grid>
          </StackPanel>
        </GroupBox>

      </StackPanel>
    </ScrollViewer>

    <!-- Splitter -->
    <GridSplitter Grid.Column="1"
            Width="5"
            HorizontalAlignment="Stretch"
            VerticalAlignment="Stretch"
            Background="LightGray"
            ResizeBehavior="PreviousAndNext"
            ResizeDirection="Columns"/>

    <!-- Right panel -->
    <Grid Grid.Column="2">
      <Grid.RowDefinitions>
        <RowDefinition Height="*"/>
        <RowDefinition Height="Auto"/>
        <RowDefinition Height="Auto"/>
      </Grid.RowDefinitions>

      <DataGrid Name="DgPreview"
            AutoGenerateColumns="False"
            CanUserAddRows="False"
            CanUserResizeColumns="True"
            ScrollViewer.HorizontalScrollBarVisibility="Visible"
            ScrollViewer.VerticalScrollBarVisibility="Visible"
            Grid.Row="0"
            Margin="0,0,0,10">
        <DataGrid.Columns>
          <DataGridCheckBoxColumn Header="Apply" Binding="{Binding Apply}" Width="80"/>
          <DataGridTextColumn Header="Original" Binding="{Binding Original}" Width="250"/>
          <DataGridTextColumn Header="Proposed" Binding="{Binding Proposed}" Width="250"/>
          <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="150"/>
          <DataGridTextColumn Header="Directory" Binding="{Binding Directory}" Width="300"/>
          <DataGridTextColumn Header="Meta" Binding="{Binding MetaSummary}" Width="300"/>
        </DataGrid.Columns>
      </DataGrid>

      <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,10,0,0">
        <Button Name="BtnSelectAll" Content="Select All" Width="100" Margin="4"/>
        <Button Name="BtnSelectNone" Content="Select None" Width="100" Margin="4"/>
        <Button Name="BtnScan" Content="Scan / Preview" Width="130" Margin="4"/>
              <Button Name="BtnApply" Content="Apply Changes" Width="130" Margin="4" IsEnabled="False"/>
        <Button Name="BtnUndo" Content="Undo Last" Width="110" Margin="4"/>
        <Button Name="BtnExportCsv" Content="Export CSV" Width="110" Margin="4" IsEnabled="False"/>
        <Button Name="BtnCombineVideos" Content="Combine Videos" Width="130" Margin="4"/>
        <Button Name="BtnReset" Content="Reset" Width="100" Margin="4"/>
        <Button Name="BtnExit" Content="Exit" Width="100" Margin="4"/>
      </StackPanel>

      <StatusBar Grid.Row="2" Margin="0,10,0,0">
        <StatusBarItem>
          <TextBlock Name="TxtStatus" Text="Ready."/>
        </StatusBarItem>
      </StatusBar>
    </Grid>
  </Grid>
  </ScrollViewer>
</Window>
"@

# --- Part 2: Load XAML and bind controls ---
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

# Left panel controls
$TxtFolder          = $window.FindName("TxtFolder")
$BtnBrowse          = $window.FindName("BtnBrowse")
$ChkRecurse         = $window.FindName("ChkRecurse")

$ChkChangePrefix    = $window.FindName("ChkChangePrefix")
$TxtOldPrefix       = $window.FindName("TxtOldPrefix")
$TxtNewPrefix       = $window.FindName("TxtNewPrefix")
$TxtDetectedPrefix  = $window.FindName("TxtDetectedPrefix")
$ChkAddPrefixAll    = $window.FindName("ChkAddPrefixAll")
$ChkOnlyIfOldPrefix = $window.FindName("ChkOnlyIfOldPrefix")
$ChkDryRun          = $window.FindName("ChkDryRun")

$ChkAddSeason       = $window.FindName("ChkAddSeason")
$TxtSeason          = $window.FindName("TxtSeason")
$CmbSeasonDigits    = $window.FindName("CmbSeasonDigits")
$ChkAddEpisode      = $window.FindName("ChkAddEpisode")
$TxtStart           = $window.FindName("TxtStart")
$CmbEpisodeDigits   = $window.FindName("CmbEpisodeDigits")
$ChkRenumberAll     = $window.FindName("ChkRenumberAll")
$ChkSeasonBeforeEpisode = $window.FindName("ChkSeasonBeforeEpisode")

$Chk720p            = $window.FindName("Chk720p")
$Chk1080p           = $window.FindName("Chk1080p")
$Chk4k              = $window.FindName("Chk4k")
$ChkHD              = $window.FindName("ChkHD")
$TxtCustomClean     = $window.FindName("TxtCustomClean")
$ChkUseAudioMetadata= $window.FindName("ChkUseAudioMetadata")
$ChkUseVideoMetadata= $window.FindName("ChkUseVideoMetadata")

$ChkVideo           = $window.FindName("ChkVideo")
$ChkPictures        = $window.FindName("ChkPictures")
$ChkDocuments       = $window.FindName("ChkDocuments")
$ChkAudio           = $window.FindName("ChkAudio")
$ChkArchives        = $window.FindName("ChkArchives")
$TxtCustomExt       = $window.FindName("TxtCustomExt")

# Right panel controls
$DgPreview          = $window.FindName("DgPreview")
$BtnSelectAll       = $window.FindName("BtnSelectAll")
$BtnSelectNone      = $window.FindName("BtnSelectNone")
$BtnScan            = $window.FindName("BtnScan")
$BtnApply           = $window.FindName("BtnApply")
$BtnUndo            = $window.FindName("BtnUndo")
$BtnExportCsv       = $window.FindName("BtnExportCsv")
$BtnCombineVideos   = $window.FindName("BtnCombineVideos")
$BtnReset           = $window.FindName("BtnReset")
$BtnExit            = $window.FindName("BtnExit")
$TxtStatus          = $window.FindName("TxtStatus")

# --- Part 3: Metadata tool detection at startup ---
$toolsDetected = @()
if (Get-Command ffprobe -ErrorAction SilentlyContinue) { $toolsDetected += "ffprobe" }
if (Get-Command exiftool -ErrorAction SilentlyContinue) { $toolsDetected += "exiftool" }

$TxtStatus.Text = if ($toolsDetected.Count -eq 0) {
    "Ready. (No metadata tools detected — install ffprobe or exiftool for metadata support)"
} else {
    "Ready. Metadata tools detected: $($toolsDetected -join ', ')"
}

# Script root to locate sibling tools
$script:FileCleanerRoot = if ($PSCommandPath) { Split-Path $PSCommandPath } else { Get-Location }




# --- Part 4: Helper functions ---

function Get-FilteredFiles {
    param(
        [string]$Path,
        [switch]$Recurse
    )
    if (-not (Test-Path $Path)) { return @() }

    $extensions = @()
    if ($ChkVideo.IsChecked)     { $extensions += '.mp4','.mkv','.avi','.mov','.wmv','.m4v' }
    if ($ChkPictures.IsChecked)  { $extensions += '.jpg','.jpeg','.png','.gif','.webp','.tiff' }
    if ($ChkDocuments.IsChecked) { $extensions += '.pdf','.doc','.docx','.txt','.md','.rtf' }
    if ($ChkAudio.IsChecked)     { $extensions += '.mp3','.flac','.wav','.m4a','.aac' }
    if ($ChkArchives.IsChecked)  { $extensions += '.zip','.rar','.7z','.tar','.gz' }

    if ($TxtCustomExt.Text) {
        $custom = $TxtCustomExt.Text -split ',' | ForEach-Object { $_.Trim().ToLower() }
        $custom = $custom | ForEach-Object { if ($_ -and $_[0] -ne '.') { ".$_" } else { $_ } }
        $extensions += $custom
    }

    $extensions = $extensions | Select-Object -Unique
    $files = Get-ChildItem -Path $Path -File -Recurse:$Recurse -ErrorAction SilentlyContinue

    if ($extensions.Count -eq 0) { return $files }
    return $files | Where-Object { $extensions -contains $_.Extension.ToLower() }
}

function Get-ProposedName {
    param(
        [System.IO.FileInfo]$File,
        [int]$EpisodeNumber,
        [switch]$ForceEpisode
    )

    $base = [System.IO.Path]::GetFileNameWithoutExtension($File.Name)
    $ext  = $File.Extension
    $originalBase = $base

    # Cleaning tokens (case-insensitive)
    if ($Chk720p.IsChecked)  { $base = $base -replace '(?i)720p','' }
    if ($Chk1080p.IsChecked) { $base = $base -replace '(?i)1080p','' }
    if ($Chk4k.IsChecked)    { $base = $base -replace '(?i)4k','' }
    if ($ChkHD.IsChecked)    { $base = $base -replace '(?i)hd','' }
    if ($TxtCustomClean.Text) {
      foreach ($token in ($TxtCustomClean.Text -split ',')) {
        $t = $token.Trim()
        if ($t) { $base = $base -replace '(?i)' + [Regex]::Escape($t), '' }
      }
    }
    $base = $base.Trim()
    if (-not $base) { $base = $originalBase }

    # Digit defaults
    $episodeDigits = 2
    if ($CmbEpisodeDigits.SelectedItem -and $CmbEpisodeDigits.SelectedItem.Content) {
        [void][int]::TryParse($CmbEpisodeDigits.SelectedItem.Content.ToString(), [ref]$episodeDigits)
    }
    $seasonDigits = 2
    if ($CmbSeasonDigits.SelectedItem -and $CmbSeasonDigits.SelectedItem.Content) {
        [void][int]::TryParse($CmbSeasonDigits.SelectedItem.Content.ToString(), [ref]$seasonDigits)
    }

    # Prefix determination (PowerShell-safe)
    $oldPrefix = if ($TxtOldPrefix.Text) { $TxtOldPrefix.Text.Trim() } else { "" }
    $newPrefix = if ($TxtNewPrefix.Text) { $TxtNewPrefix.Text.Trim() } else { "" }
    $detected  = if ($TxtDetectedPrefix.Text) { $TxtDetectedPrefix.Text.Trim() } else { "" }

    # Strip old prefix from the original base too (for tail usage later)
    $originalBaseNoOld = $originalBase
    if ($oldPrefix -and $originalBaseNoOld.StartsWith($oldPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      $originalBaseNoOld = $originalBaseNoOld.Substring($oldPrefix.Length)
    }

    # Remove old prefix entirely (case-insensitive) before applying new/detected prefixes
    if ($oldPrefix -and $base.StartsWith($oldPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      $base = $base.Substring($oldPrefix.Length)
    }

    if ($ChkChangePrefix.IsChecked -and $newPrefix) {
      $base = $newPrefix + $base
    }
    elseif ($ChkAddPrefixAll.IsChecked -and $newPrefix -and -not $base.StartsWith($newPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      $base = $newPrefix + $base
    }
    elseif (-not $oldPrefix -and $detected -and -not $base.StartsWith($detected, [System.StringComparison]::OrdinalIgnoreCase)) {
      $base = $detected + $base
    }

    # Resolve active prefix
    $prefix = ""
    foreach ($p in @($newPrefix, $detected, $oldPrefix)) {
        if ($p -and $base.StartsWith($p)) { $prefix = $p; break }
    }

    # If no prefix is present:
    # - If Add Prefix to All is on, infer leading token as prefix.
    # - If ForceEpisode (Renumber All) is on, ALWAYS infer a prefix.
    # - If AddEpisode is on, infer a prefix so we can emit the required Prefix-Episode format.
    #   Leading token is everything up to first space/underscore/dash; if no separator, use whole base.
    $shouldInferPrefix = $ChkAddPrefixAll.IsChecked -or $ForceEpisode -or $ChkAddEpisode.IsChecked
    if (-not $prefix -and $shouldInferPrefix -and $base.Length -gt 0) {
        if ($base -match '^(?<lead>[^ _\-]+)(?<rest>.*)$') {
            $prefix = $matches.lead
        }
        if (-not $prefix) {
            $prefix = $base
        }
    }

    # Split into prefix and remainder
    $remainder = $base
    if ($prefix) {
        $remainder = $base.Substring([Math]::Min($prefix.Length, $base.Length)).Trim()
    }

    # If remainder vanished, fall back to the original name without the prefix (if present) or the full original base
    $fallbackRemainder = if ($prefix -and $originalBase.StartsWith($prefix)) {
      $originalBase.Substring([Math]::Min($prefix.Length, $originalBase.Length)).Trim()
    } else {
      $originalBase
    }
    if (-not $remainder) { $remainder = $fallbackRemainder }

    # When adding prefix to all, keep the old filename tail explicitly (without the old prefix)
    $tailName = if ($ChkAddPrefixAll.IsChecked) { $originalBaseNoOld } else { $remainder }

    # Strip stray tags from remainder (enforce tags only after prefix)
    $remainder = ($remainder -replace '(?i)\bS\d{1,4}\b','').Trim()
    $remainder = ($remainder -replace '(?i)\bE\d{1,4}\b','').Trim()

    # Build tag strictly after prefix
    $seasonStr = ""
    $episodeStr = ""

    if ($ChkAddSeason.IsChecked) {
        $seasonNum = 0
        [void][int]::TryParse($TxtSeason.Text, [ref]$seasonNum)
        if ($seasonNum -gt 0) { $seasonStr = "S" + $seasonNum.ToString().PadLeft($seasonDigits,'0') }
    }

    # If ForceEpisode or AddEpisode, and we have a prefix anchor (explicit or inferred), assign episode
    if (($ForceEpisode -or $ChkAddEpisode.IsChecked) -and $prefix) {
        $episodeStr = "E" + $EpisodeNumber.ToString().PadLeft($episodeDigits,'0')
    }

    if ($seasonStr -and $episodeStr) {
      $tag = if ($ChkSeasonBeforeEpisode.IsChecked) { "$seasonStr$episodeStr" } else { "$episodeStr$seasonStr" }
    } elseif ($seasonStr -or $episodeStr) {
      $tag = "$seasonStr$episodeStr"
    } else {
      $tag = ""
    }

    # Compose final using consistent "Prefix-Tag-Remainder" with no spaces
    if ($prefix -or $tag) {
      $segments = @()
      if ($prefix)   { $segments += $prefix }
      if ($tag)      { $segments += $tag }
      $segments += $tailName
      $finalNameNoExt = ($segments -join "-").Trim()
    } else {
      # No prefix and no tag: keep cleaned name
      $finalNameNoExt = $tailName
    }

    # Remove spaces globally, collapse multiple hyphens, and trim stray hyphens
    $finalNameNoExt = $finalNameNoExt -replace '\s+','-'
    $finalNameNoExt = $finalNameNoExt -replace '-{2,}','-'
    $finalNameNoExt = $finalNameNoExt.Trim('-')

    $safeName = $finalNameNoExt.Trim()
    if (-not $safeName) { $safeName = $originalBase }

    return ($safeName + $ext)
}

function Get-MetadataSummary {
    param([System.IO.FileInfo]$File)

    $summary = ""
    if (-not ($ChkUseAudioMetadata.IsChecked -or $ChkUseVideoMetadata.IsChecked)) { return $summary }

    if (Get-Command ffprobe -ErrorAction SilentlyContinue) {
        try {
            $ffout = & ffprobe -v error -show_entries format=tags=title,artist,album,show,season_number,episode_id `
                               -of default=noprint_wrappers=1:nokey=0 -- "$($File.FullName)" 2>$null
            if ($ffout) {
                $tags = @{}
                foreach ($line in $ffout) {
                    if ($line -match "^(?<k>[^=]+)=(?<v>.+)$") { $tags[$matches.k] = $matches.v }
                }
                if ($ChkUseAudioMetadata.IsChecked) {
                    $summary = ($tags.artist, $tags.album, $tags.title -join " | ").Trim(" |")
                } elseif ($ChkUseVideoMetadata.IsChecked) {
                    $summary = ($tags.show, ("S{0}E{1}" -f $tags.season_number, $tags.episode_id), $tags.title -join " | ").Trim(" |")
                }
            }
        } catch { }
    }
    elseif (Get-Command exiftool -ErrorAction SilentlyContinue) {
        try {
            $exif = & exiftool -s -s -s -Artist -Album -Title -TVShowName -SeasonNumber -EpisodeNumber -- "$($File.FullName)" 2>$null
            if ($exif) { $summary = ($exif | Where-Object { $_ }) -join " | " }
        } catch { }
    }

    return $summary
  }

  # --- Part 5: Event handlers ---
# Browse for folder
$BtnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select a folder to process"
    $dialog.ShowNewFolderButton = $false
    $result = $dialog.ShowDialog()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) {
      $TxtFolder.Text = $dialog.SelectedPath
      $TxtStatus.Text = "Folder selected: $($dialog.SelectedPath)"
    } else {
      $TxtStatus.Text = "Browse canceled."
    }
})

# Select All / None
$BtnSelectAll.Add_Click({
    foreach ($row in $DgPreview.Items) { try { $row.Apply = $true } catch {} }
    $DgPreview.Items.Refresh()
})
$BtnSelectNone.Add_Click({
    foreach ($row in $DgPreview.Items) { try { $row.Apply = $false } catch {} }
    $DgPreview.Items.Refresh()
})

# Scan / Preview
$BtnScan.Add_Click({
    $TxtStatus.Text = "Scanning..."

    $startNum = 1
    if ($ChkAddEpisode.IsChecked -or $ChkRenumberAll.IsChecked) {
        [void][int]::TryParse($TxtStart.Text, [ref]$startNum)
        if ($startNum -lt 1) { $startNum = 1 }
    }

    $files = Get-FilteredFiles -Path $TxtFolder.Text -Recurse:$ChkRecurse.IsChecked

    # Optional: Only process files with Old Prefix
    if ($ChkOnlyIfOldPrefix.IsChecked -and $TxtOldPrefix.Text) {
      $oldPref = $TxtOldPrefix.Text.Trim()
      $files = $files | Where-Object { $_.BaseName.StartsWith($oldPref, [System.StringComparison]::OrdinalIgnoreCase) }
    }

    # Renumber All: sort alphabetically
    if ($ChkRenumberAll.IsChecked) { $files = $files | Sort-Object Name }

    $preview = @()
    $episodeCounter = $startNum

    $hasEpisodeOps = $ChkRenumberAll.IsChecked -or $ChkAddEpisode.IsChecked

    foreach ($f in $files) {
      if ($ChkRenumberAll.IsChecked) {
            # Force fresh sequential episode with inferred prefix if needed
            $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter -ForceEpisode
            $episodeCounter++
        }
        elseif ($ChkAddEpisode.IsChecked) {
            $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter
            $episodeCounter++
        }
        else {
            $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter
        }

      $status = if (-not $hasEpisodeOps -and $proposed -eq $f.Name) { "No change" } else { "Pending" }

        $preview += [PSCustomObject]@{
            Apply       = ($status -ne "No change")
            Original    = $f.Name
            Proposed    = $proposed
            Status      = $status
            Directory   = $f.DirectoryName
            MetaSummary = Get-MetadataSummary -File $f
        }
    }

    $DgPreview.ItemsSource   = $preview
    $hasPending = (@($preview | Where-Object { $_.Status -ne "No change" })).Count -gt 0
    $BtnApply.IsEnabled     = ($preview.Count -gt 0 -and $hasPending)
    $BtnExportCsv.IsEnabled = ($preview.Count -gt 0)
    $TxtStatus.Text         = if ($preview.Count -gt 0) {
        "Preview complete: $($preview.Count) files found."
    } else {
        "No files matched filters."
    }
})

# Apply Changes (mirrors preview logic and renumber behavior)
$global:LastOperations = @()
$BtnApply.Add_Click({
    $TxtStatus.Text = "Applying changes..."
    $ops = @()

    foreach ($row in $DgPreview.Items) {
        if (-not $row.Apply -or $row.Status -eq "No change") { continue }

        $fileObj = Get-Item (Join-Path $row.Directory $row.Original)
        $episodeNum = 0
        [void][int]::TryParse(($row.Proposed -replace '.*E(\d+).*','$1'), [ref]$episodeNum)

        if ($ChkRenumberAll.IsChecked) {
            $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum -ForceEpisode
        }
        elseif ($ChkAddEpisode.IsChecked) {
            $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum
        }
        else {
            $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum
        }

        if ($row.Original -ne $proposed) {
            $oldPath = Join-Path $row.Directory $row.Original
            $newPath = Join-Path $row.Directory $proposed

            if ($ChkDryRun.IsChecked) {
                $row.Status = "Dry-run"
            } else {
                try {
                    Rename-Item -Path $oldPath -NewName $proposed -ErrorAction Stop
                    $row.Status = "Renamed"
                    $ops += [PSCustomObject]@{ Old=$oldPath; New=$newPath }
                }
                catch {
                    $row.Status = "Error: $($_.Exception.Message)"
                }
            }
        } else {
            $row.Status = "No change"
            $row.Apply  = $false
        }
    }

    $global:LastOperations = $ops
    $DgPreview.Items.Refresh()
    $TxtStatus.Text = if ($ChkDryRun.IsChecked) {
        "Dry-run complete."
    } elseif ($ops.Count -gt 0) {
        "Apply complete: $($ops.Count) file(s) renamed."
    } else {
        "No changes applied."
    }
})

# Undo last batch
$BtnUndo.Add_Click({
    if ($global:LastOperations.Count -eq 0) {
        $TxtStatus.Text = "Nothing to undo."
        return
    }

    $undoCount = 0
    foreach ($op in $global:LastOperations) {
        if (Test-Path $op.New) {
            try {
                Rename-Item -Path $op.New -NewName (Split-Path $op.Old -Leaf) -ErrorAction Stop
                $undoCount++
            }
            catch {
                Write-Warning "Undo failed for $($op.New): $_"
            }
        }
    }

    $global:LastOperations = @()
    $TxtStatus.Text = "Undo complete: $undoCount file(s) reverted."
})

# Export CSV
$BtnExportCsv.Add_Click({
    $TxtStatus.Text = "Exporting CSV..."
    $dialog = New-Object Microsoft.Win32.SaveFileDialog
    $dialog.Filter = "CSV files (*.csv)|*.csv"
    $dialog.FileName = "FileCleanerExport.csv"
    if ($dialog.ShowDialog()) {
        $DgPreview.Items | Export-Csv -Path $dialog.FileName -NoTypeInformation -Encoding UTF8
        $TxtStatus.Text = "CSV exported to $($dialog.FileName)"
    } else {
        $TxtStatus.Text = "Export canceled."
    }
})

# Reset
$BtnReset.Add_Click({
    $TxtStatus.Text = "Resetting..."
    $DgPreview.ItemsSource   = $null
    $BtnApply.IsEnabled      = $false
    $BtnExportCsv.IsEnabled  = $false

    $TxtOldPrefix.Clear()
    $TxtNewPrefix.Clear()
    $TxtDetectedPrefix.Clear()
    $TxtSeason.Clear()
    $TxtStart.Clear()
    $TxtCustomClean.Clear()
    $TxtCustomExt.Clear()

    $TxtStatus.Text = "Reset complete."
})

# Launch CombineVideos helper
$BtnCombineVideos.Add_Click({
  $combinePath = Join-Path $script:FileCleanerRoot "CombineVideos.ps1"
  if (-not (Test-Path $combinePath -PathType Leaf)) {
    $TxtStatus.Text = "CombineVideos.ps1 not found next to FileCleaner."
    return
  }

  $pwsh = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
  try {
    Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-STA','-File',"`"$combinePath`"") -WorkingDirectory $script:FileCleanerRoot
    $TxtStatus.Text = "Launching Combine Videos..."
  }
  catch {
    $TxtStatus.Text = "Failed to launch Combine Videos: $($_.Exception.Message)"
  }
})

# --- Part 6: Show the window ---
$BtnExit.Add_Click({ $window.Close() })
$window.ShowDialog() | Out-Null