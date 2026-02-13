namespace PlatypusTools.Remote.Server.Models;

/// <summary>
/// Library folder for browsing.
/// </summary>
public class LibraryFolderDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int FileCount { get; set; }
}

/// <summary>
/// Library file/folder entry for browsing.
/// </summary>
public class LibraryFileDto
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsDirectory { get; set; }
}
