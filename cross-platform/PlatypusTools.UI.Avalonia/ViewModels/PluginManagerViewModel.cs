using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class PluginManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _status = "Plugins are .dll/.zip dropped into the plugins folder.";
    [ObservableProperty] private string _pluginsDir;
    public ObservableCollection<PluginRow> Items { get; } = new();

    public PluginManagerViewModel()
    {
        _pluginsDir = Path.Combine(ShellHelper.AppDataDir, "plugins");
        Directory.CreateDirectory(_pluginsDir);
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Items.Clear();
        try
        {
            foreach (var f in Directory.EnumerateFiles(PluginsDir).OrderBy(p => p))
            {
                var fi = new FileInfo(f);
                Items.Add(new PluginRow { Name = fi.Name, Size = fi.Length, Modified = fi.LastWriteTime, Path = fi.FullName });
            }
            Status = $"{Items.Count} plugin file(s) in {PluginsDir}";
        }
        catch (System.Exception ex) { Status = "Failed: " + ex.Message; }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try { _ = new FileLauncher().OpenFileAsync(PluginsDir); }
        catch (System.Exception ex) { Status = "Failed: " + ex.Message; }
    }
}

public sealed class PluginRow
{
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public System.DateTime Modified { get; set; }
    public string Path { get; set; } = "";
}
