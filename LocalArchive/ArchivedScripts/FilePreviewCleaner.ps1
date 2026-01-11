# RecentCleaner.ps1
# GUI to remove shortcuts from Windows Recent for selected directories.
# Features: include subdirectories, weekday checkboxes, monthly day numbers, default time 00:00, dry-run, CSV export, undo, config, logging, responsive layout.

# --- Ensure STA for WPF ---
if ([System.Threading.Thread]::CurrentThread.ApartmentState -ne 'STA') {
    $pwsh = (Get-Command pwsh -ErrorAction SilentlyContinue).Source
    if (-not $pwsh) { $pwsh = (Get-Command powershell -ErrorAction SilentlyContinue).Source }
    if (-not $pwsh) { Write-Error "Cannot find pwsh or powershell to restart in STA."; return }
    $args = '-NoProfile -ExecutionPolicy Bypass -STA -File "' + $MyInvocation.MyCommand.Path + '"'
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $pwsh; $psi.Arguments = $args; $psi.UseShellExecute = $true
    [System.Diagnostics.Process]::Start($psi) | Out-Null
    return
}

Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
Add-Type -AssemblyName System.Windows.Forms

# --- Utility helpers ---
function Ensure-Directory($path) {
    if (-not $path) { return }
    if (-not (Test-Path $path)) {
        try { New-Item -Path $path -ItemType Directory -Force | Out-Null } catch {}
    }
}

function To-BoolSafe($value, $default=$true) {
    if ($null -eq $value) { return [bool]$default }
    if ($value -is [bool]) { return [bool]$value }
    $s = [string]$value
    if ([string]::IsNullOrWhiteSpace($s)) { return [bool]$default }
    $sl = $s.ToLower().Trim()
    if ($sl -eq 'true' -or $sl -eq '1' -or $sl -eq 'yes' -or $sl -eq 'y') { return $true }
    if ($sl -eq 'false' -or $sl -eq '0' -or $sl -eq 'no' -or $sl -eq 'n') { return $false }
    return [bool]$default
}

# --- Responsive XAML UI ---
[xml]$xaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Recent Cleaner" Height="720" Width="1000" MinWidth="800" MinHeight="520" WindowStartupLocation="CenterScreen">
  <Window.Resources>
    <Style TargetType="Button">
      <Setter Property="Background" Value="#FF2B579A"/>
      <Setter Property="Foreground" Value="White"/>
      <Setter Property="Padding" Value="6,4"/>
      <Setter Property="Margin" Value="4"/>
      <Setter Property="BorderBrush" Value="#FF1F446E"/>
      <Setter Property="FontWeight" Value="SemiBold"/>
    </Style>
    <Style TargetType="TextBlock"><Setter Property="Foreground" Value="#222"/></Style>
    <LinearGradientBrush x:Key="HeaderBrush" StartPoint="0,0" EndPoint="1,0">
      <GradientStop Color="#FF4A90E2" Offset="0"/>
      <GradientStop Color="#FF50C878" Offset="1"/>
    </LinearGradientBrush>
  </Window.Resources>

  <DockPanel LastChildFill="True">
    <Border DockPanel.Dock="Top" Background="{StaticResource HeaderBrush}" Padding="12" CornerRadius="6" Margin="8">
      <TextBlock Text="Recent Cleaner — Remove recent shortcuts for chosen folders" FontSize="16" FontWeight="Bold" Foreground="White"/>
    </Border>

    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" HorizontalAlignment="Right" Margin="8">
      <Button Name="ShowPreview" Content="Show Preview" Width="120"/>
      <Button Name="ExportCSV" Content="Export Preview" Width="130"/>
      <Button Name="UndoLast" Content="Undo Last" Width="110"/>
      <Button Name="RunNow" Content="Run Now" Width="110"/>
      <Button Name="SaveTask" Content="Schedule Task" Width="140"/>
      <Button Name="ExitApp" Content="Exit" Width="90"/>
    </StackPanel>

    <Grid Margin="8">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="360"/>
        <ColumnDefinition Width="8"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>

      <ScrollViewer Grid.Column="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="4" MinWidth="300">
          <TextBlock Text="Directories to Exclude" FontWeight="Bold" Margin="0,0,0,6"/>
          <ListBox Name="DirList" Height="160" Margin="0,0,0,6" HorizontalContentAlignment="Stretch"/>
          <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
            <Button Name="AddDir" Content="Add Directory" Width="140"/>
            <Button Name="RemoveDir" Content="Remove Selected" Width="150"/>
          </StackPanel>

          <StackPanel Orientation="Horizontal" Margin="0,0,0,8" VerticalAlignment="Center">
            <CheckBox Name="IncludeSubdirs" Content="Include Subdirectories" Margin="0,0,8,0" VerticalAlignment="Center"/>
            <CheckBox Name="DryRunToggle" Content="Dry Run (Preview Only)" VerticalAlignment="Center"/>
          </StackPanel>

          <TextBlock Text="Output Folder" FontWeight="Bold" Margin="0,6,0,4"/>
          <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
            <TextBox Name="OutputPathBox" Width="240" HorizontalAlignment="Stretch"/>
            <Button Name="BrowseOutput" Content="Browse..." Width="90" Margin="6,0,0,0"/>
          </StackPanel>

          <GroupBox Header="Scheduled Task" Margin="0,8,0,0">
            <Grid Margin="6">
              <Grid.ColumnDefinitions>
                <ColumnDefinition Width="110"/>
                <ColumnDefinition Width="*"/>
              </Grid.ColumnDefinitions>
              <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
              </Grid.RowDefinitions>

              <TextBlock Grid.Row="0" Grid.Column="0" Text="Frequency" VerticalAlignment="Center" Margin="0,4,6,4"/>
              <ComboBox Name="FrequencyBox" Grid.Row="0" Grid.Column="1" MinWidth="140" Margin="0,4,0,4">
                <ComboBoxItem>Daily</ComboBoxItem>
                <ComboBoxItem>Weekly</ComboBoxItem>
                <ComboBoxItem>Monthly</ComboBoxItem>
              </ComboBox>

              <TextBlock Grid.Row="1" Grid.Column="0" Text="Time (HH:mm)" VerticalAlignment="Center" Margin="0,4,6,4"/>
              <TextBox Name="TimeBox" Grid.Row="1" Grid.Column="1" MinWidth="120" Margin="0,4,0,4"/>

              <TextBlock Grid.Row="2" Grid.Column="0" Text="Days" VerticalAlignment="Top" Margin="0,4,6,4"/>
              <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Vertical" Margin="0,4,0,4">
                <TextBox Name="DaysBox" Width="180" Margin="0,0,0,6" Visibility="Collapsed" ToolTip="For Monthly: enter day numbers like 1,15"/>
                <WrapPanel Name="WeekdaysPanel" Orientation="Horizontal">
                  <CheckBox Name="chkSunday" Content="Sun" Margin="2"/>
                  <CheckBox Name="chkMonday" Content="Mon" Margin="2"/>
                  <CheckBox Name="chkTuesday" Content="Tue" Margin="2"/>
                  <CheckBox Name="chkWednesday" Content="Wed" Margin="2"/>
                  <CheckBox Name="chkThursday" Content="Thu" Margin="2"/>
                  <CheckBox Name="chkFriday" Content="Fri" Margin="2"/>
                  <CheckBox Name="chkSaturday" Content="Sat" Margin="2"/>
                </WrapPanel>
              </StackPanel>

              <TextBlock Grid.Row="3" Grid.Column="0" Text="Notes" VerticalAlignment="Top" Margin="0,6,6,0"/>
              <TextBlock Grid.Row="3" Grid.Column="1" Text="Weekly: select weekdays. Monthly: use Days box (e.g. 1,15)." TextWrapping="Wrap" Foreground="Gray" FontSize="11" Margin="0,6,0,0"/>
            </Grid>
          </GroupBox>

          <TextBlock Text="Tip: Resize window — preview will expand to the right" FontStyle="Italic" Foreground="Gray" Margin="0,8,0,0"/>
        </StackPanel>
      </ScrollViewer>

      <GridSplitter Grid.Column="1" Width="6" HorizontalAlignment="Stretch" VerticalAlignment="Stretch" ShowsPreview="True" Background="#EEE"/>

      <Grid Grid.Column="2">
        <DataGrid Name="PreviewGrid" AutoGenerateColumns="True" IsReadOnly="True" Margin="0" RowBackground="#F9FBFF" AlternatingRowBackground="#FFFFFF"/>
      </Grid>
    </Grid>
  </DockPanel>
</Window>
"@

# --- Load window and validate controls ---
try {
    $reader = New-Object System.Xml.XmlNodeReader $xaml
    $window = [Windows.Markup.XamlReader]::Load($reader)
} catch {
    Write-Error "Failed to load XAML: $_"
    return
}

$required = @(
  "DirList","AddDir","RemoveDir","IncludeSubdirs","DryRunToggle","OutputPathBox","BrowseOutput",
  "FrequencyBox","TimeBox","DaysBox","WeekdaysPanel","chkSunday","chkMonday","chkTuesday","chkWednesday","chkThursday","chkFriday","chkSaturday",
  "PreviewGrid","ShowPreview","ExportCSV","UndoLast","RunNow","SaveTask","ExitApp"
)
$controls = @{}
foreach ($name in $required) {
    $ctrl = $window.FindName($name)
    if (-not $ctrl) { [System.Windows.MessageBox]::Show("Missing control in XAML: $name"); return }
    $controls[$name] = $ctrl
}

# --- Aliases ---
$dirList    = $controls["DirList"]
$addDir     = $controls["AddDir"]
$removeDir  = $controls["RemoveDir"]
$includeSub = $controls["IncludeSubdirs"]
$dryRunBox  = $controls["DryRunToggle"]
$outputBox  = $controls["OutputPathBox"]
$browseOut  = $controls["BrowseOutput"]
$frequency  = $controls["FrequencyBox"]
$timeBox    = $controls["TimeBox"]
$daysBox    = $controls["DaysBox"]
$chkSun     = $controls["chkSunday"]; $chkMon = $controls["chkMonday"]; $chkTue = $controls["chkTuesday"]
$chkWed     = $controls["chkWednesday"]; $chkThu = $controls["chkThursday"]; $chkFri = $controls["chkFriday"]; $chkSat = $controls["chkSaturday"]
$previewGrid= $controls["PreviewGrid"]
$showPreview= $controls["ShowPreview"]
$exportCSV  = $controls["ExportCSV"]
$undoLast   = $controls["UndoLast"]
$runNow     = $controls["RunNow"]
$saveTask   = $controls["SaveTask"]
$exitApp    = $controls["ExitApp"]

# --- Config init (safe) ---
$configPath = Join-Path $env:APPDATA "RecentCleanerConfig.json"
$defaultOut = Join-Path $env:LOCALAPPDATA "RecentCleaner"
Ensure-Directory $defaultOut

if (Test-Path $configPath) {
    try { $raw = Get-Content $configPath -Raw; $parsed = if ($raw) { $raw | ConvertFrom-Json } else { $null } } catch { $parsed = $null }
} else { $parsed = $null }

if (-not ($parsed -and $parsed -is [System.Management.Automation.PSCustomObject])) {
    $config = [pscustomobject]@{ ExcludedDirs = @(); OutputPath = $defaultOut; Weekdays = @(); Time = "00:00"; IncludeSubdirs = $true }
} else {
    $includeParsed = if ($parsed.PSObject.Properties.Match('IncludeSubdirs').Count -gt 0) { To-BoolSafe $parsed.IncludeSubdirs $true } else { $true }
    $config = [pscustomobject]@{
        ExcludedDirs   = if ($parsed.ExcludedDirs) { @($parsed.ExcludedDirs) } else { @() }
        OutputPath     = if ($parsed.OutputPath) { $parsed.OutputPath } else { $defaultOut }
        Weekdays       = if ($parsed.Weekdays) { @($parsed.Weekdays) } else { @() }
        Time           = if ($parsed.Time) { $parsed.Time } else { "00:00" }
        IncludeSubdirs = $includeParsed
    }
}

# --- Populate UI ---
foreach ($d in $config.ExcludedDirs) { if ($d) { $dirList.Items.Add($d) } }
$outputBox.Text = if ($config.OutputPath) { $config.OutputPath } else { $defaultOut }
$timeBox.Text = if ($config.Time) { $config.Time } else { "00:00" }
$includeSub.IsChecked = [bool]$config.IncludeSubdirs

foreach ($wd in $config.Weekdays) {
    switch ($wd.ToLower()) {
        "sunday"   { $chkSun.IsChecked = $true }
        "monday"   { $chkMon.IsChecked = $true }
        "tuesday"  { $chkTue.IsChecked = $true }
        "wednesday"{ $chkWed.IsChecked = $true }
        "thursday" { $chkThu.IsChecked = $true }
        "friday"   { $chkFri.IsChecked = $true }
        "saturday" { $chkSat.IsChecked = $true }
    }
}

# --- Logging helper ---
function Log-Error($message, $outputPath) {
    $pathToUse = if ($outputPath) { $outputPath } elseif ($config.OutputPath) { $config.OutputPath } else { $defaultOut }
    Ensure-Directory $pathToUse
    $logPath = Join-Path $pathToUse "RecentCleaner_ErrorLog_$(Get-Date -Format 'yyyyMMdd').log"
    try { Add-Content -Path $logPath -Value "$(Get-Date -Format 'u') ERROR: $message" } catch {}
}

# --- Save config safely ---
function Save-Config {
    if (-not ($config -is [System.Management.Automation.PSCustomObject])) {
        $config = [pscustomobject]@{ ExcludedDirs = @(); OutputPath = $defaultOut; Weekdays = @(); Time = "00:00"; IncludeSubdirs = $true }
    }
    $config.ExcludedDirs = @($dirList.Items | ForEach-Object { $_.ToString() })
    if ($config.PSObject.Properties.Match('OutputPath').Count -eq 0) { $config | Add-Member -NotePropertyName OutputPath -NotePropertyValue $outputBox.Text -Force } else { $config.OutputPath = $outputBox.Text }

    $selected = @()
    if ($chkSun.IsChecked) { $selected += "Sunday" }; if ($chkMon.IsChecked) { $selected += "Monday" }
    if ($chkTue.IsChecked) { $selected += "Tuesday" }; if ($chkWed.IsChecked) { $selected += "Wednesday" }
    if ($chkThu.IsChecked) { $selected += "Thursday" }; if ($chkFri.IsChecked) { $selected += "Friday" }
    if ($chkSat.IsChecked) { $selected += "Saturday" }
    if ($config.PSObject.Properties.Match('Weekdays').Count -eq 0) { $config | Add-Member -NotePropertyName Weekdays -NotePropertyValue $selected -Force } else { $config.Weekdays = $selected }

    if ($config.PSObject.Properties.Match('Time').Count -eq 0) { $config | Add-Member -NotePropertyName Time -NotePropertyValue $timeBox.Text -Force } else { $config.Time = $timeBox.Text }

    $incBool = To-BoolSafe $includeSub.IsChecked $true
    if ($config.PSObject.Properties.Match('IncludeSubdirs').Count -eq 0) { $config | Add-Member -NotePropertyName IncludeSubdirs -NotePropertyValue $incBool -Force } else { $config.IncludeSubdirs = $incBool }

    try { $config | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Force } catch { Log-Error "Failed saving config: $_" $config.OutputPath }
}

# --- Efficient .lnk resolution and removal ---
function Resolve-LnkTarget($lnkPath, [ref]$shellObj) {
    try { if (-not $shellObj.Value) { $shellObj.Value = New-Object -ComObject WScript.Shell }; return $shellObj.Value.CreateShortcut($lnkPath).TargetPath } catch { return $null }
}

function Remove-RecentShortcuts {
    param (
        [string[]]$TargetDirs,
        [switch]$DryRun,
        [string]$BackupPath,
        [bool]$IncludeSubDirs = $true
    )

    $recent = [Environment]::GetFolderPath("Recent")
    try { $shortcuts = Get-ChildItem -Path $recent -Filter *.lnk -Force -ErrorAction SilentlyContinue } catch { $shortcuts = @() }
    $matched = New-Object System.Collections.Generic.List[object]
    $shellRef = [ref] $null

    foreach ($lnk in $shortcuts) {
        try {
            $target = Resolve-LnkTarget $lnk.FullName $shellRef
            if (-not $target) { continue }
            foreach ($dir in $TargetDirs) {
                if (-not $dir) { continue }
                if ($IncludeSubDirs) {
                    $isMatch = $target.StartsWith($dir, [System.StringComparison]::OrdinalIgnoreCase)
                } else {
                    $parent = Split-Path -Path $target -Parent
                    $isMatch = ($parent -and ([string]::Equals($parent, $dir, [System.StringComparison]::OrdinalIgnoreCase)))
                }
                if ($isMatch) {
                    $matched.Add([PSCustomObject]@{ Shortcut = $lnk.FullName; Target = $target })
                    if (-not $DryRun) {
                        if ($BackupPath) { Ensure-Directory $BackupPath; Copy-Item -Path $lnk.FullName -Destination $BackupPath -Force -ErrorAction SilentlyContinue }
                        Remove-Item -Path $lnk.FullName -Force -ErrorAction SilentlyContinue
                    }
                    break
                }
            }
        } catch { Log-Error "Failed processing $($lnk.FullName): $_" $config.OutputPath }
    }

    return $matched
}

# --- Safe frequency selection handler (set default and toggle DaysBox) ---
if ($null -eq $frequency) {
    Write-Warning "Frequency control not found; cannot wire selection handler."
} else {
    try {
        if (-not $frequency.SelectedItem) {
            foreach ($item in $frequency.Items) { if ($item -and $item.Content -and $item.Content -eq 'Daily') { $frequency.SelectedItem = $item; break } }
        }
    } catch {}
    try {
        $frequency.SelectionChanged.Add({
            try {
                $sel = $frequency.SelectedItem
                if ($sel -and $sel.Content -eq 'Monthly') { $daysBox.Visibility = 'Visible' } else { $daysBox.Visibility = 'Collapsed' }
            } catch { Log-Error "Frequency handler error: $_" $outputBox.Text }
        })
    } catch {
        try {
            $frequency.add_SelectionChanged({
                try {
                    $sel = $frequency.SelectedItem
                    if ($sel -and $sel.Content -eq 'Monthly') { $daysBox.Visibility = 'Visible' } else { $daysBox.Visibility = 'Collapsed' }
                } catch { Log-Error "Frequency fallback handler error: $_" $outputBox.Text }
            })
        } catch { Write-Warning "Failed to attach Frequency selection handler: $_" }
    }
}

# --- UI event handlers ---
$addDir.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($dlg.ShowDialog() -eq 'OK') { if (-not $dirList.Items.Contains($dlg.SelectedPath)) { $dirList.Items.Add($dlg.SelectedPath) } }
})

$removeDir.Add_Click({ if ($dirList.SelectedItem) { $dirList.Items.Remove($dirList.SelectedItem) } })

$browseOut.Add_Click({
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    if ($dlg.ShowDialog() -eq 'OK') { $outputBox.Text = $dlg.SelectedPath }
})

# Default time if empty
if (-not $timeBox.Text) { $timeBox.Text = "00:00" }

$showPreview.Add_Click({
    try {
        $dirs = @($dirList.Items | ForEach-Object { $_.ToString() })
        $includeBool = To-BoolSafe $includeSub.IsChecked $true
        $results = Remove-RecentShortcuts -TargetDirs $dirs -DryRun -IncludeSubDirs:$includeBool
        $previewGrid.ItemsSource = $results
        [System.Windows.MessageBox]::Show("Preview complete. Matched: $($results.Count)")
    } catch { Log-Error "Preview failed: $_" $outputBox.Text; [System.Windows.MessageBox]::Show("Preview failed. See error log in output folder.") }
})

$runNow.Add_Click({
    try {
        $dirs = @($dirList.Items | ForEach-Object { $_.ToString() })
        $dryRun = $dryRunBox.IsChecked
        $outPath = if ($outputBox.Text) { $outputBox.Text } else { $defaultOut }
        Ensure-Directory $outPath
        $backupPath = if (-not $dryRun) { Join-Path $outPath "Backup_$(Get-Date -Format 'yyyyMMdd_HHmmss')" } else { $null }
        if ($backupPath) { Ensure-Directory $backupPath }
        $includeBool = To-BoolSafe $includeSub.IsChecked $true
        $results = Remove-RecentShortcuts -TargetDirs $dirs -DryRun:$dryRun -BackupPath $backupPath -IncludeSubDirs:$includeBool
        $previewGrid.ItemsSource = $results
        Save-Config
        [System.Windows.MessageBox]::Show("Run complete. Matched: $($results.Count)")
    } catch { Log-Error "Run failed: $_" $outputBox.Text; [System.Windows.MessageBox]::Show("Run failed. See error log in output folder.") }
})

$exportCSV.Add_Click({
    try {
        $outPath = if ($outputBox.Text) { $outputBox.Text } else { $defaultOut }
        Ensure-Directory $outPath
        $items = $previewGrid.ItemsSource
        if (-not $items -or $items.Count -eq 0) { [System.Windows.MessageBox]::Show("No preview data to export. Run Preview or Run Now first."); return }
        $csvPath = Join-Path $outPath "RecentCleanerPreview_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"
        $items | Export-Csv -Path $csvPath -NoTypeInformation -Force
        [System.Windows.MessageBox]::Show("Exported to: $csvPath")
    } catch { Log-Error "Export failed: $_" $outputBox.Text; [System.Windows.MessageBox]::Show("Export failed. See error log in output folder.") }
})

$undoLast.Add_Click({
    try {
        $outPath = if ($outputBox.Text) { $outputBox.Text } else { $defaultOut }
        if (-not (Test-Path $outPath)) { [System.Windows.MessageBox]::Show("Output folder does not exist. Cannot find backups."); return }
        $latest = Get-ChildItem -Path $outPath -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "Backup_*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latest) {
            Copy-Item -Path (Join-Path $latest.FullName "*") -Destination ([Environment]::GetFolderPath("Recent")) -Force -ErrorAction SilentlyContinue
            [System.Windows.MessageBox]::Show("Undo complete from: $($latest.Name)")
        } else {
            [System.Windows.MessageBox]::Show("No backup found in output folder.")
        }
    } catch { Log-Error "Undo failed: $_" $outputBox.Text; [System.Windows.MessageBox]::Show("Undo failed. See error log in output folder.") }
})

$saveTask.Add_Click({
    $taskName = "RecentCleanerTask"
    $scriptPath = $MyInvocation.MyCommand.Path
    $action = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-WindowStyle Hidden -File `"$scriptPath`""

    try {
        $time = [datetime]::ParseExact($timeBox.Text.Trim(), "HH:mm", $null)
    } catch {
        [System.Windows.MessageBox]::Show("Invalid time format. Use HH:mm (24hr).")
        return
    }

    $freqItem = $frequency.SelectedItem
    if (-not $freqItem) { [System.Windows.MessageBox]::Show("Select a frequency."); return }
    $freq = $freqItem.Content

    $selectedWeekdays = @()
    if ($chkSun.IsChecked) { $selectedWeekdays += "Sunday" }
    if ($chkMon.IsChecked) { $selectedWeekdays += "Monday" }
    if ($chkTue.IsChecked) { $selectedWeekdays += "Tuesday" }
    if ($chkWed.IsChecked) { $selectedWeekdays += "Wednesday" }
    if ($chkThu.IsChecked) { $selectedWeekdays += "Thursday" }
    if ($chkFri.IsChecked) { $selectedWeekdays += "Friday" }
    if ($chkSat.IsChecked) { $selectedWeekdays += "Saturday" }

    $daysText = $daysBox.Text.Trim()
    $trigger = $null

    try {
        switch ($freq) {
            "Daily" { $trigger = New-ScheduledTaskTrigger -Daily -At $time }
            "Weekly" {
                if (-not $selectedWeekdays -or $selectedWeekdays.Count -eq 0) { [System.Windows.MessageBox]::Show("Select at least one weekday for Weekly schedule."); return }
                $trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek $selectedWeekdays -At $time
            }
            "Monthly" {
                if ([string]::IsNullOrWhiteSpace($daysText)) { [System.Windows.MessageBox]::Show("Enter day numbers for Monthly schedule, e.g. 1 or 1,15"); return }
                $daysNum = $daysText -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ } | ForEach-Object { if ($_ -match '^\d+$') { [int]$_ } else { -1 } }
                if ($daysNum -contains -1) { [System.Windows.MessageBox]::Show("Invalid monthly day value. Use integers like 1,15."); return }
                $trigger = New-ScheduledTaskTrigger -Monthly -DaysOfMonth $daysNum -At $time
            }
            default { [System.Windows.MessageBox]::Show("Select a valid frequency."); return }
        }

        Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Force
        Save-Config
        [System.Windows.MessageBox]::Show("Scheduled task '$taskName' created/updated.")
    } catch {
        Log-Error "Schedule creation failed: $_" $outputBox.Text
        [System.Windows.MessageBox]::Show("Failed to create scheduled task. See error log in output folder.")
    }
})

$exitApp.Add_Click({ try { $window.Close() } catch {} })

# --- Show UI ---
try {
    $window.ShowDialog() | Out-Null
} catch {
    Log-Error "UI failed to show: $_" $outputBox.Text
    [System.Windows.MessageBox]::Show("UI failed to open. See error log in output folder.")
}