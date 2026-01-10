using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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

        public ICommand BrowseFileCommand { get; }
        public ICommand LoadMetadataCommand { get; }
        public ICommand SaveMetadataCommand { get; }
        public ICommand ClearAllCommand { get; }
        public ICommand AddTagCommand { get; }
        public ICommand RemoveTagCommand { get; }

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

            if (!_service.IsExifToolAvailable())
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

                // Populate common tags
                Title = metadata.ContainsKey("Title") ? metadata["Title"] : string.Empty;
                Artist = metadata.ContainsKey("Artist") ? metadata["Artist"] : string.Empty;
                Album = metadata.ContainsKey("Album") ? metadata["Album"] : string.Empty;
                Comment = metadata.ContainsKey("Comment") ? metadata["Comment"] : string.Empty;
                Copyright = metadata.ContainsKey("Copyright") ? metadata["Copyright"] : string.Empty;

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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
