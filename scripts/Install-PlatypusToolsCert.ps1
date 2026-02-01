<#
.SYNOPSIS
    Installs the PlatypusTools code signing certificate to the Trusted Root store.
    
.DESCRIPTION
    This script is designed to be deployed via Microsoft Intune to add the 
    PlatypusTools self-signed certificate to the Local Machine Trusted Root 
    Certification Authorities store. This allows signed PlatypusTools applications
    to be trusted on managed devices.
    
.NOTES
    Deployment: Microsoft Intune > Devices > Scripts > Add (Windows 10 and later)
    Run this script using: System context
    Enforce script signature check: No
    Run script in 64-bit PowerShell: Yes
    
    Certificate Thumbprint: B4FA73AB05DC5AE2D245D1C629D5EC59E4FC0D40
    
.EXAMPLE
    .\Install-PlatypusToolsCert.ps1
#>

$ErrorActionPreference = "Stop"

# Certificate details
$CertThumbprint = "B4FA73AB05DC5AE2D245D1C629D5EC59E4FC0D40"
$CertSubject = "CN=PlatypusTools, O=YourName"

# Base64-encoded certificate (exported from the original)
# This is the public certificate only - no private key
$CertBase64 = @"
MIIDJjCCAg6gAwIBAgIQF6v6WlW55LRAT85TRWS32jANBgkqhkiG9w0BAQsFADAr
MREwDwYDVQQKDAhZb3VyTmFtZTEWMBQGA1UEAwwNUGxhdHlwdXNUb29sczAeFw0y
NjAyMDEyMTQ4MjRaFw0zMTAyMDEyMTU4MjRaMCsxETAPBgNVBAoMCFlvdXJOYW1l
MRYwFAYDVQQDDA1QbGF0eXB1c1Rvb2xzMIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8A
MIIBCgKCAQEAumGc2C6J0YJO4sJ1Uh9Gv3MYizxJ0ilkpP3ty7V5fZcs34wyl/bB
xTTr4rxMDIM6N/doIG+FvX9OtOnZt5VU+nqoyv6yUDTR71nHQtgdb2DUQqkq8M2l
DjwauLwkXoEcDaPmQBhR5+3JT60jAbjHzf2GdoqdgPwo6g3mel+EmPX/BzW19iQe
YDjMAHTLrnoqXuWAY5HOeP4I2Z+oFx7pS2vddKu5WW0XhexEpS4IFefMdnVHqRC8
8PGM5pVmgvkVstsSTGvwVCz0J11YqeYjpSnc1A0e7y48KcAOJhxfzAaQ27J77X8n
knW+60Cna6DJsbllNyaick9wF+51xWof0QIDAQABo0YwRDAOBgNVHQ8BAf8EBAMB
4AwEwYDVR0lBAwwCgYIKwYBBQUHAwMwHQYDVR0OBBYEFLZ/HB9Z6qtiT+VCsZiT
jTdpKpYLMA0GCSqGSIb3DQEBCwUAA4IBAQBfPS2Ho1OJvCJ08NtBw50k0NDKVb4z
PccnCuVVSweMW67kUV+CDJ83FKiFCbHDITg0hU4ST1i1ySkYLPMvo5mkqM8HSIht
eB4wlLhtDVrIraUGvDorH/+NjmxkRY3IYMdyY7d1zZ3Py/A9qfVEG7xHEubdHdh7
i0QIPYPCMDFkO4CZF7ZOGv/Ec7PKMga7M0AuhXt4zHyWxY5qSMl/xH9KpaFBfKS+
OPigRqKgKp+xIPBf1w5hhNuDT91uJ2uCv80aFqcuqvGwTNjlksn7c1DhSSEKPTSM
oq8ZsOSyJmdVe/PLAWmM4+VqZaQPdmUPTMF2fE1rKufomiL1m9FNbw9A
"@

# Log function
function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logMessage = "[$timestamp] [$Level] $Message"
    Write-Output $logMessage
    
    # Also write to event log for Intune visibility
    try {
        if (-not [System.Diagnostics.EventLog]::SourceExists("PlatypusTools")) {
            New-EventLog -LogName Application -Source "PlatypusTools" -ErrorAction SilentlyContinue
        }
        $eventType = if ($Level -eq "ERROR") { "Error" } elseif ($Level -eq "WARNING") { "Warning" } else { "Information" }
        Write-EventLog -LogName Application -Source "PlatypusTools" -EventId 1000 -EntryType $eventType -Message $Message -ErrorAction SilentlyContinue
    } catch {
        # Ignore event log errors
    }
}

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Log "Script must run as Administrator" -Level "ERROR"
    exit 1
}

Write-Log "Starting PlatypusTools certificate installation"

# Check if certificate is already installed
$existingCert = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Thumbprint -eq $CertThumbprint }

if ($existingCert) {
    Write-Log "Certificate already installed in Trusted Root store (Thumbprint: $CertThumbprint)"
    Write-Log "No action needed"
    exit 0
}

Write-Log "Certificate not found in Trusted Root store, proceeding with installation"

try {
    # Create temporary file for certificate
    $tempCertPath = Join-Path $env:TEMP "PlatypusTools_Cert_$(Get-Random).cer"
    
    # Decode and save certificate
    $certBytes = [Convert]::FromBase64String($CertBase64)
    [System.IO.File]::WriteAllBytes($tempCertPath, $certBytes)
    
    Write-Log "Certificate saved to temporary file: $tempCertPath"
    
    # Import certificate to Trusted Root store
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
    $cert.Import($tempCertPath)
    
    # Verify thumbprint matches
    if ($cert.Thumbprint -ne $CertThumbprint) {
        Write-Log "Certificate thumbprint mismatch! Expected: $CertThumbprint, Got: $($cert.Thumbprint)" -Level "ERROR"
        Remove-Item $tempCertPath -Force -ErrorAction SilentlyContinue
        exit 1
    }
    
    # Open the Trusted Root store
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "LocalMachine")
    $store.Open("ReadWrite")
    
    # Add the certificate
    $store.Add($cert)
    $store.Close()
    
    Write-Log "Certificate successfully installed to Trusted Root store"
    Write-Log "Subject: $($cert.Subject)"
    Write-Log "Thumbprint: $($cert.Thumbprint)"
    Write-Log "Valid From: $($cert.NotBefore) To: $($cert.NotAfter)"
    
    # Cleanup
    Remove-Item $tempCertPath -Force -ErrorAction SilentlyContinue
    
    # Verify installation
    $verifyInstall = Get-ChildItem Cert:\LocalMachine\Root | Where-Object { $_.Thumbprint -eq $CertThumbprint }
    if ($verifyInstall) {
        Write-Log "Installation verified successfully"
        exit 0
    } else {
        Write-Log "Installation verification failed" -Level "ERROR"
        exit 1
    }
    
} catch {
    Write-Log "Error installing certificate: $_" -Level "ERROR"
    Remove-Item $tempCertPath -Force -ErrorAction SilentlyContinue
    exit 1
}
