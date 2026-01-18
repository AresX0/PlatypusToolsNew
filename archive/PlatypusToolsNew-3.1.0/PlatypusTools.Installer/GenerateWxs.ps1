$publishDir = "C:\Projects\Platypustools\PlatypusTools.Net\PlatypusTools.Net\PlatypusTools.UI\bin\Release\net10.0-windows\win-x64\publish"
$outputFile = "C:\Projects\Platypustools\PlatypusTools.Net\PlatypusTools.Net\PlatypusTools.Installer\PublishFiles.wxs"

$header = @"
<?xml version="1.0" encoding="UTF-8"?>
<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
  <Fragment>
    <ComponentGroup Id="PublishFiles" Directory="INSTALLFOLDER">
"@

$footer = @"
    </ComponentGroup>
  </Fragment>
</Wix>
"@

$fileId = 0
$components = New-Object System.Collections.ArrayList

# Process root directory files
$rootFiles = Get-ChildItem -Path $publishDir -File
foreach ($file in $rootFiles) {
    $fileId++
    $fullPath = $file.FullName
    $comp = @"
      <Component Id="cmp_$fileId" Guid="*">
        <File Id="file_$fileId" Source="$fullPath" KeyPath="yes" />
      </Component>
"@
    [void]$components.Add($comp)
}

# Process subdirectories
$dirs = Get-ChildItem -Path $publishDir -Directory
foreach ($dir in $dirs) {
    $subFiles = Get-ChildItem -Path $dir.FullName -File -Recurse
    foreach ($subFile in $subFiles) {
        $fileId++
        $relativePath = $subFile.FullName.Substring($publishDir.Length + 1)
        $dirPath = Split-Path $relativePath
        $fullPath = $subFile.FullName
        $comp = @"
      <Component Id="cmp_$fileId" Guid="*" Subdirectory="$dirPath">
        <File Id="file_$fileId" Source="$fullPath" KeyPath="yes" />
      </Component>
"@
        [void]$components.Add($comp)
    }
}

$content = $header + "`n" + ($components -join "`n") + "`n" + $footer
Set-Content -Path $outputFile -Value $content -Encoding UTF8

Write-Host "Generated $($components.Count) components to $outputFile"
