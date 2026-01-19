param(
    [Parameter(Mandatory=$false)]
    [string]$Configuration = "Release"
)

# Try multiple possible paths for .NET 10 Windows target frameworks
$possiblePaths = @(
    "..\PlatypusTools.UI\bin\$Configuration\net10.0-windows10.0.19041.0\win-x64\publish",
    "..\PlatypusTools.UI\bin\$Configuration\net10.0-windows\win-x64\publish"
)

$publishRoot = $null
foreach ($path in $possiblePaths) {
    $fullPath = Join-Path $PSScriptRoot $path
    if (Test-Path $fullPath) {
        $publishRoot = Resolve-Path $fullPath
        break
    }
}

if (-not $publishRoot) {
    throw "Publish directory not found. Tried: $($possiblePaths -join ', ')"
}
if (-not (Test-Path $publishRoot)) {
    throw "Publish directory not found: $publishRoot"
}

$allDirectories = Get-ChildItem -Path $publishRoot -Recurse -Directory
$allFiles = Get-ChildItem -Path $publishRoot -Recurse -File | Where-Object { $_.Extension -ne '.pdb' }

$tree = @{}

function Get-DirId {
    param([string]$RelativePath)
    if ([string]::IsNullOrEmpty($RelativePath)) { return 'INSTALLFOLDER' }
    $clean = ($RelativePath -replace '[^A-Za-z0-9_]', '_')
    if ([string]::IsNullOrEmpty($clean)) { $clean = 'Dir' }
    if ($clean.Length -gt 48) {
        $hash = [System.BitConverter]::ToString([System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($RelativePath))).Replace('-', '')
        $clean = $clean.Substring(0, 32) + '_' + $hash.Substring(0, 12)
    }
    return "DIR_$clean"
}

function Ensure-Node {
    param([string]$RelativePath)
    if ($tree.ContainsKey($RelativePath)) { return }
    $tree[$RelativePath] = [ordered]@{
        Id       = (Get-DirId -RelativePath $RelativePath)
        Files    = New-Object System.Collections.Generic.List[string]
        Children = New-Object System.Collections.Generic.HashSet[string]
    }
    $parent = [System.IO.Path]::GetDirectoryName($RelativePath)
    if ($parent -eq $null) { $parent = '' }
    if ($RelativePath -ne '') {
        Ensure-Node -RelativePath $parent
        $tree[$parent].Children.Add($RelativePath) | Out-Null
    }
}

Ensure-Node -RelativePath ''
foreach ($dir in $allDirectories) {
    $relative = $dir.FullName.Substring($publishRoot.Path.Length).TrimStart('\\')
    Ensure-Node -RelativePath $relative
}
foreach ($file in $allFiles) {
    $relativePath = $file.FullName.Substring($publishRoot.Path.Length).TrimStart('\\')
    $relativeDir = [System.IO.Path]::GetDirectoryName($relativePath)
    if ($relativeDir -eq $null) { $relativeDir = '' }
    Ensure-Node -RelativePath $relativeDir
    $tree[$relativeDir].Files.Add($relativePath)
}

function New-SafeId {
    param(
        [string]$Prefix,
        [string]$RelativePath
    )
    $clean = ($RelativePath -replace '[^A-Za-z0-9_]', '_')
    if ([string]::IsNullOrEmpty($clean)) { $clean = 'Item' }
    if ($clean.Length -gt 48) {
        $hash = [System.BitConverter]::ToString([System.Security.Cryptography.SHA1]::Create().ComputeHash([System.Text.Encoding]::UTF8.GetBytes($RelativePath))).Replace('-', '')
        $clean = $clean.Substring(0, 32) + '_' + $hash.Substring(0, 12)
    }
    return "$Prefix$clean"
}

$componentIds = New-Object System.Collections.Generic.List[string]
$sb = New-Object System.Text.StringBuilder
$null = $sb.AppendLine('<?xml version="1.0" encoding="UTF-8"?>')
$null = $sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')
$null = $sb.AppendLine('  <Fragment>')
$null = $sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

function Write-Directory {
    param(
        [string]$RelativePath,
        [int]$Depth
    )
    $node = $tree[$RelativePath]
    $indent = '  ' * ($Depth + 3)

    foreach ($fileRel in $node.Files) {
        $componentId = New-SafeId -Prefix 'CMP_' -RelativePath $fileRel
        $fileId = New-SafeId -Prefix 'FIL_' -RelativePath $fileRel
        $componentIds.Add($componentId) | Out-Null
        $sourcePath = '$(var.PlatypusTools.UI.TargetDir)publish\' + $fileRel
        $null = $sb.AppendLine(([string]::Format('{0}<Component Id="{1}" Guid="*" Directory="{2}">', $indent, $componentId, $node.Id)))
        $null = $sb.AppendLine(([string]::Format('{0}  <File Id="{1}" Source="{2}" KeyPath="yes" />', $indent, $fileId, $sourcePath)))
        $null = $sb.AppendLine(([string]::Format('{0}</Component>', $indent)))
    }

    foreach ($childRel in $node.Children) {
        $childNode = $tree[$childRel]
        $name = [System.IO.Path]::GetFileName($childRel)
        if ([string]::IsNullOrEmpty($childRel)) { continue }
        $dirIndent = '  ' * ($Depth + 3)
        $null = $sb.AppendLine(([string]::Format('{0}<Directory Id="{1}" Name="{2}">', $dirIndent, $childNode.Id, $name)))
        Write-Directory -RelativePath $childRel -Depth ($Depth + 1)
        $null = $sb.AppendLine(([string]::Format('{0}</Directory>', $dirIndent)))
    }
}

Write-Directory -RelativePath '' -Depth 0
$null = $sb.AppendLine('    </DirectoryRef>')
$null = $sb.AppendLine('  </Fragment>')
$null = $sb.AppendLine('  <Fragment>')
$null = $sb.AppendLine('    <ComponentGroup Id="PublishFiles">')
foreach ($id in $componentIds) {
    $null = $sb.AppendLine(([string]::Format('      <ComponentRef Id="{0}" />', $id)))
}
$null = $sb.AppendLine('    </ComponentGroup>')
$null = $sb.AppendLine('  </Fragment>')
$null = $sb.AppendLine('</Wix>')

$outPath = Join-Path $PSScriptRoot 'PublishFiles.wxs'
$sb.ToString() | Set-Content -Path $outPath -Encoding UTF8
Write-Host "Generated PublishFiles.wxs with $($componentIds.Count) components."
