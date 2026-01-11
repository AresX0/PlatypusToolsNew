<#
.SYNOPSIS
    Batch File Renamer with Metadata — scalable WPF UI (single-file PowerShell).
.DESCRIPTION
    Same functionality as before with an improved responsive layout that prevents overlapping text
    and keeps controls usable at small and large window sizes.
#>

Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml,System.Windows.Forms
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture
[System.Threading.Thread]::CurrentThread.CurrentUICulture = [System.Globalization.CultureInfo]::InvariantCulture

# --- Script root detection (robust) ---
function Get-ScriptRoot {
    if ($PSScriptRoot -and (Test-Path $PSScriptRoot)) { return $PSScriptRoot }
    try {
        if ($MyInvocation -and $MyInvocation.MyCommand -and $MyInvocation.MyCommand.Definition) {
            $def = $MyInvocation.MyCommand.Definition
            if ($def -is [string] -and $def.Trim() -ne '') {
                try { if ([System.IO.Path]::IsPathRooted($def)) { $resolved = Resolve-Path -Path $def -ErrorAction Stop; return [System.IO.Path]::GetDirectoryName($resolved.Path) } } catch {}
            }
        }
    } catch {}
    try { return (Get-Location).ProviderPath } catch { return "." }
}
$ScriptRoot = Get-ScriptRoot
$ToolsDir = Join-Path $ScriptRoot 'tools'
if (-not (Test-Path $ToolsDir)) { New-Item -Path $ToolsDir -ItemType Directory -Force | Out-Null }

# --- minimal helpers (kept small for brevity) ---
function Write-Log { param($m) "$((Get-Date).ToString('s'))`t$m" | Out-File -FilePath (Join-Path $ToolsDir 'renamer-debug.log') -Encoding UTF8 -Append }

# --- Observable preview list ---
Add-Type -AssemblyName PresentationFramework
$global:PreviewList = New-Object System.Collections.ObjectModel.ObservableCollection[psobject]
function New-PreviewItem { param($FullPath,$Original,$Proposed,$Status,$MetaSummary,$Metadata) [pscustomobject]@{ Apply = $true; Original=$Original; Proposed=$Proposed; Status=$Status; FullPath=$FullPath; Directory=[System.IO.Path]::GetDirectoryName($FullPath); MetaSummary=$MetaSummary; _Metadata=$Metadata } }

# --- Responsive XAML UI ---
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Batch File Renamer with Metadata" 
        Height="720" Width="1200" MinHeight="520" MinWidth="820" SizeToContent="Manual" WindowStartupLocation="CenterScreen">
  <DockPanel LastChildFill="True">
    <StatusBar DockPanel.Dock="Bottom" Height="26">
      <StatusBarItem>
        <TextBlock Name="TxtStatusBar" Text="Ready" TextWrapping="Wrap"/>
      </StatusBarItem>
      <StatusBarItem HorizontalAlignment="Right">
        <TextBlock Name="TxtTools" Text="Tools: detecting..." TextWrapping="Wrap"/>
      </StatusBarItem>
    </StatusBar>

    <ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" Padding="8" Focusable="False">
      <Grid ShowGridLines="False" Margin="0" >
        <Grid.RowDefinitions>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="Auto"/>
          <RowDefinition Height="*"/>
          <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Folder selection row -->
        <Grid Grid.Row="0" Margin="0,0,0,8">
          <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="Auto"/>
            <ColumnDefinition Width="200"/>
          </Grid.ColumnDefinitions>

          <TextBox Name="TxtFolder" Grid.Column="0" MinWidth="220" Margin="0,0,8,0" VerticalAlignment="Center" TextWrapping="NoWrap"/>
          <Button Name="BtnBrowse" Grid.Column="1" Width="84" Content="Browse" Margin="0,0,8,0"/>
          <CheckBox Name="ChkRecurse" Grid.Column="2" Content="Recurse" VerticalAlignment="Center" Margin="0,0,8,0"/>
          <StackPanel Grid.Column="3" Orientation="Horizontal" HorizontalAlignment="Right">
            <TextBlock Text="Dry Run" VerticalAlignment="Center" Margin="0,0,6,0"/>
            <CheckBox Name="ChkDryRun" VerticalAlignment="Center"/>
          </StackPanel>
        </Grid>

        <!-- Prefix and numbering -->
        <Border Grid.Row="1" BorderBrush="#DDD" BorderThickness="1" CornerRadius="4" Padding="8" Margin="0,0,0,8">
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="2*"/>
              <ColumnDefinition Width="3*"/>
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Column="0" Orientation="Vertical" Margin="0,0,8,0">
              <CheckBox Name="ChkChangePrefix" Content="Change Prefix" Margin="0,0,0,6"/>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="Old Prefix:" Grid.Column="0" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBox Name="TxtOldPrefix" Grid.Column="1" MinWidth="140"/>
              </Grid>

              <Grid Margin="0,6,0,0">
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                <TextBlock Text="New Prefix:" Grid.Column="0" VerticalAlignment="Center" Margin="0,0,6,0"/>
                <TextBox Name="TxtNewPrefix" Grid.Column="1" MinWidth="140"/>
              </Grid>

              <WrapPanel Margin="0,8,0,0" VerticalAlignment="Center">
                <CheckBox Name="ChkRequireExistingPrefix" Content="Require existing prefix to change" Margin="0,0,12,0" />
                <CheckBox Name="ChkAddIfMissing" Content="Add prefix when missing" Margin="0,0,12,0" IsEnabled="False"/>
              </WrapPanel>
            </StackPanel>

            <StackPanel Grid.Column="1" Orientation="Vertical">
              <TextBlock Text="Season / Episode Options" FontWeight="Bold" Margin="0,0,0,6"/>
              <Grid>
                <Grid.ColumnDefinitions>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="Auto"/>
                  <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>

                <StackPanel Grid.Column="0" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,12,0">
                  <CheckBox Name="ChkAddSeason" Content="Add Season" VerticalAlignment="Center"/>
                  <TextBox Name="TxtSeason" Width="64" Margin="8,0,0,0" Text="1"/>
                </StackPanel>

                <StackPanel Grid.Column="1" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,12,0">
                  <TextBlock Text="Digits:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                  <ComboBox Name="CmbSeasonDigits" Width="72">
                    <ComboBoxItem>2</ComboBoxItem>
                    <ComboBoxItem>3</ComboBoxItem>
                    <ComboBoxItem>4</ComboBoxItem>
                  </ComboBox>
                </StackPanel>

                <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center" Margin="0,0,12,0">
                  <CheckBox Name="ChkSeasonBeforeEpisode" Content="Season before Episode" />
                </StackPanel>

                <StackPanel Grid.Column="3" Orientation="Horizontal" HorizontalAlignment="Right">
                  <TextBlock Text="Start Ep:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                  <TextBox Name="TxtStart" Width="64" Margin="0,0,8,0" Text="1"/>
                  <TextBlock Text="Digits:" VerticalAlignment="Center" Margin="0,0,6,0"/>
                  <ComboBox Name="CmbEpisodeDigits" Width="72">
                    <ComboBoxItem>2</ComboBoxItem>
                    <ComboBoxItem>3</ComboBoxItem>
                    <ComboBoxItem>4</ComboBoxItem>
                  </ComboBox>
                  <CheckBox Name="ChkRenumberAll" Content="Renumber All" Margin="12,0,0,0" VerticalAlignment="Center"/>
                </StackPanel>
              </Grid>
            </StackPanel>
          </Grid>
        </Border>

        <!-- Options row: clean tokens, types, metadata -->
        <Border Grid.Row="2" BorderBrush="#EEE" BorderThickness="1" CornerRadius="4" Padding="8" Margin="0,0,0,8">
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="2*"/>
              <ColumnDefinition Width="3*"/>
            </Grid.ColumnDefinitions>

            <StackPanel Grid.Column="0" Orientation="Vertical">
              <TextBlock Text="Clean Tokens (remove from title)" FontWeight="Bold" Margin="0,0,0,6"/>
              <WrapPanel Name="CleanWrap" VerticalAlignment="Center" ItemHeight="28" ItemWidth="Auto">
                <CheckBox Name="Chk720p" Content="720p" Margin="0,0,8,6"/>
                <CheckBox Name="Chk1080p" Content="1080p" Margin="0,0,8,6"/>
                <CheckBox Name="Chk4k" Content="4k / 2160p" Margin="0,0,8,6"/>
                <CheckBox Name="Chk720" Content="720" Margin="0,0,8,6"/>
                <CheckBox Name="Chk1080" Content="1080" Margin="0,0,8,6"/>
                <CheckBox Name="ChkHD" Content="HD" Margin="0,0,8,6"/>
                <TextBox Name="TxtCustomClean" Width="160" Margin="0,0,8,6" ToolTip="Comma separated custom tokens" />
                <CheckBox Name="ChkCleanOnlyIfOldPrefix" Content="Only clean if old prefix present" Margin="0,0,8,6"/>
              </WrapPanel>
            </StackPanel>

            <StackPanel Grid.Column="1" Orientation="Vertical">
              <TextBlock Text="File Types and Metadata" FontWeight="Bold" Margin="0,0,0,6"/>
              <WrapPanel VerticalAlignment="Center" ItemHeight="28" ItemWidth="Auto">
                <CheckBox Name="ChkVideo" Content="Video" IsChecked="True" Margin="0,0,8,6"/>
                <CheckBox Name="ChkPictures" Content="Pictures" Margin="0,0,8,6"/>
                <CheckBox Name="ChkDocuments" Content="Documents" Margin="0,0,8,6"/>
                <CheckBox Name="ChkAudio" Content="Audio" Margin="0,0,8,6"/>
                <CheckBox Name="ChkArchives" Content="Archives" Margin="0,0,8,6"/>
                <TextBox Name="TxtCustomExt" Width="200" Margin="6,0,8,6" ToolTip="Comma separated custom extensions" />
                <CheckBox Name="ChkUseAudioMetadata" Content="Use Audio Metadata" Margin="8,0,8,6"/>
                <CheckBox Name="ChkUseVideoMetadata" Content="Use Video Metadata" Margin="8,0,8,6"/>
              </WrapPanel>
            </StackPanel>
          </Grid>
        </Border>

        <!-- Preview DataGrid (fills remaining space) -->
        <Grid Grid.Row="3" Margin="0,0,0,8">
          <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
          </Grid.RowDefinitions>

          <StackPanel Orientation="Horizontal" Grid.Row="0" HorizontalAlignment="Left" Margin="0,0,0,6">
            <Button Name="BtnScan" Content="Scan / Preview" Width="120" Margin="0,0,8,0"/>
            <Button Name="BtnScanMeta" Content="Scan Metadata" Width="120" Margin="0,0,8,0"/>
            <Button Name="BtnExportCsv" Content="Export Preview CSV" Width="160" Margin="0,0,8,0"/>
            <TextBlock Text=" " Width="12"/>
            <TextBlock Text="Preview list shows Original and Proposed names; select items to Apply." VerticalAlignment="Center" TextWrapping="Wrap" Width="520"/>
          </StackPanel>

          <DataGrid Name="DgPreview" Grid.Row="1" ItemsSource="{Binding}" AutoGenerateColumns="False" CanUserResizeRows="False" RowHeight="26" MinRowHeight="22">
            <DataGrid.Resources>
              <Style TargetType="DataGridColumnHeader">
                <Setter Property="FontWeight" Value="Bold"/>
              </Style>
            </DataGrid.Resources>
            <DataGrid.Columns>
              <DataGridCheckBoxColumn Header="Apply" Binding="{Binding Apply}" Width="60"/>
              <DataGridTextColumn Header="Original" Binding="{Binding Original}" Width="2*" IsReadOnly="True"/>
              <DataGridTextColumn Header="Proposed" Binding="{Binding Proposed}" Width="2*" IsReadOnly="False"/>
              <DataGridTextColumn Header="Status" Binding="{Binding Status}" Width="180" IsReadOnly="True"/>
              <DataGridTextColumn Header="MetaSummary" Binding="{Binding MetaSummary}" Width="200" IsReadOnly="True"/>
            </DataGrid.Columns>
          </DataGrid>
        </Grid>

        <!-- Bottom action buttons -->
        <StackPanel Grid.Row="4" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,6,0,0">
          <Button Name="BtnSelectAll" Width="92" Content="Select All" Margin="4"/>
          <Button Name="BtnSelectNone" Width="92" Content="Select None" Margin="4"/>
          <Button Name="BtnApply" Width="110" Content="Apply" Margin="4"/>
          <Button Name="BtnUndo" Width="110" Content="Undo" Margin="4" IsEnabled="False"/>
          <Button Name="BtnReset" Width="92" Content="Reset" Margin="4"/>
          <Button Name="BtnExit" Width="92" Content="Exit" Margin="4"/>
        </StackPanel>

      </Grid>
    </ScrollViewer>
  </DockPanel>
</Window>
"@

# --- Load XAML and controls ---
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$Window = [Windows.Markup.XamlReader]::Load($reader)

# find controls (essential ones used below)
$TxtFolder = $Window.FindName('TxtFolder'); $BtnBrowse = $Window.FindName('BtnBrowse'); $ChkRecurse = $Window.FindName('ChkRecurse')
$ChkChangePrefix = $Window.FindName('ChkChangePrefix'); $TxtOldPrefix = $Window.FindName('TxtOldPrefix'); $TxtNewPrefix = $Window.FindName('TxtNewPrefix')
$ChkRequireExistingPrefix = $Window.FindName('ChkRequireExistingPrefix'); $ChkAddIfMissing = $Window.FindName('ChkAddIfMissing')
$CmbSeasonDigits = $Window.FindName('CmbSeasonDigits'); $CmbEpisodeDigits = $Window.FindName('CmbEpisodeDigits')
$TxtStart = $Window.FindName('TxtStart'); $TxtSeason = $Window.FindName('TxtSeason'); $ChkAddEpisode = $Window.FindName('ChkAddEpisode'); $ChkAddSeason = $Window.FindName('ChkAddSeason')
$TxtCustomClean = $Window.FindName('TxtCustomClean'); $Chk720p = $Window.FindName('Chk720p'); $Chk1080p = $Window.FindName('Chk1080p'); $Chk4k = $Window.FindName('Chk4k')
$Chk720 = $Window.FindName('Chk720'); $Chk1080 = $Window.FindName('Chk1080'); $ChkHD = $Window.FindName('ChkHD'); $ChkCleanOnlyIfOldPrefix = $Window.FindName('ChkCleanOnlyIfOldPrefix')
$ChkVideo = $Window.FindName('ChkVideo'); $ChkPictures = $Window.FindName('ChkPictures'); $ChkDocuments = $Window.FindName('ChkDocuments'); $ChkAudio = $Window.FindName('ChkAudio'); $ChkArchives = $Window.FindName('ChkArchives')
$TxtCustomExt = $Window.FindName('TxtCustomExt'); $ChkUseAudioMetadata = $Window.FindName('ChkUseAudioMetadata'); $ChkUseVideoMetadata = $Window.FindName('ChkUseVideoMetadata')
$BtnScan = $Window.FindName('BtnScan'); $BtnScanMeta = $Window.FindName('BtnScanMeta'); $BtnExportCsv = $Window.FindName('BtnExportCsv')
$DgPreview = $Window.FindName('DgPreview'); $BtnSelectAll = $Window.FindName('BtnSelectAll'); $BtnSelectNone = $Window.FindName('BtnSelectNone')
$BtnApply = $Window.FindName('BtnApply'); $BtnUndo = $Window.FindName('BtnUndo'); $BtnReset = $Window.FindName('BtnReset'); $BtnExit = $Window.FindName('BtnExit')
$TxtStatusBar = $Window.FindName('TxtStatusBar'); $TxtTools = $Window.FindName('TxtTools'); $ChkDryRun = $Window.FindName('ChkDryRun')

# Bind preview collection
$DgPreview.ItemsSource = $global:PreviewList

# --- UI wiring (small, robust handlers) ---
$ChkChangePrefix.add_Checked({
    $TxtOldPrefix.IsEnabled = $true; $TxtNewPrefix.IsEnabled = $true; $ChkRequireExistingPrefix.IsEnabled = $true; $ChkAddIfMissing.IsEnabled = $true
})
$ChkChangePrefix.add_Unchecked({
    $TxtOldPrefix.IsEnabled = $false; $TxtNewPrefix.IsEnabled = $false; $ChkRequireExistingPrefix.IsEnabled = $false; $ChkAddIfMissing.IsEnabled = $false
})

# safe combo initialization
try { $CmbSeasonDigits.SelectedIndex = 0 } catch {}
try { $CmbEpisodeDigits.SelectedIndex = 0 } catch {}

function Set-Status { param($t) try { $action = [System.Action]{ $TxtStatusBar.Text = $t }; $Window.Dispatcher.Invoke($action) } catch { try { $TxtStatusBar.Text = $t } catch {} } }

# simple browse
$BtnBrowse.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.SelectedPath = $ScriptRoot
    if ($dlg.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { $TxtFolder.Text = $dlg.SelectedPath }
})

# minimal scan/preview logic (keeps previous behaviors but simplified for clarity)
function Build-MetaSummary { param($meta) $klist=@('Title','Artist','Album','Season','Episode','track','title'); foreach($k in $klist){ if($meta.ContainsKey($k)){ return ('{0}: {1}' -f $k, $meta[$k]) } } return '' }

# Helpers for extensions and cleaning
$ExtGroups = @{ Video=@('mp4','mkv','avi','mov','wmv','flv','mpeg','mpg','m4v'); Pictures=@('jpg','jpeg','png','gif','webp','bmp','tiff','heic'); Documents=@('pdf','doc','docx','xls','xlsx','ppt','pptx','txt','rtf'); Audio=@('mp3','flac','m4a','aac','wav','ogg'); Archives=@('zip','rar','7z','tar','gz','bz2') }

function Get-ExtensionsFromUI { param($v,$p,$d,$a,$ar,$txt) $list=@(); if($v){$list+=$ExtGroups.Video}; if($p){$list+=$ExtGroups.Pictures}; if($d){$list+=$ExtGroups.Documents}; if($a){$list+=$ExtGroups.Audio}; if($ar){$list+=$ExtGroups.Archives}; if($txt){$list+=($txt -split '[,; ]+' | ForEach-Object{$_ -replace '^\.+','' } )}; return ($list | ForEach-Object{ $_.ToLowerInvariant() } | Select-Object -Unique) }

function Get-FilesByExtensions { param($folder,$exts,$recurse) if(-not (Test-Path $folder)) { return @() } $files = Get-ChildItem -Path $folder -File -Recurse:$recurse -ErrorAction SilentlyContinue | Where-Object { $exts -contains $_.Extension.TrimStart('.').ToLowerInvariant() }; return $files }

function Get-CleanTokens { param($c720p,$c1080p,$c4k,$c720,$c1080,$cHD,$txt) $t=@(); if($c720p){$t+='720p'}; if($c1080p){$t+='1080p'}; if($c4k){$t+='4k';$t+='2160p'}; if($c720){$t+='720'}; if($c1080){$t+='1080'}; if($cHD){$t+='hd'}; if($txt){$t+=$txt -split '[,;]+'}; return ($t | ForEach-Object{$_ -as [string]} | Select-Object -Unique) }

function Clean-FilenameTokens { param($base,$tokens) if(-not $base){return ''} $s=$base; foreach($tok in $tokens){ $p=[regex]::Escape($tok); $s=[regex]::Replace($s,"(^|[^a-zA-Z0-9])$p([^a-zA-Z0-9]|$)",' ','IgnoreCase') }; $s=$s -replace '[._]+',' ' -replace '\s{2,}',' ' ; return $s.Trim() }

function Normalize-Proposed { param($origBase,$old,$new,$require,$addMissing,$addSeason,$season,$seasonDigits,$seasonBefore,$addEpisode,$epNum,$epDigits,$renumber,$cleanOnly,$cleanTokens)
    $b = [string]$origBase
    $b = [regex]::Replace($b,'(?i)S?\d{1,2}[ex-]?\d{1,2}',' ')
    $b = $b -replace '[._]+',' '
    $b = $b.Trim()
    $hasOld = $false
    if($old -and $old.Trim() -ne ''){ if($b -match ("^(?i)"+[regex]::Escape($old))){$hasOld=$true} }
    if($require -and -not $hasOld -and -not $addMissing){ $proposed = $b } else {
        $prefix = if($new -and $new.Trim() -ne ''){ $new.Trim() } elseif($old -and $old.Trim() -ne ''){ $old.Trim() } else { $null }
        if($addMissing -and $prefix){ if(-not ($b -match ("^(?i)"+[regex]::Escape($prefix)))){ $b = ($prefix + ' ' + $b).Trim() } }
        elseif($hasOld -and $new -and ($new -ne $old) -and $new.Trim() -ne ''){ $b = [regex]::Replace($b,("^(?i)"+[regex]::Escape($old)),$new) }
        $proposed = $b
    }
    if($cleanTokens -and $cleanTokens.Count -gt 0){ if(-not $cleanOnly -or $hasOld){ $proposed = Clean-FilenameTokens -baseName $proposed -TokensToRemove $cleanTokens } }
    $se=''; if($addSeason){ $sd = ('{0:D' + $seasonDigits + '}') -f [int]$season; $se = "S$sd" }
    $ep=''; if($addEpisode){ $ed = ('{0:D' + $epDigits + '}') -f [int]$epNum; $ep = "E$ed" }
    $prefixTokens=@(); if($se -and $ep){ if($seasonBefore){ $prefixTokens += ($se+$ep) } else { $prefixTokens += ($ep+$se) } } elseif($se){ $prefixTokens += $se } elseif($ep){ $prefixTokens += $ep }
    if($prefixTokens.Count -gt 0){ $proposed = ($prefixTokens -join ' ') + ' ' + $proposed }
    $proposed = $proposed -replace '\s{2,}',' '
    return $proposed.Trim()
}

# --- Simple preview generation (keeps core behaviors) ---
function Generate-Preview {
    param($folder)
    try {
        Set-Status "Scanning..."
        $exts = Get-ExtensionsFromUI -v:$ChkVideo.IsChecked -p:$ChkPictures.IsChecked -d:$ChkDocuments.IsChecked -a:$ChkAudio.IsChecked -ar:$ChkArchives.IsChecked -txt:$TxtCustomExt.Text
        $files = Get-FilesByExtensions -folder $folder -exts $exts -recurse $ChkRecurse.IsChecked
        $global:PreviewList.Clear()
        if (-not $files -or $files.Count -eq 0) { Set-Status "No matching files"; return }

        $items = @()
        $start = 1
        try { $start = [int]$TxtStart.Text } catch {}
        $season = 1
        try { $season = [int]$TxtSeason.Text } catch {}
        $ep = $start
        $seasonDigits = (try { [int]$CmbSeasonDigits.SelectedItem.Content } catch { 2 })
        $episodeDigits = (try { [int]$CmbEpisodeDigits.SelectedItem.Content } catch { 2 })
        $cleanTokens = Get-CleanTokens -c720p:$Chk720p.IsChecked -c1080p:$Chk1080p.IsChecked -c4k:$Chk4k.IsChecked -c720:$Chk720.IsChecked -c1080:$Chk1080.IsChecked -cHD:$ChkHD.IsChecked -txt:$TxtCustomClean.Text

        foreach ($f in $files) {
            $origBase = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
            $meta = @{}
            # metadata reading left minimal; integrate Read-Metadata if available
            $titleFromMeta = $null
            $baseCandidate = if ($titleFromMeta) { $titleFromMeta } else { $origBase }

            $proposedBase = Normalize-Proposed -origBase $baseCandidate -old $TxtOldPrefix.Text -new $TxtNewPrefix.Text -require $ChkRequireExistingPrefix.IsChecked -addMissing $ChkAddIfMissing.IsChecked -addSeason $ChkAddSeason.IsChecked -season $season -seasonDigits $seasonDigits -seasonBefore $Window.FindName('ChkSeasonBeforeEpisode').IsChecked -addEpisode $ChkAddEpisode.IsChecked -epNum $ep -epDigits $episodeDigits -renumber $ChkRenumberAll.IsChecked -cleanOnly $ChkCleanOnlyIfOldPrefix.IsChecked -cleanTokens $cleanTokens

            $metaSummary = Build-MetaSummary -meta $meta

            $items += [pscustomobject]@{
                FullPath = $f.FullName
                Original = $f.Name
                ProposedBase = $proposedBase
                Extension = $f.Extension
                MetaSummary = $metaSummary
                Metadata = $meta
            }

            if ($ChkAddEpisode.IsChecked) { $ep++ }
        }

        # prefer NewPrefix when enforcing AddIfMissing
        foreach ($it in $items) {
            $pref = $null
            if ($TxtNewPrefix.Text -and $TxtNewPrefix.Text.Trim() -ne '') { $pref = $TxtNewPrefix.Text.Trim() }
            elseif ($TxtOldPrefix.Text -and $TxtOldPrefix.Text.Trim() -ne '') { $pref = $TxtOldPrefix.Text.Trim() }
            if ($ChkAddIfMissing.IsChecked -and $pref) {
                if (-not ($it.ProposedBase -match ("^(?i)" + [regex]::Escape($pref)))) {
                    $it.ProposedBase = ($pref + ' ' + $it.ProposedBase).Trim()
                }
            }
        }

        # resolve duplicates
        $grouped = $items | Group-Object -Property ProposedBase
        foreach ($g in $grouped) {
            if ($g.Count -gt 1) {
                $i = 1
                foreach ($it in $g) {
                    $it.ProposedBase = "$($it.ProposedBase)-Version$i"
                    $i++
                }
            }
        }

        # convert items to preview entries and add to observable collection
        foreach ($it in $items) {
            $proposedName = "$($it.ProposedBase)$($it.Extension)"
            $pi = New-PreviewItem -FullPath $it.FullPath -Original $it.Original -Proposed $proposedName -Status 'Ready' -MetaSummary $it.MetaSummary -Metadata $it.Metadata
            $global:PreviewList.Add($pi)
        }

        $DgPreview.Items.Refresh()
        Set-Status ("Scan complete. {0} items." -f $global:PreviewList.Count)
    } catch {
        Write-Log "Generate-Preview error: $_"
        Set-Status ("Error: {0}" -f $_)
    }
}

# --- Buttons wiring ---
$BtnScan.Add_Click({
    if(-not $TxtFolder.Text -or -not (Test-Path $TxtFolder.Text)){ Set-Status "Select a valid folder"; return }
    Start-Sleep -Milliseconds 30
    Generate-Preview -folder $TxtFolder.Text
})
$BtnScanMeta.Add_Click({ $BtnScan.RaiseEvent([System.Windows.RoutedEventArgs]::new([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)) })

$BtnExportCsv.Add_Click({
    if($global:PreviewList.Count -eq 0){ Set-Status "No preview to export"; return }
    $dlg = New-Object System.Windows.Forms.SaveFileDialog; $dlg.Filter="CSV files (*.csv)|*.csv"; $dlg.FileName="preview_$((Get-Date).ToString('yyyyMMdd-HHmmss')).csv"
    if($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK){ return }
    $rows = $global:PreviewList | ForEach-Object { [pscustomobject]@{ Original=$_.Original; Proposed=$_.Proposed; Status=$_.Status; MetaSummary=$_.MetaSummary; Apply=$_.Apply } }
    try { [System.IO.File]::WriteAllLines($dlg.FileName, (,@("Original,Proposed,Status,MetaSummary,Apply") + ($rows | ForEach-Object { ('"{0}","{1}","{2}","{3}","{4}"' -f ($_.Original -replace '"','""'), ($_.Proposed -replace '"','""'), ($_.Status -replace '"','""'), ($_.MetaSummary -replace '"','""'), $_.Apply) } )), [System.Text.Encoding]::UTF8); Set-Status ("Exported to {0}" -f $dlg.FileName) } catch { Set-Status ("Export failed: {0}" -f $_) }
})

$BtnSelectAll.Add_Click({ foreach($i in $global:PreviewList){ $i.Apply = $true }; $DgPreview.Items.Refresh() })
$BtnSelectNone.Add_Click({ foreach($i in $global:PreviewList){ $i.Apply = $false }; $DgPreview.Items.Refresh() })
$BtnReset.Add_Click({ $global:PreviewList.Clear(); $DgPreview.Items.Refresh(); Set-Status "Reset" })
$BtnExit.Add_Click({ $Window.Close() })

# Minimal Apply/Undo placeholders (detailed logic can be reused from previous script)
$BtnApply.Add_Click({
    if($global:PreviewList.Count -eq 0){ Set-Status "Nothing to apply"; return }
    if($ChkDryRun.IsChecked){ Set-Status "Dry-run: no files changed"; return }
    try {
        $changes = @()
        foreach($it in $global:PreviewList){ if($it.Apply){ $src=$it.FullPath; $destDir=$it.Directory; $destName=$it.Proposed; $destPath = Join-Path $destDir $destName; if(Test-Path $destPath){ $base=[System.IO.Path]::GetFileNameWithoutExtension($destName); $ext=[System.IO.Path]::GetExtension($destName); $i=1; while(Test-Path (Join-Path $destDir ("$base ($i)$ext"))){ $i++ }; $destPath = Join-Path $destDir ("$base ($i)$ext") } Move-Item -Path $src -Destination $destPath -Force; $changes += [pscustomobject]@{ Old=$destPath; New=$src } } }
        if($changes.Count -gt 0){ $undo = $changes | ConvertTo-Json -Depth 4; $uFile = Join-Path $ScriptRoot ("undo-rename_$((Get-Date).ToString('yyyyMMdd-HHmmss')).json"); [System.IO.File]::WriteAllText($uFile,$undo,[System.Text.Encoding]::UTF8); Set-Status ("Applied changes. Undo: {0}" -f $uFile); $BtnUndo.IsEnabled = $true }
    } catch { Set-Status ("Apply failed: {0}" -f $_) }
})

$BtnUndo.Add_Click({
    try {
        $undos = Get-ChildItem -Path $ScriptRoot -Filter 'undo-rename_*.json' -File | Sort-Object LastWriteTime -Descending
        if($undos.Count -eq 0){ Set-Status "No undo file"; return }
        $data = Get-Content $undos[0].FullName -Raw | ConvertFrom-Json
        foreach($o in $data){ if(Test-Path $o.Old){ Move-Item -Path $o.Old -Destination $o.New -Force } }
        Set-Status "Undo attempted"
    } catch { Set-Status ("Undo failed: {0}" -f $_) }
})

# Show window
$Window.ShowDialog() | Out-Null