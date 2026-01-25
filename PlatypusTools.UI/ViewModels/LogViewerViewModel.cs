using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the log viewer window.
    /// Displays application logs with filtering and search capabilities.
    /// </summary>
    public class LogViewerViewModel : BindableBase
    {
        private readonly System.Timers.Timer _refreshTimer;
        private string _logDirectory;

        public LogViewerViewModel()
        {
            _logDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PlatypusTools", "Logs");

            LogEntries = new ObservableCollection<LogEntry>();
            FilteredEntries = new ObservableCollection<LogEntry>();

            // Commands
            RefreshCommand = new RelayCommand(_ => Refresh());
            ClearCommand = new RelayCommand(_ => Clear());
            ExportCommand = new RelayCommand(_ => Export());
            CopySelectedCommand = new RelayCommand(_ => CopySelected(), _ => SelectedEntry != null);

            // Auto-refresh timer
            _refreshTimer = new System.Timers.Timer(2000);
            _refreshTimer.Elapsed += (s, e) => 
            {
                if (AutoRefresh)
                {
                    _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(Refresh);
                }
            };

            // Initial load
            Refresh();
        }

        #region Properties

        public ObservableCollection<LogEntry> LogEntries { get; }
        public ObservableCollection<LogEntry> FilteredEntries { get; }

        private LogEntry? _selectedEntry;
        public LogEntry? SelectedEntry
        {
            get => _selectedEntry;
            set => SetProperty(ref _selectedEntry, value);
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (SetProperty(ref _searchText, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _showDebug = true;
        public bool ShowDebug
        {
            get => _showDebug;
            set
            {
                if (SetProperty(ref _showDebug, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _showInfo = true;
        public bool ShowInfo
        {
            get => _showInfo;
            set
            {
                if (SetProperty(ref _showInfo, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _showWarning = true;
        public bool ShowWarning
        {
            get => _showWarning;
            set
            {
                if (SetProperty(ref _showWarning, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _showError = true;
        public bool ShowError
        {
            get => _showError;
            set
            {
                if (SetProperty(ref _showError, value))
                {
                    ApplyFilter();
                }
            }
        }

        private bool _autoRefresh = false;
        public bool AutoRefresh
        {
            get => _autoRefresh;
            set
            {
                if (SetProperty(ref _autoRefresh, value))
                {
                    if (value)
                        _refreshTimer.Start();
                    else
                        _refreshTimer.Stop();
                }
            }
        }

        private bool _autoScroll = true;
        public bool AutoScroll
        {
            get => _autoScroll;
            set => SetProperty(ref _autoScroll, value);
        }

        private int _entryCount;
        public int EntryCount
        {
            get => _entryCount;
            set => SetProperty(ref _entryCount, value);
        }

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CopySelectedCommand { get; }

        #endregion

        #region Methods

        public void Refresh()
        {
            try
            {
                LogEntries.Clear();

                if (!Directory.Exists(_logDirectory))
                {
                    return;
                }

                // Get today's log file
                var todayLog = Path.Combine(_logDirectory, $"log-{DateTime.Now:yyyy-MM-dd}.log");
                if (File.Exists(todayLog))
                {
                    ParseLogFile(todayLog);
                }

                // Also check for general log file
                var generalLog = Path.Combine(_logDirectory, "platypustools.log");
                if (File.Exists(generalLog))
                {
                    ParseLogFile(generalLog);
                }

                ApplyFilter();
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Error refreshing logs: {ex.Message}");
            }
        }

        private void ParseLogFile(string path)
        {
            try
            {
                // Read file with shared access
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    var entry = ParseLogLine(line);
                    if (entry != null)
                    {
                        LogEntries.Add(entry);
                    }
                }
            }
            catch
            {
                // Ignore read errors
            }
        }

        private LogEntry? ParseLogLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;

            // Expected format: [2026-01-11 12:34:56] [LEVEL] Message
            var entry = new LogEntry { RawLine = line };

            try
            {
                // Try to parse timestamp
                if (line.StartsWith("["))
                {
                    var timestampEnd = line.IndexOf(']');
                    if (timestampEnd > 1)
                    {
                        var timestampStr = line.Substring(1, timestampEnd - 1);
                        if (DateTime.TryParse(timestampStr, out var timestamp))
                        {
                            entry.Timestamp = timestamp;
                        }
                        line = line.Substring(timestampEnd + 1).TrimStart();
                    }
                }

                // Try to parse level
                if (line.StartsWith("["))
                {
                    var levelEnd = line.IndexOf(']');
                    if (levelEnd > 1)
                    {
                        var levelStr = line.Substring(1, levelEnd - 1).ToUpperInvariant();
                        entry.Level = levelStr switch
                        {
                            "DEBUG" => LogLevel.Debug,
                            "INFO" => LogLevel.Info,
                            "WARN" or "WARNING" => LogLevel.Warning,
                            "ERROR" => LogLevel.Error,
                            _ => LogLevel.Info
                        };
                        line = line.Substring(levelEnd + 1).TrimStart();
                    }
                }

                entry.Message = line;
            }
            catch
            {
                entry.Message = entry.RawLine;
            }

            return entry;
        }

        private void ApplyFilter()
        {
            FilteredEntries.Clear();

            foreach (var entry in LogEntries)
            {
                // Level filter
                var levelMatch = entry.Level switch
                {
                    LogLevel.Debug => ShowDebug,
                    LogLevel.Info => ShowInfo,
                    LogLevel.Warning => ShowWarning,
                    LogLevel.Error => ShowError,
                    _ => true
                };

                if (!levelMatch) continue;

                // Search filter
                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    if (!entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) &&
                        !entry.RawLine.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }

                FilteredEntries.Add(entry);
            }

            EntryCount = FilteredEntries.Count;
        }

        private void Clear()
        {
            try
            {
                if (Directory.Exists(_logDirectory))
                {
                    foreach (var file in Directory.GetFiles(_logDirectory, "*.log"))
                    {
                        try
                        {
                            File.Delete(file);
                        }
                        catch
                        {
                            // Some files may be locked
                        }
                    }
                }

                LogEntries.Clear();
                FilteredEntries.Clear();
                EntryCount = 0;
            }
            catch
            {
                // Ignore errors
            }
        }

        private void Export()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Log files (*.log)|*.log|Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = $"platypustools-{DateTime.Now:yyyyMMdd-HHmmss}.log"
                };

                if (dlg.ShowDialog() == true)
                {
                    var lines = FilteredEntries.Select(e => e.RawLine);
                    File.WriteAllLines(dlg.FileName, lines);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error exporting logs: {ex.Message}", "Export Error",
                    System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void CopySelected()
        {
            if (SelectedEntry != null)
            {
                try
                {
                    System.Windows.Clipboard.SetText(SelectedEntry.RawLine);
                }
                catch
                {
                    // Ignore clipboard errors
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents a log entry.
    /// </summary>
    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel Level { get; set; } = LogLevel.Info;
        public string Message { get; set; } = string.Empty;
        public string RawLine { get; set; } = string.Empty;

        public string TimestampDisplay => Timestamp.ToString("HH:mm:ss.fff");
        public string LevelDisplay => Level.ToString().ToUpperInvariant();
    }

    /// <summary>
    /// Log level enumeration.
    /// </summary>
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }
}
