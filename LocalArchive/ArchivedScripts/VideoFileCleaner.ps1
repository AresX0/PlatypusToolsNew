# Rename-Videos-WPF-IncrementalSuffix.ps1
#Developed by Joseph Say
#Version 1.1
Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Xaml
Add-Type -AssemblyName System.Windows.Forms

# -----------------------
# Configuration
# -----------------------
$videoExts = @('.mp4','.mkv','.avi','.mov','.wmv','.flv','.webm','.mpeg','.mpg','.m4v')

# -----------------------
# XAML UI
# -----------------------
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Batch Video Renamer" Height="900" Width="1160" WindowStartupLocation="CenterScreen" MinHeight="720" MinWidth="900">
  <Grid Margin="10">
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <StackPanel Orientation="Horizontal" Grid.Row="0" Margin="0,0,0,8">
      <Label Content="Folder:" VerticalAlignment="Center"/>
      <TextBox Name="TxtFolder" Width="860" Margin="6,0,6,0" IsReadOnly="False"/>
      <Button Name="BtnBrowse" Width="90" Content="Browse"/>
      <CheckBox Name="ChkRecurse" Content="Include Subfolders" Margin="8,0,0,0" VerticalAlignment="Center"/>
    </StackPanel>

    <Border Grid.Row="1" BorderBrush="#DDD" BorderThickness="1" Padding="8" Margin="0,0,0,8" CornerRadius="4">
      <Grid>
        <Grid.ColumnDefinitions>
          <ColumnDefinition Width="*"/>
          <ColumnDefinition Width="420"/>
        </Grid.ColumnDefinitions>

        <StackPanel Orientation="Vertical" Grid.Column="0">
          <StackPanel Orientation="Horizontal" Margin="0,0,0,6" VerticalAlignment="Center">
            <CheckBox Name="ChkChangePrefix" VerticalAlignment="Center"/>
            <Label Content="Change Prefix (Old -> New). Old required when checked; New optional (empty = keep old prefix)" VerticalAlignment="Center" Margin="6,0,0,0"/>
          </StackPanel>

          <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
            <Label Content="Old Prefix" VerticalAlignment="Center" Width="80"/>
            <TextBox Name="TxtOldPrefix" Width="320" Margin="6,0,12,0" IsEnabled="False"/>
            <Label Content="New Prefix" VerticalAlignment="Center" Width="80"/>
            <TextBox Name="TxtNewPrefix" Width="320" Margin="6,0,0,0" IsEnabled="False"/>
            <CheckBox Name="ChkRequireExistingPrefix" Content="Only files that already have Old Prefix" Margin="12,0,0,0" VerticalAlignment="Center" IsEnabled="False"/>
          </StackPanel>

          <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
            <CheckBox Name="ChkAddSeason" VerticalAlignment="Center"/>
            <Label Content="Add Season" VerticalAlignment="Center" Margin="6,0,0,0"/>
            <TextBox Name="TxtSeason" Width="60" Text="1" Margin="6,0,0,0"/>
            <Label Content="Season digits" VerticalAlignment="Center" Margin="8,0,0,0"/>
            <ComboBox Name="CmbSeasonDigits" Width="60" SelectedIndex="1" Margin="6,0,0,0">
              <ComboBoxItem>1</ComboBoxItem>
              <ComboBoxItem>2</ComboBoxItem>
              <ComboBoxItem>3</ComboBoxItem>
            </ComboBox>
            <CheckBox Name="ChkSeasonBeforeEpisode" Content="Use S##E### format (no separator)" Margin="12,0,0,0" VerticalAlignment="Center"/>
          </StackPanel>

          <StackPanel Orientation="Horizontal" Margin="0,0,0,6">
            <CheckBox Name="ChkAddEpisode" VerticalAlignment="Center"/>
            <Label Content="Add/Update Episode" VerticalAlignment="Center" Margin="6,0,0,0"/>
            <Label Content="Start" VerticalAlignment="Center" Margin="12,0,0,0"/>
            <TextBox Name="TxtStart" Width="60" Text="1" Margin="6,0,0,0"/>
            <Label Content="Episode digits" VerticalAlignment="Center" Margin="10,0,0,0"/>
            <ComboBox Name="CmbEpisodeDigits" Width="60" SelectedIndex="2" Margin="6,0,0,0">
              <ComboBoxItem>1</ComboBoxItem>
              <ComboBoxItem>2</ComboBoxItem>
              <ComboBoxItem>3</ComboBoxItem>
              <ComboBoxItem>4</ComboBoxItem>
            </ComboBox>
            <CheckBox Name="ChkRenumberAll" Content="Renumber All Alphabetically" Margin="12,0,0,0" VerticalAlignment="Center"/>
          </StackPanel>

          <StackPanel Orientation="Horizontal" Margin="0,6,0,6">
            <CheckBox Name="ChkDryRun" Content="Dry-run (no files changed)" VerticalAlignment="Center"/>
            <Button Name="BtnScan" Content="Scan / Preview" Width="140" Margin="12,0,0,0"/>
            <Button Name="BtnExportCsv" Content="Export Preview CSV" Width="160" Margin="8,0,0,0" IsEnabled="False"/>
          </StackPanel>

          <GroupBox Header="Clean filename tokens" Margin="0,6,0,0">
            <StackPanel Orientation="Vertical" Margin="6">
              <TextBlock Text="Preset tokens to remove (check to remove):" FontWeight="Bold" Margin="0,0,6,6"/>
              <WrapPanel>
                <CheckBox Name="Chk720p" Content="_720p / -720p / 720p" Margin="4"/>
                <CheckBox Name="Chk1080p" Content="_1080p / -1080p / 1080p" Margin="4"/>
                <CheckBox Name="Chk4k" Content="_4k / -4k / 4k" Margin="4"/>
                <CheckBox Name="Chk720" Content="_720 / -720 / 720" Margin="4"/>
                <CheckBox Name="Chk1080" Content="_1080 / -1080 / 1080" Margin="4"/>
                <CheckBox Name="ChkHD" Content="_hd / -hd / hd" Margin="4"/>
              </WrapPanel>
              <TextBlock Text="Custom tokens to remove (comma-separated, e.g. x264,WEBRip):" Margin="0,6,0,0"/>
              <TextBox Name="TxtCustomClean" Width="860" Height="24" Margin="0,4,0,0"/>
              <StackPanel Orientation="Horizontal" Margin="0,6,0,0">
                <CheckBox Name="ChkCleanOnlyIfOldPrefix" Content="Only clean files matching Old Prefix" Margin="0,0,12,0" />
                <TextBlock Text="(requires 'Only files that already have Old Prefix' checked to enable interactive prompt)" VerticalAlignment="Center" Foreground="Gray"/>
              </StackPanel>
            </StackPanel>
          </GroupBox>
        </StackPanel>

        <StackPanel Grid.Column="1" Orientation="Vertical" HorizontalAlignment="Right">
          <TextBlock Text="Selection / Actions" FontWeight="Bold" Margin="0,0,0,8"/>
          <Button Name="BtnSelectAll" Content="Select All" Width="180" Margin="0,0,6,6"/>
          <Button Name="BtnSelectNone" Content="Select None" Width="180" Margin="0,0,0,6"/>
          <Button Name="BtnApply" Content="Apply Changes" Width="180" Margin="0,6,0,6" IsEnabled="False"/>
          <Button Name="BtnUndo" Content="Undo Last" Width="180" IsEnabled="False" Margin="0,0,0,6"/>
          <Button Name="BtnReset" Content="Reset All Options" Width="180" Margin="0,6,0,6"/>
          <Button Name="BtnExit" Content="Exit" Width="180" Margin="0,6,0,6"/>
        </StackPanel>
      </Grid>
    </Border>

    <DataGrid Grid.Row="2" Name="DgPreview" AutoGenerateColumns="False" CanUserAddRows="False" SelectionMode="Extended" IsReadOnly="False">
      <DataGrid.Columns>
        <DataGridCheckBoxColumn Header="Apply" Binding="{Binding Apply, Mode=TwoWay}" Width="70"/>
        <DataGridTextColumn Header="Original Name" Binding="{Binding Original}" IsReadOnly="True" Width="*"/>
        <DataGridTextColumn Header="Proposed Name" Binding="{Binding Proposed}" IsReadOnly="True" Width="*"/>
        <DataGridTextColumn Header="Status" Binding="{Binding Status}" IsReadOnly="True" Width="260"/>
      </DataGrid.Columns>
    </DataGrid>

    <DockPanel Grid.Row="3" LastChildFill="True" Margin="0,8,0,0">
      <StatusBar DockPanel.Dock="Bottom">
        <StatusBarItem>
          <TextBlock Name="TxtStatusBar" Text="Ready" />
        </StatusBarItem>
      </StatusBar>
    </DockPanel>

    <TextBlock Grid.Row="4" Foreground="Gray" Text="Tip: Use Dry-run to preview. Reset All returns all controls to defaults." Margin="0,8,0,0"/>
  </Grid>
</Window>
"@

# -----------------------
# Load XAML and controls
# -----------------------
$reader = (New-Object System.Xml.XmlNodeReader $xaml)
$window = [Windows.Markup.XamlReader]::Load($reader)

# main controls
$TxtFolder = $window.FindName("TxtFolder"); $BtnBrowse = $window.FindName("BtnBrowse"); $ChkRecurse = $window.FindName("ChkRecurse")
$ChkChangePrefix = $window.FindName("ChkChangePrefix"); $TxtOldPrefix = $window.FindName("TxtOldPrefix"); $TxtNewPrefix = $window.FindName("TxtNewPrefix")
$ChkRequireExistingPrefix = $window.FindName("ChkRequireExistingPrefix")
$ChkAddSeason = $window.FindName("ChkAddSeason"); $TxtSeason = $window.FindName("TxtSeason"); $CmbSeasonDigits = $window.FindName("CmbSeasonDigits"); $ChkSeasonBeforeEpisode = $window.FindName("ChkSeasonBeforeEpisode")
$ChkAddEpisode = $window.FindName("ChkAddEpisode"); $TxtStart = $window.FindName("TxtStart"); $CmbEpisodeDigits = $window.FindName("CmbEpisodeDigits"); $ChkRenumberAll = $window.FindName("ChkRenumberAll")
$ChkDryRun = $window.FindName("ChkDryRun"); $BtnScan = $window.FindName("BtnScan"); $BtnExportCsv = $window.FindName("BtnExportCsv")
$BtnSelectAll = $window.FindName("BtnSelectAll"); $BtnSelectNone = $window.FindName("BtnSelectNone"); $BtnApply = $window.FindName("BtnApply"); $BtnUndo = $window.FindName("BtnUndo")
$BtnReset = $window.FindName("BtnReset"); $BtnExit = $window.FindName("BtnExit")
$DgPreview = $window.FindName("DgPreview"); $TxtStatusBar = $window.FindName("TxtStatusBar")

# cleaning controls
$Chk720p = $window.FindName("Chk720p"); $Chk1080p = $window.FindName("Chk1080p"); $Chk4k = $window.FindName("Chk4k")
$Chk720 = $window.FindName("Chk720"); $Chk1080 = $window.FindName("Chk1080"); $ChkHD = $window.FindName("ChkHD")
$TxtCustomClean = $window.FindName("TxtCustomClean"); $ChkCleanOnlyIfOldPrefix = $window.FindName("ChkCleanOnlyIfOldPrefix")

# -----------------------
# Helpers
# -----------------------
function Set-Status($text) { $TxtStatusBar.Dispatcher.Invoke([action]{ $TxtStatusBar.Text = $text }) }

function Choose-Folder {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    $dlg.Description = "Select folder to scan for video files"
    if ($dlg.ShowDialog() -eq 'OK') { return $dlg.SelectedPath } else { return $null }
}

function Prompt-Input($title,$message){
    $form = New-Object System.Windows.Forms.Form
    $form.Text = $title; $form.Width = 420; $form.Height = 150; $form.StartPosition = 'CenterScreen'
    $lbl = New-Object System.Windows.Forms.Label; $lbl.Text = $message; $lbl.AutoSize = $true; $lbl.Location = '10,10'
    $txt = New-Object System.Windows.Forms.TextBox; $txt.Location = '10,40'; $txt.Width = 380
    $btnOk = New-Object System.Windows.Forms.Button; $btnOk.Text = "OK"; $btnOk.Location = '220,70'
    $btnOk.Add_Click({ $form.Tag = $txt.Text; $form.DialogResult = [System.Windows.Forms.DialogResult]::OK; $form.Close() })
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "Cancel"; $btnCancel.Location = '300,70'
    $btnCancel.Add_Click({ $form.DialogResult = [System.Windows.Forms.DialogResult]::Cancel; $form.Close() })
    $form.Controls.AddRange(@($lbl,$txt,$btnOk,$btnCancel))
    if ($form.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { return $form.Tag } else { return $null }
}

function Get-VideoFiles { param($folder, $recurse) if ($recurse) { Get-ChildItem -Path $folder -File -Recurse | Where-Object { $videoExts -contains $_.Extension.ToLower() } } else { Get-ChildItem -Path $folder -File | Where-Object { $videoExts -contains $_.Extension.ToLower() } } }

function Pad-Number { param($num,$pad) return $num.ToString("D$pad") }
function Pad-Episode { param($num,$pad) return ("E" + (Pad-Number -num $num -pad $pad)) }
function Pad-Season { param($num,$pad) return ("S" + (Pad-Number -num $num -pad $pad)) }

function Get-DigitsFromCombo($combo) { return [int]($combo.SelectedItem.Content) }

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

function Get-CleanTokens {
    param($use720p,$use1080p,$use4k,$use720,$use1080,$useHD,$custom)
    $tokens = @()
    if ($use720p) { $tokens += '720p' }
    if ($use1080p) { $tokens += '1080p' }
    if ($use4k) { $tokens += '4k' }
    if ($use720) { $tokens += '720' }
    if ($use1080) { $tokens += '1080' }
    if ($useHD) { $tokens += 'hd' }
    if ($custom) { $custom -split ',' | ForEach-Object { $t = $_.Trim(); if ($t) { $tokens += $t } } }
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
# State
# -----------------------
$PreviewList = New-Object System.Collections.ObjectModel.ObservableCollection[psobject]

# -----------------------
# UI wiring
# -----------------------
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

# Enforce ChangePrefix for clean-only option
$ChkCleanOnlyIfOldPrefix.add_Checked({
    if (-not $ChkChangePrefix.IsChecked) {
        $resp = [System.Windows.MessageBox]::Show(
            "The option 'Only clean files matching Old Prefix' requires 'Change Prefix' to be enabled. Would you like to enable Change Prefix now?",
            "Enable Change Prefix",
            [System.Windows.MessageBoxButton]::YesNo,
            [System.Windows.MessageBoxImage]::Question
        )
        if ($resp -eq [System.Windows.MessageBoxResult]::Yes) {
            $ChkChangePrefix.IsChecked = $true
            $TxtOldPrefix.IsEnabled = $true
            $TxtNewPrefix.IsEnabled = $true
            $ChkRequireExistingPrefix.IsEnabled = $true
            if ([string]::IsNullOrWhiteSpace($TxtOldPrefix.Text)) {
                $typed = Prompt-Input "Old Prefix required for cleaning" "Please enter the Old Prefix to use (leave blank to cancel):"
                if ($null -eq $typed -or [string]::IsNullOrWhiteSpace($typed)) {
                    $ChkCleanOnlyIfOldPrefix.IsChecked = $false
                    Set-Status "Clean-only-by-prefix cancelled; option cleared."
                    return
                }
                $TxtOldPrefix.Text = $typed.Trim()
                Set-Status "Cleaning will apply only to files matching prefix: $($TxtOldPrefix.Text)"
            }
        } else {
            $ChkCleanOnlyIfOldPrefix.IsChecked = $false
            Set-Status "Clean-only-by-prefix cancelled; option cleared."
            return
        }
    } else {
        if ([string]::IsNullOrWhiteSpace($TxtOldPrefix.Text)) {
            $typed = Prompt-Input "Old Prefix required for cleaning" "You chose to clean only files matching the Old Prefix. Please enter the Old Prefix to use (leave blank to cancel):"
            if ($null -eq $typed -or [string]::IsNullOrWhiteSpace($typed)) {
                $ChkCleanOnlyIfOldPrefix.IsChecked = $false
                Set-Status "Clean-only-by-prefix cancelled; option cleared."
                return
            }
            $TxtOldPrefix.Text = $typed.Trim()
            Set-Status "Cleaning will apply only to files matching prefix: $($TxtOldPrefix.Text)"
        } else {
            Set-Status "Cleaning will apply only to files matching prefix: $($TxtOldPrefix.Text)"
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
    $PreviewList.Clear()
    $DgPreview.ItemsSource = $null
    Set-Status "Options reset to defaults."
})

$BtnExit.Add_Click({ $window.Close() })

# -----------------------
# Scan / Preview
# -----------------------
$BtnScan.Add_Click({
    Set-Status "Scanning..."
    $PreviewList.Clear(); $DgPreview.ItemsSource = $null; $BtnApply.IsEnabled = $false; $BtnExportCsv.IsEnabled = $false; $BtnUndo.IsEnabled = $false

    $folder = $TxtFolder.Text.Trim()
    if (-not $folder -or -not (Test-Path $folder)) { [System.Windows.MessageBox]::Show("Please choose a valid folder.","Folder required",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null; Set-Status "No folder selected."; return }

    try {
        $recurse = $ChkRecurse.IsChecked
        $changePrefix = $ChkChangePrefix.IsChecked
        $oldPrefix = if ($changePrefix) { $TxtOldPrefix.Text.Trim() } else { '' }
        $newPrefix = if ($changePrefix) { $TxtNewPrefix.Text.Trim() } else { '' }

        # enforce consistency: cleaning-only-by-prefix requires Change Prefix enabled
        if ($ChkCleanOnlyIfOldPrefix.IsChecked -and -not $ChkChangePrefix.IsChecked) {
            [System.Windows.MessageBox]::Show("The option 'Only clean files matching Old Prefix' requires 'Change Prefix' to be checked. Please check Change Prefix or uncheck the cleaning-only option.","Option conflict",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Warning) | Out-Null
            Set-Status "Scan aborted: option conflict."
            return
        }

        if ($changePrefix -and [string]::IsNullOrWhiteSpace($oldPrefix)) {
            $allFilesTemp = Get-VideoFiles -folder $folder -recurse $recurse
            if (-not $allFilesTemp -or $allFilesTemp.Count -eq 0) { [System.Windows.MessageBox]::Show("No video files found to detect a prefix.","No files",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; Set-Status "No files."; return }
            $first = $allFilesTemp | Select-Object -First 1
            $nameNoExt = [System.IO.Path]::GetFileNameWithoutExtension($first.Name)
            $m = [regex]::Match($nameNoExt, '^(.*?)(?:-|_| )')
            if ($m.Success) { $candidate = $m.Groups[1].Value } else { $candidate = if ($nameNoExt.Length -gt 12) { $nameNoExt.Substring(0,12) } else { $nameNoExt } }
            $msg = "Detected candidate old prefix:`n`n'$candidate'`n`nIs this your old prefix?"
            $res = [System.Windows.MessageBox]::Show($msg,"Confirm Old Prefix",[System.Windows.MessageBoxButton]::YesNoCancel,[System.Windows.MessageBoxImage]::Question)
            if ($res -eq [System.Windows.MessageBoxResult]::Yes) { $oldPrefix = $candidate; $TxtOldPrefix.Text = $oldPrefix }
            elseif ($res -eq [System.Windows.MessageBoxResult]::No) {
                $typed = Prompt-Input "Old Prefix" "Enter the old prefix to use (leave blank to cancel scan):"
                if ($null -eq $typed -or [string]::IsNullOrWhiteSpace($typed)) { Set-Status "Scan cancelled"; return }
                $oldPrefix = $typed.Trim(); $TxtOldPrefix.Text = $oldPrefix
            } else { Set-Status "Scan cancelled"; return }
        }

        if ($ChkCleanOnlyIfOldPrefix.IsChecked -and $ChkRequireExistingPrefix.IsChecked -and [string]::IsNullOrWhiteSpace($TxtOldPrefix.Text)) {
            $typed = Prompt-Input "Old Prefix required for cleaning" "You selected 'Only clean files matching Old Prefix'. Enter Old Prefix to use (leave blank to cancel):"
            if ($null -eq $typed -or [string]::IsNullOrWhiteSpace($typed)) { $ChkCleanOnlyIfOldPrefix.IsChecked = $false; Set-Status "Clean-only-by-prefix cancelled; option cleared."; return }
            $TxtOldPrefix.Text = $typed.Trim(); Set-Status "Cleaning will apply only to files matching prefix: $($TxtOldPrefix.Text)"
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

        $files = Get-VideoFiles -folder $folder -recurse $recurse
        if (-not $files -or $files.Count -eq 0) { [System.Windows.MessageBox]::Show("No video files found.","No files",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; Set-Status "No files."; return }

        $items = @()
        foreach ($f in $files) { $items += [PSCustomObject]@{ FullPath = $f.FullName; Directory = $f.DirectoryName; Original = $f.Name; NameNoExt = [System.IO.Path]::GetFileNameWithoutExtension($f.Name); Extension = $f.Extension } }

        if ($requireExistingPrefix -and $changePrefix -and $oldPrefix) {
            $items = $items | Where-Object { $_.NameNoExt.StartsWith($oldPrefix) -or $_.NameNoExt.StartsWith("$oldPrefix-") -or $_.NameNoExt.StartsWith("$oldPrefix_") }
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
                foreach ($o in $ordered) { if (-not [regex]::IsMatch($o.NameNoExt,'(?i)E(\d{1,4})')) { $epMap[$o.Original] = $index; $index++ } }
            }
        }

        foreach ($it in $items) {
            $orig = $it.Original; $baseNoExt = $it.NameNoExt; $ext = $it.Extension
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
            if ($ChkAddSeason.IsChecked -and -not $hasSeason) { $seasonToken = Pad-Season -num ([int]$TxtSeason.Text) -pad (Get-DigitsFromCombo $CmbSeasonDigits) }

            $episodeToken = $null
            if ($addEpisode -and $epMap.ContainsKey($it.Original)) { $episodeToken = Pad-Episode -num $epMap[$it.Original] -pad (Get-DigitsFromCombo $CmbEpisodeDigits) }

            $proposedBase = Normalize-Proposed -coreBase $core -oldPrefix $oldPrefix -newPrefix $newPrefix -seasonToken $seasonToken -seasonBeforeEpisode $ChkSeasonBeforeEpisode.IsChecked -episodeToken $episodeToken -cleanTokens $cleanTokens -performClean $performClean

            $proposedBase = $proposedBase -replace '(?i)(S\d{1,3}E\d{1,4})(.*\1)+','$1'
            $proposedBase = $proposedBase -replace '(?i)(E\d{1,4})(.*\1)+','$1'

            if (-not [string]::IsNullOrWhiteSpace($newPrefix)) {
                $escapedNew = [regex]::Escape($newPrefix)
                $proposedBase = $proposedBase -replace "(?i)(?:$escapedNew[\-_\. ]*){2,}", $newPrefix
                if ($proposedBase -notmatch "^(?i)$escapedNew") { $proposedBase = "$newPrefix-$proposedBase" }
            } elseif (-not [string]::IsNullOrWhiteSpace($oldPrefix)) {
                $escapedOld = [regex]::Escape($oldPrefix)
                $proposedBase = $proposedBase -replace "(?i)(?:$escapedOld[\-_\. ]*){2,}", $oldPrefix
                if ($proposedBase -notmatch "^(?i)$escapedOld") { $proposedBase = "$oldPrefix-$proposedBase" }
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
            })
        }

        # -----------------------
        # incremental duplicate core-title detection and suffixing
        # -----------------------
        # Build map: core title -> list of preview entries
        $coreMap = @{}
        foreach ($entry in $PreviewList) {
            $proposed = $entry.Proposed
            $ext = [System.IO.Path]::GetExtension($proposed)
            $base = [System.IO.Path]::GetFileNameWithoutExtension($proposed)

            # strip leading prefix token (first token + separator) heuristically
            $core = $base -replace '^[^\-_\. ]+[_\-_\. ]+',''

            # remove S/E tokens anywhere
            $core = $core -replace '(?i)S\d{1,3}E\d{1,4}',''
            $core = $core -replace '(?i)S\d{1,3}\s*-\s*E\d{1,4}',''
            $core = $core -replace '(?i)E\d{1,4}',''
            $core = $core -replace '(?i)S\d{1,3}',''

            $core = $core.Trim(' ','_','-','.')

            if (-not $coreMap.ContainsKey($core)) { $coreMap[$core] = @() }
            $coreMap[$core] += $entry
        }

        # For cores with multiple entries, append incremental -VersionN to subsequent duplicates
        foreach ($kv in $coreMap.GetEnumerator()) {
            $list = $kv.Value
            if ($list.Count -gt 1) {
                # track suffix index per base name to support Version2, Version3...
                $seenBases = @{}
                foreach ($e in $list) {
                    $origProposed = $e.Proposed
                    $ext = [System.IO.Path]::GetExtension($origProposed)
                    $base = [System.IO.Path]::GetFileNameWithoutExtension($origProposed)

                    # base key for counting should ignore existing VersionN suffix
                    $baseKey = $base -replace '(?i)-Version\d+$',''

                    if (-not $seenBases.ContainsKey($baseKey)) {
                        $seenBases[$baseKey] = 1
                        # first occurrence: keep as-is
                    } else {
                        $seenBases[$baseKey] = $seenBases[$baseKey] + 1
                        $ver = $seenBases[$baseKey]
                        # construct new base with Version{ver}
                        $newBase = "$baseKey-Version$ver"
                        $e.Proposed = $newBase + $ext
                        $e.Apply = $true
                    }
                }
            }
        }

        # bind preview
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

# Export preview CSV
$BtnExportCsv.Add_Click({
    if (-not $PreviewList -or $PreviewList.Count -eq 0) { return }
    $dlg = New-Object Microsoft.Win32.SaveFileDialog
    $dlg.FileName = "preview-" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".csv"
    $dlg.DefaultExt = ".csv"
    $dlg.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
    $res = $dlg.ShowDialog()
    if ($res -ne $true) { return }
    $path = $dlg.FileName
    $PreviewList | ForEach-Object { [PSCustomObject]@{ Original = $_.Original; Proposed = $_.Proposed; Status = $_.Status; Apply = $_.Apply } } | Export-Csv -Path $path -NoTypeInformation -Encoding UTF8
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
        $status = ""
        $newFull = $dest
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

    $undoEntries = $results | Where-Object { $_.Status -eq 'Renamed' } | ForEach-Object { [PSCustomObject]@{ Old = $_.NewFullPath; New = $_.OriginalFullPath } }
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

$BtnUndo.Add_Click({
    $folder = $TxtFolder.Text.Trim()
    $undoFiles = Get-ChildItem -Path $folder -Filter "undo-rename_*.json" -File | Sort-Object LastWriteTime -Descending
    if (-not $undoFiles -or $undoFiles.Count -eq 0) { [System.Windows.MessageBox]::Show("No undo files found in folder.","Undo",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null; return }
    $latest = $undoFiles[0].FullName
    try { $entries = Get-Content -Path $latest -Raw | ConvertFrom-Json } catch { [System.Windows.MessageBox]::Show("Undo file invalid.","Undo",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null; return }
    $log = @()
    foreach ($e in $entries) {
        $from = $e.Old; $to = $e.New
        if (-not (Test-Path $from)) { $log += [PSCustomObject]@{ Time = (Get-Date).ToString("s"); From = $from; To = $to; Status = "Source not found" }; continue }
        try { Rename-Item -LiteralPath $from -NewName ([System.IO.Path]::GetFileName($to)) -ErrorAction Stop; $log += [PSCustomObject]@{ Time = (Get-Date).ToString("s"); From = $from; To = $to; Status = "Reverted" } } catch { $log += [PSCustomObject]@{ Time = (Get-Date).ToString("s"); From = $from; To = $to; Status = "Error: $($_.Exception.Message)" } }
    }
    $undoLogFile = Join-Path $folder ("undo-log_" + (Get-Date).ToString("yyyyMMdd-HHmmss") + ".csv")
    $log | Export-Csv -Path $undoLogFile -NoTypeInformation -Encoding UTF8
    [System.Windows.MessageBox]::Show("Undo complete. Log: $undoLogFile","Undo",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Information) | Out-Null
    $BtnUndo.IsEnabled = $false
    Set-Status "Undo complete. Log: $undoLogFile"
})

# -----------------------
# Show window
# -----------------------
[void]$window.ShowDialog()