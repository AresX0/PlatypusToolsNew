using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
    /// Service for managing plugins
    /// </summary>
    public sealed class PluginService
    {
        private static readonly Lazy<PluginService> _instance = new(() => new PluginService());
        public static PluginService Instance => _instance.Value;

        private readonly string _pluginsFolder;
        private readonly string _configFile;
        private readonly Dictionary<string, IPlugin> _loadedPlugins = new();
        private readonly Dictionary<string, bool> _enabledPlugins = new();

        public IReadOnlyDictionary<string, IPlugin> Plugins => _loadedPlugins;
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

        public void LoadPlugin(string assemblyPath)
        {
            var assembly = Assembly.LoadFrom(assemblyPath);
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
                            plugin.Initialize();
                        }

                        _loadedPlugins[plugin.Id] = plugin;
                        PluginLoaded?.Invoke(this, plugin);

                        LoggingService.Instance.Info($"Loaded plugin: {plugin.Name} v{plugin.Version}");
                    }
                }
                catch (Exception ex)
                {
                    LoggingService.Instance.Error($"Failed to initialize plugin type {type.Name}: {ex.Message}");
                }
            }
        }

        public void UnloadPlugin(string pluginId)
        {
            if (_loadedPlugins.TryGetValue(pluginId, out var plugin))
            {
                try
                {
                    plugin.Shutdown();
                }
                catch { /* Ignore shutdown errors */ }

                _loadedPlugins.Remove(pluginId);
                PluginUnloaded?.Invoke(this, plugin);
                LoggingService.Instance.Info($"Unloaded plugin: {plugin.Name}");
            }
        }

        public void EnablePlugin(string pluginId, bool enabled)
        {
            if (_loadedPlugins.TryGetValue(pluginId, out var plugin))
            {
                if (enabled && !plugin.IsEnabled)
                {
                    plugin.Initialize();
                }
                else if (!enabled && plugin.IsEnabled)
                {
                    plugin.Shutdown();
                }

                plugin.IsEnabled = enabled;
                _enabledPlugins[pluginId] = enabled;
                SavePluginConfig();
            }
        }

        public IEnumerable<T> GetPlugins<T>() where T : IPlugin
        {
            return _loadedPlugins.Values
                .OfType<T>()
                .Where(p => p.IsEnabled);
        }

        public IEnumerable<PluginMenuItem> GetAllMenuItems()
        {
            return GetPlugins<IMenuPlugin>()
                .SelectMany(p => p.GetMenuItems());
        }

        public void ShutdownAll()
        {
            foreach (var plugin in _loadedPlugins.Values.ToList())
            {
                try
                {
                    plugin.Shutdown();
                }
                catch { /* Ignore shutdown errors */ }
            }
            _loadedPlugins.Clear();
        }

        /// <summary>
        /// Reloads all plugins by shutting down and rediscovering them.
        /// </summary>
        public void ReloadAllPlugins()
        {
            ShutdownAll();
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
