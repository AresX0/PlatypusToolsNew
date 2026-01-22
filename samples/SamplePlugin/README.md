# PlatypusTools Sample Plugin

This folder contains a sample plugin that demonstrates how to create plugins for PlatypusTools.

## Building the Plugin

1. Open a terminal in this directory
2. Run: `dotnet build -c Release`
3. The compiled `SamplePlugin.dll` will be in `bin/Release/`

## Installing the Plugin

1. Copy `SamplePlugin.dll` to: `%LOCALAPPDATA%\PlatypusTools\Plugins\`
2. Restart PlatypusTools or use Tools → Plugin Manager → Reload All

## Plugin Types Included

### HelloWorldPlugin (IMenuPlugin)
Demonstrates how to add menu items to the Plugins menu:
- **Say Hello** - Shows a greeting message box
- **About Sample Plugin** - Shows plugin information

### TextFileProcessorPlugin (IFileProcessorPlugin)
Demonstrates how to process specific file types:
- Counts words, lines, and characters in text files
- Supports `.txt`, `.md`, and `.log` files

## Creating Your Own Plugin

1. Create a new .NET class library project targeting the same .NET version as PlatypusTools
2. Reference `PlatypusTools.UI.dll` (private=false to avoid including it in output)
3. Create a class that inherits from `PluginBase` and implements one or more interfaces:
   - `IPlugin` - Base plugin interface (already implemented by PluginBase)
   - `IMenuPlugin` - Add menu items
   - `IFileProcessorPlugin` - Process specific file types

### Required Properties

```csharp
public override string Id => "com.yourcompany.yourplugin"; // Unique identifier
public override string Name => "My Plugin";                 // Display name
public override string Description => "What it does";       // Description
public override string Version => "1.0.0";                  // Version string
public override string Author => "Your Name";               // Author name
```

### Lifecycle Methods

```csharp
public override void Initialize()
{
    // Called when plugin is loaded or enabled
}

public override void Shutdown()
{
    // Called when plugin is unloaded or disabled
}
```

## Plugin Sandboxing

Plugins are loaded in isolated `AssemblyLoadContext` instances for security:
- Each plugin has its own memory space
- Plugins cannot directly access host internals
- Crashing plugins won't crash the host application
- Plugins can be unloaded completely when disabled

## Best Practices

1. **Handle exceptions** - Always wrap your code in try-catch blocks
2. **Use logging** - Use `LoggingService.Instance` for debug output
3. **Cleanup resources** - Release resources in `Shutdown()`
4. **Keep it lightweight** - Plugins should be focused and efficient
5. **Version your plugins** - Update version numbers for compatibility tracking
