using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PlatypusTools.UI.Avalonia.Services;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class MediaHubViewModel : ObservableObject
{
    [ObservableProperty] private string _root = "";
    [ObservableProperty] private string _filter = "All";
    [ObservableProperty] private string _status = "Pick a folder, scan recursively. Filter by type, double-click to open.";
    [ObservableProperty] private bool _isBusy;
    public ObservableCollection<MediaItem> Items { get; } = new();
    public string[] Filters { get; } = { "All", "Audio", "Video", "Image", "Document" };

    static readonly string[] AudioExts = { ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".opus", ".wma" };
    static readonly string[] VideoExts = { ".mp4", ".mkv", ".webm", ".avi", ".mov", ".m4v", ".flv", ".wmv" };
    static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".heic" };
    static readonly string[] DocExts = { ".pdf", ".epub", ".docx", ".xlsx", ".pptx", ".odt", ".txt", ".md" };

    [RelayCommand]
    private async System.Threading.Tasks.Task ScanAsync()
    {
        if (string.IsNullOrEmpty(Root) || !Directory.Exists(Root)) { Status = "Pick a folder"; return; }
        IsBusy = true; Items.Clear();
        await System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                foreach (var f in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    string type = AudioExts.Contains(ext) ? "Audio"
                                : VideoExts.Contains(ext) ? "Video"
                                : ImageExts.Contains(ext) ? "Image"
                                : DocExts.Contains(ext)   ? "Document"
                                : "";
                    if (type == "") continue;
                    if (Filter != "All" && type != Filter) continue;
                    try { var fi = new FileInfo(f); Items.Add(new MediaItem { Type = type, Name = fi.Name, Size = fi.Length, Path = fi.FullName }); }
                    catch { }
                }
            }
            catch (System.Exception ex) { Status = ex.Message; }
        });
        Status = $"{Items.Count} items";
        IsBusy = false;
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task OpenAsync(MediaItem? item)
    {
        if (item is null) return;
        await new FileLauncher().OpenFileAsync(item.Path);
    }
}

public sealed class MediaItem
{
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public long Size { get; set; }
    public string Path { get; set; } = "";
}

public partial class MediaLibraryViewModel : MediaHubViewModel { }
