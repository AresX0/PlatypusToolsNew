# MergeScripts.ps1 - Creates PlatypusTools.ps1 by combining VideoEditor2.ps1 and SystemCleaner.ps1

$outputPath = "c:\Path\PlatypusTools.ps1"

# Read source files as arrays (for line-by-line processing)
$veLines = Get-Content -Path "c:\Path\VideoEditor2.ps1"
$scLines = Get-Content -Path "c:\Path\SystemCleaner.ps1"

# Find key markers in VideoEditor2.ps1
$veXamlStart = -1
$veXamlEnd = -1
$vePostXaml = -1
for ($i = 0; $i -lt $veLines.Count; $i++) {
    if ($veLines[$i] -match '^\[xml\]\$xaml = @"') { $veXamlStart = $i }
    if ($veLines[$i] -match '^"@$' -and $veXamlStart -gt 0 -and $veXamlEnd -lt 0) { $veXamlEnd = $i }
}

# Find key markers in SystemCleaner.ps1
$scXamlStart = -1
$scXamlEnd = -1
$scFunctionsEnd = -1
for ($i = 0; $i -lt $scLines.Count; $i++) {
    if ($scLines[$i] -match '^\[xml\]\$xaml = @"') { $scXamlStart = $i }
    if ($scLines[$i] -match '^"@$' -and $scXamlStart -gt 0 -and $scXamlEnd -lt 0) { $scXamlEnd = $i }
}

Write-Host "VideoEditor2: XAML from line $veXamlStart to $veXamlEnd"
Write-Host "SystemCleaner: XAML from line $scXamlStart to $scXamlEnd"

# Extract the TabItem sections from SystemCleaner XAML (Folder Hider, Security Scan, Recent Cleaner)
$scXamlLines = $scLines[$scXamlStart..$scXamlEnd]
$scTabsContent = @()
$inTab = $false
$tabLines = @()
for ($i = 0; $i -lt $scXamlLines.Count; $i++) {
    $line = $scXamlLines[$i]
    if ($line -match '<TabItem Header="') { $inTab = $true; $tabLines = @($line) }
    elseif ($inTab) { 
        $tabLines += $line
        if ($line -match '</TabItem>') { 
            $inTab = $false
            $scTabsContent += ,($tabLines -join "`n")
        }
    }
}

Write-Host "Extracted $($scTabsContent.Count) tabs from SystemCleaner"

# Find where to insert SystemCleaner tabs in VideoEditor XAML (before </TabControl>)
$veXamlLines = $veLines[$veXamlStart..$veXamlEnd]
$insertPoint = -1
for ($i = $veXamlLines.Count - 1; $i -ge 0; $i--) {
    if ($veXamlLines[$i] -match '</TabControl>') { $insertPoint = $i; break }
}

Write-Host "Insert point for SC tabs in VE XAML: line $insertPoint (relative to XAML start)"

# Build the combined XAML
$combinedXamlLines = @()
for ($i = 0; $i -lt $veXamlLines.Count; $i++) {
    if ($i -eq $insertPoint) {
        # Insert SystemCleaner tabs here
        foreach ($tab in $scTabsContent) {
            $combinedXamlLines += ""
            $combinedXamlLines += "      <!-- From SystemCleaner -->"
            $combinedXamlLines += ($tab -split "`n")
        }
    }
    $combinedXamlLines += $veXamlLines[$i]
}

Write-Host "Combined XAML lines: $($combinedXamlLines.Count)"

# Now build the complete output file:
# 1. Header and setup from SystemCleaner (has admin elevation which we need)
# 2. Extract functions from BOTH files (before their XAML)
# 3. Combined XAML
# 4. Control initialization and event handlers from BOTH files

# Extract SystemCleaner functions (from line 1 to XAML start)
$scFunctions = $scLines[0..($scXamlStart-1)]

# Extract VideoEditor functions (skip STA/assembly lines, get from after assemblies to XAML)
$veStartFunctions = 15  # Skip Add-Type lines and STA guard
$veFunctions = $veLines[$veStartFunctions..($veXamlStart-1)]

# Extract post-XAML content from both (control init and event handlers)
$vePostXaml = $veLines[($veXamlEnd+1)..($veLines.Count-1)]
$scPostXaml = $scLines[($scXamlEnd+1)..($scLines.Count-1)]

Write-Host "Building output file..."

# Write the output file
$output = @()

# 1. Add SystemCleaner header (has proper STA and admin guards)
$output += $scFunctions

# 2. Add VideoEditor-specific functions (avoid duplicates)
$output += ""
$output += "# ==================== VIDEO EDITOR FUNCTIONS ===================="
$output += $veFunctions

# 3. Add combined XAML
$output += ""
$output += "# ==================== COMBINED XAML ===================="
$output += $combinedXamlLines

# 4. Add VideoEditor post-XAML handlers
$output += ""
$output += "# ==================== VIDEO EDITOR HANDLERS ===================="
$output += $vePostXaml | Where-Object { $_ -notmatch '^\$window\.ShowDialog' }

# 5. Add SystemCleaner post-XAML handlers (skip the LoadXaml and final ShowDialog)
$output += ""
$output += "# ==================== SYSTEM CLEANER HANDLERS ===================="
# Find where SC handlers actually start (after reader/window loading)
$scHandlerStart = -1
for ($i = 0; $i -lt $scPostXaml.Count; $i++) {
    if ($scPostXaml[$i] -match '\$Global:HiderConfig = Get-HiderConfig') { $scHandlerStart = $i; break }
}
if ($scHandlerStart -gt 0) {
    $scHandlers = $scPostXaml[$scHandlerStart..($scPostXaml.Count-1)] | Where-Object { $_ -notmatch '^\$window\.ShowDialog' -and $_ -notmatch '^try \{' -and $_ -notmatch '^\} catch \{' -and $_ -notmatch 'UI failed to show' -and $_ -notmatch 'UI failed to open' }
    $output += $scHandlers
}

# 6. Add final ShowDialog
$output += ""
$output += "# --- Show Combined UI ---"
$output += '$window.ShowDialog() | Out-Null'

# Write to file
$output | Out-File -FilePath $outputPath -Encoding UTF8

Write-Host "`nCreated: $outputPath"
Write-Host "Total lines: $($output.Count)"
