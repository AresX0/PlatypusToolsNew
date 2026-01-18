using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the Forensics Analyzer view.
    /// </summary>
    public class ForensicsAnalyzerViewModel : INotifyPropertyChanged
    {
        private readonly ForensicsAnalyzerService _service;
        private CancellationTokenSource? _cts;

        public ForensicsAnalyzerViewModel()
        {
            _service = new ForensicsAnalyzerService();

            // Initialize commands
            RunLightweightAnalysisCommand = new RelayCommand(async _ => await RunAnalysisAsync(ForensicsMode.Lightweight), _ => !IsAnalyzing);
            RunDeepAnalysisCommand = new RelayCommand(async _ => await RunAnalysisAsync(ForensicsMode.Deep), _ => !IsAnalyzing);
            CancelAnalysisCommand = new RelayCommand(_ => CancelAnalysis(), _ => IsAnalyzing);
            OpenReportFolderCommand = new RelayCommand(_ => OpenReportFolder(), _ => !string.IsNullOrEmpty(LastReportPath));
            OpenReportCommand = new RelayCommand(_ => OpenReport(), _ => !string.IsNullOrEmpty(LastReportPath));
            ClearResultsCommand = new RelayCommand(_ => ClearResults(), _ => Findings.Count > 0);
            ExportFindingsCommand = new RelayCommand(_ => ExportFindings(), _ => Findings.Count > 0);
        }

        #region Properties

        private bool _isAnalyzing;
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (SetProperty(ref _isAnalyzing, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _status = "Ready. Select analysis mode to begin.";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private bool _isIndeterminate = true;
        public bool IsIndeterminate
        {
            get => _isIndeterminate;
            set => SetProperty(ref _isIndeterminate, value);
        }

        private ForensicsMode _selectedMode = ForensicsMode.Lightweight;
        public ForensicsMode SelectedMode
        {
            get => _selectedMode;
            set => SetProperty(ref _selectedMode, value);
        }

        private string _lastReportPath = string.Empty;
        public string LastReportPath
        {
            get => _lastReportPath;
            set
            {
                if (SetProperty(ref _lastReportPath, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _lastReportSize = string.Empty;
        public string LastReportSize
        {
            get => _lastReportSize;
            set => SetProperty(ref _lastReportSize, value);
        }

        private TimeSpan _analysisDuration;
        public TimeSpan AnalysisDuration
        {
            get => _analysisDuration;
            set => SetProperty(ref _analysisDuration, value);
        }

        // Summary statistics
        private int _totalFindings;
        public int TotalFindings
        {
            get => _totalFindings;
            set => SetProperty(ref _totalFindings, value);
        }

        private int _criticalFindings;
        public int CriticalFindings
        {
            get => _criticalFindings;
            set => SetProperty(ref _criticalFindings, value);
        }

        private int _highFindings;
        public int HighFindings
        {
            get => _highFindings;
            set => SetProperty(ref _highFindings, value);
        }

        private int _mediumFindings;
        public int MediumFindings
        {
            get => _mediumFindings;
            set => SetProperty(ref _mediumFindings, value);
        }

        private int _lowFindings;
        public int LowFindings
        {
            get => _lowFindings;
            set => SetProperty(ref _lowFindings, value);
        }

        // Memory stats
        private string _memoryUsage = "N/A";
        public string MemoryUsage
        {
            get => _memoryUsage;
            set => SetProperty(ref _memoryUsage, value);
        }

        private int _suspiciousProcesses;
        public int SuspiciousProcesses
        {
            get => _suspiciousProcesses;
            set => SetProperty(ref _suspiciousProcesses, value);
        }

        // File system stats
        private int _filesScanned;
        public int FilesScanned
        {
            get => _filesScanned;
            set => SetProperty(ref _filesScanned, value);
        }

        private int _suspiciousFiles;
        public int SuspiciousFiles
        {
            get => _suspiciousFiles;
            set => SetProperty(ref _suspiciousFiles, value);
        }

        // Registry stats
        private int _startupEntries;
        public int StartupEntries
        {
            get => _startupEntries;
            set => SetProperty(ref _startupEntries, value);
        }

        private int _suspiciousRegistry;
        public int SuspiciousRegistry
        {
            get => _suspiciousRegistry;
            set => SetProperty(ref _suspiciousRegistry, value);
        }

        // Log stats
        private int _eventsScanned;
        public int EventsScanned
        {
            get => _eventsScanned;
            set => SetProperty(ref _eventsScanned, value);
        }

        private int _suspiciousEvents;
        public int SuspiciousEvents
        {
            get => _suspiciousEvents;
            set => SetProperty(ref _suspiciousEvents, value);
        }

        // Collections
        public ObservableCollection<ForensicsFinding> Findings { get; } = new();
        public ObservableCollection<ProcessMemoryInfo> TopProcesses { get; } = new();
        public ObservableCollection<SuspiciousFileInfo> SuspiciousFilesList { get; } = new();
        public ObservableCollection<RegistryEntryInfo> RegistryEntries { get; } = new();
        public ObservableCollection<PlatypusTools.Core.Models.EventLogEntry> SecurityEventsList { get; } = new();

        // Selected items
        private ForensicsFinding? _selectedFinding;
        public ForensicsFinding? SelectedFinding
        {
            get => _selectedFinding;
            set => SetProperty(ref _selectedFinding, value);
        }

        #endregion

        #region Commands

        public ICommand RunLightweightAnalysisCommand { get; }
        public ICommand RunDeepAnalysisCommand { get; }
        public ICommand CancelAnalysisCommand { get; }
        public ICommand OpenReportFolderCommand { get; }
        public ICommand OpenReportCommand { get; }
        public ICommand ClearResultsCommand { get; }
        public ICommand ExportFindingsCommand { get; }

        #endregion

        #region Methods

        private async Task RunAnalysisAsync(ForensicsMode mode)
        {
            if (IsAnalyzing) return;

            IsAnalyzing = true;
            SelectedMode = mode;
            ClearResults();

            _cts = new CancellationTokenSource();

            var progress = new Progress<string>(msg =>
            {
                Status = msg;
            });

            try
            {
                var modeText = mode == ForensicsMode.Lightweight ? "Lightweight (Quick)" : "Deep (Comprehensive)";
                Status = $"Starting {modeText} analysis...";

                var result = await _service.AnalyzeAsync(mode, progress, _cts.Token);

                // Update UI with results
                UpdateResults(result);

                if (result.TotalFindings > 0)
                {
                    Status = $"Analysis complete. Found {result.TotalFindings} findings. Report saved.";
                }
                else
                {
                    Status = "Analysis complete. No significant findings detected.";
                }
            }
            catch (OperationCanceledException)
            {
                Status = "Analysis cancelled by user.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                MessageBox.Show($"Analysis failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsAnalyzing = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void UpdateResults(ForensicsAnalysisResult result)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Summary
                TotalFindings = result.TotalFindings;
                CriticalFindings = result.CriticalFindings;
                HighFindings = result.HighFindings;
                MediumFindings = result.MediumFindings;
                LowFindings = result.LowFindings;
                AnalysisDuration = result.Duration;
                LastReportPath = result.OutputPath;
                LastReportSize = result.OutputSizeFormatted;

                // Findings
                Findings.Clear();
                foreach (var finding in result.AllFindings.OrderByDescending(f => f.Severity))
                {
                    Findings.Add(finding);
                }

                // Memory
                if (result.MemoryAnalysis != null)
                {
                    MemoryUsage = $"{result.MemoryAnalysis.UsagePercent:F1}%";
                    SuspiciousProcesses = result.MemoryAnalysis.SuspiciousProcesses.Count;

                    TopProcesses.Clear();
                    foreach (var proc in result.MemoryAnalysis.TopProcesses.Take(20))
                    {
                        TopProcesses.Add(proc);
                    }
                }

                // File System
                if (result.FileSystemAnalysis != null)
                {
                    FilesScanned = result.FileSystemAnalysis.TotalFilesScanned;
                    SuspiciousFiles = result.FileSystemAnalysis.SuspiciousFilesFound;

                    SuspiciousFilesList.Clear();
                    foreach (var file in result.FileSystemAnalysis.SuspiciousFiles.Take(50))
                    {
                        SuspiciousFilesList.Add(file);
                    }
                }

                // Registry
                if (result.RegistryAnalysis != null)
                {
                    StartupEntries = result.RegistryAnalysis.StartupEntriesFound;
                    SuspiciousRegistry = result.RegistryAnalysis.SuspiciousEntriesFound;

                    RegistryEntries.Clear();
                    foreach (var entry in result.RegistryAnalysis.StartupEntries.Take(50))
                    {
                        RegistryEntries.Add(entry);
                    }
                }

                // Logs
                if (result.LogAnalysis != null)
                {
                    EventsScanned = result.LogAnalysis.TotalEventsScanned;
                    SuspiciousEvents = result.LogAnalysis.SuspiciousEventsFound;

                    SecurityEventsList.Clear();
                    foreach (var evt in result.LogAnalysis.SuspiciousEvents.Take(50))
                    {
                        SecurityEventsList.Add(evt);
                    }
                }
            });
        }

        private void CancelAnalysis()
        {
            _cts?.Cancel();
            _service.Cancel();
            Status = "Cancelling analysis...";
        }

        private void ClearResults()
        {
            Findings.Clear();
            TopProcesses.Clear();
            SuspiciousFilesList.Clear();
            RegistryEntries.Clear();
            SecurityEventsList.Clear();

            TotalFindings = 0;
            CriticalFindings = 0;
            HighFindings = 0;
            MediumFindings = 0;
            LowFindings = 0;
            MemoryUsage = "N/A";
            SuspiciousProcesses = 0;
            FilesScanned = 0;
            SuspiciousFiles = 0;
            StartupEntries = 0;
            SuspiciousRegistry = 0;
            EventsScanned = 0;
            SuspiciousEvents = 0;

            CommandManager.InvalidateRequerySuggested();
        }

        private void OpenReportFolder()
        {
            if (string.IsNullOrEmpty(LastReportPath)) return;

            var folder = Path.GetDirectoryName(LastReportPath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{LastReportPath}\"",
                    UseShellExecute = true
                });
            }
        }

        private void OpenReport()
        {
            if (string.IsNullOrEmpty(LastReportPath) || !File.Exists(LastReportPath)) return;

            Process.Start(new ProcessStartInfo
            {
                FileName = LastReportPath,
                UseShellExecute = true
            });
        }

        private void ExportFindings()
        {
            // Export to CSV
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv|All Files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"forensics_findings_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    using var writer = new StreamWriter(dialog.FileName);
                    writer.WriteLine("Type,Severity,Title,Description,Source,Timestamp");

                    foreach (var finding in Findings)
                    {
                        writer.WriteLine($"\"{finding.Type}\",\"{finding.Severity}\",\"{EscapeCsv(finding.Title)}\",\"{EscapeCsv(finding.Description)}\",\"{finding.Source}\",\"{finding.Timestamp}\"");
                    }

                    Status = $"Exported {Findings.Count} findings to {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private static string EscapeCsv(string value)
        {
            return value.Replace("\"", "\"\"").Replace("\n", " ").Replace("\r", "");
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
