using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Interface that all plugins must implement
    /// </summary>
    public interface IPlugin
    {
        string Id { get; }
        string Name { get; }
        string Description { get; }
        string Version { get; }
        string Author { get; }
        bool IsEnabled { get; set; }
        
        void Initialize();
        void Shutdown();
    }

    /// <summary>
    /// Interface for plugins that add menu items
    /// </summary>
    public interface IMenuPlugin : IPlugin
    {
        IEnumerable<PluginMenuItem> GetMenuItems();
    }

    /// <summary>
    /// Interface for plugins that add file processors
    /// </summary>
    public interface IFileProcessorPlugin : IPlugin
    {
        string[] SupportedExtensions { get; }
        bool CanProcess(string filePath);
        void Process(string filePath, IDictionary<string, object>? options = null);
    }
    
    /// <summary>
    /// Sandboxed AssemblyLoadContext for plugin isolation.
    /// Each plugin gets its own context to prevent interference.
    /// </summary>
    public class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _resolver;
        private readonly HashSet<string> _sharedAssemblies;
        
        public string PluginPath { get; }
        
        public PluginLoadContext(string pluginPath) : base(isCollectible: true)
        {
            PluginPath = pluginPath;
            _resolver = new AssemblyDependencyResolver(pluginPath);
            
            // Assemblies that should be shared between host and plugins
            _sharedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "PlatypusTools.UI",
                "PlatypusTools.Core",
                "System.Runtime",
                "System.Private.CoreLib",
                "netstandard"
            };
        }
        
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            // Check if this is a shared assembly
            if (_sharedAssemblies.Contains(assemblyName.Name ?? ""))
            {
                return null; // Let the default context handle it
            }
            
            // Try to resolve from plugin's dependencies
            var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (assemblyPath != null)
            {
                return LoadFromAssemblyPath(assemblyPath);
            }
            
            return null;
        }
        
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (libraryPath != null)
            {
                return LoadUnmanagedDllFromPath(libraryPath);
            }
            
            return IntPtr.Zero;
        }
    }
    
    /// <summary>
    /// Represents a loaded plugin with its isolation context.
    /// </summary>
    public class LoadedPlugin
    {
        public IPlugin Plugin { get; set; } = null!;
        public PluginLoadContext? Context { get; set; }
        public string AssemblyPath { get; set; } = "";
        public bool IsSandboxed => Context != null;
    }

    /// <summary>
    /// Service for managing plugins with sandboxing support
    /// </summary>
    public sealed class PluginService
    {
        private static readonly Lazy<PluginService> _instance = new(() => new PluginService());
        public static PluginService Instance => _instance.Value;

        private readonly string _pluginsFolder;
        private readonly string _configFile;
        private readonly Dictionary<string, LoadedPlugin> _loadedPlugins = new();
        private readonly Dictionary<string, bool> _enabledPlugins = new();

        public IReadOnlyDictionary<string, IPlugin> Plugins => 
            _loadedPlugins.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Plugin);
        public event EventHandler<IPlugin>? PluginLoaded;
        public event EventHandler<IPlugin>? PluginUnloaded;

        private PluginService()
        {
            _pluginsFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PlatypusTools", "Plugins");
            _configFile = Path.Combine(_pluginsFolder, "plugins.json");
            
            Directory.CreateDirectory(_pluginsFolder);
            LoadPluginConfig();
        }

        private void LoadPluginConfig()
        {
            try
            {
                if (File.Exists(_configFile))
                {
                    var json = File.ReadAllText(_configFile);
                    _enabledPlugins.Clear();
                    var config = JsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                    if (config != null)
                    {
                        foreach (var kvp in config)
                            _enabledPlugins[kvp.Key] = kvp.Value;
                    }
                }
            }
            catch { /* Use defaults if config fails */ }
        }

        private void SavePluginConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_enabledPlugins, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFile, json);
            }
            catch { /* Ignore save errors */ }
        }

        public void DiscoverAndLoadPlugins()
        {
            if (!Directory.Exists(_pluginsFolder)) return;

            foreach (var dllPath in Directory.GetFiles(_pluginsFolder, "*.dll"))
            {
                try
                {
                    LoadPlugin(dllPath);
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Warning($"Failed to load plugin {Path.GetFileName(dllPath)}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Loads a plugin with sandboxing isolation.
        /// </summary>
        public void LoadPlugin(string assemblyPath)
        {
            // Create isolated context for this plugin
            var loadContext = new PluginLoadContext(assemblyPath);
            
            try
            {
                var assembly = loadContext.LoadFromAssemblyPath(assemblyPath);
                var pluginTypes = assembly.GetTypes()
                    .Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                foreach (var type in pluginTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IPlugin plugin)
                        {
                            if (_loadedPlugins.ContainsKey(plugin.Id))
                            {
                                LoggingService.Instance.Warning($"Plugin {plugin.Id} already loaded, skipping duplicate");
                                continue;
                            }

                            // Check if plugin should be enabled
                            if (_enabledPlugins.TryGetValue(plugin.Id, out var enabled))
                                plugin.IsEnabled = enabled;
                            else
                                plugin.IsEnabled = true; // Enable by default

                            if (plugin.IsEnabled)
                            {
                                // Initialize in try-catch to prevent plugin crashes from affecting host
                                try
                                {
                                    plugin.Initialize();
                                }
                                catch (Exception initEx)
                                {
                                    LoggingService.Instance.Error($"Plugin {plugin.Name} initialization failed: {initEx.Message}");
                                    plugin.IsEnabled = false;
                                }
                            }

                            _loadedPlugins[plugin.Id] = new LoadedPlugin
                            {
                                Plugin = plugin,
                                Context = loadContext,
                                AssemblyPath = assemblyPath
                            };
                            
                            PluginLoaded?.Invoke(this, plugin);

                            LoggingService.Instance.Info($"Loaded plugin: {plugin.Name} v{plugin.Version} (sandboxed)");
                        }
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error($"Failed to initialize plugin type {type.Name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                // If loading fails, unload the context to free resources
                loadContext.Unload();
                throw new InvalidOperationException($"Failed to load plugin assembly: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Unloads a plugin and its isolated context.
        /// </summary>
        public void UnloadPlugin(string pluginId)
        {
            if (_loadedPlugins.TryGetValue(pluginId, out var loadedPlugin))
            {
                try
                {
                    loadedPlugin.Plugin.Shutdown();
                }
                catch (Exception ex) 
                { 
                    LoggingService.Instance.Warning($"Plugin {pluginId} shutdown error: {ex.Message}");
                }

                // Unload the isolated context if present
                if (loadedPlugin.Context != null)
                {
                    loadedPlugin.Context.Unload();
                }

                _loadedPlugins.Remove(pluginId);
                PluginUnloaded?.Invoke(this, loadedPlugin.Plugin);
                LoggingService.Instance.Info($"Unloaded plugin: {loadedPlugin.Plugin.Name}");
            }
        }

        public void EnablePlugin(string pluginId, bool enabled)
        {
            if (_loadedPlugins.TryGetValue(pluginId, out var loadedPlugin))
            {
                var plugin = loadedPlugin.Plugin;
                
                if (enabled && !plugin.IsEnabled)
                {
                    try
                    {
                        plugin.Initialize();
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Error($"Plugin {plugin.Name} initialization failed: {ex.Message}");
                        return; // Don't enable if init fails
                    }
                }
                else if (!enabled && plugin.IsEnabled)
                {
                    try
                    {
                        plugin.Shutdown();
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Warning($"Plugin {plugin.Name} shutdown error: {ex.Message}");
                    }
                }

                plugin.IsEnabled = enabled;
                _enabledPlugins[pluginId] = enabled;
                SavePluginConfig();
            }
        }

        public IEnumerable<T> GetPlugins<T>() where T : IPlugin
        {
            return _loadedPlugins.Values
                .Select(lp => lp.Plugin)
                .OfType<T>()
                .Where(p => p.IsEnabled);
        }

        public IEnumerable<PluginMenuItem> GetAllMenuItems()
        {
            return GetPlugins<IMenuPlugin>()
                .SelectMany(p => 
                {
                    try
                    {
                        return p.GetMenuItems();
                    }
                    catch (Exception ex)
                    {
                        LoggingService.Instance.Warning($"Plugin {p.Name} menu error: {ex.Message}");
                        return Enumerable.Empty<PluginMenuItem>();
                    }
                });
        }

        /// <summary>
        /// Gets information about all loaded plugins including sandbox status.
        /// </summary>
        public IEnumerable<(IPlugin Plugin, bool IsSandboxed)> GetPluginInfo()
        {
            return _loadedPlugins.Values
                .Select(lp => (lp.Plugin, lp.IsSandboxed));
        }

        public void ShutdownAll()
        {
            foreach (var loadedPlugin in _loadedPlugins.Values.ToList())
            {
                try
                {
                    loadedPlugin.Plugin.Shutdown();
                }
                catch { /* Ignore shutdown errors */ }
                
                // Unload contexts
                loadedPlugin.Context?.Unload();
            }
            _loadedPlugins.Clear();
        }

        /// <summary>
        /// Reloads all plugins by shutting down and rediscovering them.
        /// </summary>
        public void ReloadAllPlugins()
        {
            ShutdownAll();
            
            // Force garbage collection to release any remaining references
            GC.Collect();
            GC.WaitForPendingFinalizers();
            
            LoadPluginConfig();
            DiscoverAndLoadPlugins();
        }
    }

    public class PluginMenuItem
    {
        public string Header { get; set; } = "";
        public string? Icon { get; set; }
        public string? Category { get; set; }
        public Action? Command { get; set; }
        public List<PluginMenuItem>? SubItems { get; set; }
    }

    /// <summary>
    /// Base class for creating plugins
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        public abstract string Id { get; }
        public abstract string Name { get; }
        public virtual string Description => "";
        public virtual string Version => "1.0.0";
        public virtual string Author => "Unknown";
        public bool IsEnabled { get; set; } = true;

        public virtual void Initialize() { }
        public virtual void Shutdown() { }
    }
}
