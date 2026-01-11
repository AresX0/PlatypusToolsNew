# VideoEditor.ps1 - Combined File Cleaner (WPF) and Video Combiner (WPF)
Add-Type -AssemblyName PresentationCore,PresentationFramework
Add-Type -AssemblyName System.Windows.Forms

# Ensure STA
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
  $target = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Definition }
  $pwsh   = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
  if (Test-Path $pwsh -PathType Leaf -and $target) {
    Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-STA','-File',"`"$target`"") -WorkingDirectory (Get-Location)
    exit
  }
}

# ---------- XAML ----------
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Video Editor" Height="780" Width="1280"
        MinHeight="640" MinWidth="1024"
        WindowStartupLocation="CenterScreen">
  <DockPanel>
    <StatusBar DockPanel.Dock="Bottom" Margin="0,4,0,0">
      <StatusBarItem>
        <TextBlock Name="TxtStatus" Text="Ready."/>
      </StatusBarItem>
    </StatusBar>
    <TabControl Name="MainTabs">
      <!-- File Cleaner Tab (based on filecleaner1.32) -->
      <TabItem Header="File Cleaner">
        <Grid Margin="10">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="420" MinWidth="300"/>
            <ColumnDefinition Width="5"/>
            <ColumnDefinition Width="*"/>
          </Grid.ColumnDefinitions>

          <ScrollViewer Grid.Column="0"
                        VerticalScrollBarVisibility="Auto"
                        HorizontalScrollBarVisibility="Auto">
            <StackPanel Orientation="Vertical" Margin="0,0,10,0">
              <GroupBox Header="Folder Selection" Margin="0,0,0,10">
                <StackPanel Margin="8">
                  <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
                    <TextBox Name="TxtFolder" Width="320" Margin="0,0,6,0"/>
                    <Button Name="BtnBrowse" Content="Browse..." Width="90"/>
                  </StackPanel>
                  <CheckBox Name="ChkRecurse" Content="Include Subfolders"/>
                </StackPanel>
              </GroupBox>

              <GroupBox Header="Prefix Options" Margin="0,0,0,10">
                <StackPanel Margin="8" Orientation="Vertical">
                  <CheckBox Name="ChkChangePrefix" Content="Change Prefix (Old -> New)" Margin="0,0,0,6"/>
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

          <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Background="LightGray" ResizeBehavior="PreviousAndNext" ResizeDirection="Columns"/>

          <Grid Grid.Column="2">
            <Grid.RowDefinitions>
              <RowDefinition Height="*"/>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <DataGrid Name="DgPreview" AutoGenerateColumns="False" CanUserAddRows="False" CanUserResizeColumns="True" ScrollViewer.HorizontalScrollBarVisibility="Visible" ScrollViewer.VerticalScrollBarVisibility="Visible" Grid.Row="0" Margin="0,0,0,10">
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
              <Button Name="BtnReset" Content="Reset" Width="100" Margin="4"/>
            </StackPanel>

            <StatusBar Grid.Row="2" Margin="0,10,0,0">
              <StatusBarItem>
                <TextBlock Text="File Cleaner"/>
              </StatusBarItem>
            </StatusBar>
          </Grid>
        </Grid>
      </TabItem>

      <!-- Video Combiner Tab -->
      <TabItem Header="Video Combiner">
        <Grid Margin="10">
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
          </Grid.RowDefinitions>

          <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,8" HorizontalAlignment="Left">
            <Button Name="BtnCombBrowse" Content="Browse Folder" Width="120" Margin="4"/>
            <Button Name="BtnCombSelectAll" Content="Select All" Width="100" Margin="4"/>
            <Button Name="BtnCombSelectNone" Content="Select None" Width="100" Margin="4"/>
            <Button Name="BtnCombUp" Content="Move Up" Width="90" Margin="4"/>
            <Button Name="BtnCombDown" Content="Move Down" Width="90" Margin="4"/>
          </StackPanel>

          <DataGrid Name="DgCombine" Grid.Row="1" AutoGenerateColumns="False" CanUserAddRows="False" CanUserResizeColumns="True" Margin="0,0,0,8" SelectionMode="Single">
            <DataGrid.Columns>
              <DataGridCheckBoxColumn Header="Use" Binding="{Binding Apply}" Width="60"/>
              <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="220"/>
              <DataGridTextColumn Header="Path" Binding="{Binding Path}" Width="*"/>
            </DataGrid.Columns>
          </DataGrid>

          <StackPanel Orientation="Horizontal" Grid.Row="2" Margin="0,0,0,8" HorizontalAlignment="Left">
            <Button Name="BtnCombPreview" Content="Preview Encodings" Width="140" Margin="4"/>
            <Button Name="BtnCombNormalize" Content="Normalize (H264/AAC)" Width="160" Margin="4"/>
            <Button Name="BtnCombCombine" Content="Combine" Width="120" Margin="4"/>
          </StackPanel>

          <TextBox Name="TxtCombLog" Grid.Row="3" Height="140" Margin="0" IsReadOnly="True" TextWrapping="NoWrap" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"/>
        </Grid>
      </TabItem>
    </TabControl>
  </DockPanel>
</Window>
"@

# ---------- Load XAML ----------
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

# Shared status helper
function Update-Status {
  param([string]$text)
  $TxtStatus.Text = $text
}

# File Cleaner controls
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
$DgPreview          = $window.FindName("DgPreview")
$BtnSelectAll       = $window.FindName("BtnSelectAll")
$BtnSelectNone      = $window.FindName("BtnSelectNone")
$BtnScan            = $window.FindName("BtnScan")
$BtnApply           = $window.FindName("BtnApply")
$BtnUndo            = $window.FindName("BtnUndo")
$BtnExportCsv       = $window.FindName("BtnExportCsv")
$BtnReset           = $window.FindName("BtnReset")
$TxtStatus          = $window.FindName("TxtStatus")

# Video Combiner controls
$BtnCombBrowse      = $window.FindName("BtnCombBrowse")
$BtnCombSelectAll   = $window.FindName("BtnCombSelectAll")
$BtnCombSelectNone  = $window.FindName("BtnCombSelectNone")
$BtnCombUp          = $window.FindName("BtnCombUp")
$BtnCombDown        = $window.FindName("BtnCombDown")
$BtnCombPreview     = $window.FindName("BtnCombPreview")
$BtnCombNormalize   = $window.FindName("BtnCombNormalize")
$BtnCombCombine     = $window.FindName("BtnCombCombine")
$DgCombine          = $window.FindName("DgCombine")
$TxtCombLog         = $window.FindName("TxtCombLog")

$script:FileCleanerRoot = if ($PSCommandPath) { Split-Path $PSCommandPath } else { Get-Location }

# ---------- File Cleaner Logic (from filecleaner1.32, trimmed where possible) ----------
$toolsDetected = @()
if (Get-Command ffprobe -ErrorAction SilentlyContinue) { $toolsDetected += "ffprobe" }
if (Get-Command exiftool -ErrorAction SilentlyContinue) { $toolsDetected += "exiftool" }
if ($toolsDetected.Count -eq 0) {
  Update-Status "Ready. (No metadata tools detected - install ffprobe or exiftool for metadata support)"
} else {
  Update-Status "Ready. Metadata tools detected: $($toolsDetected -join ', ')"
}

function Get-FilteredFiles {
    param([string]$Path,[switch]$Recurse)
    if (-not (Test-Path $Path)) { return @() }
    $extensions = @()
    if ($ChkVideo.IsChecked)     { $extensions += '.mp4','.mkv','.avi','.mov','.wmv','.m4v' }
    if ($ChkPictures.IsChecked)  { $extensions += '.jpg','.jpeg','.png','.gif','.webp','.tiff' }
    if ($ChkDocuments.IsChecked) { $extensions += '.pdf','.doc','.docx','.txt','.md','.rtf' }
    if ($ChkAudio.IsChecked)     { $extensions += '.mp3','.flac','.wav','.m4a','.aac' }
    if ($ChkArchives.IsChecked)  { $extensions += '.zip','.rar','.7z','.tar','.gz' }
    if ($TxtCustomExt.Text) {
        $custom = $TxtCustomExt.Text -split ',' | ForEach-Object { $_.Trim().ToLower() }
        $custom = $custom | ForEach-Object { if ($_ -and $_[0] -ne '.') { "." + $_ } else { $_ } }
        $extensions += $custom
    }
    $extensions = $extensions | Select-Object -Unique
    $files = Get-ChildItem -Path $Path -File -Recurse:$Recurse -ErrorAction SilentlyContinue
    if ($extensions.Count -eq 0) { return $files }
    return $files | Where-Object { $extensions -contains $_.Extension.ToLower() }
}

function Get-ProposedName {
    param([System.IO.FileInfo]$File,[int]$EpisodeNumber,[switch]$ForceEpisode)
    $base = [System.IO.Path]::GetFileNameWithoutExtension($File.Name)
    $ext  = $File.Extension
    $originalBase = $base
    if ($Chk720p.IsChecked)  { $base = $base -replace '(?i)720p','' }
    if ($Chk1080p.IsChecked) { $base = $base -replace '(?i)1080p','' }
    if ($Chk4k.IsChecked)    { $base = $base -replace '(?i)4k','' }
    if ($ChkHD.IsChecked)    { $base = $base -replace '(?i)hd','' }
    if ($TxtCustomClean.Text) {
      foreach ($token in ($TxtCustomClean.Text -split ',')) {
        $t = $token.Trim(); if ($t) { $base = $base -replace '(?i)' + [Regex]::Escape($t), '' }
      }
    }
    $base = $base.Trim(); if (-not $base) { $base = $originalBase }
    $episodeDigits = 2; if ($CmbEpisodeDigits.SelectedItem -and $CmbEpisodeDigits.SelectedItem.Content) { [void][int]::TryParse($CmbEpisodeDigits.SelectedItem.Content.ToString(), [ref]$episodeDigits) }
    $seasonDigits = 2; if ($CmbSeasonDigits.SelectedItem -and $CmbSeasonDigits.SelectedItem.Content) { [void][int]::TryParse($CmbSeasonDigits.SelectedItem.Content.ToString(), [ref]$seasonDigits) }
    $oldPrefix = if ($TxtOldPrefix.Text) { $TxtOldPrefix.Text.Trim() } else { "" }
    $newPrefix = if ($TxtNewPrefix.Text) { $TxtNewPrefix.Text.Trim() } else { "" }
    $detected  = if ($TxtDetectedPrefix.Text) { $TxtDetectedPrefix.Text.Trim() } else { "" }
    $originalBaseNoOld = $originalBase
    if ($oldPrefix -and $originalBaseNoOld.StartsWith($oldPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { $originalBaseNoOld = $originalBaseNoOld.Substring($oldPrefix.Length) }
    if ($oldPrefix -and $base.StartsWith($oldPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { $base = $base.Substring($oldPrefix.Length) }
    if ($ChkChangePrefix.IsChecked -and $newPrefix) { $base = $newPrefix + $base }
    elseif ($ChkAddPrefixAll.IsChecked -and $newPrefix -and -not $base.StartsWith($newPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { $base = $newPrefix + $base }
    elseif (-not $oldPrefix -and $detected -and -not $base.StartsWith($detected, [System.StringComparison]::OrdinalIgnoreCase)) { $base = $detected + $base }
    $prefix = ""; foreach ($p in @($newPrefix, $detected, $oldPrefix)) { if ($p -and $base.StartsWith($p)) { $prefix = $p; break } }
    $shouldInferPrefix = $ChkAddPrefixAll.IsChecked -or $ForceEpisode -or $ChkAddEpisode.IsChecked
    if (-not $prefix -and $shouldInferPrefix -and $base.Length -gt 0) {
        if ($base -match '^(?<lead>[^ _\-]+)(?<rest>.*)$') { $prefix = $matches.lead }
        if (-not $prefix) { $prefix = $base }
    }
    $remainder = $base; if ($prefix) { $remainder = $base.Substring([Math]::Min($prefix.Length, $base.Length)).Trim() }
    $fallbackRemainder = if ($prefix -and $originalBase.StartsWith($prefix)) { $originalBase.Substring([Math]::Min($prefix.Length, $originalBase.Length)).Trim() } else { $originalBase }
    if (-not $remainder) { $remainder = $fallbackRemainder }
    $tailName = if ($ChkAddPrefixAll.IsChecked) { $originalBaseNoOld } else { $remainder }
    $remainder = ($remainder -replace '(?i)\bS\d{1,4}\b','').Trim()
    $remainder = ($remainder -replace '(?i)\bE\d{1,4}\b','').Trim()
    $seasonStr = ""; $episodeStr = ""
    if ($ChkAddSeason.IsChecked) { $seasonNum = 0; [void][int]::TryParse($TxtSeason.Text, [ref]$seasonNum); if ($seasonNum -gt 0) { $seasonStr = "S" + $seasonNum.ToString().PadLeft($seasonDigits,'0') } }
    if (($ForceEpisode -or $ChkAddEpisode.IsChecked) -and $prefix) { $episodeStr = "E" + $EpisodeNumber.ToString().PadLeft($episodeDigits,'0') }
    if ($seasonStr -and $episodeStr) { $tag = if ($ChkSeasonBeforeEpisode.IsChecked) { "$seasonStr$episodeStr" } else { "$episodeStr$seasonStr" } }
    elseif ($seasonStr -or $episodeStr) { $tag = "$seasonStr$episodeStr" } else { $tag = "" }
    if ($prefix -or $tag) {
      $segments = @(); if ($prefix) { $segments += $prefix }; if ($tag) { $segments += $tag }; $segments += $tailName; $finalNameNoExt = ($segments -join "-").Trim()
    } else { $finalNameNoExt = $tailName }
    $finalNameNoExt = $finalNameNoExt -replace '\s+','-'
    $finalNameNoExt = $finalNameNoExt -replace '-{2,}','-'
    $finalNameNoExt = $finalNameNoExt.Trim('-')
    $safeName = $finalNameNoExt.Trim(); if (-not $safeName) { $safeName = $originalBase }
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
                foreach ($line in $ffout) { if ($line -match "^(?<k>[^=]+)=(?<v>.+)$") { $tags[$matches.k] = $matches.v } }
              if ($ChkUseAudioMetadata.IsChecked) { $summary = ((@($tags.artist, $tags.album, $tags.title) -join " | ").Trim(" |")) }
              elseif ($ChkUseVideoMetadata.IsChecked) { $summary = ((@($tags.show, ("S{0}E{1}" -f $tags.season_number, $tags.episode_id), $tags.title) -join " | ").Trim(" |")) }
            }
        } catch { }
    }
    elseif (Get-Command exiftool -ErrorAction SilentlyContinue) {
          try { $exif = & exiftool -s -s -s -Artist -Album -Title -TVShowName -SeasonNumber -EpisodeNumber -- "$($File.FullName)" 2>$null; if ($exif) { $summary = ($exif | Where-Object { $_ }) -join " | " } } catch { }
    }
    return $summary
}

# Event handlers - File Cleaner
$BtnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select a folder to process"
    $dialog.ShowNewFolderButton = $false
    $result = $dialog.ShowDialog()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) { $TxtFolder.Text = $dialog.SelectedPath; Update-Status "Folder selected: $($dialog.SelectedPath)" } else { Update-Status "Browse canceled." }
})

$BtnSelectAll.Add_Click({ foreach ($row in $DgPreview.Items) { try { $row.Apply = $true } catch {} }; $DgPreview.Items.Refresh() })
$BtnSelectNone.Add_Click({ foreach ($row in $DgPreview.Items) { try { $row.Apply = $false } catch {} }; $DgPreview.Items.Refresh() })

$BtnScan.Add_Click({
    Update-Status "Scanning..."
    $startNum = 1
    if ($ChkAddEpisode.IsChecked -or $ChkRenumberAll.IsChecked) { [void][int]::TryParse($TxtStart.Text, [ref]$startNum); if ($startNum -lt 1) { $startNum = 1 } }
    $files = Get-FilteredFiles -Path $TxtFolder.Text -Recurse:$ChkRecurse.IsChecked
    if ($ChkOnlyIfOldPrefix.IsChecked -and $TxtOldPrefix.Text) { $oldPref = $TxtOldPrefix.Text.Trim(); $files = $files | Where-Object { $_.BaseName.StartsWith($oldPref, [System.StringComparison]::OrdinalIgnoreCase) } }
    if ($ChkRenumberAll.IsChecked) { $files = $files | Sort-Object Name }
    $preview = @(); $episodeCounter = $startNum; $hasEpisodeOps = $ChkRenumberAll.IsChecked -or $ChkAddEpisode.IsChecked
    foreach ($f in $files) {
      if ($ChkRenumberAll.IsChecked) { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter -ForceEpisode; $episodeCounter++ }
      elseif ($ChkAddEpisode.IsChecked) { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter; $episodeCounter++ }
      else { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter }
      $status = if (-not $hasEpisodeOps -and $proposed -eq $f.Name) { "No change" } else { "Pending" }
      $preview += [PSCustomObject]@{ Apply=($status -ne "No change"); Original=$f.Name; Proposed=$proposed; Status=$status; Directory=$f.DirectoryName; MetaSummary=Get-MetadataSummary -File $f }
    }
    $DgPreview.ItemsSource = $preview
    $hasPending = (@($preview | Where-Object { $_.Status -ne "No change" })).Count -gt 0
    $BtnApply.IsEnabled     = ($preview.Count -gt 0 -and $hasPending)
    $BtnExportCsv.IsEnabled = ($preview.Count -gt 0)
    Update-Status (if ($preview.Count -gt 0) { "Preview complete: $($preview.Count) files." } else { "No files matched filters." })
})

$global:LastOperations = @()
$BtnApply.Add_Click({
    Update-Status "Applying changes..."; $ops = @()
    foreach ($row in $DgPreview.Items) {
        if (-not $row.Apply -or $row.Status -eq "No change") { continue }
        $fileObj = Get-Item (Join-Path $row.Directory $row.Original)
        $episodeNum = 0; [void][int]::TryParse(($row.Proposed -replace '.*E(\d+).*','$1'), [ref]$episodeNum)
        if ($ChkRenumberAll.IsChecked) { $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum -ForceEpisode }
        elseif ($ChkAddEpisode.IsChecked) { $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum }
        else { $proposed = Get-ProposedName -File $fileObj -EpisodeNumber $episodeNum }
        if ($row.Original -ne $proposed) {
            $oldPath = Join-Path $row.Directory $row.Original; $newPath = Join-Path $row.Directory $proposed
            if ($ChkDryRun.IsChecked) { $row.Status = "Dry-run" }
            else {
                try { Rename-Item -Path $oldPath -NewName $proposed -ErrorAction Stop; $row.Status = "Renamed"; $ops += [PSCustomObject]@{ Old=$oldPath; New=$newPath } }
                catch { $row.Status = "Error: $($_.Exception.Message)" }
            }
        } else { $row.Status = "No change"; $row.Apply = $false }
    }
    $global:LastOperations = $ops; $DgPreview.Items.Refresh();
    Update-Status (if ($ChkDryRun.IsChecked) { "Dry-run complete." } elseif ($ops.Count -gt 0) { "Apply complete: $($ops.Count) renamed." } else { "No changes applied." })
})

$BtnUndo.Add_Click({
    if ($global:LastOperations.Count -eq 0) { Update-Status "Nothing to undo."; return }
    $undoCount = 0
    foreach ($op in $global:LastOperations) {
        if (Test-Path $op.New) {
            try { Rename-Item -Path $op.New -NewName (Split-Path $op.Old -Leaf) -ErrorAction Stop; $undoCount++ }
            catch { Write-Warning "Undo failed for $($op.New): $_" }
        }
    }
    $global:LastOperations = @(); Update-Status "Undo complete: $undoCount file(s) reverted."
})

$BtnExportCsv.Add_Click({
    Update-Status "Exporting CSV..."; $dialog = New-Object Microsoft.Win32.SaveFileDialog; $dialog.Filter = "CSV files (*.csv)|*.csv"; $dialog.FileName = "FileCleanerExport.csv"
    if ($dialog.ShowDialog()) { $DgPreview.Items | Export-Csv -Path $dialog.FileName -NoTypeInformation -Encoding UTF8; Update-Status "CSV exported to $($dialog.FileName)" } else { Update-Status "Export canceled." }
})

$BtnReset.Add_Click({
    Update-Status "Resetting..."; $DgPreview.ItemsSource = $null; $BtnApply.IsEnabled = $false; $BtnExportCsv.IsEnabled = $false
    $TxtOldPrefix.Clear(); $TxtNewPrefix.Clear(); $TxtDetectedPrefix.Clear(); $TxtSeason.Clear(); $TxtStart.Clear(); $TxtCustomClean.Clear(); $TxtCustomExt.Clear();
    Update-Status "Reset complete."
})

# ---------- Video Combiner Logic ----------
$CombItems = New-Object System.Collections.ArrayList
$supportedCombExt = @('.mp4','.mkv','.avi','.mov','.wmv','.webm','.flv')
$targetDir = "C:\\convert"
$ffmpegPath  = Join-Path $targetDir "ffmpeg.exe"
$ffprobePath = Join-Path $targetDir "ffprobe.exe"

function Ensure-Tools {
    if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }
    if (-not (Test-Path -LiteralPath $ffmpegPath) -or -not (Test-Path -LiteralPath $ffprobePath)) {
        [System.Windows.MessageBox]::Show("FFmpeg/ffprobe not found in $targetDir. Download portable build (e.g., gyan.dev) and place ffmpeg.exe & ffprobe.exe there.","Missing FFmpeg",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning)
        return $false
    }
    return $true
}

function Refresh-CombGrid { $DgCombine.ItemsSource = $null; $DgCombine.ItemsSource = $CombItems }

function Load-CombFiles {
    param([string]$Folder)
    if (-not (Test-Path $Folder)) { Update-Status "Folder not found."; return }
    $CombItems.Clear() | Out-Null
    Get-ChildItem -Path $Folder -File | Where-Object { $supportedCombExt -contains $_.Extension.ToLower() } | ForEach-Object {
        [void]$CombItems.Add([PSCustomObject]@{ Apply=$false; Name=$_.Name; Path=$_.FullName })
    }
    Refresh-CombGrid
    Update-Status "Loaded $($CombItems.Count) videos"
    $TxtCombLog.Text = "Loaded $($CombItems.Count) video files from $Folder"
}

function Get-CombChecked {
    return @($CombItems | Where-Object { $_.Apply })
}

function Move-CombItem {
    param([int]$Offset)
    $selected = $DgCombine.SelectedItem
    if (-not $selected) { return }
    $index = $CombItems.IndexOf($selected)
    $target = $index + $Offset
    if ($target -lt 0 -or $target -ge $CombItems.Count) { return }
    $CombItems.RemoveAt($index)
    $CombItems.Insert($target, $selected)
    Refresh-CombGrid
    $DgCombine.SelectedIndex = $target
}

function Get-VideoInfo($file) {
    $args = @('-v','error','-select_streams','v:0','-show_entries','stream=codec_name,width,height,r_frame_rate,format=duration','-of','default=noprint_wrappers=1',"$file")
    & $ffprobePath @args
}

function Compare-Encodings($files) {
    $details = @()
    foreach ($f in $files) {
        $info = Get-VideoInfo $f.Path
        $codec    = ($info | Select-String "codec_name="     | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $width    = ($info | Select-String "width="          | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $height   = ($info | Select-String "height="         | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $fps      = ($info | Select-String "r_frame_rate="   | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $duration = ($info | Select-String "duration="       | ForEach-Object { $_.ToString().Split("=")[1] }) -join ""
        $details += [PSCustomObject]@{ File=[System.IO.Path]::GetFileName($f.Path); FullPath=$f.Path; Codec=$codec; Width=$width; Height=$height; FPS=$fps; Duration=$duration }
    }
    return $details
}

$BtnCombBrowse.Add_Click({ $fb = New-Object System.Windows.Forms.FolderBrowserDialog; if ($fb.ShowDialog() -eq 'OK') { Load-CombFiles -Folder $fb.SelectedPath } })
$BtnCombSelectAll.Add_Click({ foreach ($item in $CombItems) { $item.Apply = $true }; Refresh-CombGrid })
$BtnCombSelectNone.Add_Click({ foreach ($item in $CombItems) { $item.Apply = $false }; Refresh-CombGrid })
$BtnCombUp.Add_Click({ Move-CombItem -Offset -1 })
$BtnCombDown.Add_Click({ Move-CombItem -Offset 1 })

$BtnCombPreview.Add_Click({
    if (-not (Ensure-Tools)) { return }
    $checked = Get-CombChecked
    if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
    $TxtCombLog.Clear(); Update-Status "Reading metadata..."
    $details = Compare-Encodings $checked
    foreach ($d in $details) { $TxtCombLog.AppendText(("{0,-40} | {1,-8} | {2}x{3} | FPS:{4,-10} | Dur:{5}" -f $d.File,$d.Codec,$d.Width,$d.Height,$d.FPS,$d.Duration) + "`r`n") }
    $codecMismatch = ($details.Codec | Select-Object -Unique).Count -gt 1
    $resMismatch   = ($details.Width | Select-Object -Unique).Count -gt 1 -or ($details.Height | Select-Object -Unique).Count -gt 1
    $fpsMismatch   = ($details.FPS | Select-Object -Unique).Count -gt 1
    if ($codecMismatch -or $resMismatch -or $fpsMismatch) { $TxtCombLog.AppendText("Differences detected -> normalize recommended.`r`n"); Update-Status "Differences detected" }
    else { $TxtCombLog.AppendText("Encodings compatible -> safe to combine.`r`n"); Update-Status "Encodings compatible" }
})

$BtnCombNormalize.Add_Click({
    if (-not (Ensure-Tools)) { return }
    $checked = Get-CombChecked
    if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
    $TxtCombLog.Clear(); $TxtCombLog.AppendText("Normalizing to H.264/AAC in $targetDir ...`r`n"); Update-Status "Normalizing..."
    $fixed = @()
    foreach ($f in $checked) {
        $outFile = Join-Path $targetDir ("fixed_" + [System.IO.Path]::GetFileNameWithoutExtension($f.Path) + ".mp4")
        $args = @('-y','-i',$f.Path,'-c:v','libx264','-preset','fast','-crf','23','-c:a','aac','-b:a','192k',$outFile)
        $TxtCombLog.AppendText("-> $([System.IO.Path]::GetFileName($f.Path)) -> $([System.IO.Path]::GetFileName($outFile))`r`n")
        & $ffmpegPath @args
        $fixed += $outFile
    }
    $CombItems.Clear() | Out-Null; foreach ($f in $fixed) { [void]$CombItems.Add([PSCustomObject]@{ Apply=$true; Name=[System.IO.Path]::GetFileName($f); Path=$f }) }
    Refresh-CombGrid
    $TxtCombLog.AppendText("Done. Files normalized for concat in $targetDir.`r`n"); Update-Status "Normalization finished"
})

$BtnCombCombine.Add_Click({
    if (-not (Ensure-Tools)) { return }
    $checked = Get-CombChecked
    if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
    $sfd = New-Object System.Windows.Forms.SaveFileDialog; $sfd.Filter = "MP4 files|*.mp4"; $sfd.FileName = "combined.mp4"
    if ($sfd.ShowDialog() -ne 'OK') { return }
    $listFile = Join-Path ([System.IO.Path]::GetDirectoryName($sfd.FileName)) "videolist.txt"
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $lines = @(); foreach ($f in $checked) { $normalized = [System.IO.Path]::GetFullPath($f.Path); $escaped = ($normalized -replace "'", "'\''"); $lines += "file '$escaped'" }
    [System.IO.File]::WriteAllLines($listFile, $lines, $utf8NoBom)
    $TxtCombLog.Clear(); $TxtCombLog.AppendText("Combining into: $($sfd.FileName)`r`nList file: $listFile`r`n")
    $idx = 1; foreach ($f in $checked) { $TxtCombLog.AppendText(("  {0}. {1}" -f $idx, [System.IO.Path]::GetFileName($f.Path)) + "`r`n"); $idx++ }
    Update-Status "Combining with ffmpeg..."
    & $ffmpegPath -f concat -safe 0 -i $listFile -c copy $sfd.FileName
    Remove-Item $listFile -Force
    [System.Windows.MessageBox]::Show("Videos combined successfully into $($sfd.FileName)")
    Update-Status "Combine complete"
})

# ---------- Show window ----------
$window.ShowDialog() | Out-Null
