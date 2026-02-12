# PlatypusTools Plugin Developer Guide

## Overview

PlatypusTools supports a plugin architecture that allows developers to extend the application with custom tools, services, and UI views. This guide covers the plugin system architecture, how to create a plugin, and best practices.

## Plugin Architecture

### Plugin Discovery

Plugins are .NET assemblies placed in the `plugins/` directory relative to the application executable. On startup, PlatypusTools scans this directory for assemblies containing plugin entry points.

```
PlatypusTools.UI.exe
plugins/
  MyCustomPlugin/
    MyCustomPlugin.dll
    MyCustomPlugin.deps.json
```

### Plugin Interface

All plugins must implement the `IPlugin` interface from `PlatypusTools.Core`:

```csharp
namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Interface for PlatypusTools plugins.
    /// </summary>
    public interface IPlugin
    {
        /// <summary>Plugin display name.</summary>
        string Name { get; }
        
        /// <summary>Plugin version string.</summary>
        string Version { get; }
        
        /// <summary>Brief description of plugin functionality.</summary>
        string Description { get; }
        
        /// <summary>Plugin author name.</summary>
        string Author { get; }
        
        /// <summary>Called when the plugin is loaded.</summary>
        void Initialize();
        
        /// <summary>Called when the plugin is unloaded.</summary>
        void Shutdown();
    }
}
```

### Plugin Loading

The `PluginLoaderService` handles discovery, loading, and lifecycle management:

```csharp
var loader = PluginLoaderService.Instance;
loader.LoadPlugins("plugins/");

foreach (var plugin in loader.LoadedPlugins)
{
    Console.WriteLine($"Loaded: {plugin.Name} v{plugin.Version}");
}
```

## Creating a Plugin

### Step 1: Create a Class Library Project

```bash
dotnet new classlib -n MyPlatypusPlugin -f net10.0
cd MyPlatypusPlugin
dotnet add reference ../PlatypusTools.Core/PlatypusTools.Core.csproj
```

### Step 2: Implement the Plugin Interface

```csharp
using PlatypusTools.Core.Services;

namespace MyPlatypusPlugin
{
    public class MyPlugin : IPlugin
    {
        public string Name => "My Custom Plugin";
        public string Version => "1.0.0";
        public string Description => "A sample PlatypusTools plugin";
        public string Author => "Your Name";

        public void Initialize()
        {
            // Register services, add menu items, etc.
            Console.WriteLine($"{Name} initialized!");
        }

        public void Shutdown()
        {
            // Cleanup resources
            Console.WriteLine($"{Name} shutting down.");
        }
    }
}
```

### Step 3: Build and Deploy

```bash
dotnet publish -c Release -o ../PlatypusTools.UI/bin/Release/plugins/MyPlatypusPlugin
```

Place the output DLL and dependencies in the `plugins/` subdirectory.

## Available Services

Plugins can access the following Core services:

### DFIR & Forensics

| Service | Description |
|---------|-------------|
| `ForensicsAnalyzerService` | Automated forensic analysis (memory, filesystem, registry, event logs) |
| `PcapParserService` | Parse PCAP/PCAPNG network capture files |
| `IOCScannerService` | Scan files for Indicators of Compromise |
| `YaraService` | YARA rule-based malware detection |

### Media & Files

| Service | Description |
|---------|-------------|
| `MetadataExtractorService` | Extract audio file metadata |
| `LibraryIndexService` | Audio library indexing and search |
| `ImageSimilarityService` | Find visually similar images using perceptual hashing |
| `BatchUpscaleService` | Batch image upscaling with queue management |
| `PdfService` | PDF manipulation (merge, split, watermark, encrypt) |
| `ArchiveService` | Create and extract ZIP/GZ/TAR archives |
| `MetadataTemplateService` | Metadata template management and batch application |

### Video Editing

| Service | Description |
|---------|-------------|
| `SimpleVideoExporter` | FFmpeg-based video export with filter pipeline |

## Service Access Patterns

Most services use a singleton pattern:

```csharp
// Singleton services
var upscaler = BatchUpscaleService.Instance;
var pcap = PcapParserService.Instance;
var templates = MetadataTemplateService.Instance;

// Instantiated services
var forensics = new ForensicsAnalyzerService();
var similarity = new ImageSimilarityService();
var pdf = new PdfService();
var archive = new ArchiveService();
```

## Event-Driven Progress Reporting

Many services provide event-based progress reporting:

```csharp
var upscaler = BatchUpscaleService.Instance;
upscaler.OverallProgressChanged += (s, progress) =>
{
    Console.WriteLine($"Progress: {progress:P0}");
};
upscaler.JobCompleted += (s, job) =>
{
    Console.WriteLine($"Job completed: {job.Name}");
};
```

## Best Practices

1. **Don't block the UI thread** — Use `async/await` for all long-running operations
2. **Handle cancellation** — Accept and honor `CancellationToken` parameters
3. **Report progress** — Subscribe to and raise progress events for user feedback
4. **Clean up resources** — Implement `IDisposable` when managing unmanaged resources
5. **Use temp directories** — Use `Path.GetTempPath()` for intermediate files
6. **Log errors** — Catch exceptions gracefully and provide meaningful error messages
7. **Target .NET 10** — Ensure your plugin targets the same framework version

## Debugging Plugins

1. Set your plugin project's debug launch to the PlatypusTools.UI executable
2. Place your built DLL in the plugins directory
3. Set breakpoints in your plugin code
4. Launch PlatypusTools from your plugin project

## Version Compatibility

| PlatypusTools Version | Plugin API Version | Notes |
|----------------------|-------------------|-------|
| 3.4.x | 1.0 | Current stable API |

Plugin API stability is maintained within major versions. Breaking changes will only occur in major version bumps.
