# MetadataEditorGUI-Async.ps1
# Responsive metadata browser/writer with background metadata loading to avoid UI freezes.

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Ensure STA for WPF
if ([System.Threading.Thread]::CurrentThread.GetApartmentState() -ne [System.Threading.ApartmentState]::STA) {
	$target = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Definition }
	$pwsh = "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe"
	if (Test-Path $pwsh -PathType Leaf -and $target) {
		Start-Process -FilePath $pwsh -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-STA','-File',"`"$target`"") -WorkingDirectory (Get-Location)
		exit
	}
}

# ---------- Globals ----------
$script:Items = [System.Collections.ObjectModel.ObservableCollection[object]]::new()
$script:CurrentMeta = $null
$script:LogBuffer = New-Object System.Text.StringBuilder
$script:ExifToolPath = $null
$script:FfprobePath = $null
$script:ShellLabelCache = @{}
$script:MetadataToken = 0
$script:UseExifPreview = $false
$script:UseFfprobePreview = $false
$script:FileListToken = 0
$script:LogFilePath = Join-Path 'C:\Path\logs' ('metadata_{0:yyyyMMdd_HHmmss}.log' -f (Get-Date))

# ---------- Utility helpers ----------
function Add-Log {
	param([string]$Message)
	$timestamp = (Get-Date).ToString('s')
	[void]$script:LogBuffer.AppendLine("$timestamp`t$Message")
	if ($script:TxtLog) { $script:TxtLog.Text = $script:LogBuffer.ToString() }
	try {
		Ensure-LogFile
		[System.IO.File]::AppendAllText($script:LogFilePath, "$timestamp`t$Message`r`n", [System.Text.Encoding]::UTF8)
	} catch {
		# swallow file log errors to avoid UI impact
	}
}

function Ensure-LogFile {
	try {
		$dir = Split-Path -Path $script:LogFilePath -Parent
		if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
		if (-not (Test-Path $script:LogFilePath)) { New-Item -ItemType File -Path $script:LogFilePath -Force | Out-Null }
	} catch {
		# ignore failures silently; UI log still works
	}
}

function Measure-Action {
	param(
		[string]$Label,
		[scriptblock]$Action
	)
	$sw = [System.Diagnostics.Stopwatch]::StartNew()
	try {
		$result = & $Action
		return @{ Value = $result; Ms = $sw.ElapsedMilliseconds }
	} catch {
		Add-Log "$Label failed after $($sw.ElapsedMilliseconds) ms : $($_.Exception.Message)"
		throw
	} finally {
		$sw.Stop()
		Add-Log "$Label took $($sw.ElapsedMilliseconds) ms"
	}
}

function Set-Status {
	param([string]$Message)
	if ($script:TxtStatus) { $script:TxtStatus.Text = $Message }
}

function Invoke-UI {
	param([scriptblock]$Action)
	if ($script:window) {
		$script:window.Dispatcher.Invoke([action]{ & $Action }, [System.Windows.Threading.DispatcherPriority]::Background)
	} else {
		& $Action
	}
}

function Start-Background {
	param(
		[scriptblock]$Work,
		[scriptblock]$OnSuccess,
		[scriptblock]$OnError
	)

	$task = [System.Threading.Tasks.Task]::Run([Func[object]]{
		try {
			return & $Work
		} catch {
			throw $_
		}
	})

	$continuation = [System.Action[System.Threading.Tasks.Task]]{
		param($t)
		Invoke-UI {
			if ($t.IsFaulted) {
				if ($OnError) { & $OnError.Invoke($t.Exception.InnerException) }
			} else {
				if ($OnSuccess) { & $OnSuccess.Invoke($t.Result) }
			}
		}
	}

	$task.ContinueWith($continuation) | Out-Null
}

# ---------- ExifTool / ffprobe management ----------
function Find-ExifToolLocal {
	if ($script:ExifToolPath -and (Test-Path $script:ExifToolPath)) { return $script:ExifToolPath }
	if ($script:TxtExifPath -and $script:TxtExifPath.Text -and (Test-Path $script:TxtExifPath.Text)) { return $script:TxtExifPath.Text }

	$cmd = Get-Command exiftool.exe -ErrorAction SilentlyContinue
	if ($cmd -and $cmd.Source -and (Test-Path $cmd.Source)) { return $cmd.Source }

	$cmdAlt = Get-Command "exiftool(-k).exe" -ErrorAction SilentlyContinue
	if ($cmdAlt -and $cmdAlt.Source -and (Test-Path $cmdAlt.Source)) { return $cmdAlt.Source }

	$scriptRoot = if ($PSCommandPath) { Split-Path $PSCommandPath } elseif ($PSScriptRoot) { $PSScriptRoot } else { Get-Location }
	foreach ($name in @("exiftool.exe","exiftool(-k).exe")) {
		$candidate = Join-Path $scriptRoot $name
		if (Test-Path $candidate) { return $candidate }
	}

	foreach ($name in @("exiftool.exe","exiftool(-k).exe")) {
		$workspaceCandidate = "C:\Path\$name"
		if (Test-Path $workspaceCandidate) { return $workspaceCandidate }
	}

	return $null
}

function Ensure-ExifTool {
	param([switch]$Force)
	if ($script:ExifToolPath -and -not $Force) { return $script:ExifToolPath }

	$exeName = "exiftool.exe"
	$localPath = Join-Path -Path $env:TEMP -ChildPath "exiftool"
	$destExe = Join-Path $localPath $exeName
	if ((Test-Path $destExe) -and -not $Force) {
		$script:ExifToolPath = $destExe
		return $destExe
	}

	New-Item -Path $localPath -ItemType Directory -Force | Out-Null
	$url = "https://sourceforge.net/projects/exiftool/files/exiftool-13.43_64.zip/download"
	try {
		$zip = Join-Path $localPath "exiftool.zip"
		Write-Host "Downloading exiftool (64-bit) ..." -ForegroundColor Yellow
		Invoke-WebRequest -Uri $url -OutFile $zip -UseBasicParsing -ErrorAction Stop
		Add-Type -AssemblyName System.IO.Compression.FileSystem
		[System.IO.Compression.ZipFile]::ExtractToDirectory($zip, $localPath)
		$found = Get-ChildItem -Path $localPath -Filter "exiftool*.exe" -Recurse | Select-Object -First 1
		if ($found) {
			Copy-Item -Path $found.FullName -Destination $destExe -Force
			Remove-Item -Path $zip -Force -ErrorAction SilentlyContinue
			$script:ExifToolPath = $destExe
			Add-Log "ExifTool installed to $destExe"
			return $destExe
		}
		throw "ExifTool extracted but executable not found"
	} catch {
		[System.Windows.MessageBox]::Show("Failed to download ExifTool: $($_.Exception.Message)`nInstall manually and set the path.","Download error",[System.Windows.MessageBoxButton]::OK,[System.Windows.MessageBoxImage]::Error) | Out-Null
		Add-Log "ExifTool install failed: $($_.Exception.Message)"
		return $null
	}
}

function Ensure-ExifToolInteractive {
	$found = Find-ExifToolLocal
	if ($found) { $script:ExifToolPath = $found; return $true }

	$msg = "ExifTool not found. Yes = browse, No = download automatically, Cancel = skip."
	$choice = [System.Windows.MessageBox]::Show($msg, "ExifTool required", [System.Windows.MessageBoxButton]::YesNoCancel, [System.Windows.MessageBoxImage]::Information)
	if ($choice -eq [System.Windows.MessageBoxResult]::Cancel) { return $false }

	if ($choice -eq [System.Windows.MessageBoxResult]::Yes) {
		$ofd = New-Object Microsoft.Win32.OpenFileDialog
		$ofd.Filter = "ExifTool (exiftool.exe)|exiftool.exe|All files (*.*)|*.*"
		$ofd.Title = "Locate exiftool.exe"
		if ($ofd.ShowDialog()) {
			if (Test-Path $ofd.FileName) {
				$script:ExifToolPath = $ofd.FileName
				if ($script:TxtExifPath) { $script:TxtExifPath.Text = $ofd.FileName }
				return $true
			}
		}
		return $false
	}

	$res = Ensure-ExifTool -Force
	if ($res -and $script:TxtExifPath) { $script:TxtExifPath.Text = $res }
	return [bool]$res
}

function Detect-ffprobe {
	$ff = Get-Command ffprobe -ErrorAction SilentlyContinue
	if ($ff -and $ff.Source -and (Test-Path $ff.Source)) {
		$script:FfprobePath = $ff.Source
		Add-Log "ffprobe detected at $($ff.Source)"
	}
}

# ---------- Metadata readers ----------
function Get-ShellDetailMap {
	param([string]$FolderPath)
	if ($script:ShellLabelCache.ContainsKey($FolderPath)) { return $script:ShellLabelCache[$FolderPath] }
	$map = @{}
	try {
		$shell = New-Object -ComObject Shell.Application
		$folder = $shell.Namespace($FolderPath)
		if ($folder) {
			for ($i = 0; $i -le 180; $i++) {
				$label = $folder.GetDetailsOf($folder.Items(), $i)
				if (-not [string]::IsNullOrWhiteSpace($label)) { $map[$i] = $label }
			}
		}
	} catch {
		Add-Log "Shell map failed for $FolderPath : $($_.Exception.Message)"
	}
	$script:ShellLabelCache[$FolderPath] = $map
	return $map
}

function Get-WindowsShellMetadata {
	param([string]$Path)
	$meta = [ordered]@{ FullName = $Path; Name = (Split-Path -Path $Path -Leaf) }
	try {
		$folderPath = Split-Path -Path $Path -Parent
		if (-not $folderPath) { return $meta }
		$shell = New-Object -ComObject Shell.Application
		$folder = $shell.Namespace($folderPath)
		if (-not $folder) { return $meta }
		$item = $folder.ParseName((Split-Path -Path $Path -Leaf))
		if (-not $item) { return $meta }
		$map = Get-ShellDetailMap -FolderPath $folderPath
		foreach ($index in $map.Keys) {
			$val = $folder.GetDetailsOf($item, [int]$index)
			if (-not [string]::IsNullOrWhiteSpace($val)) {
				$meta[$map[$index]] = $val
			}
		}
	} catch {
		Add-Log "Shell metadata failed for $Path : $($_.Exception.Message)"
	}
	return $meta
}

function Get-ExifToolMetadata {
	param([string]$Path)
	if (-not $script:UseExifPreview) { return $null }
	if (-not $script:ExifToolPath -or -not (Test-Path $script:ExifToolPath)) { return $null }
	$args = @(
		'-j','-g1','-n','-fast2','-api','RequestAll=0',
		'--charset','UTF8','--',$Path
	)
	try {
		$out = & $script:ExifToolPath @args 2>$null
		if (-not $out) { return $null }
		$json = $out | Out-String
		$parsed = $json | ConvertFrom-Json -ErrorAction Stop
		if ($parsed -is [System.Array]) { return $parsed[0] }
		return $parsed
	} catch {
		Add-Log "ExifTool parse failed for $Path : $($_.Exception.Message)"
		return $null
	}
}

function Get-FfprobeMetadata {
	param([string]$Path)
	if (-not $script:UseFfprobePreview) { return $null }
	if (-not $script:FfprobePath -or -not (Test-Path $script:FfprobePath)) { return $null }
	$args = @('-v','quiet','-print_format','json','-show_format','-show_streams','--',$Path)
	try {
		$out = & $script:FfprobePath @args 2>$null
		if (-not $out) { return $null }
		return ($out | Out-String) | ConvertFrom-Json -ErrorAction Stop
	} catch {
		Add-Log "ffprobe parse failed for $Path : $($_.Exception.Message)"
		return $null
	}
}

function Read-MetadataForFile {
	param([string]$Path)
	$overall = [System.Diagnostics.Stopwatch]::StartNew()
	$metaShell = Measure-Action "Shell metadata for $Path" { Get-WindowsShellMetadata -Path $Path }
	$metaExif = Measure-Action "ExifTool metadata for $Path" { Get-ExifToolMetadata -Path $Path }
	$metaFf = Measure-Action "ffprobe metadata for $Path" { Get-FfprobeMetadata -Path $Path }

	$combined = [ordered]@{}
	if ($metaShell.Value) {
		foreach ($k in $metaShell.Value.Keys) { $combined[$k] = $metaShell.Value[$k] }
	}
	if ($metaExif.Value) {
		foreach ($k in $metaExif.Value.PSObject.Properties.Name) { $combined[$k] = $metaExif.Value.$k }
	}
	if ($metaFf.Value) { $combined['ffprobe'] = $metaFf.Value }
	if (-not $combined.ContainsKey('FullName')) { $combined['FullName'] = $Path }
	if (-not $combined.ContainsKey('Name')) { $combined['Name'] = (Split-Path -Path $Path -Leaf) }
	$overall.Stop()
	Add-Log "Metadata aggregation for $Path finished in $($overall.ElapsedMilliseconds) ms"
	return $combined
}

function Write-ExifToolMetadata {
	param(
		[string]$Path,
		[hashtable]$changes,
		[switch]$DryRun
	)
	if (-not $script:ExifToolPath -or -not (Test-Path $script:ExifToolPath)) {
		throw "ExifTool not available"
	}

	$args = @()
	foreach ($k in $changes.Keys) {
		$raw = $changes[$k]
		$v = if ($null -eq $raw) { "" } else { [string]$raw }
		$v = $v -replace '"','\"'
		$args += ("-{0}={1}" -f $k, ('"' + $v + '"'))
	}
	$args += '-overwrite_original'
	$args += '--'
	$args += $Path

	if ($DryRun) {
		return @{ Path = $Path; Args = ($args -join ' ') }
	}

	try {
		$out = & $script:ExifToolPath @args 2>&1
		Add-Log "ExifTool wrote $Path : $($out -join "`n")"
		return $true
	} catch {
		Add-Log "ExifTool write failed: $($_.Exception.Message)"
		throw
	}
}

function Build-EditableRows {
	param([hashtable]$Meta)
	$rows = New-Object System.Collections.Generic.List[object]
	foreach ($k in $Meta.Keys) {
		if ($k -eq 'ffprobe') { continue }
		$rows.Add([pscustomobject]@{ Key = $k; Value = [string]$Meta[$k] })
	}
	return $rows
}

function Compute-Changes {
	param([hashtable]$Current)
	$changes = @{}
	foreach ($r in $Current.List) {
		$key = $r.Key
		$val = $r.Value
		$orig = $Current.Raw[$key]
		if ($null -eq $orig) { $orig = "" }
		if ("$orig" -ne "$val") { $changes[$key] = $val }
	}
	return $changes
}

function Load-PreviewImage {
	param([string]$Path)
	$sw = [System.Diagnostics.Stopwatch]::StartNew()
	try {
		$ext = [io.path]::GetExtension($Path).ToLower()
		if ($ext -notin '.jpg','.jpeg','.png','.bmp','.gif','.tif','.tiff') {
			$script:ImgPreview.Source = $null
			$script:TxtPreviewMeta.Text = "No visual preview"
			return
		}
		$fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
		try {
			$bitmap = New-Object System.Windows.Media.Imaging.BitmapImage
			$bitmap.BeginInit()
			$bitmap.CacheOption = 'OnLoad'
			$bitmap.StreamSource = $fs
			$bitmap.DecodePixelWidth = 900
			$bitmap.EndInit()
			$bitmap.Freeze()
			$script:ImgPreview.Source = $bitmap
			$script:TxtPreviewMeta.Text = "$($bitmap.PixelWidth)x$($bitmap.PixelHeight)  $(Split-Path -Path $Path -Leaf)"
		} finally {
			$fs.Dispose()
		}
	} catch {
		$script:ImgPreview.Source = $null
		$script:TxtPreviewMeta.Text = "Preview error: $($_.Exception.Message)"
		Add-Log "Preview load error: $($_.Exception.Message)"
	} finally {
		$sw.Stop()
		Add-Log "Preview load for $Path took $($sw.ElapsedMilliseconds) ms"
	}
}

# ---------- UI markup ----------
$xaml = @"
	<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
		Title="Metadata Editor" Height="720" Width="1100" MinHeight="620" MinWidth="900">
	<DockPanel LastChildFill="True">
		<StackPanel Orientation="Horizontal" DockPanel.Dock="Top" Margin="6">
			<Button Name="BtnSelect" Width="120" Margin="4">Select Folder</Button>
			<Button Name="BtnPreviewFiles" Width="140" Margin="4">Preview Files</Button>
			<Button Name="BtnScan" Width="100" Margin="4">Scan</Button>
			<Button Name="BtnDownloadExif" Width="160" Margin="4">Install ExifTool</Button>
			<CheckBox Name="ChkRecursive" IsChecked="False" Margin="8,4">Recursive</CheckBox>
			<Button Name="BtnExportJson" Width="120" Margin="4">Export JSON</Button>
			<Button Name="BtnSave" Width="120" Margin="4">Save Changes</Button>
			<Button Name="BtnDryRun" Width="120" Margin="4">Dry Run</Button>
			<Button Name="BtnExit" Width="100" Margin="4">Exit</Button>
			<TextBox Name="TxtFilter" Width="220" Margin="8,4" VerticalAlignment="Center" ToolTip="filter eg: .jpg;.png;.mp4"/>
			<StackPanel Orientation="Horizontal" VerticalAlignment="Center" Margin="4,4">
				<TextBlock Text="Max Files" Margin="0,0,6,0" VerticalAlignment="Center"/>
				<TextBox Name="TxtMaxFiles" Width="80" VerticalAlignment="Center" Text="500" ToolTip="Limit number of files loaded for speed"/>
			</StackPanel>
			<CheckBox Name="ChkUseExifPreview" IsChecked="False" Margin="8,4" ToolTip="Use ExifTool while previewing (slower)">Use ExifTool on preview</CheckBox>
			<CheckBox Name="ChkUseFfprobePreview" IsChecked="False" Margin="8,4" ToolTip="Run ffprobe while previewing videos (slower)">Use ffprobe on preview</CheckBox>
			<TextBox Name="TxtExifPath" Width="240" Margin="8,4" VerticalAlignment="Center" ToolTip="Path to exiftool.exe"/>
			<Button Name="BtnBrowseExif" Width="120" Margin="4">Browse ExifTool</Button>
		</StackPanel>

		<ScrollViewer VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Auto" Margin="0,0,0,6">
			<Grid Margin="6">
				<Grid.ColumnDefinitions>
					<ColumnDefinition Width="3*"/>
					<ColumnDefinition Width="2*"/>
				</Grid.ColumnDefinitions>
				<Grid.RowDefinitions>
					<RowDefinition Height="*"/>
					<RowDefinition Height="140"/>
				</Grid.RowDefinitions>

				<DataGrid Name="DataGridFiles" Grid.Column="0" Grid.Row="0" AutoGenerateColumns="False" CanUserAddRows="False" SelectionMode="Extended" IsReadOnly="False" Margin="4" HorizontalScrollBarVisibility="Auto"
					EnableRowVirtualization="True" EnableColumnVirtualization="True"
					VirtualizingStackPanel.IsVirtualizing="True" VirtualizingStackPanel.VirtualizationMode="Recycling">
					<DataGrid.Columns>
						<DataGridCheckBoxColumn Binding="{Binding Selected, Mode=TwoWay}" Header="Select" Width="SizeToHeader" MinWidth="80"/>
						<DataGridTextColumn Binding="{Binding Name}" Header="Name" IsReadOnly="True" Width="2*" MinWidth="180"/>
						<DataGridTextColumn Binding="{Binding FullName}" Header="FullName" IsReadOnly="True" Width="3*" MinWidth="260"/>
						<DataGridTextColumn Binding="{Binding FileType}" Header="Type" IsReadOnly="True" Width="*" MinWidth="90"/>
					</DataGrid.Columns>
				</DataGrid>

				<StackPanel Grid.Column="1" Grid.Row="0" Margin="4">
					<Border BorderBrush="Gray" BorderThickness="1" Padding="6" Margin="0,0,0,6" Height="320">
						<StackPanel>
							<Image Name="ImgPreview" Width="360" Height="200" Stretch="Uniform"/>
							<TextBlock Name="TxtPreviewMeta" TextWrapping="Wrap" Margin="4,6,4,0"/>
						</StackPanel>
					</Border>
					<TextBlock FontWeight="Bold" Text="Editable Metadata (selected item)"/>
					<DataGrid Name="DataGridMeta" AutoGenerateColumns="False" CanUserAddRows="False" Margin="0,6,0,0" Height="240" HorizontalScrollBarVisibility="Auto">
						<DataGrid.Columns>
							<DataGridTextColumn Binding="{Binding Key}" Header="Field" IsReadOnly="True" Width="2*" MinWidth="200"/>
							<DataGridTextColumn Binding="{Binding Value, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" Header="Value" Width="3*" MinWidth="260"/>
						</DataGrid.Columns>
					</DataGrid>
				</StackPanel>

				<TextBox Grid.Column="0" Grid.Row="1" Name="TxtLog" Margin="4" IsReadOnly="True" VerticalScrollBarVisibility="Auto" TextWrapping="Wrap"/>
				<StackPanel Grid.Column="1" Grid.Row="1" Margin="4">
					<TextBlock Text="Actions and status" FontWeight="Bold"/>
					<TextBlock Name="TxtStatus" Text="Ready" TextWrapping="Wrap" Margin="0,6,0,0"/>
				</StackPanel>
			</Grid>
		</ScrollViewer>
	</DockPanel>
</Window>
"@

# ---------- Build WPF window ----------
$reader = New-Object System.Xml.XmlNodeReader ([xml]$xaml)
$script:window = [Windows.Markup.XamlReader]::Load($reader)

# Controls
$script:BtnSelect = $window.FindName('BtnSelect')
$script:BtnPreviewFiles = $window.FindName('BtnPreviewFiles')
$script:BtnScan = $window.FindName('BtnScan')
$script:BtnDownloadExif = $window.FindName('BtnDownloadExif')
$script:BtnExportJson = $window.FindName('BtnExportJson')
$script:BtnSave = $window.FindName('BtnSave')
$script:BtnDryRun = $window.FindName('BtnDryRun')
$script:BtnExit = $window.FindName('BtnExit')
$script:ChkRecursive = $window.FindName('ChkRecursive')
$script:TxtFilter = $window.FindName('TxtFilter')
$script:TxtMaxFiles = $window.FindName('TxtMaxFiles')
$script:ChkUseExifPreview = $window.FindName('ChkUseExifPreview')
$script:ChkUseFfprobePreview = $window.FindName('ChkUseFfprobePreview')
$script:TxtExifPath = $window.FindName('TxtExifPath')
$script:BtnBrowseExif = $window.FindName('BtnBrowseExif')
$script:DataGridFiles = $window.FindName('DataGridFiles')
$script:DataGridMeta = $window.FindName('DataGridMeta')
$script:ImgPreview = $window.FindName('ImgPreview')
$script:TxtPreviewMeta = $window.FindName('TxtPreviewMeta')
$script:TxtLog = $window.FindName('TxtLog')
$script:TxtStatus = $window.FindName('TxtStatus')

$DataGridFiles.ItemsSource = $script:Items

# ---------- UI Events ----------
$BtnSelect.Add_Click({
	$folder = [System.Windows.Forms.FolderBrowserDialog]::new()
	$folder.Description = "Select folder to scan"
	if ($folder.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
		$window.Tag = $folder.SelectedPath
		Set-Status "Selected: $($folder.SelectedPath)"
		$filter = @()
		if ($TxtFilter.Text) { $filter = $TxtFilter.Text -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
		$max = 500
		if ($TxtMaxFiles -and [int]::TryParse($TxtMaxFiles.Text, [ref]$null)) { $max = [int]$TxtMaxFiles.Text }
		Add-Log "Auto-loading after folder select: $($folder.SelectedPath)"
		Load-FilesIntoGrid -Folder $folder.SelectedPath -Recursive ($ChkRecursive.IsChecked -eq $true) -Filter $filter -MaxItems $max
	}
})

$BtnDownloadExif.Add_Click({
	Set-Status "Installing ExifTool..."
	Start-Background {
		Ensure-ExifTool -Force
	} {
		param($path)
		if ($path) {
			if ($TxtExifPath) { $TxtExifPath.Text = $path }
			Set-Status "ExifTool installed: $path"
		} else {
			Set-Status "ExifTool not installed"
		}
	} {
		param($err)
		Set-Status "ExifTool install failed: $($err.Message)"
	}
})

if ($ChkUseExifPreview) {
	$ChkUseExifPreview.Add_Checked({ $script:UseExifPreview = $true; Set-Status "ExifTool preview ON (slower)" })
	$ChkUseExifPreview.Add_Unchecked({ $script:UseExifPreview = $false; Set-Status "ExifTool preview OFF" })
}

if ($ChkUseFfprobePreview) {
	$ChkUseFfprobePreview.Add_Checked({ $script:UseFfprobePreview = $true; Set-Status "ffprobe preview ON (slower)" })
	$ChkUseFfprobePreview.Add_Unchecked({ $script:UseFfprobePreview = $false; Set-Status "ffprobe preview OFF" })
}

if ($BtnExit) {
	$BtnExit.Add_Click({ $window.Close() })
}

$BtnPreviewFiles.Add_Click({
	$folder = $window.Tag
	if (-not $folder) {
		$dialog = [System.Windows.Forms.FolderBrowserDialog]::new()
		$dialog.Description = "Select folder to preview"
		if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
			$folder = $dialog.SelectedPath
			$window.Tag = $folder
			Set-Status "Selected: $folder"
		} else { return }
	}
	$filter = @()
	if ($TxtFilter.Text) { $filter = $TxtFilter.Text -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ } }
	$max = 500
	if ($TxtMaxFiles -and [int]::TryParse($TxtMaxFiles.Text, [ref]$null)) { $max = [int]$TxtMaxFiles.Text }
	Load-FilesIntoGrid -Folder $folder -Recursive ($ChkRecursive.IsChecked -eq $true) -Filter $filter -MaxItems $max
})

function Build-FileRows {
	param(
		[string]$Folder,
		[bool]$Recursive,
		[string[]]$Filter,
		[int]$MaxItems = 500
	)
	$sw = [System.Diagnostics.Stopwatch]::StartNew()
	Add-Log "Build-FileRows start: Folder=$Folder Recursive=$Recursive Filter=$($Filter -join ';') Max=$MaxItems"
	$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
	$rows = New-Object System.Collections.Generic.List[object]

	$extSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
	$wildcards = New-Object System.Collections.Generic.List[string]
	$hasFilters = $false
	if ($Filter -and $Filter.Count -gt 0) {
		$hasFilters = $true
		foreach ($pat in $Filter) {
			$pattern = $pat.Trim()
			if (-not $pattern) { continue }
			if ($pattern.StartsWith('*.')) {
				[void]$extSet.Add($pattern.Substring(1))
				continue
			}
			if ($pattern.StartsWith('.')) {
				[void]$extSet.Add($pattern)
				continue
			}
			$wildcards.Add($pattern)
		}
	}

	$enumOptions = [System.IO.EnumerationOptions]::new()
	$enumOptions.RecurseSubdirectories = $Recursive
	$enumOptions.IgnoreInaccessible = $true
	$enumOptions.ReturnSpecialDirectories = $false
	$enumOptions.AttributesToSkip = [System.IO.FileAttributes]::System -bor [System.IO.FileAttributes]::Temporary

	$iter = 0
	foreach ($filePath in [System.IO.Directory]::EnumerateFiles($Folder, '*', $enumOptions)) {
		$ext = [System.IO.Path]::GetExtension($filePath)
		if ($hasFilters) {
			$match = $false
			if ($extSet.Count -gt 0 -and $extSet.Contains($ext)) { $match = $true }
			if (-not $match -and $wildcards.Count -gt 0) {
				foreach ($wc in $wildcards) {
					if ([System.IO.Path]::GetFileName($filePath) -like $wc -or $filePath -like $wc) { $match = $true; break }
				}
			}
			if (-not $match) { continue }
		}

		if ($seen.Add($filePath)) {
			$rows.Add([pscustomobject]@{
				Selected = $true
				FullName = $filePath
				Name = [System.IO.Path]::GetFileName($filePath)
				FileType = $ext
				Metadata = $null
			})
		}
		$iter++
		if ($iter % 200 -eq 0) {
			Add-Log "Enumerated $iter candidates, kept $($rows.Count) (elapsed ${($sw.ElapsedMilliseconds)} ms)"
		}
		if ($rows.Count -ge $MaxItems) { break }
	}
	$sw.Stop()
	Add-Log "Build-FileRows complete: enumerated $iter, returning $($rows.Count) in $($sw.ElapsedMilliseconds) ms"
	return $rows
}

function Stream-FileRows {
	param(
		[string]$Folder,
		[bool]$Recursive,
		[string[]]$Filter,
		[int]$MaxItems = 500,
		[scriptblock]$OnBatch,
		[int]$InitialBatchSize = 1,
		[int]$BatchSize = 50,
		[int]$Token
	)
	$sw = [System.Diagnostics.Stopwatch]::StartNew()
	Add-Log "Stream-FileRows start: Folder=$Folder Recursive=$Recursive Filter=$($Filter -join ';') Max=$MaxItems"
	$seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
	$rows = New-Object System.Collections.Generic.List[object]
	$batch = New-Object System.Collections.Generic.List[object]

	$extSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
	$wildcards = New-Object System.Collections.Generic.List[string]
	$hasFilters = $false
	if ($Filter -and $Filter.Count -gt 0) {
		$hasFilters = $true
		foreach ($pat in $Filter) {
			$pattern = $pat.Trim()
			if (-not $pattern) { continue }
			if ($pattern.StartsWith('*.')) { [void]$extSet.Add($pattern.Substring(1)); continue }
			if ($pattern.StartsWith('.')) { [void]$extSet.Add($pattern); continue }
			$wildcards.Add($pattern)
		}
	}

	$enumOptions = [System.IO.EnumerationOptions]::new()
	$enumOptions.RecurseSubdirectories = $Recursive
	$enumOptions.IgnoreInaccessible = $true
	$enumOptions.ReturnSpecialDirectories = $false
	$enumOptions.AttributesToSkip = [System.IO.FileAttributes]::System -bor [System.IO.FileAttributes]::Temporary

	$iter = 0
	foreach ($filePath in [System.IO.Directory]::EnumerateFiles($Folder, '*', $enumOptions)) {
		$ext = [System.IO.Path]::GetExtension($filePath)
		if ($script:FileListToken -ne $Token) {
			Add-Log "Stream-FileRows canceled (token changed) after $iter files"
			break
		}
		if ($hasFilters) {
			$match = $false
			if ($extSet.Count -gt 0 -and $extSet.Contains($ext)) { $match = $true }
			if (-not $match -and $wildcards.Count -gt 0) {
				foreach ($wc in $wildcards) {
					if ([System.IO.Path]::GetFileName($filePath) -like $wc -or $filePath -like $wc) { $match = $true; break }
				}
			}
			if (-not $match) { continue }
		}

		if ($seen.Add($filePath)) {
			$entry = [pscustomobject]@{
				Selected = $true
				FullName = $filePath
				Name = [System.IO.Path]::GetFileName($filePath)
				FileType = $ext
				Metadata = $null
			}
			$rows.Add($entry)
			$batch.Add($entry)
		}
		$iter++
		$threshold = if ($rows.Count -le $InitialBatchSize) { $InitialBatchSize } else { $BatchSize }
		if ($batch.Count -ge $threshold -and $OnBatch) {
			& $OnBatch.Invoke($batch.ToArray(), $rows.Count, $sw.ElapsedMilliseconds)
			$batch.Clear()
		}
		if ($rows.Count -ge $MaxItems) { break }
	}

	if ($batch.Count -gt 0 -and $OnBatch) {
		& $OnBatch.Invoke($batch.ToArray(), $rows.Count, $sw.ElapsedMilliseconds)
	}

	$sw.Stop()
	Add-Log "Stream-FileRows complete: enumerated $iter, returned $($rows.Count) in $($sw.ElapsedMilliseconds) ms"
	return @{ Count = $rows.Count; Ms = $sw.ElapsedMilliseconds }
}

function Load-FilesIntoGrid {
	param([string]$Folder,[bool]$Recursive,[string[]]$Filter,[int]$MaxItems)
	Set-Status "Loading file list..."
	$token = [System.Threading.Interlocked]::Increment([ref]$script:FileListToken)
	$script:Items.Clear()
	Start-Background {
		Stream-FileRows -Folder $Folder -Recursive $Recursive -Filter $Filter -MaxItems $MaxItems -InitialBatchSize 1 -BatchSize 40 -Token $token -OnBatch {
			param($batch,$count,$elapsed)
			Invoke-UI {
				if ($token -ne $script:FileListToken) { return }
				foreach ($r in $batch) { $script:Items.Add($r) }
				Set-Status "Loading... $count items ($elapsed ms)"
			}
		}
	} {
		param($result)
		if ($token -ne $script:FileListToken) { return }
		Set-Status "Loaded $($result.Count) files from $Folder"
		Add-Log "Loaded $($result.Count) files from $Folder in $($result.Ms) ms"
		if ($script:Items.Count -gt 0) { $DataGridFiles.SelectedIndex = 0 }
	} {
		param($err)
		if ($token -ne $script:FileListToken) { return }
		Set-Status "Load error: $($err.Message)"
		Add-Log "Load error: $($err.Message)"
	}
}

$BtnScan.Add_Click({
	$folder = $window.Tag
	if (-not $folder) { [System.Windows.MessageBox]::Show("Please select a folder first","No folder") | Out-Null; return }
	$filter = @()
	if ($TxtFilter.Text) { $filter = $TxtFilter.Text -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ } }

	Set-Status "Scanning..."
	Add-Log "Scan requested for $folder"
	$max = 500
	if ($TxtMaxFiles -and [int]::TryParse($TxtMaxFiles.Text, [ref]$null)) { $max = [int]$TxtMaxFiles.Text }
	Load-FilesIntoGrid -Folder $folder -Recursive ($ChkRecursive.IsChecked -eq $true) -Filter $filter -MaxItems $max
})

$BtnBrowseExif.Add_Click({
	$ofd = New-Object Microsoft.Win32.OpenFileDialog
	$ofd.Filter = "ExifTool (exiftool.exe)|exiftool.exe|All files (*.*)|*.*"
	$ofd.Title = "Locate exiftool.exe"
	if ($ofd.ShowDialog()) {
		if (Test-Path $ofd.FileName) {
			$script:ExifToolPath = $ofd.FileName
			if ($TxtExifPath) { $TxtExifPath.Text = $ofd.FileName }
			Set-Status "Using exiftool: $($ofd.FileName)"
			Add-Log "User selected exiftool: $($ofd.FileName)"
		}
	}
})

$DataGridFiles.Add_SelectionChanged({
	$sel = $DataGridFiles.SelectedItem
	if (-not $sel) { return }
	$path = $sel.FullName
	$token = [System.Threading.Interlocked]::Increment([ref]$script:MetadataToken)
	$selSw = [System.Diagnostics.Stopwatch]::StartNew()
	Set-Status "Reading metadata..."
	Add-Log "Selection change -> $path (token $token)"

	Start-Background {
		Read-MetadataForFile -Path $path
	} {
		param($meta)
		if ($token -ne $script:MetadataToken) { return }
		$sel.Metadata = $meta
		$rows = Build-EditableRows -Meta $meta
		$DataGridMeta.ItemsSource = $rows
		$script:CurrentMeta = @{ Path = $path; List = $rows; Raw = $meta }
		Load-PreviewImage -Path $path
		$selSw.Stop()
		Add-Log "Selection pipeline finished for $path in $($selSw.ElapsedMilliseconds) ms"
		Set-Status "Metadata loaded for: $(Split-Path -Path $path -Leaf)"
	} {
		param($err)
		if ($token -ne $script:MetadataToken) { return }
		Set-Status "Metadata read failed: $($err.Message)"
		Add-Log "Read metadata failed: $($err.Message)"
	}
})

$BtnExportJson.Add_Click({
	$dialog = New-Object Microsoft.Win32.SaveFileDialog
	$dialog.Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
	$dialog.FileName = "metadata_export.json"
	if (-not $dialog.ShowDialog()) { return }

	$paths = $script:Items | ForEach-Object { $_.FullName }
	if (-not $paths) { [System.Windows.MessageBox]::Show("Nothing to export","Export") | Out-Null; return }

	Set-Status "Exporting metadata..."
	Start-Background {
		$arr = New-Object System.Collections.Generic.List[object]
		foreach ($p in $paths) { $arr.Add((Read-MetadataForFile -Path $p)) }
		return $arr
	} {
		param($arr)
		$json = $arr | ConvertTo-Json -Depth 12
		[System.IO.File]::WriteAllText($dialog.FileName, $json, [System.Text.Encoding]::UTF8)
		Set-Status "Exported JSON to $($dialog.FileName)"
		Add-Log "Exported JSON to $($dialog.FileName)"
	} {
		param($err)
		Set-Status "Export failed: $($err.Message)"
		Add-Log "Export failed: $($err.Message)"
	}
})

$BtnDryRun.Add_Click({
	if (-not $script:CurrentMeta) { [System.Windows.MessageBox]::Show("Select an item first","No item") | Out-Null; return }
	if (-not (Ensure-ExifToolInteractive)) { Set-Status "ExifTool required for dry run."; return }
	if ($TxtExifPath -and $script:ExifToolPath) { $TxtExifPath.Text = $script:ExifToolPath }

	$changes = Compute-Changes -Current $script:CurrentMeta
	if ($changes.Count -eq 0) { [System.Windows.MessageBox]::Show("No changes detected","Dry run") | Out-Null; return }

	try {
		$plan = Write-ExifToolMetadata -Path $script:CurrentMeta.Path -changes $changes -DryRun
		[System.Windows.MessageBox]::Show("DRY RUN - exiftool would run with args:`n$($plan.Args)","Dry run") | Out-Null
		Set-Status "Dry run prepared"
		Add-Log "Dry run for $($script:CurrentMeta.Path): $($plan.Args)"
	} catch {
		Set-Status "Dry run failed: $($_.Exception.Message)"
		Add-Log "Dry run failed: $($_.Exception.Message)"
	}
})

$BtnSave.Add_Click({
	if (-not $script:CurrentMeta) { [System.Windows.MessageBox]::Show("Select an item first","No item") | Out-Null; return }
	if (-not (Ensure-ExifToolInteractive)) { Set-Status "ExifTool required to save changes."; return }
	if ($TxtExifPath -and $script:ExifToolPath) { $TxtExifPath.Text = $script:ExifToolPath }

	$changes = Compute-Changes -Current $script:CurrentMeta
	if ($changes.Count -eq 0) { [System.Windows.MessageBox]::Show("No changes to save","Save") | Out-Null; return }

	Set-Status "Saving with ExifTool..."
	Start-Background {
		Write-ExifToolMetadata -Path $script:CurrentMeta.Path -changes $changes
	} {
		param($result)
		[System.Windows.MessageBox]::Show("Saved via ExifTool","Saved") | Out-Null
		Add-Log "Saved changes to $($script:CurrentMeta.Path)"

		# reload metadata after save
		$token = [System.Threading.Interlocked]::Increment([ref]$script:MetadataToken)
		Start-Background {
			Read-MetadataForFile -Path $script:CurrentMeta.Path
		} {
			param($meta)
			if ($token -ne $script:MetadataToken) { return }
			$rows = Build-EditableRows -Meta $meta
			$DataGridMeta.ItemsSource = $rows
			$script:CurrentMeta = @{ Path = $script:CurrentMeta.Path; List = $rows; Raw = $meta }
			Set-Status "Saved and reloaded metadata"
		} {
			param($err)
			if ($token -ne $script:MetadataToken) { return }
			Set-Status "Reload after save failed: $($err.Message)"
			Add-Log "Reload failed: $($err.Message)"
		}
	} {
		param($err)
		Set-Status "Save error: $($err.Message)"
		Add-Log "Save error: $($err.Message)"
	}
})

# ---------- Startup ----------
$TxtLog.Text = $script:LogBuffer.ToString()
$window.ShowDialog() | Out-Null