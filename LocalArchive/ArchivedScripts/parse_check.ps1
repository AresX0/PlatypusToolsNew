$errors = [ref]$null
$scriptText = Get-Content 'C:\Projects\Platypustools\PlatypusTools.ps1' -Raw
[System.Management.Automation.Language.Parser]::ParseInput($scriptText, [ref]$null, $errors)
if ($errors.Value -and $errors.Value.Count -gt 0) {
    $errors.Value | Format-List -Force
    exit 1
} else {
    Write-Host 'No parse errors'
}