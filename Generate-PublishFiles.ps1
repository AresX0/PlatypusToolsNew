# Generate PublishFiles.wxs from the publish folder
$publishDir = "C:\Projects\PlatypusToolsNew\PlatypusTools.UI\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish"
$outputFile = "C:\Projects\PlatypusToolsNew\PlatypusTools.Installer\PublishFiles.wxs"

function Get-SafeId {
    param([string]$name)
    # Replace invalid characters with underscores
    $safe = $name -replace '[^a-zA-Z0-9_.]', '_'
    # Ensure it starts with a letter
    if ($safe -match '^[0-9]') {
        $safe = "F_$safe"
    }
    return $safe
}

$componentRefs = @()
$directoriesNeeded = @{}
$components = @()

# Get all files recursively
$files = Get-ChildItem -Path $publishDir -Recurse -File

foreach ($file in $files) {
    $relativePath = $file.FullName.Substring($publishDir.Length + 1)
    $relativeDir = if ($file.DirectoryName -eq $publishDir) { "" } else { $file.DirectoryName.Substring($publishDir.Length + 1) }
    
    $safeFileName = Get-SafeId $file.Name
    $componentId = "CMP_$safeFileName"
    $fileId = "FIL_$safeFileName"
    
    # If there's a directory, we need to track it
    $dirRef = "INSTALLFOLDER"
    if ($relativeDir) {
        $dirId = "DIR_" + (Get-SafeId ($relativeDir -replace '\\', '_'))
        $dirRef = $dirId
        $directoriesNeeded[$relativeDir] = $dirId
    }
    
    $sourcePath = "`$(var.PlatypusTools.UI.TargetDir)publish\$relativePath"
    
    $components += [PSCustomObject]@{
        ComponentId = $componentId
        FileId = $fileId
        Source = $sourcePath
        DirectoryRef = $dirRef
        RelativeDir = $relativeDir
    }
    
    $componentRefs += $componentId
}

# Start building the WXS file
$xml = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <DirectoryRef Id="INSTALLFOLDER">
"@

# Group components by directory
$rootComponents = $components | Where-Object { -not $_.RelativeDir }
$subDirComponents = $components | Where-Object { $_.RelativeDir }

# Add root-level components
foreach ($comp in $rootComponents) {
    $xml += @"

      <Component Id="$($comp.ComponentId)" Guid="*" Directory="INSTALLFOLDER">
        <File Id="$($comp.FileId)" Source="$($comp.Source)" KeyPath="yes" />
      </Component>
"@
}

# Create directory structure and add components
$directories = $subDirComponents | Group-Object RelativeDir | Sort-Object Name

foreach ($dirGroup in $directories) {
    $dirPath = $dirGroup.Name
    $dirId = "DIR_" + (Get-SafeId ($dirPath -replace '\\', '_'))
    
    # Build nested directory structure
    $parts = $dirPath -split '\\'
    $currentPath = ""
    $parentId = "INSTALLFOLDER"
    
    foreach ($part in $parts) {
        $currentPath = if ($currentPath) { "$currentPath\$part" } else { $part }
        $currentDirId = "DIR_" + (Get-SafeId ($currentPath -replace '\\', '_'))
        
        if (-not $directoriesNeeded.ContainsKey($currentDirId + "_declared")) {
            $xml += @"

      <Directory Id="$currentDirId" Name="$part">
"@
            $directoriesNeeded[$currentDirId + "_declared"] = $true
        }
    }
    
    # Add components for this directory
    foreach ($comp in $dirGroup.Group) {
        $xml += @"

        <Component Id="$($comp.ComponentId)" Guid="*">
          <File Id="$($comp.FileId)" Source="$($comp.Source)" KeyPath="yes" />
        </Component>
"@
    }
    
    # Close directory tags
    foreach ($part in $parts) {
        $xml += @"

      </Directory>
"@
    }
}

$xml += @"

    </DirectoryRef>
  </Fragment>

  <Fragment>
    <ComponentGroup Id="PublishedComponents">
"@

foreach ($ref in $componentRefs) {
    $xml += @"

      <ComponentRef Id="$ref" />
"@
}

$xml += @"

    </ComponentGroup>
  </Fragment>
</Wix>
"@

$xml | Out-File -FilePath $outputFile -Encoding UTF8

Write-Host "Generated $outputFile with $($componentRefs.Count) components"
