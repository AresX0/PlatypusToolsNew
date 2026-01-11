# Bootable USB Creator - Elevation Implementation Guide

## Overview

The Bootable USB Creator feature requires administrator privileges to perform low-level disk operations. This document explains how UAC (User Account Control) elevation is implemented in PlatypusTools.NET.

## Architecture

### Components

1. **ElevationHelper** (`PlatypusTools.Core/Utilities/ElevationHelper.cs`)
   - Utility class for checking and requesting administrator privileges
   - No dependencies on UI or specific features - can be reused for any elevation needs

2. **BootableUSBService** (`PlatypusTools.Core/Services/BootableUSBService.cs`)
   - Implements IBootableUSBService interface
   - Uses ElevationHelper to check privileges before operations
   - Provides RequestElevation() method for ViewModels

3. **BootableUSBViewModel** (`PlatypusTools.UI/ViewModels/BootableUSBViewModel.cs`)
   - Checks elevation status on initialization
   - Displays warning UI when not elevated
   - Provides RequestElevationCommand for users

4. **BootableUSBView** (`PlatypusTools.UI/Views/BootableUSBView.xaml`)
   - Shows elevation warning banner when `IsElevated = false`
   - Disables Create Bootable USB button when not elevated
   - Provides "Request Elevation" button

## How Elevation Works

### 1. Checking if Running as Administrator

```csharp
public static bool IsElevated()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}
```

This checks if the current process has administrator privileges.

### 2. Requesting Elevation

```csharp
public static bool RestartAsAdmin()
{
    try
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty,
            UseShellExecute = true,
            Verb = "runas"  // This triggers the UAC prompt
        };
        
        Process.Start(startInfo);
        return true;
    }
    catch (Exception ex)
    {
        Debug.WriteLine($"Failed to restart as admin: {ex.Message}");
        return false;
    }
}
```

**Key Points:**
- `Verb = "runas"` triggers Windows UAC prompt
- `UseShellExecute = true` is required for elevation
- Returns true if UAC prompt succeeded, false if user cancelled or error occurred
- The calling application should shutdown after successful elevation

### 3. Running External Commands with Elevation

```csharp
public static bool RunElevated(string fileName, string arguments, bool waitForExit = false)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = fileName,
        Arguments = arguments,
        UseShellExecute = true,
        Verb = "runas",
        CreateNoWindow = true
    };
    
    var process = Process.Start(startInfo);
    if (process != null && waitForExit)
    {
        process.WaitForExit();
        return process.ExitCode == 0;
    }
    return process != null;
}
```

This allows running external tools (like `bootsect.exe`, `diskpart.exe`) with elevation.

## User Experience Flow

### Initial State (Not Elevated)

1. User opens Bootable USB Creator tab
2. ViewModel checks `ElevationHelper.IsElevated()`
3. If false, `IsElevated` property is set to false
4. View displays warning banner: "⚠️ Administrator Privileges Required"
5. "Create Bootable USB" button is disabled

### Requesting Elevation

1. User clicks "Request Elevation" button
2. `RequestElevationCommand` executes
3. Calls `ElevationHelper.RestartAsAdmin()`
4. Windows UAC prompt appears: "Do you want to allow this app to make changes to your device?"
5. User clicks "Yes"
6. Application restarts with administrator privileges
7. Current instance calls `Application.Current.Shutdown()`

### Elevated State

1. Application restarts with admin privileges
2. `ElevationHelper.IsElevated()` now returns true
3. Warning banner is hidden (via `InverseBooleanToVisibilityConverter`)
4. All features are enabled
5. User can create bootable USB drives

## Security Considerations

### Why Elevation is Required

- **Disk Formatting**: `Format-Volume` PowerShell cmdlet requires admin rights
- **Drive Access**: Direct disk access for bootloader installation
- **ISO Mounting**: `Mount-DiskImage` requires elevation
- **File System Changes**: Writing boot sectors and partition tables

### Best Practices Implemented

1. **Granular Elevation**: Only the operations that need elevation require it
2. **Clear User Communication**: Warning banner explains why elevation is needed
3. **User Consent**: User explicitly requests elevation via button click
4. **No Silent Elevation**: Never requests elevation without user interaction
5. **Graceful Degradation**: App runs fine without elevation, just disables bootable USB creation

## PowerShell Integration

The service uses PowerShell commands that require elevation:

### Format Drive
```powershell
Format-Volume -DriveLetter X -FileSystem NTFS -NewFileSystemLabel "BOOTABLE" -Force
```

### Mount ISO
```powershell
$mount = Mount-DiskImage -ImagePath "C:\path\to\file.iso" -PassThru
$driveLetter = ($mount | Get-Volume).DriveLetter
```

### Unmount ISO
```powershell
Dismount-DiskImage -ImagePath "C:\path\to\file.iso"
```

## Testing Elevation

### Testing Without Elevation

1. Run Visual Studio (or the app) as a normal user
2. Navigate to Bootable USB Creator
3. Verify warning banner appears
4. Verify "Create Bootable USB" button is disabled
5. Click "Request Elevation" and cancel UAC prompt
6. Verify error message appears

### Testing With Elevation

1. Run Visual Studio as Administrator (right-click → Run as Administrator)
2. Run the application
3. Navigate to Bootable USB Creator
4. Verify NO warning banner appears
5. Verify "Create Bootable USB" button is enabled
6. Test USB creation (with test ISO and USB drive)

## Troubleshooting

### "Access Denied" Errors

**Symptom**: Operations fail with UnauthorizedAccessException  
**Cause**: Application not running with administrator privileges  
**Solution**: Click "Request Elevation" button or restart app as administrator

### UAC Prompt Doesn't Appear

**Symptom**: Clicking "Request Elevation" does nothing  
**Cause**: UAC disabled in Windows settings or application already elevated  
**Solution**: Check Windows UAC settings or verify app isn't already running as admin

### App Doesn't Restart After Elevation

**Symptom**: UAC prompt succeeds but app doesn't restart  
**Cause**: Process.GetCurrentProcess().MainModule?.FileName is null  
**Solution**: Check application deployment method and executable location

## Alternative Approaches Considered

### 1. App Manifest with requireAdministrator
**Pros**: App always runs elevated  
**Cons**: Violates principle of least privilege, requires elevation for all features  
**Decision**: Rejected - only USB creator needs elevation

### 2. External Helper Process
**Pros**: Main app doesn't need elevation  
**Cons**: Complex inter-process communication, harder to debug  
**Decision**: Rejected - restart-with-elevation is simpler and Windows-standard

### 3. Task Scheduler Elevated Task
**Pros**: Can run elevated without UAC prompt  
**Cons**: Complex setup, security implications  
**Decision**: Rejected - UAC prompt is important security boundary

## Code Example: Complete Flow

```csharp
// In BootableUSBViewModel.cs

// 1. Check elevation on load
public BootableUSBViewModel(IBootableUSBService service)
{
    _service = service;
    IsElevated = _service.IsElevated();
    
    if (!IsElevated)
    {
        StatusMessage = "⚠️ Administrator privileges required. Click 'Request Elevation' to continue.";
    }
}

// 2. Request elevation command
private void RequestElevation()
{
    try
    {
        if (_service.RequestElevation())
        {
            // The application will restart elevated, so close this instance
            System.Windows.Application.Current.Shutdown();
        }
        else
        {
            // User cancelled UAC or error occurred
            System.Windows.MessageBox.Show(
                "Failed to elevate privileges. Please run the application as Administrator.",
                "Elevation Failed",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
    catch (Exception ex)
    {
        System.Windows.MessageBox.Show(
            $"Error requesting elevation:\n\n{ex.Message}",
            "Error",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
    }
}

// 3. Create bootable USB (requires elevation)
private async Task CreateBootableUSBAsync()
{
    if (!IsElevated)
    {
        MessageBox.Show("Administrator privileges required...");
        return;
    }
    
    // Proceed with USB creation...
}
```

## References

- [Microsoft Docs: UAC and ProcessStartInfo](https://docs.microsoft.com/en-us/dotnet/api/system.diagnostics.processstartinfo.verb)
- [Windows Security: User Account Control](https://docs.microsoft.com/en-us/windows/security/identity-protection/user-account-control/how-user-account-control-works)
- [Best Practices: Least Privilege](https://docs.microsoft.com/en-us/windows/security/threat-protection/security-policy-settings/user-account-control-admin-approval-mode-for-the-built-in-administrator-account)

## Summary

The elevation implementation follows Windows security best practices:
- ✅ Uses standard Windows UAC mechanism
- ✅ Only requests elevation when needed
- ✅ Provides clear user communication
- ✅ Allows graceful degradation
- ✅ Can be reused for other features needing elevation

This approach balances security, usability, and maintainability.
