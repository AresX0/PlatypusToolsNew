# Helper for non-interactive script execution
# Provides functions to make scripts fail fast instead of prompting.

function Set-NonInteractive {
    param([switch]$Enable)
    if ($Enable) {
        $global:NonInteractive = $true
        try { $ConfirmPreference = 'None' } catch {}
    }
}

function Require-Parameter {
    param([string]$Name, $Value)
    if ($null -eq $Value -or ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value))) {
        if ($global:NonInteractive) {
            throw "Missing required parameter: -$Name (non-interactive mode)."
        } else {
            Write-Error "Missing required parameter: -$Name"
            exit 1
        }
    }
}

function Prompt-Or-Throw {
    param([string]$Prompt)
    if ($global:NonInteractive) {
        throw "Interactive input required: $Prompt (running in non-interactive mode)."
    }
    return Read-Host $Prompt
}

# Export-ModuleMember removed so script can be dot-sourced in script contexts
