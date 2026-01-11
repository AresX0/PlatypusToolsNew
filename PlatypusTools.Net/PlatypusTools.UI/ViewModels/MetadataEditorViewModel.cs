using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class MetadataTag : INotifyPropertyChanged
    {
        private string _key = string.Empty;
        public string Key
        {
            get => _key;
            set { _key = value; OnPropertyChanged(); }
        }

        private string _value = string.Empty;
        public string Value
        {
            get => _value;
            set { _value = value; OnPropertyChanged(); }
        }

        private bool _isModified;
        public bool IsModified
        {
            get => _isModified;
            set { _isModified = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class FolderFileMetadata : INotifyPropertyChanged
    {
        public string FullPath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileSizeFormatted => FileSize < 1024 ? $"{FileSize} B" 
            : FileSize < 1024 * 1024 ? $"{FileSize / 1024.0:F1} KB" 
            : FileSize < 1024 * 1024 * 1024 ? $"{FileSize / (1024.0 * 1024.0):F1} MB"
            : $"{FileSize / (1024.0 * 1024.0 * 1024.0):F2} GB";
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public int TagCount { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class MetadataEditorViewModel : INotifyPropertyChanged
    {
        private readonly IMetadataService _service;

        public ObservableCollection<MetadataTag> Metadata { get; } = new();

        private string _filePath = string.Empty;
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set { _isLoading = value; OnPropertyChanged(); }
        }

        // File type detection
        private string _fileTypeDisplay = string.Empty;
        public string FileTypeDisplay
        {
            get => _fileTypeDisplay;
            set { _fileTypeDisplay = value; OnPropertyChanged(); }
        }

        public bool IsVideoFile => !string.IsNullOrEmpty(FilePath) && 
            new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mpeg", ".mpg", ".m4v" }
            .Contains(Path.GetExtension(FilePath).ToLowerInvariant());

        public bool IsPictureFile => !string.IsNullOrEmpty(FilePath) && 
            new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp", ".raw", ".cr2", ".nef" }
            .Contains(Path.GetExtension(FilePath).ToLowerInvariant());

        public bool IsAudioFile => !string.IsNullOrEmpty(FilePath) && 
            new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a", ".opus" }
            .Contains(Path.GetExtension(FilePath).ToLowerInvariant());

        public bool IsTextFile => !string.IsNullOrEmpty(FilePath) && 
            new[] { ".txt", ".pdf", ".doc", ".docx", ".rtf", ".md", ".xml", ".json" }
            .Contains(Path.GetExtension(FilePath).ToLowerInvariant());

        // Universal tags (always shown)
        private string _owner = string.Empty;
        public string Owner
        {
            get => _owner;
            set { _owner = value; OnPropertyChanged(); UpdateTag("XMP:OwnerName", value); }
        }

        // Common tags for quick access
        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); UpdateTag("Title", value); }
        }

        private string _artist = string.Empty;
        public string Artist
        {
            get => _artist;
            set { _artist = value; OnPropertyChanged(); UpdateTag("Artist", value); }
        }

        private string _album = string.Empty;
        public string Album
        {
            get => _album;
            set { _album = value; OnPropertyChanged(); UpdateTag("Album", value); }
        }

        private string _comment = string.Empty;
        public string Comment
        {
            get => _comment;
            set { _comment = value; OnPropertyChanged(); UpdateTag("Comment", value); }
        }

        private string _copyright = string.Empty;
        public string Copyright
        {
            get => _copyright;
            set { _copyright = value; OnPropertyChanged(); UpdateTag("Copyright", value); }
        }

        // Video-specific tags
        private string _series = string.Empty;
        public string Series
        {
            get => _series;
            set { _series = value; OnPropertyChanged(); UpdateTag("XMP:Series", value); }
        }

        private string _episode = string.Empty;
        public string Episode
        {
            get => _episode;
            set { _episode = value; OnPropertyChanged(); UpdateTag("XMP:Episode", value); }
        }

        private string _movieName = string.Empty;
        public string MovieName
        {
            get => _movieName;
            set { _movieName = value; OnPropertyChanged(); UpdateTag("XMP:MovieName", value); }
        }

        // Text-specific tags
        private string _version = string.Empty;
        public string Version
        {
            get => _version;
            set { _version = value; OnPropertyChanged(); UpdateTag("XMP:Version", value); }
        }

        private string _author = string.Empty;
        public string Author
        {
            get => _author;
            set { _author = value; OnPropertyChanged(); UpdateTag("Author", value); }
        }

        private bool _isExifToolMissing;
        public bool IsExifToolMissing
        {
            get => _isExifToolMissing;
            set { _isExifToolMissing = value; OnPropertyChanged(); }
        }

        // Folder analysis properties
        public ObservableCollection<FolderFileMetadata> FolderFiles { get; } = new();

        private string _folderPath = string.Empty;
        public string FolderPath
        {
            get => _folderPath;
            set { _folderPath = value; OnPropertyChanged(); }
        }

        private string _folderStatusMessage = "Select a folder to scan";
        public string FolderStatusMessage
        {
            get => _folderStatusMessage;
            set { _folderStatusMessage = value; OnPropertyChanged(); }
        }

        private bool _isFolderScanning;
        public bool IsFolderScanning
        {
            get => _isFolderScanning;
            set { _isFolderScanning = value; OnPropertyChanged(); }
        }

        private bool _includeSubfolders = true;
        public bool IncludeSubfolders
        {
            get => _includeSubfolders;
            set { _includeSubfolders = value; OnPropertyChanged(); }
        }

        private int _selectedFileFilterIndex;
        public int SelectedFileFilterIndex
        {
            get => _selectedFileFilterIndex;
            set { _selectedFileFilterIndex = value; OnPropertyChanged(); }
        }

        private FolderFileMetadata? _selectedFolderFile;
        public FolderFileMetadata? SelectedFolderFile
        {
            get => _selectedFolderFile;
            set { _selectedFolderFile = value; OnPropertyChanged(); }
        }

        public ICommand BrowseFileCommand { get; }
        public ICommand LoadMetadataCommand { get; }
        public ICommand SaveMetadataCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand AddTagCommand { get; }
        public ICommand RemoveTagCommand { get; }
        public ICommand OpenExifToolWebsiteCommand { get; }

        // Folder commands
        public ICommand BrowseFolderCommand { get; }
        public ICommand ScanFolderCommand { get; }
        public ICommand ExportToCsvCommand { get; }
        public ICommand OpenSelectedFileCommand { get; }

        public MetadataEditorViewModel() : this(new MetadataServiceEnhanced()) { }

        public MetadataEditorViewModel(IMetadataService service)
        {
            _service = service;

            BrowseFileCommand = new RelayCommand(_ => BrowseFile());
            LoadMetadataCommand = new RelayCommand(_ => LoadMetadata(), _ => !string.IsNullOrEmpty(FilePath));
            SaveMetadataCommand = new RelayCommand(_ => SaveMetadata(), _ => Metadata.Any(m => m.IsModified));
            ClearAllCommand = new RelayCommand(_ => ClearAll(), _ => Metadata.Any());
            AddTagCommand = new RelayCommand(_ => AddTag());
            RemoveTagCommand = new RelayCommand(obj => RemoveTag(obj as MetadataTag));
            OpenExifToolWebsiteCommand = new RelayCommand(_ => OpenExifToolWebsite());

            // Folder commands
            BrowseFolderCommand = new RelayCommand(_ => BrowseFolder());
            ScanFolderCommand = new RelayCommand(_ => ScanFolder(), _ => !string.IsNullOrEmpty(FolderPath));
            ExportToCsvCommand = new RelayCommand(_ => ExportToCsv(), _ => FolderFiles.Any());
            OpenSelectedFileCommand = new RelayCommand(_ => OpenSelectedFile(), _ => SelectedFolderFile != null);

            IsExifToolMissing = !_service.IsExifToolAvailable();
            if (IsExifToolMissing)
            {
                StatusMessage = "⚠️ ExifTool not found. Please install ExifTool to use this feature.";
            }
        }

        private void BrowseFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*|Images (*.jpg;*.png;*.gif)|*.jpg;*.png;*.gif|Videos (*.mp4;*.mkv;*.avi)|*.mp4;*.mkv;*.avi|Audio (*.mp3;*.flac;*.wav)|*.mp3;*.flac;*.wav",
                Title = "Select file to edit metadata"
            };

            if (dialog.ShowDialog() == true)
            {
                FilePath = dialog.FileName;
                LoadMetadata();
            }
        }

        private async void LoadMetadata()
        {
            if (!File.Exists(FilePath))
            {
                StatusMessage = "File not found";
                return;
            }

            if (!_service.IsExifToolAvailable())
            {
                StatusMessage = "ExifTool not available";
                return;
            }

            IsLoading = true;
            StatusMessage = "Loading metadata...";
            Metadata.Clear();

            try
            {
                var metadata = await _service.ReadMetadata(FilePath);

                foreach (var kvp in metadata.OrderBy(k => k.Key))
                {
                    Metadata.Add(new MetadataTag { Key = kvp.Key, Value = kvp.Value });
                }

                // Update file type display
                UpdateFileTypeDisplay();

                // Populate common tags - try various tag names
                Title = GetTagValue(metadata, "Title", "XMP:Title");
                Artist = GetTagValue(metadata, "Artist", "XMP:Artist", "Creator");
                Album = GetTagValue(metadata, "Album", "XMP:Album");
                Comment = GetTagValue(metadata, "Comment", "XMP:Description", "Description");
                Copyright = GetTagValue(metadata, "Copyright", "XMP:Rights", "Rights");
                Owner = GetTagValue(metadata, "OwnerName", "XMP:OwnerName");
                
                // Video-specific tags
                Series = GetTagValue(metadata, "Series", "XMP:Series");
                Episode = GetTagValue(metadata, "Episode", "XMP:Episode");
                MovieName = GetTagValue(metadata, "MovieName", "XMP:MovieName");
                
                // Text-specific tags
                Version = GetTagValue(metadata, "Version", "XMP:Version");
                Author = GetTagValue(metadata, "Author", "XMP:Author", "Creator");

                StatusMessage = $"Loaded {Metadata.Count} metadata tags";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private string GetTagValue(Dictionary<string, string> metadata, params string[] tagNames)
        {
            foreach (var tagName in tagNames)
            {
                if (metadata.TryGetValue(tagName, out var value) && !string.IsNullOrEmpty(value))
                    return value;
            }
            return string.Empty;
        }

        private void UpdateFileTypeDisplay()
        {
            var ext = Path.GetExtension(FilePath).ToLowerInvariant();
            FileTypeDisplay = ext switch
            {
                ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv" or ".webm" => "- Video File",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tiff" or ".webp" => "- Picture File",
                ".mp3" or ".flac" or ".wav" or ".aac" or ".ogg" or ".wma" or ".m4a" => "- Audio File",
                ".txt" or ".pdf" or ".doc" or ".docx" or ".rtf" or ".md" => "- Text File",
                _ => $"- {ext.TrimStart('.')}"
            };
            
            // Notify file type changes
            OnPropertyChanged(nameof(IsVideoFile));
            OnPropertyChanged(nameof(IsPictureFile));
            OnPropertyChanged(nameof(IsAudioFile));
            OnPropertyChanged(nameof(IsTextFile));
        }

        private async void SaveMetadata()
        {
            if (!File.Exists(FilePath))
            {
                StatusMessage = "File not found";
                return;
            }

            IsLoading = true;
            StatusMessage = "Saving metadata...";

            try
            {
                var modifiedTags = Metadata.Where(m => m.IsModified)
                    .ToDictionary(m => m.Key, m => m.Value);

                if (modifiedTags.Any())
                {
                    var success = await _service.WriteMetadata(FilePath, modifiedTags);
                    if (success)
                    {
                        foreach (var tag in Metadata)
                            tag.IsModified = false;
                        
                        StatusMessage = $"Saved {modifiedTags.Count} metadata tags";
                    }
                    else
                    {
                        StatusMessage = "Failed to save metadata";
                    }
                }
                else
                {
                    StatusMessage = "No changes to save";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async void ClearAll()
        {
            if (!File.Exists(FilePath))
            {
                StatusMessage = "File not found";
                return;
            }

            IsLoading = true;
            StatusMessage = "Clearing all metadata...";

            try
            {
                var allTags = Metadata.Select(m => m.Key).ToArray();
                var success = await _service.ClearMetadata(FilePath, allTags);
                
                if (success)
                {
                    Metadata.Clear();
                    Title = Artist = Album = Comment = Copyright = string.Empty;
                    StatusMessage = "All metadata cleared";
                }
                else
                {
                    StatusMessage = "Failed to clear metadata";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void AddTag()
        {
            var newTag = new MetadataTag { Key = "NewTag", Value = string.Empty, IsModified = true };
            Metadata.Add(newTag);
        }

        private void RemoveTag(MetadataTag? tag)
        {
            if (tag != null)
                Metadata.Remove(tag);
        }

        private void OpenExifToolWebsite()
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://exiftool.org/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open browser: {ex.Message}";
            }
        }

        private void UpdateTag(string key, string value)
        {
            var existing = Metadata.FirstOrDefault(m => m.Key == key);
            if (existing != null)
            {
                existing.Value = value;
                existing.IsModified = true;
            }
            else if (!string.IsNullOrEmpty(value))
            {
                Metadata.Add(new MetadataTag { Key = key, Value = value, IsModified = true });
            }
        }

        // Folder analysis methods
        private void BrowseFolder()
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder to scan for metadata",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                FolderPath = dialog.SelectedPath;
            }
        }

        private async void ScanFolder()
        {
            if (!Directory.Exists(FolderPath))
            {
                FolderStatusMessage = "Folder not found";
                return;
            }

            IsFolderScanning = true;
            FolderStatusMessage = "Scanning folder...";
            FolderFiles.Clear();

            try
            {
                var searchOption = IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                var extensions = GetFilterExtensions();
                
                var files = Directory.GetFiles(FolderPath, "*.*", searchOption)
                    .Where(f => extensions.Length == 0 || extensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                FolderStatusMessage = $"Found {files.Count} files, reading metadata...";

                int processed = 0;
                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        var fileMetadata = new FolderFileMetadata
                        {
                            FullPath = file,
                            FileName = fileInfo.Name,
                            Extension = fileInfo.Extension,
                            FileSize = fileInfo.Length,
                            DateCreated = fileInfo.CreationTime,
                            DateModified = fileInfo.LastWriteTime
                        };

                        // Try to read metadata if ExifTool is available
                        if (_service.IsExifToolAvailable())
                        {
                            try
                            {
                                var metadata = await _service.ReadMetadata(file);
                                fileMetadata.Title = metadata.ContainsKey("Title") ? metadata["Title"] : string.Empty;
                                fileMetadata.Artist = metadata.ContainsKey("Artist") ? metadata["Artist"] : string.Empty;
                                fileMetadata.Album = metadata.ContainsKey("Album") ? metadata["Album"] : string.Empty;
                                fileMetadata.TagCount = metadata.Count;
                            }
                            catch
                            {
                                // Ignore metadata read errors for individual files
                            }
                        }

                        FolderFiles.Add(fileMetadata);
                        processed++;

                        if (processed % 10 == 0)
                        {
                            FolderStatusMessage = $"Processed {processed}/{files.Count} files...";
                        }
                    }
                    catch
                    {
                        // Skip files that can't be read
                    }
                }

                FolderStatusMessage = $"Scanned {FolderFiles.Count} files";
            }
            catch (Exception ex)
            {
                FolderStatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsFolderScanning = false;
            }
        }

        private string[] GetFilterExtensions()
        {
            return SelectedFileFilterIndex switch
            {
                1 => new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" },
                2 => new[] { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm" },
                3 => new[] { ".mp3", ".flac", ".wav", ".aac", ".ogg", ".wma", ".m4a" },
                4 => new[] { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx" },
                _ => Array.Empty<string>()
            };
        }

        private void ExportToCsv()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"metadata_export_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Metadata to CSV"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using var writer = new StreamWriter(dialog.FileName);
                    writer.WriteLine("File Name,Extension,Size,Title,Artist,Album,Date Created,Date Modified,Tags Count,Full Path");
                    
                    foreach (var file in FolderFiles)
                    {
                        writer.WriteLine($"\"{file.FileName}\",\"{file.Extension}\",\"{file.FileSizeFormatted}\",\"{EscapeCsv(file.Title)}\",\"{EscapeCsv(file.Artist)}\",\"{EscapeCsv(file.Album)}\",\"{file.DateCreated:yyyy-MM-dd HH:mm}\",\"{file.DateModified:yyyy-MM-dd HH:mm}\",{file.TagCount},\"{file.FullPath}\"");
                    }

                    FolderStatusMessage = $"Exported {FolderFiles.Count} files to CSV";
                }
                catch (Exception ex)
                {
                    FolderStatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            return value?.Replace("\"", "\"\"") ?? string.Empty;
        }

        private void OpenSelectedFile()
        {
            if (SelectedFolderFile == null) return;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = SelectedFolderFile.FullPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                FolderStatusMessage = $"Failed to open file: {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
