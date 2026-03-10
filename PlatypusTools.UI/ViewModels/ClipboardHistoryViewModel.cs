using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class ClipboardHistoryViewModel : BindableBase
    {
        private readonly ClipboardHistoryService _service;
        private DispatcherTimer? _autoClearTimer;

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
            ClearClipboardNowCommand = new RelayCommand(_ => ClearClipboardNow());

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
        public ICommand ClearClipboardNowCommand { get; }

        private bool _autoClearEnabled;
        public bool AutoClearEnabled
        {
            get => _autoClearEnabled;
            set
            {
                if (SetProperty(ref _autoClearEnabled, value))
                    UpdateAutoClearTimer();
            }
        }

        private int _autoClearSeconds = 30;
        public int AutoClearSeconds
        {
            get => _autoClearSeconds;
            set
            {
                if (SetProperty(ref _autoClearSeconds, Math.Max(5, value)))
                    UpdateAutoClearTimer();
            }
        }

        private string _autoClearCountdown = "";
        public string AutoClearCountdown { get => _autoClearCountdown; set => SetProperty(ref _autoClearCountdown, value); }

        private DateTime _lastClipboardChange = DateTime.MinValue;
        private DispatcherTimer? _countdownTimer;

        private void UpdateAutoClearTimer()
        {
            _autoClearTimer?.Stop();
            _countdownTimer?.Stop();

            if (AutoClearEnabled)
            {
                _lastClipboardChange = DateTime.Now;

                _autoClearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(AutoClearSeconds) };
                _autoClearTimer.Tick += (_, _) =>
                {
                    ClearClipboardNow();
                    _autoClearTimer.Stop();
                };
                _autoClearTimer.Start();

                _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _countdownTimer.Tick += (_, _) =>
                {
                    var remaining = AutoClearSeconds - (int)(DateTime.Now - _lastClipboardChange).TotalSeconds;
                    if (remaining > 0)
                        AutoClearCountdown = $"Auto-clear in {remaining}s";
                    else
                        AutoClearCountdown = "";
                };
                _countdownTimer.Start();

                StatusMessage = $"Auto-clear enabled: clipboard will clear after {AutoClearSeconds}s";
            }
            else
            {
                AutoClearCountdown = "";
                StatusMessage = "Auto-clear disabled";
            }
        }

        private void ClearClipboardNow()
        {
            try
            {
                System.Windows.Clipboard.Clear();
                StatusMessage = "Clipboard cleared";
                AutoClearCountdown = "";

                // Reset timer for next clipboard change
                if (AutoClearEnabled)
                {
                    _lastClipboardChange = DateTime.Now;
                    _autoClearTimer?.Stop();
                    _autoClearTimer?.Start();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Clear failed: {ex.Message}";
            }
        }

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
