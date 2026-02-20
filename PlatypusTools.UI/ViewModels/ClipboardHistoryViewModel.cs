using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class ClipboardHistoryViewModel : BindableBase
    {
        private readonly ClipboardHistoryService _service;

        public ClipboardHistoryViewModel()
        {
            _service = ClipboardHistoryService.Instance;
            Entries = _service.History;

            ToggleTrackingCommand = new RelayCommand(_ => ToggleTracking());
            ClearHistoryCommand = new RelayCommand(_ => { _service.ClearHistory(); StatusMessage = "History cleared"; });
            RestoreCommand = new RelayCommand(_ => RestoreEntry(), _ => SelectedEntry != null);
            DeleteCommand = new RelayCommand(_ => DeleteEntry(), _ => SelectedEntry != null);
            TogglePinCommand = new RelayCommand(_ => TogglePin(), _ => SelectedEntry != null);
            CopyTextCommand = new RelayCommand(_ => CopyText(), _ => SelectedEntry != null);

            _service.Start();
            IsTracking = true;
            StatusMessage = "Clipboard tracking active";
        }

        private ObservableCollection<ClipboardHistoryService.ClipboardEntry> _entries = null!;
        public ObservableCollection<ClipboardHistoryService.ClipboardEntry> Entries
        {
            get => _entries;
            set => SetProperty(ref _entries, value);
        }

        private ClipboardHistoryService.ClipboardEntry? _selectedEntry;
        public ClipboardHistoryService.ClipboardEntry? SelectedEntry
        {
            get => _selectedEntry;
            set => SetProperty(ref _selectedEntry, value);
        }

        private bool _isTracking;
        public bool IsTracking { get => _isTracking; set => SetProperty(ref _isTracking, value); }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                FilterEntries();
            }
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        public ICommand ToggleTrackingCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand TogglePinCommand { get; }
        public ICommand CopyTextCommand { get; }

        private void ToggleTracking()
        {
            if (IsTracking)
            {
                _service.Stop();
                IsTracking = false;
                StatusMessage = "Clipboard tracking paused";
            }
            else
            {
                _service.Start();
                IsTracking = true;
                StatusMessage = "Clipboard tracking active";
            }
        }

        private void RestoreEntry()
        {
            if (SelectedEntry != null)
            {
                _service.RestoreToClipboard(SelectedEntry);
                StatusMessage = $"Restored {SelectedEntry.ContentType} content to clipboard";
            }
        }

        private void DeleteEntry()
        {
            if (SelectedEntry != null)
            {
                _service.DeleteEntry(SelectedEntry);
                StatusMessage = "Entry deleted";
            }
        }

        private void TogglePin()
        {
            if (SelectedEntry != null)
            {
                _service.TogglePin(SelectedEntry);
                StatusMessage = SelectedEntry.IsPinned ? "Entry pinned" : "Entry unpinned";
            }
        }

        private void CopyText()
        {
            if (SelectedEntry?.TextContent != null)
            {
                System.Windows.Clipboard.SetText(SelectedEntry.TextContent);
                StatusMessage = "Text copied";
            }
        }

        private void FilterEntries()
        {
            // For now, the ObservableCollection from the service is the source.
            // Filtering is done client-side via binding.
            StatusMessage = string.IsNullOrEmpty(SearchText) 
                ? $"{Entries.Count} entries"
                : $"Filter: '{SearchText}'";
        }
    }
}
