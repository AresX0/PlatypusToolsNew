using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.UI.Services;
using static PlatypusTools.Core.Services.RebootAnalyzerService;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Reboot Analyzer view
    /// </summary>
    public class RebootAnalyzerViewModel : BindableBase
    {
        private readonly RebootAnalyzerService _analyzerService;
        
        private bool _isAnalyzing;
        private string _statusMessage = "Ready to analyze";
        private string _systemUptime = "--";
        private int _unexpectedRebootCount;
        private int _bsodCount;
        private int _appCrashCount;
        private int _crashDumpCount;
        private int _cleanShutdownCount;
        private int _daysToAnalyze = 30;
        private DateTime? _lastAnalysisTime;
        private DateTime? _lastBootTime;
        private RebootEvent? _selectedEvent;
        private CrashDumpInfo? _selectedDump;

        public RebootAnalyzerViewModel()
        {
            _analyzerService = new RebootAnalyzerService();
            
            UnexpectedReboots = new ObservableCollection<RebootEvent>();
            BSODEvents = new ObservableCollection<RebootEvent>();
            AppCrashes = new ObservableCollection<RebootEvent>();
            CrashDumps = new ObservableCollection<CrashDumpInfo>();
            CleanShutdowns = new ObservableCollection<RebootEvent>();
            RootCauseSummary = new ObservableCollection<KeyValuePair<string, int>>();

            AnalyzeCommand = new RelayCommand(async _ => await AnalyzeAsync(), _ => !IsAnalyzing);
            ClearCommand = new RelayCommand(_ => Clear(), _ => !IsAnalyzing);
            OpenDumpFolderCommand = new RelayCommand(_ => OpenDumpFolder(), _ => SelectedDump != null);
            DeleteDumpCommand = new RelayCommand(_ => DeleteSelectedDump(), _ => SelectedDump != null);

            // Get system uptime on load
            UpdateSystemUptime();
        }

        #region Properties

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (SetProperty(ref _isAnalyzing, value))
                {
                    OnPropertyChanged(nameof(IsNotAnalyzing));
                }
            }
        }

        public bool IsNotAnalyzing => !IsAnalyzing;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public string SystemUptime
        {
            get => _systemUptime;
            set => SetProperty(ref _systemUptime, value);
        }

        public int UnexpectedRebootCount
        {
            get => _unexpectedRebootCount;
            set => SetProperty(ref _unexpectedRebootCount, value);
        }

        public int BSODCount
        {
            get => _bsodCount;
            set => SetProperty(ref _bsodCount, value);
        }

        public int AppCrashCount
        {
            get => _appCrashCount;
            set => SetProperty(ref _appCrashCount, value);
        }

        public int CrashDumpCount
        {
            get => _crashDumpCount;
            set => SetProperty(ref _crashDumpCount, value);
        }

        public int CleanShutdownCount
        {
            get => _cleanShutdownCount;
            set => SetProperty(ref _cleanShutdownCount, value);
        }

        public int DaysToAnalyze
        {
            get => _daysToAnalyze;
            set => SetProperty(ref _daysToAnalyze, value);
        }

        public int DaysAnalyzed => DaysToAnalyze;

        public DateTime? LastAnalysisTime
        {
            get => _lastAnalysisTime;
            set => SetProperty(ref _lastAnalysisTime, value);
        }

        public DateTime? LastBootTime
        {
            get => _lastBootTime;
            set => SetProperty(ref _lastBootTime, value);
        }

        public RebootEvent? SelectedEvent
        {
            get => _selectedEvent;
            set => SetProperty(ref _selectedEvent, value);
        }

        public CrashDumpInfo? SelectedDump
        {
            get => _selectedDump;
            set => SetProperty(ref _selectedDump, value);
        }

        public ObservableCollection<RebootEvent> UnexpectedReboots { get; }
        public ObservableCollection<RebootEvent> BSODEvents { get; }
        public ObservableCollection<RebootEvent> AppCrashes { get; }
        public ObservableCollection<CrashDumpInfo> CrashDumps { get; }
        public ObservableCollection<RebootEvent> CleanShutdowns { get; }
        public ObservableCollection<KeyValuePair<string, int>> RootCauseSummary { get; }

        #endregion

        #region Commands

        public ICommand AnalyzeCommand { get; }
        public ICommand ClearCommand { get; }
        public ICommand OpenDumpFolderCommand { get; }
        public ICommand DeleteDumpCommand { get; }

        #endregion

        #region Methods

        private void UpdateSystemUptime()
        {
            try
            {
                var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
                LastBootTime = DateTime.Now - uptime;
                
                if (uptime.TotalDays >= 1)
                    SystemUptime = $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
                else if (uptime.TotalHours >= 1)
                    SystemUptime = $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
                else
                    SystemUptime = $"{uptime.Minutes}m {uptime.Seconds}s";
            }
            catch
            {
                SystemUptime = "Unknown";
            }
        }

        private async Task AnalyzeAsync()
        {
            IsAnalyzing = true;
            StatusMessage = "Analyzing event logs and crash dumps...";
            Clear();

            try
            {
                var result = await _analyzerService.AnalyzeAsync(DaysToAnalyze);

                // Populate collections
                foreach (var evt in result.UnexpectedReboots.OrderByDescending(e => e.TimeStamp))
                {
                    UnexpectedReboots.Add(evt);
                }

                foreach (var evt in result.BSODEvents.OrderByDescending(e => e.TimeStamp))
                {
                    BSODEvents.Add(evt);
                }

                foreach (var evt in result.ApplicationCrashes.OrderByDescending(e => e.TimeStamp))
                {
                    AppCrashes.Add(evt);
                }

                foreach (var dump in result.CrashDumps.OrderByDescending(d => d.CreatedTime))
                {
                    CrashDumps.Add(dump);
                }

                foreach (var evt in result.CleanShutdowns.OrderByDescending(e => e.TimeStamp))
                {
                    CleanShutdowns.Add(evt);
                }

                // Populate root cause summary
                foreach (var kvp in result.RootCauseSummary.OrderByDescending(x => x.Value))
                {
                    RootCauseSummary.Add(kvp);
                }

                // Update counts
                UnexpectedRebootCount = result.UnexpectedReboots.Count;
                BSODCount = result.BSODEvents.Count;
                AppCrashCount = result.ApplicationCrashes.Count;
                CrashDumpCount = result.CrashDumps.Count;
                CleanShutdownCount = result.CleanShutdowns.Count;

                LastAnalysisTime = DateTime.Now;
                UpdateSystemUptime();

                var totalEvents = UnexpectedRebootCount + BSODCount + AppCrashCount + CleanShutdownCount;
                StatusMessage = $"Analysis complete. Found {totalEvents} events and {CrashDumpCount} crash dumps.";
                
                if (UnexpectedRebootCount > 0 || BSODCount > 0)
                {
                    StatusMessage += $" ⚠️ {UnexpectedRebootCount + BSODCount} critical events detected!";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                MessageBox.Show($"Error during analysis:\n\n{ex.Message}\n\nNote: Administrator privileges may be required to read some event logs.",
                    "Analysis Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private void Clear()
        {
            UnexpectedReboots.Clear();
            BSODEvents.Clear();
            AppCrashes.Clear();
            CrashDumps.Clear();
            CleanShutdowns.Clear();
            RootCauseSummary.Clear();

            UnexpectedRebootCount = 0;
            BSODCount = 0;
            AppCrashCount = 0;
            CrashDumpCount = 0;
            CleanShutdownCount = 0;

            SelectedEvent = null;
            SelectedDump = null;
            StatusMessage = "Ready to analyze";
        }

        private void OpenDumpFolder()
        {
            if (SelectedDump == null || string.IsNullOrEmpty(SelectedDump.FilePath))
                return;

            try
            {
                var folder = Path.GetDirectoryName(SelectedDump.FilePath);
                if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{SelectedDump.FilePath}\"",
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void DeleteSelectedDump()
        {
            if (SelectedDump == null || string.IsNullOrEmpty(SelectedDump.FilePath))
                return;

            var result = MessageBox.Show(
                $"Are you sure you want to delete this crash dump?\n\n{SelectedDump.FileName}\n\nYou can undo this with Ctrl+Z.",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Backup to temp before deleting so undo is possible
                    var backupPath = Path.Combine(Path.GetTempPath(), "PlatypusTools_Undo", Path.GetFileName(SelectedDump.FilePath));
                    Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
                    File.Copy(SelectedDump.FilePath, backupPath, overwrite: true);
                    File.Delete(SelectedDump.FilePath);
                    UndoRedoService.Instance.RecordDeleteWithToast(SelectedDump.FilePath, backupPath);
                    var toRemove = SelectedDump;
                    SelectedDump = null;
                    CrashDumps.Remove(toRemove);
                    CrashDumpCount--;
                    StatusMessage = $"Deleted: {toRemove.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting file: {ex.Message}\n\nAdministrator privileges may be required.",
                        "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}
