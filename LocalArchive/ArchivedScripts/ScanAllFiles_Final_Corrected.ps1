<#
ScanAllFiles_TraceLogging.ps1

- Creates C:\Path\Logs and C:\Path\ScanData if missing.
- Logs every step BEFORE it runs and records full stdout/stderr/exit codes for every external call.
- Preflight: resolves and runs fpcalc -version and ffprobe -version; halts with a clear error if either fails.
- For each file: runs fpcalc and ffprobe (no timeout), saves raw outputs to timestamped XML under C:\Path\ScanData, computes SHA256, parses results, and live-updates GUI.
- All log entries are appended to C:\Path\Logs\scan_log_YYYYMMdd_HHmmss.txt with timestamps.
- Run from a PowerShell console: powershell -ExecutionPolicy Bypass -File .\ScanAllFiles_TraceLogging.ps1
#>

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# ---------- Configuration ----------
$global:LogRoot = 'C:\Path\Logs'
$global:ScanDataRoot = 'C:\Path\ScanData'

foreach ($d in @($global:LogRoot, $global:ScanDataRoot)) {
    if (-not (Test-Path $d)) {
        try { New-Item -Path $d -ItemType Directory -Force | Out-Null } catch { Write-Error "Failed to create $d : $($_.Exception.Message)"; return }
    }
}

$logFile = Join-Path $global:LogRoot ("scan_log_{0}.txt" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
function Log {
    param([string]$Text)
    $line = "{0:yyyy-MM-dd HH:mm:ss} {1}" -f (Get-Date), $Text
    try { Add-Content -Path $logFile -Value $line } catch { Write-Error "Failed to write log: $($_.Exception.Message)" }
}

# ---------- Trace helper (log then return) ----------
function Trace {
    param([string]$Message)
    Log $Message
    return $Message
}

# ---------- Tool resolver (returns string path or $null only) ----------
function Resolve-Tool {
    param([string]$Name, [string]$ConfiguredPath)
    Trace ("Resolve-Tool: start for {0} with configured path '{1}'" -f $Name, $ConfiguredPath)
    try {
        if ($ConfiguredPath -and ($ConfiguredPath -is [string]) -and (Test-Path $ConfiguredPath)) {
            $p = (Get-Item $ConfiguredPath).FullName
            Trace ("Resolve-Tool: using configured path {0}" -f $p)
            return $p
        }
        Trace ("Resolve-Tool: trying Get-Command for {0}" -f $Name)
        try {
            $cmd = Get-Command $Name -ErrorAction SilentlyContinue
            if ($cmd -and $cmd.Source -and ($cmd.Source -is [string]) -and (Test-Path $cmd.Source)) {
                Trace ("Resolve-Tool: found on PATH {0}" -f $cmd.Source)
                return $cmd.Source
            }
        } catch { Trace ("Resolve-Tool: Get-Command failed for {0}: {1}" -f $Name, $_.Exception.Message) }
        $candidates = @(
            "$env:ProgramFiles\$Name",
            "$env:ProgramFiles(x86)\$Name",
            "C:\Program Files\$Name",
            "C:\Program Files (x86)\$Name",
            "C:\Tools\$Name",
            "C:\Tools\Chromaprint\$Name",
            "C:\Tools\ffmpeg\$Name",
            "C:\Windows\System32\$Name"
        )
        foreach ($c in $candidates) {
            Trace ("Resolve-Tool: checking candidate {0}" -f $c)
            if ((Test-Path $c) -and ($c -is [string]) ) {
                $p = (Get-Item $c).FullName
                Trace ("Resolve-Tool: candidate matched {0}" -f $p)
                return $p
            }
        }
        Trace ("Resolve-Tool: performing limited search under C:\ (depth 3)")
        try {
            $found = Get-ChildItem -Path C:\ -Filter $Name -Recurse -ErrorAction SilentlyContinue -Force -Depth 3 | Select-Object -First 1
            if ($found -and $found.FullName -and ($found.FullName -is [string])) {
                Trace ("Resolve-Tool: found by search {0}" -f $found.FullName)
                return $found.FullName
            }
        } catch { Trace ("Resolve-Tool: limited search failed: {0}" -f $_.Exception.Message) }
    } catch {
        Trace ("Resolve-Tool exception for {0}: {1}" -f $Name, $_.Exception.ToString())
    }
    Trace ("Resolve-Tool: no path found for {0}" -f $Name)
    return $null
}

# ---------- Process runner (no timeout) with full trace logging ----------
function Run-ProcessCaptureNoTimeout {
    param([string]$ExePath, [string[]]$Args)
    Trace ("Run-Process: preparing to run. ExePath='{0}' Args='{1}'" -f $ExePath, ($Args -join ' '))
    $result = @{ ExitCode = -1; StdOut = ""; StdErr = ""; ElapsedMs = 0; Exception = $null }
    if (-not $ExePath) {
        $result.Exception = "Executable path is null or empty"
        Trace ("Run-Process: exe path null or empty")
        return $result
    }
    if (-not ($ExePath -is [string])) {
        $result.Exception = "Executable path is not a string"
        Trace ("Run-Process: exe path not string: {0}" -f ($ExePath | Out-String))
        return $result
    }
    if (-not (Test-Path $ExePath)) {
        $result.Exception = "Executable not found: $ExePath"
        Trace ("Run-Process: exe missing: {0}" -f $ExePath)
        return $result
    }
    try {
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = $ExePath
        $psi.Arguments = ($Args -join ' ')
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true

        Trace ("Run-Process: starting process {0} {1}" -f $ExePath, $psi.Arguments)
        $proc = New-Object System.Diagnostics.Process
        $proc.StartInfo = $psi

        $stdOutBuilder = New-Object System.Text.StringBuilder
        $stdErrBuilder = New-Object System.Text.StringBuilder

        $outHandler = [System.Diagnostics.DataReceivedEventHandler]{ param($sender,$e) if ($e.Data) { [void]$stdOutBuilder.AppendLine($e.Data); Trace ("Run-Process: stdout: {0}" -f $e.Data) } }
        $errHandler = [System.Diagnostics.DataReceivedEventHandler]{ param($sender,$e) if ($e.Data) { [void]$stdErrBuilder.AppendLine($e.Data); Trace ("Run-Process: stderr: {0}" -f $e.Data) } }

        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        $proc.Start() | Out-Null
        $proc.add_OutputDataReceived($outHandler)
        $proc.add_ErrorDataReceived($errHandler)
        $proc.BeginOutputReadLine()
        $proc.BeginErrorReadLine()

        Trace ("Run-Process: waiting for exit of {0}" -f $ExePath)
        $proc.WaitForExit()
        $sw.Stop()

        $result.ElapsedMs = $sw.ElapsedMilliseconds
        $result.StdOut = $stdOutBuilder.ToString()
        $result.StdErr = $stdErrBuilder.ToString()
        $result.ExitCode = $proc.ExitCode

        Trace ("Run-Process: finished Exe={0} Exit={1} ElapsedMs={2}" -f $ExePath, $result.ExitCode, $result.ElapsedMs)
        Trace ("Run-Process: captured StdOut length={0} StdErr length={1}" -f ($result.StdOut.Length), ($result.StdErr.Length))

        $proc.remove_OutputDataReceived($outHandler)
        $proc.remove_ErrorDataReceived($errHandler)
        $proc.Dispose()
        return $result
    } catch {
        $result.Exception = $_.Exception.Message
        Trace ("Run-Process exception: Exe={0} Err={1}" -f $ExePath, $_.Exception.ToString())
        return $result
    }
}

# ---------- Helpers ----------
function Get-FullHashSafe {
    param([string]$Path)
    Trace ("Get-FullHashSafe: computing SHA256 for {0}" -f $Path)
    try { return (Get-FileHash -Path $Path -Algorithm SHA256 -ErrorAction Stop).Hash } catch { Trace ("Get-FullHashSafe failed: {0}" -f $_.Exception.Message); throw "Get-FileHash failed: $($_.Exception.Message)" }
}

# ---------- UI creation ----------
try {
    Trace "UI creation: start"
    $form = New-Object System.Windows.Forms.Form
    $form.Text = "ScanAllFiles — Trace Logging"
    $form.Size = New-Object System.Drawing.Size(1280,860)
    $form.StartPosition = "CenterScreen"

    $lblFpcalc = New-Object System.Windows.Forms.Label; $lblFpcalc.Text = "fpcalc.exe path (optional)"; $lblFpcalc.Location = New-Object System.Drawing.Point(12,12); $lblFpcalc.AutoSize = $true; $form.Controls.Add($lblFpcalc)
    $txtFpcalc = New-Object System.Windows.Forms.TextBox; $txtFpcalc.Location = New-Object System.Drawing.Point(12,32); $txtFpcalc.Size = New-Object System.Drawing.Size(820,24); $form.Controls.Add($txtFpcalc)
    $btnFpcalcBrowse = New-Object System.Windows.Forms.Button; $btnFpcalcBrowse.Text = "Browse..."; $btnFpcalcBrowse.Location = New-Object System.Drawing.Point(840,30); $btnFpcalcBrowse.Size = New-Object System.Drawing.Size(90,26); $form.Controls.Add($btnFpcalcBrowse)

    $lblFfprobe = New-Object System.Windows.Forms.Label; $lblFfprobe.Text = "ffprobe.exe path (optional)"; $lblFfprobe.Location = New-Object System.Drawing.Point(12,62); $lblFfprobe.AutoSize = $true; $form.Controls.Add($lblFfprobe)
    $txtFfprobe = New-Object System.Windows.Forms.TextBox; $txtFfprobe.Location = New-Object System.Drawing.Point(12,82); $txtFfprobe.Size = New-Object System.Drawing.Size(820,24); $form.Controls.Add($txtFfprobe)
    $btnFfprobeBrowse = New-Object System.Windows.Forms.Button; $btnFfprobeBrowse.Text = "Browse..."; $btnFfprobeBrowse.Location = New-Object System.Drawing.Point(840,80); $btnFfprobeBrowse.Size = New-Object System.Drawing.Size(90,26); $form.Controls.Add($btnFfprobeBrowse)

    $lblFolder = New-Object System.Windows.Forms.Label; $lblFolder.Text = "Folder to scan"; $lblFolder.Location = New-Object System.Drawing.Point(12,112); $lblFolder.AutoSize = $true; $form.Controls.Add($lblFolder)
    $txtFolder = New-Object System.Windows.Forms.TextBox; $txtFolder.Location = New-Object System.Drawing.Point(12,132); $txtFolder.Size = New-Object System.Drawing.Size(820,24); $form.Controls.Add($txtFolder)
    $btnBrowse = New-Object System.Windows.Forms.Button; $btnBrowse.Text = "Browse..."; $btnBrowse.Location = New-Object System.Drawing.Point(840,130); $btnBrowse.Size = New-Object System.Drawing.Size(90,26); $form.Controls.Add($btnBrowse)

    $chkRecursive = New-Object System.Windows.Forms.CheckBox; $chkRecursive.Text = "Scan subfolders"; $chkRecursive.Location = New-Object System.Drawing.Point(940,132); $chkRecursive.AutoSize = $true; $chkRecursive.Checked = $true; $form.Controls.Add($chkRecursive)

    $btnStart = New-Object System.Windows.Forms.Button; $btnStart.Text = "Start"; $btnStart.Location = New-Object System.Drawing.Point(1040,130); $btnStart.Size = New-Object System.Drawing.Size(100,26); $form.Controls.Add($btnStart)
    $btnCancel = New-Object System.Windows.Forms.Button; $btnCancel.Text = "Cancel"; $btnCancel.Location = New-Object System.Drawing.Point(1040,162); $btnCancel.Size = New-Object System.Drawing.Size(100,26); $form.Controls.Add($btnCancel)
    $btnReset = New-Object System.Windows.Forms.Button; $btnReset.Text = "Reset"; $btnReset.Location = New-Object System.Drawing.Point(1040,194); $btnReset.Size = New-Object System.Drawing.Size(100,26); $form.Controls.Add($btnReset)

    $split = New-Object System.Windows.Forms.SplitContainer
    $split.Orientation = 'Vertical'; $split.Location = New-Object System.Drawing.Point(12,230); $split.Size = New-Object System.Drawing.Size(1256,580); $split.SplitterDistance = 860; $form.Controls.Add($split)

    $grid = New-Object System.Windows.Forms.DataGridView
    $grid.Dock = 'Fill'; $grid.ReadOnly = $false; $grid.AllowUserToAddRows = $false; $grid.SelectionMode = 'FullRowSelect'; $grid.MultiSelect = $false; $grid.AutoSizeColumnsMode = 'Fill'
    $split.Panel1.Controls.Add($grid)

    $previewPanel = New-Object System.Windows.Forms.Panel; $previewPanel.Dock = 'Fill'; $split.Panel2.Controls.Add($previewPanel)
    $lblPreviewTitle = New-Object System.Windows.Forms.Label; $lblPreviewTitle.Text = "Preview / Status"; $lblPreviewTitle.Font = New-Object System.Drawing.Font("Segoe UI",10,[System.Drawing.FontStyle]::Bold); $lblPreviewTitle.Location = New-Object System.Drawing.Point(8,8); $lblPreviewTitle.AutoSize = $true; $previewPanel.Controls.Add($lblPreviewTitle)
    $txtPreviewPath = New-Object System.Windows.Forms.TextBox; $txtPreviewPath.Location = New-Object System.Drawing.Point(8,36); $txtPreviewPath.Size = New-Object System.Drawing.Size(360,22); $txtPreviewPath.ReadOnly = $true; $previewPanel.Controls.Add($txtPreviewPath)
    $txtMeta = New-Object System.Windows.Forms.TextBox; $txtMeta.Location = New-Object System.Drawing.Point(8,64); $txtMeta.Size = New-Object System.Drawing.Size(360,480); $txtMeta.Multiline = $true; $txtMeta.ReadOnly = $true; $txtMeta.ScrollBars = 'Vertical'; $previewPanel.Controls.Add($txtMeta)

    $btnExport = New-Object System.Windows.Forms.Button; $btnExport.Text = "Export CSV"; $btnExport.Location = New-Object System.Drawing.Point(12,820); $btnExport.Size = New-Object System.Drawing.Size(100,26); $form.Controls.Add($btnExport)
    $btnOpenLog = New-Object System.Windows.Forms.Button; $btnOpenLog.Text = "Open Log"; $btnOpenLog.Location = New-Object System.Drawing.Point(120,820); $btnOpenLog.Size = New-Object System.Drawing.Size(100,26); $form.Controls.Add($btnOpenLog)
    $btnOpenScanData = New-Object System.Windows.Forms.Button; $btnOpenScanData.Text = "Open ScanData Folder"; $btnOpenScanData.Location = New-Object System.Drawing.Point(228,820); $btnOpenScanData.Size = New-Object System.Drawing.Size(160,26); $form.Controls.Add($btnOpenScanData)
    $btnClose = New-Object System.Windows.Forms.Button; $btnClose.Text = "Close"; $btnClose.Location = New-Object System.Drawing.Point(1148,820); $btnClose.Size = New-Object System.Drawing.Size(120,26); $form.Controls.Add($btnClose)

    $statusStrip = New-Object System.Windows.Forms.StatusStrip; $statusStrip.Dock = 'Bottom'; $form.Controls.Add($statusStrip)
    $statusLabel = New-Object System.Windows.Forms.ToolStripStatusLabel; $statusLabel.Text = "Ready"; $statusStrip.Items.Add($statusLabel) | Out-Null
    $statusProgress = New-Object System.Windows.Forms.ToolStripProgressBar; $statusProgress.Size = New-Object System.Drawing.Size(300,16); $statusProgress.Value = 0; $statusStrip.Items.Add($statusProgress) | Out-Null

    $dt = New-Object System.Data.DataTable
    $dt.Columns.Add((New-Object System.Data.DataColumn("Delete",[bool]))) | Out-Null
    $dt.Columns.Add("Path") | Out-Null
    $dt.Columns.Add("Size") | Out-Null
    $dt.Columns.Add("VideoLength") | Out-Null
    $dt.Columns.Add("FullHash") | Out-Null
    $dt.Columns.Add("AudioFingerprint") | Out-Null
    $dt.Columns.Add("Width") | Out-Null
    $dt.Columns.Add("Height") | Out-Null
    $dt.Columns.Add("Status") | Out-Null
    $dt.Columns.Add("Error") | Out-Null
    $dt.Columns.Add("FpcalcMs") | Out-Null
    $dt.Columns.Add("FfprobeMs") | Out-Null

    $grid.DataSource = $dt
    if (-not $grid.Columns.Contains("Delete")) {
        $chkCol = New-Object System.Windows.Forms.DataGridViewCheckBoxColumn; $chkCol.Name = "Delete"; $chkCol.HeaderText = "Delete"; $chkCol.Width = 60; $grid.Columns.Insert(0, $chkCol) | Out-Null
    } else { $grid.Columns["Delete"].DisplayIndex = 0 }

    Trace "UI creation: complete"
} catch {
    Trace ("UI creation failed: {0}" -f $_.Exception.ToString())
    [System.Windows.Forms.MessageBox]::Show("UI creation failed. See log: $logFile","Error",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error)
    throw
}

# ---------- Background worker ----------
$analyzer = New-Object System.ComponentModel.BackgroundWorker
$analyzer.WorkerReportsProgress = $true
$analyzer.WorkerSupportsCancellation = $true
$global:PathList = @()
$global:ScanDataFile = $null

function Preflight-CheckTools {
    param([string]$FpCandidate, [string]$FfCandidate)
    Trace ("Preflight-CheckTools: start. fpCandidate='{0}' ffCandidate='{1}'" -f $FpCandidate, $FfCandidate)
    try {
        $fpcalcExe = Resolve-Tool -Name 'fpcalc.exe' -ConfiguredPath $FpCandidate
        $ffprobeExe = Resolve-Tool -Name 'ffprobe.exe' -ConfiguredPath $FfCandidate
        Trace ("Preflight-CheckTools: resolved fpcalc='{0}' ffprobe='{1}'" -f $fpcalcExe, $ffprobeExe)

        if (-not $fpcalcExe -or -not ($fpcalcExe -is [string])) { $msg = "fpcalc resolution failed or returned non-string. Resolved value: $fpcalcExe"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$fpcalcExe } }
        if (-not $ffprobeExe -or -not ($ffprobeExe -is [string])) { $msg = "ffprobe resolution failed or returned non-string. Resolved value: $ffprobeExe"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$ffprobeExe } }
        if (-not (Test-Path $fpcalcExe)) { $msg = "fpcalc path does not exist: $fpcalcExe"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$fpcalcExe } }
        if (-not (Test-Path $ffprobeExe)) { $msg = "ffprobe path does not exist: $ffprobeExe"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$ffprobeExe } }

        Trace ("Preflight-CheckTools: running fpcalc -version")
        $fpver = Run-ProcessCaptureNoTimeout -ExePath $fpcalcExe -Args @("-version")
        Trace ("Preflight-CheckTools: running ffprobe -version")
        $ffver = Run-ProcessCaptureNoTimeout -ExePath $ffprobeExe -Args @("-version")

        Trace ("Preflight-CheckTools: fpcalc result Exit={0} StdOutLen={1} StdErrLen={2}" -f $fpver.ExitCode, ($fpver.StdOut.Length), ($fpver.StdErr.Length))
        Trace ("Preflight-CheckTools: ffprobe result Exit={0} StdOutLen={1} StdErrLen={2}" -f $ffver.ExitCode, ($ffver.StdOut.Length), ($ffver.StdErr.Length))

        if ($fpver.Exception) { $msg = "fpcalc failed to start: $($fpver.Exception)"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$fpver } }
        if (($fpver.ExitCode -ne 0) -and (-not $fpver.StdOut -and -not $fpver.StdErr)) { $msg = "fpcalc returned non-zero exit code and no output. Exit=$($fpver.ExitCode). StdErr: $($fpver.StdErr -replace '\r|\n',' ')"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$fpver } }
        $fp_ok = ($fpver.StdOut -and ($fpver.StdOut -match 'fpcalc|Chromaprint|fpcalc version')) -or ($fpver.StdErr -and ($fpver.StdErr -match 'fpcalc|Chromaprint|fpcalc version'))
        if (-not $fp_ok) { $msg = "fpcalc did not produce recognizable version output. StdOut: $($fpver.StdOut -replace '\r|\n',' '); StdErr: $($fpver.StdErr -replace '\r|\n',' ')"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$fpver } }

        if ($ffver.Exception) { $msg = "ffprobe failed to start: $($ffver.Exception)"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$ffver } }
        if (($ffver.ExitCode -ne 0) -and (-not $ffver.StdOut -and -not $ffver.StdErr)) { $msg = "ffprobe returned non-zero exit code and no output. Exit=$($ffver.ExitCode). StdErr: $($ffver.StdErr -replace '\r|\n',' ')"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$ffver } }
        $ff_ok = ($ffver.StdOut -and ($ffver.StdOut -match 'ffprobe|ffmpeg')) -or ($ffver.StdErr -and ($ffver.StdErr -match 'ffprobe|ffmpeg'))
        if (-not $ff_ok) { $msg = "ffprobe did not produce recognizable version output. StdOut: $($ffver.StdOut -replace '\r|\n',' '); StdErr: $($ffver.StdErr -replace '\r|\n',' ')"; Trace $msg; return @{ Ok=$false; Message=$msg; Details=$ffver } }

        Trace "Preflight-CheckTools: success"
        return @{ Ok=$true; Fpcalc=$fpcalcExe; Ffprobe=$ffprobeExe; FpVer=$fpver; FfVer=$ffver }
    } catch {
        Trace ("Preflight exception: {0}" -f $_.Exception.ToString())
        return @{ Ok=$false; Message="Preflight exception"; Details = $_.Exception.ToString() }
    }
}

# ---------- Worker DoWork ----------
$analyzer.add_DoWork({
    param($sender,$e)
    try {
        $fpcalcExe = $e.Argument.Fpcalc
        $ffprobeExe = $e.Argument.Ffprobe

        Trace ("Worker: creating ScanData XML at {0}" -f $global:ScanDataRoot)
        $scanXmlName = Join-Path $global:ScanDataRoot ("scandata_{0}.xml" -f (Get-Date -Format "yyyyMMdd_HHmmss"))
        $global:ScanDataFile = $scanXmlName
        $xmlDoc = New-Object System.Xml.XmlDocument
        $root = $xmlDoc.CreateElement("ScanData")
        $xmlDoc.AppendChild($root) | Out-Null
        $xmlDoc.Save($scanXmlName)
        Trace ("Worker: created ScanData XML {0}" -f $scanXmlName)

        $count = $global:PathList.Count
        for ($i = 0; $i -lt $count; $i++) {
            if ($sender.CancellationPending) { Trace "Worker: cancellation requested"; $e.Cancel = $true; break }
            $path = $global:PathList[$i]
            Trace ("Worker: processing file {0} ({1}/{2})" -f $path, ($i+1), $count)
            $form.Invoke([System.Windows.Forms.MethodInvoker]{ if ($i -lt $dt.Rows.Count) { $dt.Rows[$i]["Status"] = "Processing"; $dt.Rows[$i]["Error"] = "" } }) | Out-Null

            Trace ("Worker: running fpcalc for {0}" -f $path)
            $fpRes = Run-ProcessCaptureNoTimeout -ExePath $fpcalcExe -Args @("-raw","-length","120",$path)
            Trace ("Worker: fpcalc done Exit={0} StdOutLen={1} StdErrLen={2}" -f $fpRes.ExitCode, ($fpRes.StdOut.Length), ($fpRes.StdErr.Length))

            Trace ("Worker: running ffprobe for {0}" -f $path)
            $ffRes = Run-ProcessCaptureNoTimeout -ExePath $ffprobeExe -Args @("-v","error","-select_streams","v:0","-show_entries","format=duration","-show_entries","stream=width,height","-of","default=noprint_wrappers=1:nokey=1","--",$path)
            Trace ("Worker: ffprobe done Exit={0} StdOutLen={1} StdErrLen={2}" -f $ffRes.ExitCode, ($ffRes.StdOut.Length), ($ffRes.StdErr.Length))

            # Append raw outputs to XML
            try {
                Trace ("Worker: appending raw outputs to XML for {0}" -f $path)
                $xmlDoc = New-Object System.Xml.XmlDocument
                $xmlDoc.Load($global:ScanDataFile)
                $root = $xmlDoc.DocumentElement

                $fileNode = $xmlDoc.CreateElement("File")
                $fileNode.SetAttribute("Path", $path)
                $fileNode.SetAttribute("Timestamp", (Get-Date).ToString("o"))

                $sizeNode = $xmlDoc.CreateElement("FileSize")
                $sizeNode.InnerText = (Get-Item $path).Length
                $fileNode.AppendChild($sizeNode) | Out-Null

                $fpNode = $xmlDoc.CreateElement("Fpcalc")
                $fpNode.SetAttribute("ExitCode", ($fpRes.ExitCode -as [string]))
                $fpNode.SetAttribute("ElapsedMs", ($fpRes.ElapsedMs -as [string]))
                $outNode = $xmlDoc.CreateElement("StdOut"); $outNode.InnerText = $fpRes.StdOut; $fpNode.AppendChild($outNode) | Out-Null
                $errNode = $xmlDoc.CreateElement("StdErr"); $errNode.InnerText = $fpRes.StdErr; $fpNode.AppendChild($errNode) | Out-Null
                $fileNode.AppendChild($fpNode) | Out-Null

                $ffNode = $xmlDoc.CreateElement("Ffprobe")
                $ffNode.SetAttribute("ExitCode", ($ffRes.ExitCode -as [string]))
                $ffNode.SetAttribute("ElapsedMs", ($ffRes.ElapsedMs -as [string]))
                $outNode = $xmlDoc.CreateElement("StdOut"); $outNode.InnerText = $ffRes.StdOut; $ffNode.AppendChild($outNode) | Out-Null
                $errNode = $xmlDoc.CreateElement("StdErr"); $errNode.InnerText = $ffRes.StdErr; $ffNode.AppendChild($errNode) | Out-Null
                $fileNode.AppendChild($ffNode) | Out-Null

                $root.AppendChild($fileNode) | Out-Null
                $xmlDoc.Save($global:ScanDataFile)
                Trace ("Worker: XML append complete for {0}" -f $path)
            } catch {
                Trace ("Worker: XML write error for {0}: {1}" -f $path, $_.Exception.Message)
            }

            # Analyze and update UI
            try {
                $fingerprint = ""
                if ($fpRes -and $fpRes.StdOut) {
                    $fpLine = ($fpRes.StdOut -split "`n" | Where-Object { $_ -like 'FINGERPRINT=*' } | Select-Object -First 1)
                    if ($fpLine) { $fingerprint = ($fpLine -replace '^FINGERPRINT=','').Trim() }
                }
                $duration = $null; $width = $null; $height = $null
                if ($ffRes -and $ffRes.StdOut) {
                    $lines = ($ffRes.StdOut -split "`n") | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
                    if ($lines.Count -ge 1) { $d=0.0; if ([double]::TryParse($lines[0],[ref]$d)) { $duration = $d } }
                    if ($lines.Count -ge 3) { $w=0; $h=0; if ([int]::TryParse($lines[1],[ref]$w)) { $width=$w }; if ([int]::TryParse($lines[2],[ref]$h)) { $height=$h } }
                }

                $hash = ""
                try { $hash = Get-FullHashSafe -Path $path } catch { $hash = ""; Trace ("Worker: hash compute failed for {0}: {1}" -f $path, $_.Exception.Message) }

                $form.Invoke([System.Windows.Forms.MethodInvoker]{
                    if ($i -lt $dt.Rows.Count) {
                        $dt.Rows[$i]["FullHash"] = $hash
                        $dt.Rows[$i]["AudioFingerprint"] = $fingerprint
                        $dt.Rows[$i]["VideoLength"] = if ($duration) { "{0:N2}" -f $duration } else { "" }
                        $dt.Rows[$i]["Width"] = if ($width) { $width } else { "" }
                        $dt.Rows[$i]["Height"] = if ($height) { $height } else { "" }
                        $dt.Rows[$i]["FpcalcMs"] = $fpRes.ElapsedMs
                        $dt.Rows[$i]["FfprobeMs"] = $ffRes.ElapsedMs
                        $dt.Rows[$i]["Status"] = "Done"
                        $dt.Rows[$i]["Error"] = ""
                        $txtPreviewPath.Text = $path
                        $meta = "Size (bytes): " + $dt.Rows[$i]["Size"] + "`r`n"
                        if ($dt.Rows[$i]["VideoLength"]) { $meta += "Video length: " + $dt.Rows[$i]["VideoLength"] + " s`r`n" }
                        if ($dt.Rows[$i]["Width"] -and $dt.Rows[$i]["Height"]) { $meta += "Resolution: " + $dt.Rows[$i]["Width"] + "x" + $dt.Rows[$i]["Height"] + "`r`n" }
                        $meta += "FullHash: " + $hash + "`r`n"
                        $meta += "AudioFingerprint: " + ($fingerprint -replace '(.{64}).*','$1...') + "`r`n"
                        $meta += "fpcalc time (ms): " + $fpRes.ElapsedMs + "`r`n"
                        $meta += "ffprobe time (ms): " + $ffRes.ElapsedMs + "`r`n"
                        $txtMeta.Text = $meta
                    }
                }) | Out-Null

                Trace ("Worker: analysis complete for {0}" -f $path)
            } catch {
                Trace ("Worker: analysis error for {0}: {1}" -f $path, $_.Exception.Message)
                $form.Invoke([System.Windows.Forms.MethodInvoker]{ if ($i -lt $dt.Rows.Count) { $dt.Rows[$i]["Status"] = "Error"; $dt.Rows[$i]["Error"] = $_.Exception.Message } }) | Out-Null
            }

            $sender.ReportProgress([int]((($i+1)/$count)*100), "Analyzing $([System.IO.Path]::GetFileName($path)) ($($i+1)/$count)")
        }

        $e.Result = @{ Count = $global:PathList.Count; ScanDataFile = $global:ScanDataFile }
    } catch {
        Trace ("Worker DoWork fatal exception: " + $_.Exception.ToString())
        $e.Result = @{ Error = $_.Exception.ToString() }
    }
})

$analyzer.add_ProgressChanged({
    param($sender,$args)
    $form.Invoke([System.Windows.Forms.MethodInvoker]{ $statusLabel.Text = $args.UserState; $statusProgress.Value = [int]$args.ProgressPercentage }) | Out-Null
})

$analyzer.add_RunWorkerCompleted({
    param($sender,$args)
    try {
        if ($args.Cancelled) { $statusLabel.Text = "Analysis cancelled" } else { $statusLabel.Text = "Analysis complete"; $statusProgress.Value = 100 }
        Trace ("Worker completed. Result: {0}" -f ($args.Result | Out-String))
    } catch { Trace ("RunWorkerCompleted exception: " + $_.Exception.ToString()) }
})

# ---------- Safe event wiring ----------
function SafeAddClick { param($btn, $scriptBlock) if ($null -eq $btn) { Trace "SafeAddClick: button variable is null; skipping wiring"; return } try { $btn.Add_Click($scriptBlock) } catch { Trace ("SafeAddClick: failed to wire event: {0}" -f $_.Exception.Message) } }

SafeAddClick $btnFpcalcBrowse {
    try {
        $ofd = New-Object System.Windows.Forms.OpenFileDialog; $ofd.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        if ($ofd.ShowDialog() -eq 'OK') { $txtFpcalc.Text = $ofd.FileName; Trace ("btnFpcalcBrowse: selected {0}" -f $ofd.FileName) }
    } catch { Trace ("btnFpcalcBrowse handler exception: " + $_.Exception.ToString()) }
}

SafeAddClick $btnFfprobeBrowse {
    try {
        $ofd = New-Object System.Windows.Forms.OpenFileDialog; $ofd.Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        if ($ofd.ShowDialog() -eq 'OK') { $txtFfprobe.Text = $ofd.FileName; Trace ("btnFfprobeBrowse: selected {0}" -f $ofd.FileName) }
    } catch { Trace ("btnFfprobeBrowse handler exception: " + $_.Exception.ToString()) }
}

SafeAddClick $btnBrowse {
    try {
        $fbd = New-Object System.Windows.Forms.FolderBrowserDialog; $fbd.Description = "Select folder to scan"
        if ($fbd.ShowDialog() -eq 'OK') { $txtFolder.Text = $fbd.SelectedPath; Trace ("btnBrowse: selected folder {0}" -f $fbd.SelectedPath) }
    } catch { Trace ("btnBrowse handler exception: " + $_.Exception.ToString()) }
}

SafeAddClick $btnStart {
    try {
        Trace "btnStart: clicked"
        if ($analyzer.IsBusy) { [System.Windows.Forms.MessageBox]::Show("Analysis already running. Use Cancel or Reset first.","Busy",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning); Trace "btnStart: aborted because analyzer is busy"; return }
        $root = $txtFolder.Text.Trim()
        if (-not (Test-Path $root)) { [System.Windows.Forms.MessageBox]::Show("Please select a valid folder to scan.","Folder required",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Warning); Trace "btnStart: invalid folder"; return }

        Trace ("btnStart: running preflight with fpcalc='{0}' ffprobe='{1}'" -f $txtFpcalc.Text.Trim(), $txtFfprobe.Text.Trim())
        $pre = Preflight-CheckTools -FpCandidate $txtFpcalc.Text.Trim() -FfCandidate $txtFfprobe.Text.Trim()
        if (-not $pre.Ok) {
            $details = ""
            if ($pre.Details) { $details = "`r`nDetails:`r`n" + ($pre.Details | Out-String) }
            $msg = "Tool preflight failed:`r`n$($pre.Message)$details"
            Trace ("btnStart: preflight failed: {0}" -f $msg)
            [System.Windows.Forms.MessageBox]::Show($msg, "Preflight failed", [System.Windows.Forms.MessageBoxButtons]::OK, [System.Windows.Forms.MessageBoxIcon]::Error)
            return
        }

        Trace ("btnStart: enumerating files in {0} (recursive={1})" -f $root, $chkRecursive.Checked)
        $dt.Rows.Clear(); $global:PathList = @(); $txtPreviewPath.Text = ""; $txtMeta.Text = ""
        try {
            if ($chkRecursive.Checked) { $allFiles = Get-ChildItem -Path $root -Recurse -File -ErrorAction SilentlyContinue } else { $allFiles = Get-ChildItem -Path $root -File -ErrorAction SilentlyContinue }
        } catch { Trace ("btnStart: enumeration failed: {0}" -f $_.Exception.Message); [System.Windows.Forms.MessageBox]::Show("Failed to enumerate files: $($_.Exception.Message)","Error",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error); return }

        $total = $allFiles.Count; $idx = 0
        foreach ($f in $allFiles) {
            $idx++
            $form.Invoke([System.Windows.Forms.MethodInvoker]{
                $r = $dt.NewRow()
                $r.Delete = $false
                $r.Path = $f.FullName
                $r.Size = $f.Length
                $r.VideoLength = ""
                $r.FullHash = ""
                $r.AudioFingerprint = ""
                $r.Width = ""
                $r.Height = ""
                $r.Status = "Queued"
                $r.Error = ""
                $r.FpcalcMs = ""
                $r.FfprobeMs = ""
                $dt.Rows.Add($r)
                $statusLabel.Text = "Listed $idx of $total files"
                $statusProgress.Value = [int]([math]::Min(100, [int](($idx/$total)*100)))
            }) | Out-Null
            $global:PathList += $f.FullName
        }

        Trace ("btnStart: starting analysis for {0} files" -f $global:PathList.Count)
        $analyzer.RunWorkerAsync(@{ Fpcalc = $pre.Fpcalc; Ffprobe = $pre.Ffprobe })
    } catch { Trace ("btnStart handler exception: " + $_.Exception.ToString()); [System.Windows.Forms.MessageBox]::Show("Start failed. See log: $logFile","Error",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error) }
}

SafeAddClick $btnCancel {
    try {
        Trace "btnCancel: clicked"
        if ($analyzer.IsBusy) { $analyzer.CancelAsync(); $form.Invoke([System.Windows.Forms.MethodInvoker]{ $statusLabel.Text = "Cancellation requested. Current file will finish." }) | Out-Null; Trace "btnCancel: cancellation requested" } else { [System.Windows.Forms.MessageBox]::Show("No analysis is running.","Cancel",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information) }
    } catch { Trace ("btnCancel handler exception: " + $_.Exception.ToString()) }
}

SafeAddClick $btnReset {
    try {
        Trace "btnReset: clicked"
        if ($analyzer.IsBusy) { $analyzer.CancelAsync(); $form.Invoke([System.Windows.Forms.MethodInvoker]{ $statusLabel.Text = "Reset requested. Cancelling analysis..." }) | Out-Null; Trace "btnReset: cancelling running analysis"; Start-Sleep -Milliseconds 200 }
        $form.Invoke([System.Windows.Forms.MethodInvoker]{ $dt.Rows.Clear(); $global:PathList = @(); $txtPreviewPath.Text = ""; $txtMeta.Text = ""; $statusLabel.Text = "Reset complete"; $statusProgress.Value = 0 }) | Out-Null
        Trace "btnReset: reset complete"
    } catch { Trace ("btnReset handler exception: " + $_.Exception.ToString()) }
}

SafeAddClick $btnExport {
    try {
        Trace "btnExport: clicked"
        if ($dt.Rows.Count -eq 0) { [System.Windows.Forms.MessageBox]::Show("No data to export.","No data",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information); Trace "btnExport: no data"; return }
        $sfd = New-Object System.Windows.Forms.SaveFileDialog; $sfd.Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"; $sfd.FileName = "scanned_files.csv"
        if ($sfd.ShowDialog() -eq 'OK') {
            $rows = @()
            foreach ($r in $dt.Rows) {
                $rows += [PSCustomObject]@{
                    Path = $r.Path; Size = $r.Size; VideoLength = $r.VideoLength; FullHash = $r.FullHash; AudioFingerprint = $r.AudioFingerprint
                    Width = $r.Width; Height = $r.Height; Status = $r.Status; Error = $r.Error; FpcalcMs = $r.FpcalcMs; FfprobeMs = $r.FfprobeMs
                }
            }
            $rows | Export-Csv -Path $sfd.FileName -NoTypeInformation -Force
            [System.Windows.Forms.MessageBox]::Show("Exported to $($sfd.FileName)","Export complete",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information)
            Trace ("btnExport: exported CSV to {0}" -f $sfd.FileName)
        }
    } catch { Trace ("btnExport handler exception: " + $_.Exception.ToString()); [System.Windows.Forms.MessageBox]::Show("Export failed. See log.","Error",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error) }
}

SafeAddClick $btnOpenLog {
    try { Trace "btnOpenLog: clicked"; if (Test-Path $logFile) { Start-Process -FilePath $logFile } else { [System.Windows.Forms.MessageBox]::Show("Log file not found: $logFile","No log",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information) } } catch { Trace ("btnOpenLog handler exception: " + $_.Exception.ToString()) }
}

SafeAddClick $btnOpenScanData {
    try { Trace "btnOpenScanData: clicked"; if (Test-Path $global:ScanDataRoot) { Start-Process -FilePath $global:ScanDataRoot } else { [System.Windows.Forms.MessageBox]::Show("ScanData folder not found: $global:ScanDataRoot","No folder",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Information) } } catch { Trace ("btnOpenScanData handler exception: " + $_.Exception.ToString()) }
}

SafeAddClick $btnClose {
    try { Trace "btnClose: clicked"; if ($analyzer.IsBusy -and $analyzer.WorkerSupportsCancellation) { $analyzer.CancelAsync() }; $form.Close() } catch { Trace ("btnClose handler exception: " + $_.Exception.ToString()) }
}

# Prompt for folder on launch
try {
    $startFbd = New-Object System.Windows.Forms.FolderBrowserDialog; $startFbd.Description = "Select folder to scan"
    if ($startFbd.ShowDialog() -eq 'OK') { $txtFolder.Text = $startFbd.SelectedPath; Trace ("Initial folder selected: {0}" -f $txtFolder.Text) }
} catch { Trace ("Initial folder prompt exception: " + $_.Exception.ToString()) }

Trace "UI ready. Log file: $logFile ; ScanData folder: $global:ScanDataRoot"
try { [void]$form.ShowDialog() } catch { Trace ("form.ShowDialog exception: " + $_.Exception.ToString()); [System.Windows.Forms.MessageBox]::Show("Fatal UI error. See log: $logFile","Fatal",[System.Windows.Forms.MessageBoxButtons]::OK,[System.Windows.Forms.MessageBoxIcon]::Error) }