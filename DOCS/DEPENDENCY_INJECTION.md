# Dependency Injection Architecture

**Last Updated:** February 11, 2026  
**Status:** Phase 6 Implementation Complete

---

## Overview

PlatypusTools uses dependency injection (DI) via `Microsoft.Extensions.DependencyInjection` for service management. This document describes the architecture and provides guidance for service usage and migration.

## Architecture

### ServiceContainer

The `ServiceContainer` class in `PlatypusTools.Core.Services` is the central DI hub:

```csharp
// Initialization (in App.xaml.cs)
ServiceContainer.Initialize(services =>
{
    // Register additional UI services
    services.AddSingleton<MyService>();
});

// Service resolution
var service = ServiceContainer.GetService<VideoConverterService>();
var required = ServiceContainer.GetRequiredService<PdfService>();
```

### Registered Services

#### Core Services (PlatypusTools.Core)
| Service | Lifetime | Description |
|---------|----------|-------------|
| `VideoConverterService` | Singleton | Video format conversion |
| `VideoCombinerService` | Singleton | Video concatenation |
| `UpscalerService` | Singleton | Video upscaling |
| `FileRenamerService` | Singleton | Batch file renaming |
| `DiskSpaceAnalyzerService` | Singleton | Disk usage analysis |
| `ProcessManagerService` | Singleton | Process management |
| `ScheduledTasksService` | Singleton | Task scheduler |
| `StartupManagerService` | Singleton | Startup items management |
| `SystemRestoreService` | Singleton | System restore points |
| `PdfService` | Singleton | PDF manipulation |
| `MetadataExtractorService` | Singleton | Media metadata extraction |
| `ImageSimilarityService` | Singleton | Duplicate image detection |
| `ImageResizerService` | Singleton | Image resizing |
| `BatchUpscaleService` | Singleton | Batch image upscaling |
| `ArchiveService` | Singleton | Archive operations |
| `DiskCleanupService` | Singleton | Disk cleanup |
| `PrivacyCleanerService` | Singleton | Privacy data cleanup |
| `ForensicsAnalyzerService` | Singleton | Security forensics |
| `CloudSyncService` | Singleton | Cloud synchronization |

#### UI Services (PlatypusTools.UI)
| Service | Lifetime | Description |
|---------|----------|-------------|
| `ThemeManager` | Singleton | Theme management |
| `KeyboardShortcutService` | Singleton | Keyboard shortcuts |
| `RecentWorkspacesService` | Singleton | Recent workspaces tracking |
| `UpdateService` | Singleton | Application updates |
| `PluginService` | Singleton | Plugin management |
| `LoggingService` | Singleton | Application logging |
| `ToastNotificationService` | Singleton | Toast notifications |
| `CommandService` | Singleton | Command routing |
| `EnhancedAudioPlayerService` | Singleton | Audio playback |
| `AudioStreamingService` | Singleton | Internet radio streaming |
| `TabVisibilityService` | Singleton | Tab visibility settings |

#### Forensics Services (PlatypusTools.UI.Services.Forensics)
| Service | Lifetime | Description |
|---------|----------|-------------|
| `IOCScannerService` | Singleton | Indicators of Compromise scanning |
| `YaraService` | Singleton | YARA rule matching |
| `PcapParserService` | Singleton | Network capture parsing |
| `BrowserForensicsService` | Singleton | Browser artifact analysis |
| `RegistryDiffService` | Singleton | Registry comparison |
| `TaskSchedulerService` | Singleton | Task scheduler forensics |

---

## Migration Guide

### From ServiceLocator (Legacy)

The `ServiceLocator` static class is being deprecated in favor of DI. Here's how to migrate:

#### Before (Legacy)
```csharp
public class MyViewModel
{
    private readonly VideoConverterService _converter;
    
    public MyViewModel()
    {
        _converter = ServiceLocator.VideoConverter;
    }
}
```

#### After (DI)
```csharp
public class MyViewModel
{
    private readonly VideoConverterService _converter;
    
    // Constructor injection (preferred)
    public MyViewModel(VideoConverterService converter)
    {
        _converter = converter;
    }
    
    // Or resolve from container (when DI not available)
    public MyViewModel()
    {
        _converter = ServiceContainer.GetRequiredService<VideoConverterService>();
    }
}
```

### From Static Instance Pattern

Services with `.Instance` static properties should migrate to DI:

#### Before (Static Instance)
```csharp
var player = EnhancedAudioPlayerService.Instance;
var theme = ThemeManager.Instance;
```

#### After (DI)
```csharp
var player = ServiceContainer.GetService<EnhancedAudioPlayerService>();
var theme = ServiceContainer.GetService<ThemeManager>();
```

---

## Service Interfaces

Service interfaces are defined in `PlatypusTools.Core.Services.Abstractions`:

- `IVideoConverterService`
- `IVideoCombinerService`
- `IUpscalerService`
- `IFileRenamerService`
- `IDiskSpaceAnalyzerService`
- `IProcessManagerService`
- `IPdfService`
- `IStartupManagerService`
- `ILoggingService`

These interfaces enable:
1. **Testability** - Mock services in unit tests
2. **Flexibility** - Swap implementations without changing consumers
3. **Loose Coupling** - Depend on abstractions, not implementations

---

## Best Practices

### 1. Prefer Constructor Injection
```csharp
public class MyViewModel
{
    private readonly IVideoConverterService _converter;
    
    public MyViewModel(IVideoConverterService converter)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
    }
}
```

### 2. Use Interface Types When Available
```csharp
// Good: depends on abstraction
public MyService(ILoggingService logger) { }

// Acceptable: concrete type when no interface exists
public MyService(ThemeManager themeManager) { }
```

### 3. Register Services Early
All services should be registered in `App.xaml.cs InitializeServiceContainer()` before the main window is created.

### 4. Don't Resolve in Constructors Unless Necessary
Avoid calling `ServiceContainer.GetService<T>()` in constructors. Prefer constructor injection.

### 5. Dispose Scoped Services
When using `ServiceContainer.CreateScope()`, ensure the scope is disposed:
```csharp
using var scope = ServiceContainer.CreateScope();
var service = scope.ServiceProvider.GetService<MyService>();
// Use service...
// Scope disposes automatically
```

---

## Static Services

Some services remain static due to their nature:
- `FFmpegService` - Static utility methods for FFmpeg
- `FFprobeService` - Static utility methods for FFprobe
- `SimpleLogger` - Global logging (lightweight, always available)

These services don't participate in DI and are accessed directly.

---

## Testing

To test code that uses DI:

```csharp
[TestInitialize]
public void Setup()
{
    ServiceContainer.Reset(); // Clear previous state
    ServiceContainer.Initialize(services =>
    {
        // Register mocks
        services.AddSingleton<ILoggingService>(new MockLoggingService());
    });
}

[TestCleanup]
public void Cleanup()
{
    ServiceContainer.Reset();
}
```

---

## Future Improvements

1. **Complete Interface Coverage** - Add interfaces for all major services
2. **ViewLocator Integration** - Resolve ViewModels via DI
3. **Scoped Services** - Add request-scoped services for operation contexts
4. **Keyed Services** - Support multiple implementations of same interface
