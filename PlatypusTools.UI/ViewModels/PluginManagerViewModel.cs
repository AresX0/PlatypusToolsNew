using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Plugin information for UI display.
    /// </summary>
    public class PluginInfo : BindableBase
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string AssemblyPath { get; set; } = string.Empty;

        private bool _isEnabled;
        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }
    }

    /// <summary>
    /// ViewModel for the Plugin Manager view.
    /// </summary>
    public class PluginManagerViewModel : BindableBase
    {
        private readonly PluginService _pluginService;

        public PluginManagerViewModel()
        {
            _pluginService = PluginService.Instance;

            // Initialize commands
            RefreshPluginsCommand = new RelayCommand(_ => RefreshPlugins());
            OpenPluginsFolderCommand = new RelayCommand(_ => OpenPluginsFolder());
            InstallPluginCommand = new RelayCommand(_ => InstallPlugin());
            UninstallPluginCommand = new RelayCommand(_ => UninstallPlugin(), _ => SelectedPlugin != null);
            EnablePluginCommand = new RelayCommand(_ => EnablePlugin(), _ => SelectedPlugin != null && !SelectedPlugin.IsEnabled);
            DisablePluginCommand = new RelayCommand(_ => DisablePlugin(), _ => SelectedPlugin != null && SelectedPlugin.IsEnabled);
            ReloadPluginsCommand = new RelayCommand(_ => ReloadPlugins());

            // Load plugins
            RefreshPlugins();
        }

        #region Properties

        public ObservableCollection<PluginInfo> Plugins { get; } = new();

        private PluginInfo? _selectedPlugin;
        public PluginInfo? SelectedPlugin
        {
            get => _selectedPlugin;
            set
            {
                if (SetProperty(ref _selectedPlugin, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _status = "Ready";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private int _totalPlugins;
        public int TotalPlugins
        {
            get => _totalPlugins;
            set => SetProperty(ref _totalPlugins, value);
        }

        private int _enabledPlugins;
        public int EnabledPlugins
        {
            get => _enabledPlugins;
            set => SetProperty(ref _enabledPlugins, value);
        }

        public string PluginsFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PlatypusTools", "Plugins");

        #endregion

        #region Commands

        public ICommand RefreshPluginsCommand { get; }
        public ICommand OpenPluginsFolderCommand { get; }
        public ICommand InstallPluginCommand { get; }
        public ICommand UninstallPluginCommand { get; }
        public ICommand EnablePluginCommand { get; }
        public ICommand DisablePluginCommand { get; }
        public ICommand ReloadPluginsCommand { get; }

        #endregion

        #region Methods

        private void RefreshPlugins()
        {
            Plugins.Clear();

            // Discover plugins first
            _pluginService.DiscoverAndLoadPlugins();

            foreach (var kvp in _pluginService.Plugins)
            {
                var plugin = kvp.Value;
                var info = new PluginInfo
                {
                    Id = plugin.Id,
                    Name = plugin.Name,
                    Description = plugin.Description,
                    Version = plugin.Version,
                    Author = plugin.Author,
                    IsEnabled = plugin.IsEnabled,
                    Type = GetPluginType(plugin)
                };

                info.PropertyChanged += OnPluginEnabledChanged;
                Plugins.Add(info);
            }

            TotalPlugins = Plugins.Count;
            EnabledPlugins = Plugins.Count(p => p.IsEnabled);

            Status = $"Found {TotalPlugins} plugin(s), {EnabledPlugins} enabled";
        }

        private string GetPluginType(IPlugin plugin)
        {
            if (plugin is IMenuPlugin)
                return "Menu Extension";
            if (plugin is IFileProcessorPlugin)
                return "File Processor";
            return "General";
        }

        private void OnPluginEnabledChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PluginInfo.IsEnabled) && sender is PluginInfo info)
            {
                if (_pluginService.Plugins.TryGetValue(info.Id, out var plugin))
                {
                    plugin.IsEnabled = info.IsEnabled;
                    EnabledPlugins = Plugins.Count(p => p.IsEnabled);
                    Status = info.IsEnabled
                        ? $"Plugin '{info.Name}' enabled"
                        : $"Plugin '{info.Name}' disabled";
                }
            }
        }

        private void OpenPluginsFolder()
        {
            Directory.CreateDirectory(PluginsFolder);
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = PluginsFolder,
                UseShellExecute = true
            });
        }

        private void InstallPlugin()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Plugin DLL",
                Filter = "Plugin Files (*.dll)|*.dll|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                int installed = 0;
                foreach (var file in dialog.FileNames)
                {
                    try
                    {
                        var destPath = Path.Combine(PluginsFolder, Path.GetFileName(file));
                        File.Copy(file, destPath, overwrite: true);
                        installed++;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to install {Path.GetFileName(file)}: {ex.Message}",
                            "Installation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                if (installed > 0)
                {
                    Status = $"Installed {installed} plugin(s). Reloading...";
                    RefreshPlugins();
                }
            }
        }

        private void UninstallPlugin()
        {
            if (SelectedPlugin == null) return;

            var result = MessageBox.Show(
                $"Are you sure you want to uninstall '{SelectedPlugin.Name}'?\n\nThis will delete the plugin file.",
                "Confirm Uninstall",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                // Find and delete the plugin file
                var pluginFiles = Directory.GetFiles(PluginsFolder, "*.dll");
                foreach (var file in pluginFiles)
                {
                    // Try to match by loading and checking ID
                    // For now, just delete based on name match
                    if (Path.GetFileNameWithoutExtension(file).Contains(SelectedPlugin.Id, StringComparison.OrdinalIgnoreCase) ||
                        Path.GetFileNameWithoutExtension(file).Contains(SelectedPlugin.Name.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(file);
                        Status = $"Plugin '{SelectedPlugin.Name}' uninstalled";
                        RefreshPlugins();
                        return;
                    }
                }

                MessageBox.Show("Could not find plugin file to delete.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to uninstall plugin: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void EnablePlugin()
        {
            if (SelectedPlugin == null) return;

            if (_pluginService.Plugins.TryGetValue(SelectedPlugin.Id, out var plugin))
            {
                plugin.IsEnabled = true;
                SelectedPlugin.IsEnabled = true;
                Status = $"Plugin '{SelectedPlugin.Name}' enabled";
            }
        }

        private void DisablePlugin()
        {
            if (SelectedPlugin == null) return;

            if (_pluginService.Plugins.TryGetValue(SelectedPlugin.Id, out var plugin))
            {
                plugin.IsEnabled = false;
                SelectedPlugin.IsEnabled = false;
                Status = $"Plugin '{SelectedPlugin.Name}' disabled";
            }
        }

        private void ReloadPlugins()
        {
            Status = "Reloading all plugins...";
            _pluginService.ReloadAllPlugins();
            RefreshPlugins();
            Status = "Plugins reloaded";
        }

        #endregion
    }
}
