using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class VideoMetadataViewModel : BindableBase
    {
        private readonly VideoMetadataService _service = new();
        private CancellationTokenSource? _cts;

        private static readonly string[] VideoExtensions = { ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".ts", ".mpg", ".mpeg" };

        public VideoMetadataViewModel()
        {
            Items = new ObservableCollection<VideoMetadataService.VideoMetadata>();

            AddFilesCommand = new RelayCommand(_ => AddFiles());
            AddFolderCommand = new RelayCommand(_ => AddFolder());
            RemoveSelectedCommand = new RelayCommand(_ => { if (SelectedItem != null) Items.Remove(SelectedItem); }, _ => SelectedItem != null);
            ClearCommand = new RelayCommand(_ => Items.Clear());
            ReadMetadataCommand = new RelayCommand(async _ => await ReadAllMetadataAsync(), _ => Items.Count > 0 && !IsWorking);
            SaveMetadataCommand = new RelayCommand(async _ => await SaveSelectedMetadataAsync(), _ => SelectedItem != null && !IsWorking);
            SaveAllCommand = new RelayCommand(async _ => await SaveAllMetadataAsync(), _ => Items.Count > 0 && !IsWorking);
            ApplyToAllCommand = new RelayCommand(_ => ApplyToSelected(), _ => SelectedItem != null && Items.Count > 1);
            CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsWorking);
        }

        private VideoMetadataService.VideoMetadata? _selectedItem;
        public VideoMetadataService.VideoMetadata? SelectedItem
        {
            get => _selectedItem;
            set
            {
                SetProperty(ref _selectedItem, value);
                if (value != null)
                {
                    EditTitle = value.Title;
                    EditArtist = value.Artist;
                    EditAlbum = value.Album;
                    EditGenre = value.Genre;
                    EditDescription = value.Description;
                    EditYear = value.Year?.ToString() ?? "";
                }
            }
        }

        private string _editTitle = "";
        public string EditTitle { get => _editTitle; set => SetProperty(ref _editTitle, value); }

        private string _editArtist = "";
        public string EditArtist { get => _editArtist; set => SetProperty(ref _editArtist, value); }

        private string _editAlbum = "";
        public string EditAlbum { get => _editAlbum; set => SetProperty(ref _editAlbum, value); }

        private string _editGenre = "";
        public string EditGenre { get => _editGenre; set => SetProperty(ref _editGenre, value); }

        private string _editDescription = "";
        public string EditDescription { get => _editDescription; set => SetProperty(ref _editDescription, value); }

        private string _editYear = "";
        public string EditYear { get => _editYear; set => SetProperty(ref _editYear, value); }

        private bool _isWorking;
        public bool IsWorking { get => _isWorking; set => SetProperty(ref _isWorking, value); }

        private int _progress;
        public int Progress { get => _progress; set => SetProperty(ref _progress, value); }

        private string _statusMessage = "Add video files to edit metadata";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ObservableCollection<VideoMetadataService.VideoMetadata> Items { get; }

        public ICommand AddFilesCommand { get; }
        public ICommand AddFolderCommand { get; }
        public ICommand RemoveSelectedCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ReadMetadataCommand { get; }
        public ICommand SaveMetadataCommand { get; }
        public ICommand SaveAllCommand { get; }
        public ICommand ApplyToAllCommand { get; }
        public ICommand CancelCommand { get; }

        private void AddFiles()
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Multiselect = true,
                Filter = "Video files|*.mp4;*.mkv;*.avi;*.mov;*.wmv;*.webm;*.m4v|All files|*.*"
            };
            if (dlg.ShowDialog() == true)
            {
                foreach (var f in dlg.FileNames)
                    Items.Add(new VideoMetadataService.VideoMetadata { FilePath = f, FileSize = new FileInfo(f).Length });
            }
        }

        private void AddFolder()
        {
            using var dlg = new System.Windows.Forms.FolderBrowserDialog();
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            var files = Directory.GetFiles(dlg.SelectedPath, "*", SearchOption.AllDirectories)
                .Where(f => VideoExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
            foreach (var f in files)
                Items.Add(new VideoMetadataService.VideoMetadata { FilePath = f, FileSize = new FileInfo(f).Length });
        }

        private async Task ReadAllMetadataAsync()
        {
            IsWorking = true;
            _cts = new CancellationTokenSource();

            try
            {
                var progressReporter = new Progress<int>(p => Progress = p);
                var paths = Items.Select(i => i.FilePath).ToList();
                var results = await _service.BatchReadAsync(paths, progressReporter, _cts.Token);

                Items.Clear();
                foreach (var r in results) Items.Add(r);
                StatusMessage = $"Read metadata for {results.Count} files";
            }
            catch (OperationCanceledException) { StatusMessage = "Cancelled"; }
            catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
            finally { IsWorking = false; _cts?.Dispose(); _cts = null; }
        }

        private async Task SaveSelectedMetadataAsync()
        {
            if (SelectedItem == null) return;
            IsWorking = true;

            try
            {
                SelectedItem.Title = EditTitle;
                SelectedItem.Artist = EditArtist;
                SelectedItem.Album = EditAlbum;
                SelectedItem.Genre = EditGenre;
                SelectedItem.Description = EditDescription;
                if (int.TryParse(EditYear, out var y)) SelectedItem.Year = y;

                var ok = await _service.WriteMetadataAsync(SelectedItem);
                StatusMessage = ok ? $"Saved metadata for {Path.GetFileName(SelectedItem.FilePath)}" : "Failed to save metadata";
            }
            catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
            finally { IsWorking = false; }
        }

        private async Task SaveAllMetadataAsync()
        {
            IsWorking = true;
            _cts = new CancellationTokenSource();

            try
            {
                var count = await _service.BatchWriteAsync(Items.ToList(), _cts.Token);
                StatusMessage = $"Saved metadata for {count}/{Items.Count} files";
            }
            catch (OperationCanceledException) { StatusMessage = "Cancelled"; }
            catch (Exception ex) { StatusMessage = $"Error: {ex.Message}"; }
            finally { IsWorking = false; _cts?.Dispose(); _cts = null; }
        }

        private void ApplyToSelected()
        {
            if (SelectedItem == null) return;
            foreach (var item in Items)
            {
                if (!string.IsNullOrEmpty(EditArtist)) item.Artist = EditArtist;
                if (!string.IsNullOrEmpty(EditAlbum)) item.Album = EditAlbum;
                if (!string.IsNullOrEmpty(EditGenre)) item.Genre = EditGenre;
                if (int.TryParse(EditYear, out var y)) item.Year = y;
            }
            StatusMessage = $"Applied metadata to {Items.Count} files (save to write)";
        }
    }
}
