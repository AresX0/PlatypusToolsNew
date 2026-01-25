using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
    public class ForensicsAnalyzerViewModel : BindableBase
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
            OpenReportFolderCommand = new RelayCommand(_ => OpenReportFolder());
            OpenReportCommand = new RelayCommand(_ => OpenReport());
            ClearResultsCommand = new RelayCommand(_ => ClearResults());
            ExportFindingsCommand = new RelayCommand(_ => ExportFindings());
            ExportHtmlReportCommand = new RelayCommand(_ => ExportHtmlReport());
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

        private bool _hasFindings;
        public bool HasFindings
        {
            get => _hasFindings;
            set => SetProperty(ref _hasFindings, value);
        }

        private bool _hasReport;
        public bool HasReport
        {
            get => _hasReport;
            set => SetProperty(ref _hasReport, value);
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
        public ICommand ExportHtmlReportCommand { get; }

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

                // Force refresh of command states on UI thread
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateResults(ForensicsAnalysisResult result)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() =>
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

                // Update button enable states
                HasFindings = result.TotalFindings > 0;
                HasReport = !string.IsNullOrEmpty(result.OutputPath);

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

                // Refresh command states so buttons become enabled
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
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

            // Reset button states
            HasFindings = false;
            HasReport = false;
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

        private void ExportHtmlReport()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "HTML Files (*.html)|*.html|All Files (*.*)|*.*",
                DefaultExt = ".html",
                FileName = $"forensics_report_{DateTime.Now:yyyyMMdd_HHmmss}.html"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var html = GenerateHtmlReport();
                    File.WriteAllText(dialog.FileName, html);
                    Status = $"Exported HTML report to {dialog.FileName}";

                    // Offer to open the report
                    var result = MessageBox.Show("Report exported successfully. Would you like to open it?", 
                        "Export Complete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (result == MessageBoxResult.Yes)
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = dialog.FileName,
                            UseShellExecute = true
                        });
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private string GenerateHtmlReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head>");
            sb.AppendLine("<meta charset='UTF-8'>");
            sb.AppendLine("<title>PlatypusTools Forensics Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: 'Segoe UI', Arial, sans-serif; margin: 20px; background: #f5f5f5; }");
            sb.AppendLine(".container { max-width: 1200px; margin: 0 auto; background: white; padding: 20px; border-radius: 8px; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }");
            sb.AppendLine("h1 { color: #1976D2; border-bottom: 2px solid #1976D2; padding-bottom: 10px; }");
            sb.AppendLine("h2 { color: #424242; margin-top: 30px; }");
            sb.AppendLine(".summary { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 15px; margin: 20px 0; }");
            sb.AppendLine(".summary-card { background: #f8f9fa; padding: 15px; border-radius: 6px; border-left: 4px solid #1976D2; }");
            sb.AppendLine(".summary-card.critical { border-left-color: #d32f2f; }");
            sb.AppendLine(".summary-card.high { border-left-color: #f57c00; }");
            sb.AppendLine(".summary-card.medium { border-left-color: #fbc02d; }");
            sb.AppendLine(".summary-card.low { border-left-color: #388e3c; }");
            sb.AppendLine(".summary-card h3 { margin: 0 0 5px 0; font-size: 14px; color: #666; }");
            sb.AppendLine(".summary-card .value { font-size: 24px; font-weight: bold; color: #333; }");
            sb.AppendLine("table { width: 100%; border-collapse: collapse; margin: 15px 0; }");
            sb.AppendLine("th, td { padding: 10px; text-align: left; border-bottom: 1px solid #ddd; }");
            sb.AppendLine("th { background: #1976D2; color: white; }");
            sb.AppendLine("tr:hover { background: #f5f5f5; }");
            sb.AppendLine(".severity-critical { color: #d32f2f; font-weight: bold; }");
            sb.AppendLine(".severity-high { color: #f57c00; font-weight: bold; }");
            sb.AppendLine(".severity-medium { color: #fbc02d; font-weight: bold; }");
            sb.AppendLine(".severity-low { color: #388e3c; }");
            sb.AppendLine(".severity-info { color: #1976D2; }");
            sb.AppendLine(".footer { margin-top: 30px; padding-top: 15px; border-top: 1px solid #ddd; color: #666; font-size: 12px; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<div class='container'>");

            // Header
            sb.AppendLine("<h1>🔍 PlatypusTools Forensics Report</h1>");
            sb.AppendLine($"<p><strong>Analysis Type:</strong> {SelectedMode} | <strong>Generated:</strong> {DateTime.Now:yyyy-MM-dd HH:mm:ss} | <strong>Duration:</strong> {AnalysisDuration.TotalMinutes:F1} minutes</p>");

            // Summary Cards
            sb.AppendLine("<div class='summary'>");
            sb.AppendLine($"<div class='summary-card critical'><h3>Critical</h3><div class='value'>{CriticalFindings}</div></div>");
            sb.AppendLine($"<div class='summary-card high'><h3>High</h3><div class='value'>{HighFindings}</div></div>");
            sb.AppendLine($"<div class='summary-card medium'><h3>Medium</h3><div class='value'>{MediumFindings}</div></div>");
            sb.AppendLine($"<div class='summary-card low'><h3>Low/Info</h3><div class='value'>{LowFindings}</div></div>");
            sb.AppendLine("</div>");

            // System Summary
            sb.AppendLine("<div class='summary'>");
            sb.AppendLine($"<div class='summary-card'><h3>Memory Usage</h3><div class='value'>{MemoryUsage}</div><small>Suspicious: {SuspiciousProcesses}</small></div>");
            sb.AppendLine($"<div class='summary-card'><h3>Files Scanned</h3><div class='value'>{FilesScanned:N0}</div><small>Suspicious: {SuspiciousFiles}</small></div>");
            sb.AppendLine($"<div class='summary-card'><h3>Registry Entries</h3><div class='value'>{StartupEntries}</div><small>Suspicious: {SuspiciousRegistry}</small></div>");
            sb.AppendLine($"<div class='summary-card'><h3>Events Scanned</h3><div class='value'>{EventsScanned:N0}</div><small>Suspicious: {SuspiciousEvents}</small></div>");
            sb.AppendLine("</div>");

            // Findings Table
            sb.AppendLine("<h2>📋 All Findings</h2>");
            sb.AppendLine("<table>");
            sb.AppendLine("<tr><th>Type</th><th>Severity</th><th>Title</th><th>Description</th><th>Source</th></tr>");
            foreach (var finding in Findings)
            {
                var severityClass = finding.Severity.ToString().ToLower();
                sb.AppendLine($"<tr>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(finding.TypeIcon)}</td>");
                sb.AppendLine($"<td class='severity-{severityClass}'>{finding.Severity}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(finding.Title)}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(finding.Description)}</td>");
                sb.AppendLine($"<td>{System.Net.WebUtility.HtmlEncode(finding.Source)}</td>");
                sb.AppendLine("</tr>");
            }
            sb.AppendLine("</table>");

            // Suspicious Files Section
            if (SuspiciousFilesList.Count > 0)
            {
                sb.AppendLine("<h2>📁 Suspicious Files</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>File Path</th><th>Reason</th></tr>");
                foreach (var file in SuspiciousFilesList.Take(100))
                {
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(file.FilePath)}</td><td>{System.Net.WebUtility.HtmlEncode(file.Reason)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            // Registry Entries Section
            if (RegistryEntries.Count > 0)
            {
                sb.AppendLine("<h2>🔧 Registry Startup Entries</h2>");
                sb.AppendLine("<table>");
                sb.AppendLine("<tr><th>Value Name</th><th>Value Data</th><th>Key Path</th></tr>");
                foreach (var entry in RegistryEntries.Take(100))
                {
                    sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(entry.ValueName)}</td><td>{System.Net.WebUtility.HtmlEncode(entry.ValueData)}</td><td>{System.Net.WebUtility.HtmlEncode(entry.KeyPath)}</td></tr>");
                }
                sb.AppendLine("</table>");
            }

            // Footer
            sb.AppendLine("<div class='footer'>");
            sb.AppendLine($"<p>Report generated by PlatypusTools Forensics Analyzer v3.2.6.1</p>");
            sb.AppendLine($"<p>Computer: {Environment.MachineName} | User: {Environment.UserName}</p>");
            sb.AppendLine("</div>");

            sb.AppendLine("</div></body></html>");
            return sb.ToString();
        }

        #endregion
    }
}
