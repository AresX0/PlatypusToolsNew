# VideoEditor.ps1 - Unified File Cleaner and Video Combiner
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

$script:Root = if ($PSCommandPath) { Split-Path $PSCommandPath } else { Get-Location }

# ---------------- XAML ----------------
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Video Editor" Height="800" Width="1280"
        MinHeight="640" MinWidth="1024"
        WindowStartupLocation="CenterScreen">
  <DockPanel>
    <StatusBar DockPanel.Dock="Bottom" Margin="0,4,0,0">
      <StatusBarItem>
        <TextBlock Name="TxtStatus" Text="Ready."/>
      </StatusBarItem>
      <StatusBarItem HorizontalAlignment="Right">
        <Button Name="BtnExit" Content="Exit" Width="70"/>
      </StatusBarItem>
    </StatusBar>

    <TabControl>
      <!-- File Cleaner Tab -->
      <TabItem Header="File Cleaner">
        <Grid Margin="10">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="420" MinWidth="300"/>
            <ColumnDefinition Width="5"/>
            <ColumnDefinition Width="*"/>
          </Grid.ColumnDefinitions>

          <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">
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
                      <ColumnDefinition Width="110"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Detected Prefix:" Grid.Column="0"/>
                    <TextBox Name="TxtDetectedPrefix" Grid.Column="1" Margin="4,0"/>
                    <Button Name="BtnDetectPrefix" Content="Detect" Grid.Column="2" Width="90" Margin="6,0,0,0"/>
                  </Grid>
                  <Grid Margin="0,0,0,6">
                    <Grid.ColumnDefinitions>
                      <ColumnDefinition Width="120"/>
                      <ColumnDefinition Width="*"/>
                    </Grid.ColumnDefinitions>
                    <Label Content="Ignore Prefix:" Grid.Column="0"/>
                    <TextBox Name="TxtIgnorePrefix" Grid.Column="1" Margin="4,0"/>
                  </Grid>
                  <CheckBox Name="ChkAddPrefixAll" Content="Add Prefix to All Files"/>
                  <CheckBox Name="ChkNormalizePrefixCase" Content="Normalize prefix casing (force apply case-only changes)"/>
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
                  <TextBlock Text="Name normalization:" FontWeight="Bold" Margin="0,8,0,0"/>
                  <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                    <ComboBox Name="CmbSpaceReplace" Width="280">
                      <ComboBoxItem Content="Spaces -> '-'" Tag="space-to-dash" IsSelected="True"/>
                      <ComboBoxItem Content="Spaces -> '_'" Tag="space-to-underscore"/>
                      <ComboBoxItem Content="Remove spaces" Tag="space-remove"/>
                      <ComboBoxItem Content="'-' -> spaces" Tag="dash-to-space"/>
                      <ComboBoxItem Content="'-' -> '_'" Tag="dash-to-underscore"/>
                      <ComboBoxItem Content="'_' -> '-'" Tag="underscore-to-dash"/>
                      <ComboBoxItem Content="'_' -> spaces" Tag="underscore-to-space"/>
                    </ComboBox>
                    <Button Name="BtnSpaceCleanup" Content="Normalize Names" Width="140" Margin="8,0,0,0"/>
                  </StackPanel>
                </StackPanel>
              </GroupBox>

            </StackPanel>
          </ScrollViewer>

          <GridSplitter Grid.Column="1" Width="5" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" Background="LightGray" ResizeBehavior="PreviousAndNext" ResizeDirection="Columns"/>

          <ScrollViewer Grid.Column="2"
                        VerticalScrollBarVisibility="Auto"
                        HorizontalScrollBarVisibility="Auto">
            <Grid>
              <Grid.RowDefinitions>
                <RowDefinition Height="*"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
              </Grid.RowDefinitions>

              <DataGrid Name="DgPreview"
                        AutoGenerateColumns="False"
                        CanUserAddRows="False"
                        CanUserResizeColumns="True"
                        ScrollViewer.CanContentScroll="False"
                        ScrollViewer.HorizontalScrollBarVisibility="Visible"
                        ScrollViewer.VerticalScrollBarVisibility="Auto"
                        HorizontalAlignment="Stretch"
                        VerticalAlignment="Stretch"
                        Grid.Row="0" Margin="0,0,0,10">
                <DataGrid.Columns>
                  <DataGridCheckBoxColumn Header="Apply" Binding="{Binding Apply}" Width="100"/>
                  <DataGridTextColumn Header="Original" Binding="{Binding Original}" Width="300"/>
                  <DataGridTextColumn Header="Proposed" Binding="{Binding Proposed}" Width="300"/>
                  <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="180"/>
                  <DataGridTextColumn Header="Directory" Binding="{Binding Directory}" Width="400"/>
                  <DataGridTextColumn Header="Meta" Binding="{Binding MetaSummary}" Width="400"/>
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
          </ScrollViewer>
        </Grid>
      </TabItem>

      <!-- Video Combiner Tab -->
      <TabItem Header="Video Combiner">
        <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto">
          <Grid Margin="10">
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="*"/>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="Auto"/>
            </Grid.RowDefinitions>

            <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,8" HorizontalAlignment="Left">
              <TextBlock Text="Tool Folder:" VerticalAlignment="Center" Margin="0,0,6,0"/>
              <TextBox Name="TxtToolDir" Width="360" Margin="0,0,6,0" ToolTip="Folder containing ffmpeg, ffprobe, exiftool"/>
              <Button Name="BtnToolDirBrowse" Content="Browse..." Width="90" Margin="0,0,6,0"/>
              <TextBlock Text="Used for ffmpeg/ffprobe/exiftool" VerticalAlignment="Center" Opacity="0.7"/>
            </StackPanel>

            <StackPanel Orientation="Horizontal" Grid.Row="1" Margin="0,0,0,8" HorizontalAlignment="Left">
              <Button Name="BtnCombBrowse" Content="Browse Folder" Width="120" Margin="4"/>
              <Button Name="BtnCombSelectAll" Content="Select All" Width="100" Margin="4"/>
              <Button Name="BtnCombSelectNone" Content="Select None" Width="100" Margin="4"/>
              <Button Name="BtnCombUp" Content="Move Up" Width="90" Margin="4"/>
              <Button Name="BtnCombDown" Content="Move Down" Width="90" Margin="4"/>
            </StackPanel>

            <DataGrid Name="DgCombine" Grid.Row="2" AutoGenerateColumns="False" CanUserAddRows="False" CanUserResizeColumns="True" Margin="0,0,0,8" SelectionMode="Single" ScrollViewer.VerticalScrollBarVisibility="Visible">
              <DataGrid.Columns>
                <DataGridCheckBoxColumn Header="Use" Binding="{Binding Apply}" Width="80"/>
                <DataGridTextColumn Header="Name" Binding="{Binding Name}" Width="260"/>
                <DataGridTextColumn Header="Path" Binding="{Binding Path}" Width="540"/>
              </DataGrid.Columns>
            </DataGrid>

            <StackPanel Orientation="Horizontal" Grid.Row="3" Margin="0,0,0,8" HorizontalAlignment="Left">
              <Button Name="BtnCombPreview" Content="Preview Encodings" Width="140" Margin="4"/>
              <Button Name="BtnCombNormalize" Content="Normalize (H264/AAC)" Width="160" Margin="4"/>
              <Button Name="BtnCombSafe" Content="Safe Combine (Re-encode)" Width="200" Margin="4"/>
              <Button Name="BtnCombCombine" Content="Combine" Width="120" Margin="4"/>
              <ComboBox Name="CmbConvFormat" Width="90" Margin="12,4,4,4">
                <ComboBoxItem Content="MP4" Tag="mp4" IsSelected="True"/>
                <ComboBoxItem Content="WMV" Tag="wmv"/>
              </ComboBox>
              <ComboBox Name="CmbConvRes" Width="110" Margin="4">
                <ComboBoxItem Content="Source" Tag="source" IsSelected="True"/>
                <ComboBoxItem Content="720p" Tag="720p"/>
                <ComboBoxItem Content="1080p" Tag="1080p"/>
                <ComboBoxItem Content="4K" Tag="4k"/>
              </ComboBox>
              <TextBlock Text="Output Folder:" VerticalAlignment="Center" Margin="8,0,4,0"/>
              <TextBox Name="TxtConvOut" Width="260" Margin="0,4,4,4" ToolTip="Folder for converts and combined files"/>
              <Button Name="BtnConvBrowse" Content="Browse..." Width="90" Margin="4"/>
              <Button Name="BtnCombConvert" Content="Convert" Width="100" Margin="4"/>
            </StackPanel>

            <StackPanel Grid.Row="4" Orientation="Horizontal" Margin="0,0,0,6">
              <TextBlock Name="TxtConvStatus" Text="Conversion status: idle" VerticalAlignment="Center" Margin="0,0,8,0"/>
              <ProgressBar Name="ConvProgress" Height="16" Width="200" Minimum="0" Maximum="100" IsIndeterminate="False" Visibility="Collapsed"/>
            </StackPanel>

            <TextBox Name="TxtCombLog" Grid.Row="5" Height="160" Margin="0" IsReadOnly="True" TextWrapping="NoWrap" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" FontFamily="Consolas" FontSize="12"/>
          </Grid>
        </ScrollViewer>
      </TabItem>
    </TabControl>
  </DockPanel>
</Window>
"@

# --------------- Load XAML ---------------
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

function Update-Status {
    param([string]$text)
    $TxtStatus.Text = $text
}

# Controls - File Cleaner
$TxtFolder          = $window.FindName("TxtFolder")
$BtnBrowse          = $window.FindName("BtnBrowse")
$ChkRecurse         = $window.FindName("ChkRecurse")
$ChkChangePrefix    = $window.FindName("ChkChangePrefix")
$TxtOldPrefix       = $window.FindName("TxtOldPrefix")
$TxtNewPrefix       = $window.FindName("TxtNewPrefix")
$TxtDetectedPrefix  = $window.FindName("TxtDetectedPrefix")
$BtnDetectPrefix    = $window.FindName("BtnDetectPrefix")
$TxtIgnorePrefix    = $window.FindName("TxtIgnorePrefix")
$ChkAddPrefixAll    = $window.FindName("ChkAddPrefixAll")
$ChkNormalizePrefixCase = $window.FindName("ChkNormalizePrefixCase")
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
$CmbSpaceReplace    = $window.FindName("CmbSpaceReplace")
$BtnSpaceCleanup    = $window.FindName("BtnSpaceCleanup")
$DgPreview          = $window.FindName("DgPreview")
$BtnSelectAll       = $window.FindName("BtnSelectAll")
$BtnSelectNone      = $window.FindName("BtnSelectNone")
$BtnScan            = $window.FindName("BtnScan")
$BtnApply           = $window.FindName("BtnApply")
$BtnUndo            = $window.FindName("BtnUndo")
$BtnExportCsv       = $window.FindName("BtnExportCsv")
$BtnReset           = $window.FindName("BtnReset")
$BtnExit            = $window.FindName("BtnExit")
$TxtStatus          = $window.FindName("TxtStatus")

# Controls - Combiner
$BtnCombBrowse      = $window.FindName("BtnCombBrowse")
$BtnCombSelectAll   = $window.FindName("BtnCombSelectAll")
$BtnCombSelectNone  = $window.FindName("BtnCombSelectNone")
$BtnCombUp          = $window.FindName("BtnCombUp")
$BtnCombDown        = $window.FindName("BtnCombDown")
$BtnCombPreview     = $window.FindName("BtnCombPreview")
$BtnCombNormalize   = $window.FindName("BtnCombNormalize")
$BtnCombSafe        = $window.FindName("BtnCombSafe")
$BtnCombCombine     = $window.FindName("BtnCombCombine")
$BtnCombConvert     = $window.FindName("BtnCombConvert")
$CmbConvFormat      = $window.FindName("CmbConvFormat")
$CmbConvRes         = $window.FindName("CmbConvRes")
$TxtConvOut         = $window.FindName("TxtConvOut")
$BtnConvBrowse      = $window.FindName("BtnConvBrowse")
$TxtConvStatus      = $window.FindName("TxtConvStatus")
$ConvProgress       = $window.FindName("ConvProgress")
$DgCombine          = $window.FindName("DgCombine")
$TxtCombLog         = $window.FindName("TxtCombLog")
$TxtToolDir         = $window.FindName("TxtToolDir")
$BtnToolDirBrowse   = $window.FindName("BtnToolDirBrowse")

# Tool directory selection and tool resolution
$ToolDirCandidates = @("C:\VideoEditor","C:\convert",$script:Root)
function Resolve-ToolPath {
  param([string]$ToolName,[string]$PreferredDir)
  $exe = if ($ToolName -like '*.exe') { $ToolName } else { "$ToolName.exe" }
  $search = @()
  if ($PreferredDir) { $search += (Join-Path $PreferredDir $exe) }
  $search += (Join-Path "C:\VideoEditor" $exe)
  $search += (Join-Path $script:Root $exe)
  $search += (Join-Path "C:\convert" $exe)
  $cmd = Get-Command $exe -ErrorAction SilentlyContinue
  if ($cmd -and $cmd.Source) { $search += $cmd.Source }
  foreach ($p in ($search | Select-Object -Unique)) { if ($p -and (Test-Path -LiteralPath $p)) { return (Resolve-Path $p).Path } }
  return $null
}

function Refresh-ToolPaths {
  $script:ffmpegPath   = Resolve-ToolPath -ToolName 'ffmpeg'   -PreferredDir $script:ToolDir
  $script:ffprobePath  = Resolve-ToolPath -ToolName 'ffprobe'  -PreferredDir $script:ToolDir
  $script:exiftoolPath = Resolve-ToolPath -ToolName 'exiftool' -PreferredDir $script:ToolDir
}

function Set-ToolDir {
  param([string]$Path)
  if (-not $Path) { return }
  $expanded = [Environment]::ExpandEnvironmentVariables($Path.Trim())
  $script:ToolDir  = $expanded
  $script:targetDir = $expanded
  if ($TxtToolDir) { $TxtToolDir.Text = $expanded }
  Refresh-ToolPaths
  if ($TxtConvOut -and [string]::IsNullOrWhiteSpace($TxtConvOut.Text)) { $TxtConvOut.Text = $expanded }
  $script:toolsDetected = @(); if ($ffprobePath) { $script:toolsDetected += "ffprobe" }; if ($exiftoolPath) { $script:toolsDetected += "exiftool" }
  if ($toolsDetected.Count -eq 0) { Update-Status "Tool folder set. No metadata tools detected (ffprobe/exiftool)." }
  else { Update-Status "Tool folder set. Tools detected: $($toolsDetected -join ', ')" }
}

$initialToolDir = ($ToolDirCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1)
if (-not $initialToolDir) { $initialToolDir = $ToolDirCandidates[0] }
Set-ToolDir $initialToolDir

# --------------- Shared helpers ---------------
$toolsDetected = @()
Refresh-ToolPaths
if ($ffprobePath) { $toolsDetected += "ffprobe" }
if ($exiftoolPath) { $toolsDetected += "exiftool" }
if ($toolsDetected.Count -eq 0) { Update-Status "Ready. (No metadata tools detected - set Tool Folder for ffprobe/exiftool)" }
else { Update-Status "Ready. Metadata tools detected: $($toolsDetected -join ', ')" }

# ---------- File Cleaner Logic ----------
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

  function Strip-ExistingEpisodeTags {
    param([string]$Name)
    if (-not $Name) { return $Name }
    $clean = $Name
    $clean = $clean -replace '(?i)\bS\d{1,4}\s*E\d{1,4}\b',''
    $clean = $clean -replace '(?i)\bE\d{1,4}\b',''
    $clean = $clean -replace '(?i)\bEpisode\s*\d{1,4}\b',''
    $clean = $clean.Trim(' ','-','_','.')
    return $clean
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
    if ($ChkRenumberAll.IsChecked) {
      $base = Strip-ExistingEpisodeTags $base
      $originalBase = Strip-ExistingEpisodeTags $originalBase
    }
    $base = $base.Trim(); if (-not $base) { $base = $originalBase }
    $episodeDigits = 2; if ($CmbEpisodeDigits.SelectedItem -and $CmbEpisodeDigits.SelectedItem.Content) { [void][int]::TryParse($CmbEpisodeDigits.SelectedItem.Content.ToString(), [ref]$episodeDigits) }
    $seasonDigits = 2; if ($CmbSeasonDigits.SelectedItem -and $CmbSeasonDigits.SelectedItem.Content) { [void][int]::TryParse($CmbSeasonDigits.SelectedItem.Content.ToString(), [ref]$seasonDigits) }
    $oldPrefix = if ($TxtOldPrefix.Text) { $TxtOldPrefix.Text.Trim() } else { "" }
    $newPrefix = if ($TxtNewPrefix.Text) { $TxtNewPrefix.Text.Trim() } else { "" }
    $detected  = if ($TxtDetectedPrefix.Text) { $TxtDetectedPrefix.Text.Trim() } else { "" }
    $originalBaseNoOld = $originalBase
    if ($oldPrefix -and $originalBaseNoOld.StartsWith($oldPrefix, [System.StringComparison]::OrdinalIgnoreCase)) { $originalBaseNoOld = $originalBaseNoOld.Substring($oldPrefix.Length) }
    if ($ChkRenumberAll.IsChecked) { $originalBaseNoOld = Strip-ExistingEpisodeTags $originalBaseNoOld }
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
    $finalNameNoExt = Normalize-NameSpaces $finalNameNoExt
    $safeName = $finalNameNoExt.Trim(); if (-not $safeName) { $safeName = $originalBase }
    return ($safeName + $ext)
}

  function Get-CommonPrefix {
    param([string[]]$Names)
    if (-not $Names -or $Names.Count -eq 0) { return "" }
    $first = $Names[0]
    $minLen = ($Names | Measure-Object Length -Minimum).Minimum
    $idx = 0
    while ($idx -lt $minLen) {
      $char = $first[$idx]
      if ($Names | Where-Object { $_[$idx] -ne $char -and [char]::ToLowerInvariant($_[$idx]) -ne [char]::ToLowerInvariant($char) }) { break }
      $idx++
    }
    $prefix = $first.Substring(0, $idx)
    $prefix = $prefix.Trim(' ','-','_','.')
    return $prefix
  }

function Get-MetadataSummary {
    param([System.IO.FileInfo]$File)
    $summary = ""
    if (-not ($ChkUseAudioMetadata.IsChecked -or $ChkUseVideoMetadata.IsChecked)) { return $summary }
  if ($ffprobePath) {
    try {
      $ffout = & $ffprobePath -v error -show_entries format=tags=title,artist,album,show,season_number,episode_id `
                   -of default=noprint_wrappers=1:nokey=0 -- "$($File.FullName)" 2>$null
      if ($ffout) {
        $tags = @{}
        foreach ($line in $ffout) { if ($line -match "^(?<k>[^=]+)=(?<v>.+)$") { $tags[$matches.k] = $matches.v } }
        if ($ChkUseAudioMetadata.IsChecked) { $summary = ((@($tags.artist, $tags.album, $tags.title) -join " | ").Trim(" |")) }
        elseif ($ChkUseVideoMetadata.IsChecked) { $summary = ((@($tags.show, ("S{0}E{1}" -f $tags.season_number, $tags.episode_id), $tags.title) -join " | ").Trim(" |")) }
      }
    } catch { }
  }
  elseif ($exiftoolPath) {
    try { $exif = & $exiftoolPath -s -s -s -Artist -Album -Title -TVShowName -SeasonNumber -EpisodeNumber -- "$($File.FullName)" 2>$null; if ($exif) { $summary = ($exif | Where-Object { $_ }) -join " | " } } catch { }
  }
    return $summary
}

function Normalize-NameSpaces {
  param([string]$Name)
  $mode = "space-to-dash"
  if ($CmbSpaceReplace -and $CmbSpaceReplace.SelectedItem) {
    $sel = $CmbSpaceReplace.SelectedItem
    if ($sel.Tag -ne $null) { $mode = $sel.Tag.ToString() }
  }
  switch ($mode) {
    "space-to-dash" {
      $clean = $Name -replace '\s+','-'
      $clean = $clean -replace '-{2,}','-'
      $clean = $clean.Trim('-')
    }
    "space-to-underscore" {
      $clean = $Name -replace '\s+','_'
      $clean = $clean -replace '_{2,}','_'
      $clean = $clean.Trim('_')
    }
    "space-remove" {
      $clean = $Name -replace '\s+',''
    }
    "dash-to-space" {
      $clean = $Name -replace '-',' '
      $clean = $clean -replace '\s{2,}',' '
      $clean = $clean.Trim()
    }
    "dash-to-underscore" {
      $clean = $Name -replace '-','_'
      $clean = $clean -replace '_{2,}','_'
      $clean = $clean.Trim('_')
    }
    "underscore-to-dash" {
      $clean = $Name -replace '_','-'
      $clean = $clean -replace '-{2,}','-'
      $clean = $clean.Trim('-')
    }
    "underscore-to-space" {
      $clean = $Name -replace '_',' '
      $clean = $clean -replace '\s{2,}',' '
      $clean = $clean.Trim()
    }
    default { $clean = $Name }
  }
  if (-not $clean) { $clean = $Name }
  return $clean
}

function Rename-ItemCaseAware {
  param([string]$Path,[string]$NewName)
  $dir = [System.IO.Path]::GetDirectoryName($Path)
  $currentName = [System.IO.Path]::GetFileName($Path)
  $targetPath = Join-Path $dir $NewName
  $caseOnlyChange = ($currentName -ieq $NewName) -and -not ($currentName -ceq $NewName)
  if ($caseOnlyChange) {
    $tempName = "__temp__" + [Guid]::NewGuid().ToString() + "__" + $NewName
    $tempPath = Join-Path $dir $tempName
    Rename-Item -Path $Path -NewName $tempName -ErrorAction Stop
    Rename-Item -Path $tempPath -NewName $NewName -ErrorAction Stop
  }
  else {
    Rename-Item -Path $Path -NewName $NewName -ErrorAction Stop
  }
}

# File Cleaner events
$BtnBrowse.Add_Click({
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    $dialog.Description = "Select a folder to process"
    $dialog.ShowNewFolderButton = $false
    $result = $dialog.ShowDialog()
    if ($result -eq [System.Windows.Forms.DialogResult]::OK) { $TxtFolder.Text = $dialog.SelectedPath; Update-Status "Folder selected: $($dialog.SelectedPath)" } else { Update-Status "Browse canceled." }
})

$BtnExit.Add_Click({
  $window.Close()
})

$BtnSelectAll.Add_Click({ foreach ($row in $DgPreview.Items) { try { $row.Apply = $true } catch {} }; $DgPreview.Items.Refresh() })
$BtnSelectNone.Add_Click({ foreach ($row in $DgPreview.Items) { try { $row.Apply = $false } catch {} }; $DgPreview.Items.Refresh() })
$BtnDetectPrefix.Add_Click({
  if (-not $TxtFolder.Text) { Update-Status "Select a folder first."; return }
  $files = Get-FilteredFiles -Path $TxtFolder.Text -Recurse:$ChkRecurse.IsChecked
  if ($ChkOnlyIfOldPrefix.IsChecked -and $TxtOldPrefix.Text) { $oldPref = $TxtOldPrefix.Text.Trim(); $files = $files | Where-Object { $_.BaseName.StartsWith($oldPref, [System.StringComparison]::OrdinalIgnoreCase) } }
  if ($TxtIgnorePrefix.Text) { $ignorePref = $TxtIgnorePrefix.Text.Trim(); if ($ignorePref) { $files = $files | Where-Object { -not $_.BaseName.StartsWith($ignorePref, [System.StringComparison]::OrdinalIgnoreCase) } } }
  if (-not $files -or $files.Count -eq 0) { Update-Status "No files found for prefix detection."; return }
  $names = $files | Select-Object -ExpandProperty BaseName
  $prefix = Get-CommonPrefix $names
  if ($prefix) { $TxtDetectedPrefix.Text = $prefix; Update-Status "Detected prefix: $prefix" }
  else { $TxtDetectedPrefix.Clear(); Update-Status "No common prefix detected." }
})
$BtnSpaceCleanup.Add_Click({
  if (-not $DgPreview.ItemsSource) { Update-Status "No preview to update."; return }
  foreach ($row in $DgPreview.Items) {
    $ext = [System.IO.Path]::GetExtension($row.Proposed)
    $base = [System.IO.Path]::GetFileNameWithoutExtension($row.Proposed)
    $newBase = Normalize-NameSpaces $base
    $newName = if ($ext) { "$newBase$ext" } else { $newBase }
    $row.Proposed = $newName
    $row.Status = if ($newName -eq $row.Original) { "No change" } else { "Pending" }
    $row.Apply = ($row.Status -ne "No change")
  }
  $DgPreview.Items.Refresh()
  $hasPending = (@($DgPreview.Items | Where-Object { $_.Status -ne "No change" })).Count -gt 0
  $BtnApply.IsEnabled = ($DgPreview.Items.Count -gt 0 -and $hasPending)
  Update-Status "Space cleanup applied."
})

$BtnScan.Add_Click({
    if (-not $TxtFolder.Text) {
      Update-Status "Please select or enter a folder path."
      [System.Windows.MessageBox]::Show("Please select or enter a folder path before scanning.", "Folder Required", [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Information) | Out-Null
      return
    }
    Update-Status "Scanning..."
    $startNum = 1
    if ($ChkAddEpisode.IsChecked -or $ChkRenumberAll.IsChecked) { [void][int]::TryParse($TxtStart.Text, [ref]$startNum); if ($startNum -lt 1) { $startNum = 1 } }
    $files = Get-FilteredFiles -Path $TxtFolder.Text -Recurse:$ChkRecurse.IsChecked
    if ($ChkOnlyIfOldPrefix.IsChecked -and $TxtOldPrefix.Text) { $oldPref = $TxtOldPrefix.Text.Trim(); $files = $files | Where-Object { $_.BaseName.StartsWith($oldPref, [System.StringComparison]::OrdinalIgnoreCase) } }
    if ($TxtIgnorePrefix.Text) { $ignorePref = $TxtIgnorePrefix.Text.Trim(); if ($ignorePref) { $files = $files | Where-Object { -not $_.BaseName.StartsWith($ignorePref, [System.StringComparison]::OrdinalIgnoreCase) } } }
    if ($ChkRenumberAll.IsChecked) { $files = $files | Sort-Object Name }
    $preview = @(); $episodeCounter = $startNum; $hasEpisodeOps = $ChkRenumberAll.IsChecked -or $ChkAddEpisode.IsChecked
    foreach ($f in $files) {
      if ($ChkRenumberAll.IsChecked) { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter -ForceEpisode; $episodeCounter++ }
      elseif ($ChkAddEpisode.IsChecked) { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter; $episodeCounter++ }
      else { $proposed = Get-ProposedName -File $f -EpisodeNumber $episodeCounter }
      $isCaseOnly = $false
      if ($ChkNormalizePrefixCase -and $ChkNormalizePrefixCase.IsChecked) { $isCaseOnly = ($proposed -ieq $f.Name) -and -not ($proposed -ceq $f.Name) }
      $status = if (($proposed -eq $f.Name) -and -not $isCaseOnly) { "No change" } else { "Pending" }
      $applyFlag = ($status -ne "No change")
      $preview += [PSCustomObject]@{ Apply=$applyFlag; Original=$f.Name; Proposed=$proposed; Status=$status; Directory=$f.DirectoryName; MetaSummary=Get-MetadataSummary -File $f }
    }
    $DgPreview.ItemsSource = $preview
    $hasPending = (@($preview | Where-Object { $_.Status -ne "No change" })).Count -gt 0
    $BtnApply.IsEnabled     = ($preview.Count -gt 0 -and $hasPending)
    $BtnExportCsv.IsEnabled = ($preview.Count -gt 0)
    if ($preview.Count -gt 0) {
      Update-Status "Preview complete: $($preview.Count) files."
    } else {
      Update-Status "No files matched filters."
    }
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
        $isCaseOnly = $false
        if ($ChkNormalizePrefixCase -and $ChkNormalizePrefixCase.IsChecked) { $isCaseOnly = ($proposed -ieq $row.Original) -and -not ($proposed -ceq $row.Original) }
        if (($row.Original -eq $proposed) -and -not $isCaseOnly) {
          $row.Status = "No change"
          $row.Apply = $false
        }
        elseif ($row.Original -ne $proposed -or $isCaseOnly) {
            $oldPath = Join-Path $row.Directory $row.Original; $newPath = Join-Path $row.Directory $proposed
            if ($ChkDryRun.IsChecked) { $row.Status = "Dry-run" }
            else {
            try { Rename-ItemCaseAware -Path $oldPath -NewName $proposed; $row.Status = "Renamed"; $ops += [PSCustomObject]@{ Old=$oldPath; New=$newPath } }
                catch { $row.Status = "Error: $($_.Exception.Message)" }
            }
        } else { $row.Status = "No change"; $row.Apply = $false }
    }
    $global:LastOperations = $ops; $DgPreview.Items.Refresh();
    if ($ChkDryRun.IsChecked) {
      Update-Status "Dry-run complete."
    } elseif ($ops.Count -gt 0) {
      Update-Status "Apply complete: $($ops.Count) renamed."
    } else {
      Update-Status "No changes applied."
    }
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
  $TxtOldPrefix.Clear(); $TxtNewPrefix.Clear(); $TxtDetectedPrefix.Clear(); $TxtIgnorePrefix.Clear(); $TxtSeason.Clear(); $TxtStart.Clear(); $TxtCustomClean.Clear(); $TxtCustomExt.Clear();
  if ($CmbSpaceReplace) { $CmbSpaceReplace.SelectedIndex = 0 }
  if ($ChkNormalizePrefixCase) { $ChkNormalizePrefixCase.IsChecked = $false }
    Update-Status "Reset complete."
})

# ---------- Video Combiner Logic ----------
$CombItems = New-Object System.Collections.ArrayList
$supportedCombExt = @('.mp4','.mkv','.avi','.mov','.wmv','.webm','.flv')
$targetDir = $ToolDir
$CombLogDir  = Join-Path $script:Root "logs"

if ($TxtConvOut -and -not [string]::IsNullOrWhiteSpace($targetDir)) { $TxtConvOut.Text = $targetDir }

function Pump-CombUI {
  if (-not $window) { return }
  $null = $window.Dispatcher.Invoke([Action]{}, [System.Windows.Threading.DispatcherPriority]::Background)
}

function Set-ConvStatus {
  param([string]$Text)
  if ($TxtConvStatus) { $TxtConvStatus.Text = $Text }
}

function Set-ConvProgress {
  param([bool]$Active,[int]$Index = 0,[int]$Total = 0)
  if (-not $ConvProgress) { return }
  if ($Active) {
    $ConvProgress.Visibility = [System.Windows.Visibility]::Visible
    if ($Total -gt 0) {
      $ConvProgress.IsIndeterminate = $false
      $pct = if ($Total -gt 0) { [Math]::Min(100,[Math]::Max(0,[int](100 * $Index / $Total))) } else { 0 }
      $ConvProgress.Value = $pct
    }
    else {
      $ConvProgress.IsIndeterminate = $true
      $ConvProgress.Value = 0
    }
  }
  else {
    $ConvProgress.IsIndeterminate = $false
    $ConvProgress.Value = 0
    $ConvProgress.Visibility = [System.Windows.Visibility]::Collapsed
  }
}

function Ensure-LogDir {
  if (-not (Test-Path -LiteralPath $CombLogDir)) { New-Item -ItemType Directory -Path $CombLogDir -Force | Out-Null }
}

function New-CombLogFile {
  param([string]$Prefix = "combine")
  Ensure-LogDir
  $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
  return Join-Path $CombLogDir ("{0}_{1}.log" -f $Prefix, $stamp)
}

function Write-CombLog {
  param([string]$Message,[string]$LogFile)
  $line = "[{0}] {1}" -f (Get-Date -Format "HH:mm:ss"), $Message
  $TxtCombLog.AppendText($line + "`r`n")
  if ($LogFile) { $line | Out-File -FilePath $LogFile -Append -Encoding UTF8 }
}

function Render-Args {
  param([string[]]$Array)
  return ($Array | ForEach-Object { if ($_ -match '\s') { "'$_'" } else { $_ } }) -join ' '
}

function Run-FFmpegLogged {
  param(
    [string[]]$CmdArgs,
    [string]$LogFile,
    [string]$Label,
    [int]$MaxUiLines = 200
  )
  Ensure-LogDir
  if (-not $ffmpegPath) {
    Write-CombLog "ffmpeg path not set; update Tool Folder." $LogFile
    return 1
  }
  $argList = if ($CmdArgs) { @($CmdArgs) } else { @() }
  $renderedArgs = Render-Args $argList
  if ($LogFile) {
    "===== $Label $(Get-Date -Format s) =====" | Out-File -FilePath $LogFile -Append -Encoding UTF8
    "cmd: $ffmpegPath $renderedArgs"         | Out-File -FilePath $LogFile -Append -Encoding UTF8
  }
  Write-CombLog "$Label -> logging to $LogFile" $LogFile
  if (-not $argList -or $argList.Count -eq 0) {
    Write-CombLog "No ffmpeg arguments provided; aborting." $LogFile
    return 1
  }
  $output = & $ffmpegPath @argList 2>&1
  $exitCode = $LASTEXITCODE
  if ($output) {
    $lines = @($output | ForEach-Object { $_.ToString() })
    if ($LogFile) { $lines | Out-File -FilePath $LogFile -Append -Encoding UTF8 }
    $lines | Select-Object -First $MaxUiLines | ForEach-Object { $TxtCombLog.AppendText($_ + "`r`n") }
    if ($lines.Count -gt $MaxUiLines) { $TxtCombLog.AppendText("... (output truncated, see log file)`r`n") }
  }
  return $exitCode
}

function Ensure-Tools {
  Ensure-LogDir
  if ($ToolDir -and -not (Test-Path -LiteralPath $ToolDir)) { New-Item -ItemType Directory -Path $ToolDir -Force | Out-Null }
  Refresh-ToolPaths
  if (-not ($ffmpegPath -and (Test-Path -LiteralPath $ffmpegPath))) {
    [System.Windows.MessageBox]::Show("ffmpeg.exe not found. Set the Tool Folder to a directory containing ffmpeg.exe.","Missing FFmpeg",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
    return $false
  }
  if (-not ($ffprobePath -and (Test-Path -LiteralPath $ffprobePath))) {
    [System.Windows.MessageBox]::Show("ffprobe.exe not found. Set the Tool Folder to a directory containing ffprobe.exe.","Missing FFprobe",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
    return $false
  }
  return $true
}

function Refresh-CombGrid { $DgCombine.ItemsSource = $null; $DgCombine.ItemsSource = $CombItems }
function Get-CombChecked { return @($CombItems | Where-Object { $_.Apply }) }

function Get-OutputDir {
  $chosen = if ($TxtConvOut -and $TxtConvOut.Text) { [Environment]::ExpandEnvironmentVariables($TxtConvOut.Text.Trim()) } else { $null }
  if (-not $chosen) { $chosen = $targetDir }
  if (-not $chosen) { $chosen = $ToolDir }
  if (-not $chosen) { $chosen = $script:Root }
  if (-not $chosen) { return $null }
  if (-not (Test-Path -LiteralPath $chosen)) { New-Item -ItemType Directory -Path $chosen -Force | Out-Null }
  return (Resolve-Path $chosen).Path
}

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

function Get-VideoInfo {
    param([string]$file)
  if (-not $ffprobePath) { return @() }
    $args = @('-v','error','-select_streams','v:0','-show_entries','stream=codec_name,width,height,r_frame_rate,format=duration','-of','default=noprint_wrappers=1',"$file")
    & $ffprobePath @args
}

function Compare-Encodings {
    param($files)
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

# Combiner events
$BtnToolDirBrowse.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') {
    Set-ToolDir $fb.SelectedPath
    Update-Status "Tool folder set to $($fb.SelectedPath)"
  }
})
if ($TxtToolDir) {
  $TxtToolDir.Add_LostFocus({ Set-ToolDir $TxtToolDir.Text })
}

$BtnCombBrowse.Add_Click({ $fb = New-Object System.Windows.Forms.FolderBrowserDialog; if ($fb.ShowDialog() -eq 'OK') { Load-CombFiles -Folder $fb.SelectedPath } })
$BtnCombSelectAll.Add_Click({ foreach ($item in $CombItems) { $item.Apply = $true }; Refresh-CombGrid })
$BtnCombSelectNone.Add_Click({ foreach ($item in $CombItems) { $item.Apply = $false }; Refresh-CombGrid })
$BtnCombUp.Add_Click({ Move-CombItem -Offset -1 })
$BtnCombDown.Add_Click({ Move-CombItem -Offset 1 })
$BtnConvBrowse.Add_Click({
  $fb = New-Object System.Windows.Forms.FolderBrowserDialog
  if ($fb.ShowDialog() -eq 'OK') {
    $TxtConvOut.Text = $fb.SelectedPath
    Update-Status "Output folder set for convert/combine: $($fb.SelectedPath)"
  }
})
$BtnCombConvert.Add_Click({
  if (-not (Ensure-Tools)) { return }
  $checked = Get-CombChecked
  if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
  $fmt = "mp4"
  if ($CmbConvFormat -and $CmbConvFormat.SelectedItem -and $CmbConvFormat.SelectedItem.Tag) {
    $fmt = $CmbConvFormat.SelectedItem.Tag.ToString().ToLower()
  }
  $resTag = "source"
  if ($CmbConvRes -and $CmbConvRes.SelectedItem -and $CmbConvRes.SelectedItem.Tag) {
    $resTag = $CmbConvRes.SelectedItem.Tag.ToString().ToLower()
  }
  $scaleArgs = @()
  switch ($resTag) {
    "720p"   { $scaleArgs = @('-vf','scale=-2:720') }
    "1080p"  { $scaleArgs = @('-vf','scale=-2:1080') }
    "4k"     { $scaleArgs = @('-vf','scale=-2:2160') }
    default   { $scaleArgs = @() }
  }
  $outDir = if ($TxtConvOut -and $TxtConvOut.Text) { $TxtConvOut.Text } else { $targetDir }
  $outDir = Get-OutputDir
  if (-not $outDir) { [System.Windows.MessageBox]::Show("Please set an output folder."); return }
  $logFile = New-CombLogFile -Prefix ("convert_{0}" -f $fmt)
  $TxtCombLog.Clear(); Write-CombLog ("Converting to {0} ({1}) in {2} ..." -f $fmt, $resTag, $outDir) $logFile; Update-Status "Converting..."; Set-ConvStatus "Converting to $fmt ($resTag) ..."
  $total = $checked.Count
  Set-ConvProgress $true 0 $total; Pump-CombUI
  try {
    $success = 0; $fail = 0
    $index = 0
    foreach ($f in $checked) {
      $base = [System.IO.Path]::GetFileNameWithoutExtension($f.Path)
      $outFile = Join-Path $outDir ("conv_" + $base + "." + $fmt)
      $ffArgs = if ($fmt -eq 'wmv') {
        @('-y','-i',$f.Path) + $scaleArgs + @('-c:v','wmv2','-b:v','4000k','-c:a','wmav2','-b:a','192k',$outFile)
      } else {
        @('-y','-i',$f.Path) + $scaleArgs + @('-c:v','libx264','-preset','fast','-crf','20','-c:a','aac','-b:a','192k',$outFile)
      }
      Write-CombLog ("-> {0} => {1} [{2}]" -f [System.IO.Path]::GetFileName($f.Path), [System.IO.Path]::GetFileName($outFile), $resTag) $logFile
      Set-ConvStatus ("Converting {0}/{1}: {2}" -f ($index + 1), $total, [System.IO.Path]::GetFileName($f.Path))
      $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label ("Convert to {0}" -f $fmt) -MaxUiLines 10000
      if ($exit -eq 0 -and (Test-Path $outFile)) { $success++ }
      else { $fail++ }
      $index++
      Set-ConvProgress $true $index $total; Pump-CombUI
    }
    Write-CombLog ("Convert finished. Success: {0}, Fail: {1}" -f $success, $fail) $logFile
    Update-Status ("Convert complete. Success: {0}, Fail: {1}" -f $success, $fail)
    Set-ConvStatus ("Convert complete. Success: {0}, Fail: {1}" -f $success, $fail)
  }
  finally {
    Set-ConvProgress $false; Pump-CombUI
  }
})

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
  $logFile = New-CombLogFile -Prefix "normalize"
  $TxtCombLog.Clear(); Write-CombLog "Normalizing to H.264/AAC in $targetDir ..." $logFile; Update-Status "Normalizing..."
  $fixed = @()
    foreach ($f in $checked) {
        $outFile = Join-Path $targetDir ("fixed_" + [System.IO.Path]::GetFileNameWithoutExtension($f.Path) + ".mp4")
        $ffArgs = @('-y','-i',$f.Path,'-c:v','libx264','-preset','fast','-crf','23','-c:a','aac','-b:a','192k',$outFile)
    Write-CombLog "-> $([System.IO.Path]::GetFileName($f.Path)) -> $([System.IO.Path]::GetFileName($outFile))" $logFile
    $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label ("Normalize {0}" -f [System.IO.Path]::GetFileName($f.Path))
    if ($exit -eq 0 -and (Test-Path $outFile)) { $fixed += $outFile } else { Write-CombLog "Normalization failed (exit $exit) for $($f.Path)" $logFile }
    }
    $CombItems.Clear() | Out-Null; foreach ($f in $fixed) { [void]$CombItems.Add([PSCustomObject]@{ Apply=$true; Name=[System.IO.Path]::GetFileName($f); Path=$f }) }
    Refresh-CombGrid
  Write-CombLog "Done. Files normalized for concat in $targetDir. Log: $logFile" $logFile; Update-Status "Normalization finished"
})

$BtnCombSafe.Add_Click({
  if (-not (Ensure-Tools)) { return }
  $checked = Get-CombChecked
  if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
  $outDir = Get-OutputDir
  if (-not $outDir) { [System.Windows.MessageBox]::Show("Please set an output folder."); return }
  $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
  $outFile = Join-Path $outDir ("combined_safe_{0}.mp4" -f $stamp)
  if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
  $logFile = New-CombLogFile -Prefix "combine_safe"
    $listFile = Join-Path $outDir ("videolist_safe_{0}.txt" -f $stamp)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $lines = @(); foreach ($f in $checked) { $normalized = [System.IO.Path]::GetFullPath($f.Path); $escaped = ($normalized -replace "'", "'\''"); $lines += "file '$escaped'" }
    [System.IO.File]::WriteAllLines($listFile, $lines, $utf8NoBom)
    if ($logFile) {
      "Input list (concat file content):" | Out-File -FilePath $logFile -Append -Encoding UTF8
      $lines | Out-File -FilePath $logFile -Append -Encoding UTF8
    }
    $TxtCombLog.Clear(); Write-CombLog "Safe combine (re-encode) into: $outFile" $logFile; Write-CombLog "List file: $listFile" $logFile
    $idx = 1
    foreach ($f in $checked) {
      $entry = ("  {0}. {1}" -f $idx, [System.IO.Path]::GetFileName($f.Path))
      Write-CombLog $entry $logFile
      $idx++
    }
    Update-Status "Safe combining with ffmpeg..."
    $ffArgs = @(
      '-y','-fflags','+genpts','-safe','0','-f','concat','-i',$listFile,
      '-c:v','libx264','-preset','medium','-crf','20',
      '-c:a','aac','-b:a','192k',
      '-vsync','2','-af','aresample=async=1',
      '-avoid_negative_ts','make_zero','-reset_timestamps','1','-movflags','+faststart',
      $outFile
    )
    Write-CombLog ("ffmpeg cmd: {0} {1}" -f $ffmpegPath, (Render-Args $ffArgs)) $logFile
    $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label "Safe combine (re-encode)"
    Remove-Item $listFile -Force -ErrorAction SilentlyContinue
  if ($exit -eq 0) {
    [System.Windows.MessageBox]::Show("Videos safely combined into $outFile. Log saved to $logFile")
    Update-Status "Safe combine complete"
  }
  else {
    Write-CombLog "Safe combine failed with exit code $exit. See $logFile" $logFile
    [System.Windows.MessageBox]::Show("Safe combine failed (exit $exit). See log: $logFile","Safe combine failed",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
    Update-Status "Safe combine failed"
  }
})

$BtnCombCombine.Add_Click({
    if (-not (Ensure-Tools)) { return }
    $checked = Get-CombChecked
    if ($checked.Count -eq 0) { [System.Windows.MessageBox]::Show("No videos selected."); return }
    $outDir = Get-OutputDir
    if (-not $outDir) { [System.Windows.MessageBox]::Show("Please set an output folder."); return }
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
    $outFile = Join-Path $outDir ("combined_{0}.mp4" -f $stamp)
    if (-not (Test-Path -LiteralPath $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }
  $logFile = New-CombLogFile -Prefix "combine_copy"
    $listFile = Join-Path $outDir ("videolist_copy_{0}.txt" -f $stamp)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $lines = @(); foreach ($f in $checked) { $normalized = [System.IO.Path]::GetFullPath($f.Path); $escaped = ($normalized -replace "'", "'\''"); $lines += "file '$escaped'" }
    [System.IO.File]::WriteAllLines($listFile, $lines, $utf8NoBom)
    if ($logFile) {
      "Input list (concat file content):" | Out-File -FilePath $logFile -Append -Encoding UTF8
      $lines | Out-File -FilePath $logFile -Append -Encoding UTF8
    }
  $TxtCombLog.Clear(); Write-CombLog "Combining into: $outFile (stream copy)" $logFile; Write-CombLog "List file: $listFile" $logFile
    $idx = 1
    foreach ($f in $checked) {
      $entry = ("  {0}. {1}" -f $idx, [System.IO.Path]::GetFileName($f.Path))
      Write-CombLog $entry $logFile
      $idx++
    }
    Update-Status "Combining with ffmpeg..."
  $ffArgs = @('-y','-fflags','+genpts','-safe','0','-f','concat','-i',$listFile,'-c','copy','-avoid_negative_ts','make_zero','-reset_timestamps','1','-movflags','+faststart',$outFile)
  Write-CombLog ("ffmpeg cmd: {0} {1}" -f $ffmpegPath, (Render-Args $ffArgs)) $logFile
  $exit = Run-FFmpegLogged -CmdArgs $ffArgs -LogFile $logFile -Label "Combine (stream copy)"
  Remove-Item $listFile -Force -ErrorAction SilentlyContinue
  if ($exit -eq 0) {
    [System.Windows.MessageBox]::Show("Videos combined successfully into $outFile. Log saved to $logFile")
    Update-Status "Combine complete"
  }
  else {
    Write-CombLog "Combine failed with exit code $exit. See $logFile" $logFile
    [System.Windows.MessageBox]::Show("Combine failed (exit $exit). See log: $logFile","Combine failed",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
    Update-Status "Combine failed"
  }
})

# --------------- Run ---------------
$window.ShowDialog() | Out-Null
