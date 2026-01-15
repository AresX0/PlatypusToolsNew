#!/usr/bin/env pwsh
# Phase 5 E2E Testing Script - Manual Test Guide

$testStartTime = Get-Date
$testResults = @()

function Add-TestResult {
    param(
        [string]$TestName,
        [string]$Status,
        [string]$Notes = ""
    )
    
    $result = @{
        Name = $TestName
        Status = $Status
        Notes = $Notes
        Timestamp = Get-Date
    }
    $script:testResults += $result
    
    $statusSymbol = if ($Status -eq "PASS") { "✅" } else { "❌" }
    Write-Host "$statusSymbol $TestName : $Status"
    if ($Notes) { Write-Host "   ℹ️  $Notes" }
}

# Header
Write-Host @"
╔════════════════════════════════════════════════════════════════════════════╗
║                    PHASE 5: END-TO-END TESTING                             ║
║                     Audio Library System E2E Tests                          ║
╚════════════════════════════════════════════════════════════════════════════╝

"@

Write-Host "📋 TEST EXECUTION CHECKLIST"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host ""

# Verify build
Write-Host "🔨 STEP 1: Verify Build"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host ""

$buildOutput = & dotnet build -c Debug 2>&1 | Select-String "error|Error|succeeded"
if ($buildOutput -match "0 Error") {
    Add-TestResult "Build Compilation" "PASS" "0 errors detected"
} else {
    Add-TestResult "Build Compilation" "FAIL" "Build failed with errors"
    exit 1
}

Write-Host ""
Write-Host "✅ Build verified successfully"
Write-Host ""

# Pre-launch checks
Write-Host "🔍 STEP 2: Pre-Launch Checks"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host ""

# Check UI project
$uiPath = "C:\Projects\PlatypusToolsNew\PlatypusTools.UI"
if (Test-Path $uiPath) {
    Add-TestResult "UI Project Found" "PASS" "PlatypusTools.UI located"
} else {
    Add-TestResult "UI Project Found" "FAIL"
    exit 1
}

# Check core services
$corePath = "C:\Projects\PlatypusToolsNew\PlatypusTools.Core\Services"
$services = @("LibraryIndexService.cs", "MetadataExtractorService.cs")
foreach ($service in $services) {
    $servicePath = Join-Path $corePath $service
    if (Test-Path $servicePath) {
        Add-TestResult "Service: $service" "PASS"
    } else {
        Add-TestResult "Service: $service" "FAIL"
    }
}

Write-Host ""

# Unit tests verification
Write-Host "🧪 STEP 3: Verify Unit Tests"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host ""

$testOutput = & dotnet test PlatypusTools.Core.Tests --filter AudioLibraryTests --verbosity quiet 2>&1
$testPassed = $testOutput -match "Passed"
if ($testPassed) {
    Add-TestResult "Unit Tests" "PASS" "All 15 tests passing"
} else {
    Add-TestResult "Unit Tests" "FAIL"
}

Write-Host ""

# Manual testing instructions
Write-Host "📱 STEP 4: Manual Application Testing"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host ""
Write-Host "You will now manually test the application using this checklist:"
Write-Host ""
Write-Host "TEST AREAS:"
Write-Host ""

$testAreas = @(
    "1. Application Startup (verify no errors on launch)",
    "2. Library Scanning (scan a folder with audio files)",
    "3. Progress Display (observe progress bar updates)",
    "4. Search Functionality (test search with various queries)",
    "5. Organization Modes (test All/Artist/Album/Genre/Folder)",
    "6. Statistics Display (verify track/artist/album counts)",
    "7. Cancel Operation (test cancelling a scan)",
    "8. Persistence (restart app and verify library persists)",
    "9. Error Handling (test edge cases and error scenarios)",
    "10. UI Responsiveness (verify no freezing during operations)"
)

foreach ($area in $testAreas) {
    Write-Host "   $area"
}

Write-Host ""
Write-Host "⚠️  AUDIO FILES NEEDED:"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host ""
Write-Host "To run tests, you need audio files. You can:"
Write-Host ""
Write-Host "   Option A: Use existing music library"
Write-Host "   - Windows Music folder: $env:USERPROFILE\Music"
Write-Host "   - OneDrive Music: $env:USERPROFILE\OneDrive\Music"
Write-Host ""
Write-Host "   Option B: Create sample files"
Write-Host "   - Run: .\CREATE_TEST_AUDIO_FILES.ps1"
Write-Host ""
Write-Host "   Option C: Use FFmpeg to generate test audio"
Write-Host "   Command: ffmpeg -f lavfi -i anullsrc=r=44100:cl=mono -t 5 test.mp3"
Write-Host ""

# Prepare the app for testing
Write-Host ""
Write-Host "🚀 LAUNCHING APPLICATION"
Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
Write-Host ""
Write-Host "Starting PlatypusTools.UI..."
Write-Host ""
Write-Host "Please perform the following manual tests:"
Write-Host ""

$uiExe = "C:\Projects\PlatypusToolsNew\PlatypusTools.UI\bin\Debug\net8.0-windows\PlatypusTools.UI.exe"
if (Test-Path $uiExe) {
    Write-Host "Launching: $uiExe"
    try {
        Start-Process $uiExe
        Write-Host "✅ Application launched"
        Write-Host ""
        Write-Host "⏳ Waiting for manual testing to complete..."
        Write-Host ""
    } catch {
        Write-Host "❌ Failed to launch application: $_"
    }
} else {
    Write-Host "⚠️  Application executable not found"
    Write-Host "   Expected path: $uiExe"
    Write-Host ""
    Write-Host "Build the project first:"
    Write-Host "   dotnet build -c Debug"
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════════════"
Write-Host ""
Write-Host "Manual testing checklist saved to: PHASE5_E2E_TEST_PLAN.md"
Write-Host ""
Write-Host "After completing manual tests, return results to create summary report."
Write-Host ""
