using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PlatypusTools.Core.Models;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for the AD Security Analyzer view (PLATYPUS-style functionality).
    /// Provides Active Directory and Entra ID security analysis capabilities.
    /// </summary>
    public class AdSecurityAnalyzerViewModel : BindableBase
    {
        private readonly AdSecurityAnalysisService _analysisService;
        private readonly AdSecurityDatabaseService _databaseService;
        private CancellationTokenSource? _cts;

        public AdSecurityAnalyzerViewModel()
        {
            _analysisService = new AdSecurityAnalysisService(new Progress<string>(msg => AppendLog(msg)));
            _databaseService = new AdSecurityDatabaseService();

            // Initialize collections
            PrivilegedMembers = new ObservableCollection<AdPrivilegedMember>();
            RiskyAcls = new ObservableCollection<AdRiskyAcl>();
            RiskyGpos = new ObservableCollection<AdRiskyGpo>();
            SysvolFiles = new ObservableCollection<SysvolRiskyFile>();
            KerberosDelegations = new ObservableCollection<AdKerberosDelegation>();
            AdminCountAnomalies = new ObservableCollection<AdAdminCountAnomaly>();
            AnalysisHistory = new ObservableCollection<StoredAnalysisRun>();
            LogMessages = new ObservableCollection<string>();
            DeploymentResults = new ObservableCollection<AdObjectCreationResult>();

            // Initialize commands
            DiscoverDomainCommand = new RelayCommand(async _ => await DiscoverDomainAsync(), _ => !IsAnalyzing);
            RunFullAnalysisCommand = new RelayCommand(async _ => await RunFullAnalysisAsync(), _ => !IsAnalyzing && IsDomainDiscovered);
            RunPrivilegedAnalysisCommand = new RelayCommand(async _ => await RunPrivilegedAnalysisAsync(), _ => !IsAnalyzing && IsDomainDiscovered);
            RunAclAnalysisCommand = new RelayCommand(async _ => await RunAclAnalysisAsync(), _ => !IsAnalyzing && IsDomainDiscovered);
            RunDelegationAnalysisCommand = new RelayCommand(async _ => await RunDelegationAnalysisAsync(), _ => !IsAnalyzing && IsDomainDiscovered);
            RunSysvolAnalysisCommand = new RelayCommand(async _ => await RunSysvolAnalysisAsync(), _ => !IsAnalyzing && IsDomainDiscovered);
            CancelAnalysisCommand = new RelayCommand(_ => CancelAnalysis(), _ => IsAnalyzing);
            
            ExportToCsvCommand = new RelayCommand(async _ => await ExportToCsvAsync(), _ => HasResults);
            ExportSelectedRunCommand = new RelayCommand(async _ => await ExportSelectedRunAsync(), _ => SelectedHistoryRun != null);
            LoadHistoryCommand = new RelayCommand(async _ => await LoadHistoryAsync());
            LoadSelectedRunCommand = new RelayCommand(async _ => await LoadSelectedRunAsync(), _ => SelectedHistoryRun != null);
            DeleteSelectedRunCommand = new RelayCommand(async _ => await DeleteSelectedRunAsync(), _ => SelectedHistoryRun != null);
            ClearHistoryCommand = new RelayCommand(async _ => await ClearHistoryAsync(), _ => AnalysisHistory.Count > 0);
            ClearLogCommand = new RelayCommand(_ => LogMessages.Clear());
            OpenDatabaseFolderCommand = new RelayCommand(_ => OpenDatabaseFolder());

            // Deployment commands
            DeployTieredOusCommand = new RelayCommand(async _ => await DeployTieredOusAsync(), _ => !IsAnalyzing && IsDomainDiscovered);
            DeployBaselineGposCommand = new RelayCommand(async _ => await DeployBaselineGposAsync(), _ => !IsAnalyzing && IsDomainDiscovered);
            PreviewDeploymentCommand = new RelayCommand(_ => PreviewDeployment(), _ => IsDomainDiscovered);
            ClearDeploymentResultsCommand = new RelayCommand(_ => DeploymentResults.Clear(), _ => DeploymentResults.Count > 0);

            // Initialize
            _ = InitializeAsync();
        }

        #region Properties

        // Analysis State
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

        private bool _isDomainDiscovered;
        public bool IsDomainDiscovered
        {
            get => _isDomainDiscovered;
            set
            {
                if (SetProperty(ref _isDomainDiscovered, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        private string _status = "Ready. Click 'Discover Domain' to begin.";
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

        // Domain Info
        private string _targetDomain = string.Empty;
        public string TargetDomain
        {
            get => _targetDomain;
            set => SetProperty(ref _targetDomain, value);
        }

        private string _targetDc = string.Empty;
        public string TargetDc
        {
            get => _targetDc;
            set => SetProperty(ref _targetDc, value);
        }

        private AdDomainInfo? _domainInfo;
        public AdDomainInfo? DomainInfo
        {
            get => _domainInfo;
            set => SetProperty(ref _domainInfo, value);
        }

        // Analysis Options
        private bool _analyzePrivilegedGroups = true;
        public bool AnalyzePrivilegedGroups
        {
            get => _analyzePrivilegedGroups;
            set => SetProperty(ref _analyzePrivilegedGroups, value);
        }

        private bool _analyzeRiskyAcls = true;
        public bool AnalyzeRiskyAcls
        {
            get => _analyzeRiskyAcls;
            set => SetProperty(ref _analyzeRiskyAcls, value);
        }

        private bool _analyzeGpos = true;
        public bool AnalyzeGpos
        {
            get => _analyzeGpos;
            set => SetProperty(ref _analyzeGpos, value);
        }

        private bool _analyzeSysvol = true;
        public bool AnalyzeSysvol
        {
            get => _analyzeSysvol;
            set => SetProperty(ref _analyzeSysvol, value);
        }

        private bool _analyzeKerberosDelegation = true;
        public bool AnalyzeKerberosDelegation
        {
            get => _analyzeKerberosDelegation;
            set => SetProperty(ref _analyzeKerberosDelegation, value);
        }

        private bool _analyzeAdminCount = true;
        public bool AnalyzeAdminCount
        {
            get => _analyzeAdminCount;
            set => SetProperty(ref _analyzeAdminCount, value);
        }

        private bool _filterSafeIdentities = true;
        public bool FilterSafeIdentities
        {
            get => _filterSafeIdentities;
            set => SetProperty(ref _filterSafeIdentities, value);
        }

        private bool _includeForestMode;
        public bool IncludeForestMode
        {
            get => _includeForestMode;
            set => SetProperty(ref _includeForestMode, value);
        }

        // Deployment Options (BILL Model)
        private string _deploymentBaseName = "Admin";
        public string DeploymentBaseName
        {
            get => _deploymentBaseName;
            set => SetProperty(ref _deploymentBaseName, value);
        }

        private string _tier0Name = "Tier0";
        public string Tier0Name
        {
            get => _tier0Name;
            set => SetProperty(ref _tier0Name, value);
        }

        private string _tier1Name = "Tier1";
        public string Tier1Name
        {
            get => _tier1Name;
            set => SetProperty(ref _tier1Name, value);
        }

        private string _tier2Name = "Tier2";
        public string Tier2Name
        {
            get => _tier2Name;
            set => SetProperty(ref _tier2Name, value);
        }

        private bool _createPawOus = true;
        public bool CreatePawOus
        {
            get => _createPawOus;
            set => SetProperty(ref _createPawOus, value);
        }

        private bool _createServiceAccountOus = true;
        public bool CreateServiceAccountOus
        {
            get => _createServiceAccountOus;
            set => SetProperty(ref _createServiceAccountOus, value);
        }

        private bool _createGroupsOus = true;
        public bool CreateGroupsOus
        {
            get => _createGroupsOus;
            set => SetProperty(ref _createGroupsOus, value);
        }

        private bool _createUsersOus = true;
        public bool CreateUsersOus
        {
            get => _createUsersOus;
            set => SetProperty(ref _createUsersOus, value);
        }

        private bool _createDevicesOus = true;
        public bool CreateDevicesOus
        {
            get => _createDevicesOus;
            set => SetProperty(ref _createDevicesOus, value);
        }

        private bool _protectOusFromDeletion = true;
        public bool ProtectOusFromDeletion
        {
            get => _protectOusFromDeletion;
            set => SetProperty(ref _protectOusFromDeletion, value);
        }

        private bool _deployPasswordPolicyGpo = true;
        public bool DeployPasswordPolicyGpo
        {
            get => _deployPasswordPolicyGpo;
            set => SetProperty(ref _deployPasswordPolicyGpo, value);
        }

        private bool _deployAuditPolicyGpo = true;
        public bool DeployAuditPolicyGpo
        {
            get => _deployAuditPolicyGpo;
            set => SetProperty(ref _deployAuditPolicyGpo, value);
        }

        private bool _deploySecurityBaselineGpo = true;
        public bool DeploySecurityBaselineGpo
        {
            get => _deploySecurityBaselineGpo;
            set => SetProperty(ref _deploySecurityBaselineGpo, value);
        }

        private bool _deployPawGpo = true;
        public bool DeployPawGpo
        {
            get => _deployPawGpo;
            set => SetProperty(ref _deployPawGpo, value);
        }

        private string _deploymentPreview = string.Empty;
        public string DeploymentPreview
        {
            get => _deploymentPreview;
            set => SetProperty(ref _deploymentPreview, value);
        }

        // Results
        private bool _hasResults;
        public bool HasResults
        {
            get => _hasResults;
            set
            {
                if (SetProperty(ref _hasResults, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

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

        private TimeSpan _analysisDuration;
        public TimeSpan AnalysisDuration
        {
            get => _analysisDuration;
            set => SetProperty(ref _analysisDuration, value);
        }

        private long? _currentRunId;
        public long? CurrentRunId
        {
            get => _currentRunId;
            set => SetProperty(ref _currentRunId, value);
        }

        // Collections
        public ObservableCollection<AdPrivilegedMember> PrivilegedMembers { get; }
        public ObservableCollection<AdRiskyAcl> RiskyAcls { get; }
        public ObservableCollection<AdRiskyGpo> RiskyGpos { get; }
        public ObservableCollection<SysvolRiskyFile> SysvolFiles { get; }
        public ObservableCollection<AdKerberosDelegation> KerberosDelegations { get; }
        public ObservableCollection<AdAdminCountAnomaly> AdminCountAnomalies { get; }
        public ObservableCollection<StoredAnalysisRun> AnalysisHistory { get; }
        public ObservableCollection<string> LogMessages { get; }
        public ObservableCollection<AdObjectCreationResult> DeploymentResults { get; }

        private StoredAnalysisRun? _selectedHistoryRun;
        public StoredAnalysisRun? SelectedHistoryRun
        {
            get => _selectedHistoryRun;
            set
            {
                if (SetProperty(ref _selectedHistoryRun, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        // Selected Items for Detail View
        private AdPrivilegedMember? _selectedPrivilegedMember;
        public AdPrivilegedMember? SelectedPrivilegedMember
        {
            get => _selectedPrivilegedMember;
            set => SetProperty(ref _selectedPrivilegedMember, value);
        }

        private AdRiskyAcl? _selectedRiskyAcl;
        public AdRiskyAcl? SelectedRiskyAcl
        {
            get => _selectedRiskyAcl;
            set => SetProperty(ref _selectedRiskyAcl, value);
        }

        private AdKerberosDelegation? _selectedDelegation;
        public AdKerberosDelegation? SelectedDelegation
        {
            get => _selectedDelegation;
            set => SetProperty(ref _selectedDelegation, value);
        }

        #endregion

        #region Commands

        public ICommand DiscoverDomainCommand { get; }
        public ICommand RunFullAnalysisCommand { get; }
        public ICommand RunPrivilegedAnalysisCommand { get; }
        public ICommand RunAclAnalysisCommand { get; }
        public ICommand RunDelegationAnalysisCommand { get; }
        public ICommand RunSysvolAnalysisCommand { get; }
        public ICommand CancelAnalysisCommand { get; }
        public ICommand ExportToCsvCommand { get; }
        public ICommand ExportSelectedRunCommand { get; }
        public ICommand LoadHistoryCommand { get; }
        public ICommand LoadSelectedRunCommand { get; }
        public ICommand DeleteSelectedRunCommand { get; }
        public ICommand ClearHistoryCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand OpenDatabaseFolderCommand { get; }

        // Deployment Commands
        public ICommand DeployTieredOusCommand { get; }
        public ICommand DeployBaselineGposCommand { get; }
        public ICommand PreviewDeploymentCommand { get; }
        public ICommand ClearDeploymentResultsCommand { get; }

        #endregion

        #region Initialization

        private async Task InitializeAsync()
        {
            try
            {
                await _databaseService.InitializeAsync();
                AppendLog($"Database initialized: {_databaseService.DatabasePath}");
                await LoadHistoryAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Error initializing database: {ex.Message}");
            }
        }

        #endregion

        #region Domain Discovery

        private async Task DiscoverDomainAsync()
        {
            IsAnalyzing = true;
            Status = "Discovering Active Directory domain...";
            ClearResults();

            try
            {
                _cts = new CancellationTokenSource();

                DomainInfo = await _analysisService.DiscoverDomainAsync(
                    string.IsNullOrWhiteSpace(TargetDomain) ? null : TargetDomain,
                    string.IsNullOrWhiteSpace(TargetDc) ? null : TargetDc,
                    _cts.Token);

                if (!string.IsNullOrEmpty(DomainInfo?.ChosenDc))
                {
                    IsDomainDiscovered = true;
                    Status = $"Connected to {DomainInfo.DomainFqdn} via {DomainInfo.ChosenDc}";
                    AppendLog($"Domain: {DomainInfo.DomainFqdn}");
                    AppendLog($"Domain Controller: {DomainInfo.ChosenDc}");
                    AppendLog($"PDC Emulator: {DomainInfo.PdcEmulator}");
                    AppendLog($"AD Recycle Bin: {(DomainInfo.IsAdRecycleBinEnabled ? "Enabled" : "Disabled")}");
                    AppendLog($"SYSVOL: {DomainInfo.SysvolReplicationInfo}");
                }
                else
                {
                    IsDomainDiscovered = false;
                    Status = "Could not connect to domain. Check domain name and credentials.";
                }
            }
            catch (OperationCanceledException)
            {
                Status = "Discovery cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        #endregion

        #region Analysis Methods

        private async Task RunFullAnalysisAsync()
        {
            IsAnalyzing = true;
            Status = "Running full security analysis...";
            ClearResults();

            try
            {
                _cts = new CancellationTokenSource();

                var options = new AdSecurityAnalysisOptions
                {
                    AnalyzePrivilegedGroups = AnalyzePrivilegedGroups,
                    AnalyzeRiskyAcls = AnalyzeRiskyAcls,
                    AnalyzeGpos = AnalyzeGpos,
                    AnalyzeSysvol = AnalyzeSysvol,
                    AnalyzeKerberosDelegation = AnalyzeKerberosDelegation,
                    AnalyzeAdminCount = AnalyzeAdminCount,
                    FilterSafeIdentities = FilterSafeIdentities,
                    IncludeForestMode = IncludeForestMode,
                    TargetDomain = string.IsNullOrWhiteSpace(TargetDomain) ? null : TargetDomain,
                    TargetDc = string.IsNullOrWhiteSpace(TargetDc) ? null : TargetDc
                };

                var result = await _analysisService.RunFullAnalysisAsync(options, _cts.Token);
                
                // Update UI
                await Application.Current.Dispatcher.InvokeAsync(() => PopulateResults(result));

                // Save to database
                CurrentRunId = await _databaseService.SaveAnalysisAsync(result);
                AppendLog($"Analysis saved to database (Run ID: {CurrentRunId})");

                // Refresh history
                await LoadHistoryAsync();

                Status = $"Analysis complete. Found {result.TotalFindings} findings in {result.Duration.TotalSeconds:F1}s";
            }
            catch (OperationCanceledException)
            {
                Status = "Analysis cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private async Task RunPrivilegedAnalysisAsync()
        {
            IsAnalyzing = true;
            Status = "Analyzing privileged group memberships...";

            try
            {
                _cts = new CancellationTokenSource();
                var members = await _analysisService.GetPrivilegedMembersAsync(IncludeForestMode, _cts.Token);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    PrivilegedMembers.Clear();
                    foreach (var m in members)
                    {
                        PrivilegedMembers.Add(m);
                    }
                    HasResults = members.Count > 0;
                });

                Status = $"Found {members.Count} privileged accounts";
                AppendLog($"Privileged members: {members.Count}");
            }
            catch (OperationCanceledException)
            {
                Status = "Analysis cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private async Task RunAclAnalysisAsync()
        {
            IsAnalyzing = true;
            Status = "Analyzing ACLs on sensitive objects...";

            try
            {
                _cts = new CancellationTokenSource();
                var acls = await _analysisService.GetRiskyAclsAsync(FilterSafeIdentities, null, _cts.Token);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    RiskyAcls.Clear();
                    foreach (var a in acls)
                    {
                        RiskyAcls.Add(a);
                    }
                    HasResults = acls.Count > 0;
                });

                Status = $"Found {acls.Count} risky ACL entries";
                AppendLog($"Risky ACLs: {acls.Count}");
            }
            catch (OperationCanceledException)
            {
                Status = "Analysis cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private async Task RunDelegationAnalysisAsync()
        {
            IsAnalyzing = true;
            Status = "Analyzing Kerberos delegation...";

            try
            {
                _cts = new CancellationTokenSource();
                var delegations = await _analysisService.GetKerberosDelegationsAsync(_cts.Token);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    KerberosDelegations.Clear();
                    foreach (var d in delegations)
                    {
                        KerberosDelegations.Add(d);
                    }
                    HasResults = delegations.Count > 0;
                });

                Status = $"Found {delegations.Count} accounts with delegation";
                AppendLog($"Kerberos delegations: {delegations.Count}");
            }
            catch (OperationCanceledException)
            {
                Status = "Analysis cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private async Task RunSysvolAnalysisAsync()
        {
            IsAnalyzing = true;
            Status = "Scanning SYSVOL for risky files...";

            try
            {
                _cts = new CancellationTokenSource();
                var files = await _analysisService.ScanSysvolAsync(_cts.Token);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    SysvolFiles.Clear();
                    foreach (var f in files)
                    {
                        SysvolFiles.Add(f);
                    }
                    HasResults = files.Count > 0;
                });

                Status = $"Found {files.Count} risky files in SYSVOL";
                AppendLog($"SYSVOL files: {files.Count}");
            }
            catch (OperationCanceledException)
            {
                Status = "Analysis cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private void CancelAnalysis()
        {
            _cts?.Cancel();
            Status = "Cancelling...";
        }

        #endregion

        #region Results Management

        private void PopulateResults(AdSecurityAnalysisResult result)
        {
            DomainInfo = result.DomainInfo;

            PrivilegedMembers.Clear();
            foreach (var m in result.PrivilegedMembers)
                PrivilegedMembers.Add(m);

            RiskyAcls.Clear();
            foreach (var a in result.RiskyAcls)
                RiskyAcls.Add(a);

            RiskyGpos.Clear();
            foreach (var g in result.RiskyGpos)
                RiskyGpos.Add(g);

            SysvolFiles.Clear();
            foreach (var f in result.SysvolRiskyFiles)
                SysvolFiles.Add(f);

            KerberosDelegations.Clear();
            foreach (var d in result.KerberosDelegations)
                KerberosDelegations.Add(d);

            AdminCountAnomalies.Clear();
            foreach (var a in result.AdminCountAnomalies)
                AdminCountAnomalies.Add(a);

            TotalFindings = result.TotalFindings;
            CriticalFindings = result.CriticalCount;
            HighFindings = result.HighCount;
            MediumFindings = result.MediumCount;
            LowFindings = result.LowCount;
            AnalysisDuration = result.Duration;
            HasResults = result.TotalFindings > 0;
        }

        private void ClearResults()
        {
            PrivilegedMembers.Clear();
            RiskyAcls.Clear();
            RiskyGpos.Clear();
            SysvolFiles.Clear();
            KerberosDelegations.Clear();
            AdminCountAnomalies.Clear();
            TotalFindings = 0;
            CriticalFindings = 0;
            HighFindings = 0;
            MediumFindings = 0;
            LowFindings = 0;
            HasResults = false;
            CurrentRunId = null;
        }

        #endregion

        #region Export Methods

        private async Task ExportToCsvAsync()
        {
            if (!CurrentRunId.HasValue)
            {
                MessageBox.Show("No analysis to export. Run an analysis first.", "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder for CSV export",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    await _databaseService.ExportToCsvAsync(CurrentRunId.Value, dialog.SelectedPath);
                    Status = $"Exported to {dialog.SelectedPath}";
                    AppendLog($"Export complete: {dialog.SelectedPath}");

                    // Open folder
                    Process.Start("explorer.exe", dialog.SelectedPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ExportSelectedRunAsync()
        {
            if (SelectedHistoryRun == null) return;

            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select folder for CSV export",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                try
                {
                    await _databaseService.ExportToCsvAsync(SelectedHistoryRun.Id, dialog.SelectedPath);
                    Status = $"Exported run {SelectedHistoryRun.Id} to {dialog.SelectedPath}";
                    AppendLog($"Export complete: {dialog.SelectedPath}");

                    Process.Start("explorer.exe", dialog.SelectedPath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region History Management

        private async Task LoadHistoryAsync()
        {
            try
            {
                var history = await _databaseService.GetAnalysisHistoryAsync();
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    AnalysisHistory.Clear();
                    foreach (var run in history)
                    {
                        AnalysisHistory.Add(run);
                    }
                });
            }
            catch (Exception ex)
            {
                AppendLog($"Error loading history: {ex.Message}");
            }
        }

        private async Task LoadSelectedRunAsync()
        {
            if (SelectedHistoryRun == null) return;

            try
            {
                Status = $"Loading analysis run {SelectedHistoryRun.Id}...";
                var result = await _databaseService.GetAnalysisResultAsync(SelectedHistoryRun.Id);

                if (result != null)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        PopulateResults(result);
                        CurrentRunId = SelectedHistoryRun.Id;
                        IsDomainDiscovered = true;
                    });

                    Status = $"Loaded analysis from {SelectedHistoryRun.RunTime:g}";
                }
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Error loading run: {ex.Message}");
            }
        }

        private async Task DeleteSelectedRunAsync()
        {
            if (SelectedHistoryRun == null) return;

            var result = MessageBox.Show(
                $"Delete analysis run from {SelectedHistoryRun.RunTime:g}?",
                "Confirm Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _databaseService.DeleteAnalysisRunAsync(SelectedHistoryRun.Id);
                    await LoadHistoryAsync();
                    Status = "Analysis run deleted.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Delete failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task ClearHistoryAsync()
        {
            var result = MessageBox.Show(
                "Delete ALL analysis history? This cannot be undone.",
                "Confirm Clear History",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    await _databaseService.ClearAllHistoryAsync();
                    await LoadHistoryAsync();
                    ClearResults();
                    Status = "All history cleared.";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Clear failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenDatabaseFolder()
        {
            var folder = Path.GetDirectoryName(_databaseService.DatabasePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
            {
                Process.Start("explorer.exe", folder);
            }
        }

        #endregion

        #region Logging

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var entry = $"[{timestamp}] {message}";

            Application.Current?.Dispatcher?.InvokeAsync(() =>
            {
                LogMessages.Add(entry);
                
                // Keep log size manageable
                while (LogMessages.Count > 1000)
                {
                    LogMessages.RemoveAt(0);
                }
            });
        }

        #endregion

        #region Deployment Methods

        private void PreviewDeployment()
        {
            var preview = new System.Text.StringBuilder();
            preview.AppendLine("=== OU Structure Preview ===");
            preview.AppendLine();
            
            if (DomainInfo != null)
            {
                preview.AppendLine($"Target Domain: {DomainInfo.DomainFqdn}");
                preview.AppendLine($"Domain DN: {DomainInfo.DomainDn}");
                preview.AppendLine();
            }

            preview.AppendLine($"OU={DeploymentBaseName},{DomainInfo?.DomainDn ?? "DC=domain,DC=local"}");
            preview.AppendLine($"  ├── OU={Tier0Name}");
            if (CreatePawOus) preview.AppendLine($"  │   ├── OU=PAW");
            if (CreateServiceAccountOus) preview.AppendLine($"  │   ├── OU=ServiceAccounts");
            if (CreateGroupsOus) preview.AppendLine($"  │   ├── OU=Groups");
            if (CreateUsersOus) preview.AppendLine($"  │   └── OU=Users");
            
            preview.AppendLine($"  ├── OU={Tier1Name}");
            if (CreatePawOus) preview.AppendLine($"  │   ├── OU=PAW");
            if (CreateServiceAccountOus) preview.AppendLine($"  │   ├── OU=ServiceAccounts");
            if (CreateGroupsOus) preview.AppendLine($"  │   ├── OU=Groups");
            if (CreateUsersOus) preview.AppendLine($"  │   ├── OU=Users");
            if (CreateDevicesOus) preview.AppendLine($"  │   └── OU=Servers");
            
            preview.AppendLine($"  └── OU={Tier2Name}");
            if (CreatePawOus) preview.AppendLine($"      ├── OU=PAW");
            if (CreateServiceAccountOus) preview.AppendLine($"      ├── OU=ServiceAccounts");
            if (CreateGroupsOus) preview.AppendLine($"      ├── OU=Groups");
            if (CreateUsersOus) preview.AppendLine($"      ├── OU=Users");
            if (CreateDevicesOus) preview.AppendLine($"      └── OU=Workstations");

            preview.AppendLine();
            preview.AppendLine("=== GPO Preview ===");
            preview.AppendLine();
            if (DeployPasswordPolicyGpo) preview.AppendLine("• Password Policy GPO (linked to domain)");
            if (DeployAuditPolicyGpo) preview.AppendLine("• Advanced Audit Policy GPO (linked to domain)");
            if (DeploySecurityBaselineGpo) preview.AppendLine("• Security Baseline GPO (linked to each tier)");
            if (DeployPawGpo) preview.AppendLine("• PAW Security GPO (linked to PAW OUs)");

            preview.AppendLine();
            preview.AppendLine($"Protection from deletion: {(ProtectOusFromDeletion ? "Enabled" : "Disabled")}");

            DeploymentPreview = preview.ToString();
        }

        private async Task DeployTieredOusAsync()
        {
            if (DomainInfo == null)
            {
                MessageBox.Show("Please discover the domain first.", "Domain Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Create tiered admin OU structure in {DomainInfo.DomainFqdn}?\n\n" +
                "This will create:\n" +
                $"• {DeploymentBaseName} (base OU)\n" +
                $"• {Tier0Name}, {Tier1Name}, {Tier2Name} (tier OUs)\n" +
                "• Sub-OUs for PAW, ServiceAccounts, Groups, Users, Devices\n\n" +
                "Continue?",
                "Confirm OU Deployment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsAnalyzing = true;
            Status = "Deploying tiered OU structure...";

            try
            {
                _cts = new CancellationTokenSource();
                DeploymentResults.Clear();

                var template = new BillOuTemplate
                {
                    BaseName = DeploymentBaseName,
                    Tier0Name = Tier0Name,
                    Tier1Name = Tier1Name,
                    Tier2Name = Tier2Name,
                    CreatePawOus = CreatePawOus,
                    CreateServiceAccountOus = CreateServiceAccountOus,
                    CreateGroupsOus = CreateGroupsOus
                };

                var results = await _analysisService.DeployTieredOuStructureAsync(
                    template, 
                    ProtectOusFromDeletion,
                    CreateUsersOus,
                    CreateDevicesOus,
                    _cts.Token);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var r in results)
                    {
                        DeploymentResults.Add(r);
                        AppendLog($"{(r.Success ? "✓" : "✗")} {r.ObjectType}: {r.ObjectName} - {r.Message}");
                    }
                });

                var successCount = results.Count(r => r.Success);
                var failCount = results.Count(r => !r.Success);
                Status = $"Deployment complete: {successCount} succeeded, {failCount} failed";
            }
            catch (OperationCanceledException)
            {
                Status = "Deployment cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"Deployment error: {ex.Message}");
                MessageBox.Show($"Deployment failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private async Task DeployBaselineGposAsync()
        {
            if (DomainInfo == null)
            {
                MessageBox.Show("Please discover the domain first.", "Domain Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Deploy baseline GPOs to {DomainInfo.DomainFqdn}?\n\n" +
                "This will create and link:\n" +
                (DeployPasswordPolicyGpo ? "• Password Policy GPO\n" : "") +
                (DeployAuditPolicyGpo ? "• Advanced Audit Policy GPO\n" : "") +
                (DeploySecurityBaselineGpo ? "• Security Baseline GPO\n" : "") +
                (DeployPawGpo ? "• PAW Security GPO\n" : "") +
                "\nContinue?",
                "Confirm GPO Deployment",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            IsAnalyzing = true;
            Status = "Deploying baseline GPOs...";

            try
            {
                _cts = new CancellationTokenSource();

                var gpoOptions = new GpoDeploymentOptions
                {
                    DeployPasswordPolicy = DeployPasswordPolicyGpo,
                    DeployAuditPolicy = DeployAuditPolicyGpo,
                    DeploySecurityBaseline = DeploySecurityBaselineGpo,
                    DeployPawPolicy = DeployPawGpo,
                    TieredOuBaseName = DeploymentBaseName
                };

                var results = await _analysisService.DeployBaselineGposAsync(gpoOptions, _cts.Token);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var r in results)
                    {
                        DeploymentResults.Add(r);
                        AppendLog($"{(r.Success ? "✓" : "✗")} {r.ObjectType}: {r.ObjectName} - {r.Message}");
                    }
                });

                var successCount = results.Count(r => r.Success);
                var failCount = results.Count(r => !r.Success);
                Status = $"GPO deployment complete: {successCount} succeeded, {failCount} failed";
            }
            catch (OperationCanceledException)
            {
                Status = "GPO deployment cancelled.";
            }
            catch (Exception ex)
            {
                Status = $"Error: {ex.Message}";
                AppendLog($"GPO deployment error: {ex.Message}");
                MessageBox.Show($"GPO deployment failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        #endregion
    }
}
