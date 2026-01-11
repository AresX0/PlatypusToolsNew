<#
Usage:
  # dry run (shows proposed edits)
  .\EnableNonInteractiveMode.ps1 -WhatIf

  # apply changes
  .\EnableNonInteractiveMode.ps1 -Apply

This script scans all .ps1 files in the repo (excluding Tools folder itself) and for any function that
uses [Parameter(Mandatory)] it will:
 - add a [switch]$NonInteractive parameter to the function's param block if missing
 - insert dot-sourcing of Tools\NonInteractive.ps1 and Set-NonInteractive -Enable:$NonInteractive immediately after the param block
 - insert Require-Parameter lines for each mandatory parameter

It will not modify functions without Mandatory parameters, and will attempt to preserve formatting.
#>
param(
    [switch]$Apply,
    [switch]$WhatIf
)

$repo = Get-Location
$psFiles = Get-ChildItem -Path $repo -Filter '*.ps1' -Recurse | Where-Object { $_.FullName -notmatch '\\Tools\\EnableNonInteractiveMode.ps1$' -and $_.DirectoryName -notmatch '\\.git\\' }

function Parse-ParamBlock {
    param([string[]]$lines, [int]$startIndex)
    # startIndex should be index of line containing 'param('
    $i = $startIndex
    $block = @()
    $open = 0
    for ($j = $i; $j -lt $lines.Length; $j++) {
        $line = $lines[$j]
        $block += $line
        $open += ($line -split '\(').Length - 1
        $open -= ($line -split '\)').Length - 1
        if ($open -eq 0) { return @{ EndIndex = $j; Block = $block } }
    }
    return $null
}

foreach ($f in $psFiles) {
    $content = Get-Content -Raw -Path $f.FullName -ErrorAction SilentlyContinue
    if (-not $content) { continue }
    $lines = $content -split "`n"
    $modified = $false
    $edits = @()

    for ($idx = 0; $idx -lt $lines.Length; $idx++) {
        $line = $lines[$idx]
        if ($line -match '^[\s]*param\s*\(') {
            # parse param block
            $pb = Parse-ParamBlock -lines $lines -startIndex $idx
            if (-not $pb) { continue }
            $blockLines = $pb.Block
            $endIdx = $pb.EndIndex
            $blockText = ($blockLines -join "`n")
            if ($blockText -notmatch '\[Parameter\(Mandatory\)') { continue }

            # find parameter names that are Mandatory
            $paramPattern = '\[Parameter\(Mandatory\)\]\s*\[[^\]]+\]\s*\$([A-Za-z0-9_]+)'
            $names = @()
            foreach ($m in [regex]::Matches($blockText, $paramPattern)) { $names += $m.Groups[1].Value }
            # fallback pattern (type omitted)
            if ($names.Count -eq 0) {
                foreach ($m in [regex]::Matches($blockText, '\[Parameter\(Mandatory\)\]\s*\$([A-Za-z0-9_]+)')) { $names += $m.Groups[1].Value }
            }
            if ($names.Count -eq 0) { continue }

            # ensure NonInteractive switch exists in param block
            if ($blockText -notmatch '\$NonInteractive') {
                # add ', [switch]$NonInteractive' before the final closing paren
                $insertLine = "    [switch]`$NonInteractive"
                # if block has multiple param lines, append before last non-empty line inside block
                # build new block
                $newBlockLines = @()
                $inserted = $false
                for ($k = 0; $k -lt $blockLines.Length; $k++) {
                    $ln = $blockLines[$k]
                    if (-not $inserted -and $k -eq ($blockLines.Length -1)) {
                        # before the final ')'
                        # find last index of ')' within line; we will insert a line before this closing
                        $newBlockLines += $insertLine
                        $inserted = $true
                    }
                    $newBlockLines += $ln
                }
                $blockLines = $newBlockLines
            }

            # prepare inserted lines after param block
            $inserts = @()
            $inserts += '. "$PSScriptRoot\\Tools\\NonInteractive.ps1"'
            $inserts += 'Set-NonInteractive -Enable:$NonInteractive'
            foreach ($n in $names) { $inserts += ("Require-Parameter '{0}' ${1}" -f $n, $n) }

            # check if these lines already present
            $already = $false
            $checkRangeStart = $endIdx + 1
            $checkRangeEnd = [math]::Min($endIdx + 6, $lines.Length -1)
            $rangeText = ($lines[$checkRangeStart..$checkRangeEnd] -join "`n") -as [string]
            foreach ($ins in $inserts) { if ($rangeText -match [regex]::Escape($ins)) { $already = $true } }
            if ($already) { continue }

            # perform in-memory edits
            $before = $lines[0..($idx-1)]
            $after = $lines[($endIdx+1)..($lines.Length-1)]
            $newSection = $blockLines + $inserts
            $lines = $before + $newSection + $after
            $modified = $true
            # move index forward
            $idx = $idx + $newSection.Length - 1
        }
    }

    if ($modified) {
        $newContent = ($lines -join "`n").Replace("`r`n","`n")
        if ($WhatIf) {
            Write-Output "[WHATIF] Would modify: $($f.FullName) - would insert non-interactive safe lines for mandatory params"
        } elseif ($Apply) {
            Set-Content -Path $f.FullName -Value $newContent -Encoding UTF8
            Write-Output "[APPLIED] Modified: $($f.FullName)"
        } else {
            Write-Output "[DRYRUN] Detected: $($f.FullName) - run with -Apply to apply changes"
        }
    }
}

Write-Output "Done."
