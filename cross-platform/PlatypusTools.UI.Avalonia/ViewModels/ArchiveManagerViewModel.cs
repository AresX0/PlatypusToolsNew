using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PlatypusTools.UI.Avalonia.ViewModels;

public partial class ArchiveManagerViewModel : ObservableObject
{
    [ObservableProperty] private string _sourceFolder = "";
    [ObservableProperty] private string _archivePath = "";
    [ObservableProperty] private string _extractTo = "";
    [ObservableProperty] private string _status = "";

    [RelayCommand]
    public async Task CreateZipAsync()
    {
        if (!Directory.Exists(SourceFolder)) { Status = "Source folder missing"; return; }
        if (string.IsNullOrEmpty(ArchivePath))
            ArchivePath = SourceFolder.TrimEnd('/', '\\') + ".zip";
        try
        {
            if (File.Exists(ArchivePath)) File.Delete(ArchivePath);
            await Task.Run(() => ZipFile.CreateFromDirectory(SourceFolder, ArchivePath, CompressionLevel.Optimal, false));
            Status = $"Created {ArchivePath}";
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }

    [RelayCommand]
    public async Task ExtractZipAsync()
    {
        if (!File.Exists(ArchivePath)) { Status = "Archive missing"; return; }
        if (string.IsNullOrEmpty(ExtractTo))
            ExtractTo = Path.Combine(Path.GetDirectoryName(ArchivePath)!,
                Path.GetFileNameWithoutExtension(ArchivePath));
        try
        {
            Directory.CreateDirectory(ExtractTo);
            await Task.Run(() => ZipFile.ExtractToDirectory(ArchivePath, ExtractTo, true));
            Status = $"Extracted → {ExtractTo}";
        }
        catch (Exception ex) { Status = $"Failed: {ex.Message}"; }
    }
}
