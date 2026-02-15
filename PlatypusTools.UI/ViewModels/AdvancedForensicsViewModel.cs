using System;
using System.Collections.ObjectModel;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Advanced DFIR (Digital Forensics & Incident Response) features based on the DFIR Playbook.
    /// Supports memory acquisition, artifact parsing, OpenSearch integration, Kusto queries, 
    /// Plaso timelines, Velociraptor, oletools, bulk_extractor, and OSINT metadata extraction.
    /// </summary>
    public class AdvancedForensicsViewModel : BindableBase
    {
        #region Fields
        private CancellationTokenSource? _cancellationTokenSource;
        private readonly HttpClient _httpClient;
        private ForensicsKqlEngine? _kqlEngine;
        private bool _kqlEngineInitialized;
        #endregion

        #region Properties

        // ============ MEMORY ACQUISITION ============
        private string _winPmemPath = string.Empty;
        public string WinPmemPath
        {
            get => _winPmemPath;
            set => SetProperty(ref _winPmemPath, value);
        }

        private string _dumpOutputPath = string.Empty;
        public string DumpOutputPath
        {
            get => _dumpOutputPath;
            set => SetProperty(ref _dumpOutputPath, value);
        }

        private string _dumpFormat = "raw";
        public string DumpFormat
        {
            get => _dumpFormat;
            set => SetProperty(ref _dumpFormat, value);
        }

        public ObservableCollection<string> DumpFormats { get; } = new() { "raw", "aff4", "crashdump" };
        
        // Memory acquisition tool selection
        private string _selectedMemoryTool = "WinPmem";
        public string SelectedMemoryTool
        {
            get => _selectedMemoryTool;
            set => SetProperty(ref _selectedMemoryTool, value);
        }
        
        public ObservableCollection<string> MemoryAcquisitionTools { get; } = new() 
        { 
            "WinPmem", 
            "Magnet RAM Capture", 
            "ProcDump (Process)"
        };
        
        // Magnet RAM Capture
        private string _magnetRamCapturePath = string.Empty;
        public string MagnetRamCapturePath
        {
            get => _magnetRamCapturePath;
            set => SetProperty(ref _magnetRamCapturePath, value);
        }
        
        // ProcDump
        private string _procDumpPath = string.Empty;
        public string ProcDumpPath
        {
            get => _procDumpPath;
            set => SetProperty(ref _procDumpPath, value);
        }
        
        private string _selectedProcessForDump = string.Empty;
        public string SelectedProcessForDump
        {
            get => _selectedProcessForDump;
            set => SetProperty(ref _selectedProcessForDump, value);
        }
        
        private bool _procDumpFullDump = true;
        public bool ProcDumpFullDump
        {
            get => _procDumpFullDump;
            set => SetProperty(ref _procDumpFullDump, value);
        }
        
        public ObservableCollection<ProcessInfo> RunningProcesses { get; } = new();
        
        // Memory Analysis (existing)
        private string _memoryDumpPath = string.Empty;
        public string MemoryDumpPath
        {
            get => _memoryDumpPath;
            set => SetProperty(ref _memoryDumpPath, value);
        }

        private string _memoryOutputPath = string.Empty;
        public string MemoryOutputPath
        {
            get => _memoryOutputPath;
            set => SetProperty(ref _memoryOutputPath, value);
        }

        // Volatility Analysis
        private string _volatilityPath = string.Empty;
        public string VolatilityPath
        {
            get => _volatilityPath;
            set => SetProperty(ref _volatilityPath, value);
        }

        private bool _runPsList = true;
        public bool RunPsList
        {
            get => _runPsList;
            set => SetProperty(ref _runPsList, value);
        }

        private bool _runNetScan = true;
        public bool RunNetScan
        {
            get => _runNetScan;
            set => SetProperty(ref _runNetScan, value);
        }

        private bool _runMalfind = true;
        public bool RunMalfind
        {
            get => _runMalfind;
            set => SetProperty(ref _runMalfind, value);
        }

        private bool _runDllList = true;
        public bool RunDllList
        {
            get => _runDllList;
            set => SetProperty(ref _runDllList, value);
        }

        private bool _runHandles = false;
        public bool RunHandles
        {
            get => _runHandles;
            set => SetProperty(ref _runHandles, value);
        }

        private bool _runCmdline = true;
        public bool RunCmdline
        {
            get => _runCmdline;
            set => SetProperty(ref _runCmdline, value);
        }

        private bool _runFileScan = false;
        public bool RunFileScan
        {
            get => _runFileScan;
            set => SetProperty(ref _runFileScan, value);
        }

        private bool _runRegHive = false;
        public bool RunRegHive
        {
            get => _runRegHive;
            set => SetProperty(ref _runRegHive, value);
        }

        // Advanced Volatility plugins
        private bool _runPsTree = false;
        public bool RunPsTree
        {
            get => _runPsTree;
            set => SetProperty(ref _runPsTree, value);
        }

        private bool _runEnvars = false;
        public bool RunEnvars
        {
            get => _runEnvars;
            set => SetProperty(ref _runEnvars, value);
        }

        private bool _runSvcScan = false;
        public bool RunSvcScan
        {
            get => _runSvcScan;
            set => SetProperty(ref _runSvcScan, value);
        }

        private bool _runCallbacks = false;
        public bool RunCallbacks
        {
            get => _runCallbacks;
            set => SetProperty(ref _runCallbacks, value);
        }

        private bool _runDriverScan = false;
        public bool RunDriverScan
        {
            get => _runDriverScan;
            set => SetProperty(ref _runDriverScan, value);
        }

        private bool _runSsdt = false;
        public bool RunSsdt
        {
            get => _runSsdt;
            set => SetProperty(ref _runSsdt, value);
        }

        private bool _runMutantScan = false;
        public bool RunMutantScan
        {
            get => _runMutantScan;
            set => SetProperty(ref _runMutantScan, value);
        }

        // ============ KAPE/EZ TOOLS ============
        private string _kapePath = string.Empty;
        public string KapePath
        {
            get => _kapePath;
            set => SetProperty(ref _kapePath, value);
        }

        private string _kapeTargetPath = string.Empty;
        public string KapeTargetPath
        {
            get => _kapeTargetPath;
            set => SetProperty(ref _kapeTargetPath, value);
        }

        private string _kapeOutputPath = string.Empty;
        public string KapeOutputPath
        {
            get => _kapeOutputPath;
            set => SetProperty(ref _kapeOutputPath, value);
        }

        private bool _collectPrefetch = true;
        public bool CollectPrefetch
        {
            get => _collectPrefetch;
            set => SetProperty(ref _collectPrefetch, value);
        }

        private bool _collectAmcache = true;
        public bool CollectAmcache
        {
            get => _collectAmcache;
            set => SetProperty(ref _collectAmcache, value);
        }

        private bool _collectEventLogs = true;
        public bool CollectEventLogs
        {
            get => _collectEventLogs;
            set => SetProperty(ref _collectEventLogs, value);
        }

        private bool _collectMft = true;
        public bool CollectMft
        {
            get => _collectMft;
            set => SetProperty(ref _collectMft, value);
        }

        private bool _collectRegistry = true;
        public bool CollectRegistry
        {
            get => _collectRegistry;
            set => SetProperty(ref _collectRegistry, value);
        }

        private bool _collectSrum = true;
        public bool CollectSrum
        {
            get => _collectSrum;
            set => SetProperty(ref _collectSrum, value);
        }

        // Additional artifacts
        private bool _collectShellBags = false;
        public bool CollectShellBags
        {
            get => _collectShellBags;
            set => SetProperty(ref _collectShellBags, value);
        }

        private bool _collectJumpLists = false;
        public bool CollectJumpLists
        {
            get => _collectJumpLists;
            set => SetProperty(ref _collectJumpLists, value);
        }

        private bool _collectLnkFiles = false;
        public bool CollectLnkFiles
        {
            get => _collectLnkFiles;
            set => SetProperty(ref _collectLnkFiles, value);
        }

        private bool _collectShimcache = false;
        public bool CollectShimcache
        {
            get => _collectShimcache;
            set => SetProperty(ref _collectShimcache, value);
        }

        private bool _collectRecycleBin = false;
        public bool CollectRecycleBin
        {
            get => _collectRecycleBin;
            set => SetProperty(ref _collectRecycleBin, value);
        }

        private bool _collectUsnJrnl = false;
        public bool CollectUsnJrnl
        {
            get => _collectUsnJrnl;
            set => SetProperty(ref _collectUsnJrnl, value);
        }

        // ============ PLASO INTEGRATION ============
        private string _plasoPath = string.Empty;
        public string PlasoPath
        {
            get => _plasoPath;
            set => SetProperty(ref _plasoPath, value);
        }

        private string _plasoStorageFile = string.Empty;
        public string PlasoStorageFile
        {
            get => _plasoStorageFile;
            set => SetProperty(ref _plasoStorageFile, value);
        }

        private string _plasoEvidencePath = string.Empty;
        public string PlasoEvidencePath
        {
            get => _plasoEvidencePath;
            set => SetProperty(ref _plasoEvidencePath, value);
        }

        // ============ VELOCIRAPTOR ============
        private string _velociraptorPath = string.Empty;
        public string VelociraptorPath
        {
            get => _velociraptorPath;
            set => SetProperty(ref _velociraptorPath, value);
        }

        private string _velociraptorConfig = string.Empty;
        public string VelociraptorConfig
        {
            get => _velociraptorConfig;
            set => SetProperty(ref _velociraptorConfig, value);
        }

        private string _velociraptorArtifact = "Windows.KapeFiles.Targets";
        public string VelociraptorArtifact
        {
            get => _velociraptorArtifact;
            set => SetProperty(ref _velociraptorArtifact, value);
        }

        public ObservableCollection<string> VelociraptorArtifacts { get; } = new()
        {
            "Windows.KapeFiles.Targets",
            "Windows.System.TaskScheduler",
            "Windows.System.Services",
            "Windows.Registry.Autoruns",
            "Windows.Forensics.Prefetch",
            "Windows.Forensics.Amcache",
            "Windows.Network.Netstat",
            "Windows.Memory.Acquisition"
        };

        // ============ KUSTO QUERIES ============
        private string _kustoQuery = string.Empty;
        public string KustoQuery
        {
            get => _kustoQuery;
            set => SetProperty(ref _kustoQuery, value);
        }

        private string _kustoEndpoint = string.Empty;
        public string KustoEndpoint
        {
            get => _kustoEndpoint;
            set => SetProperty(ref _kustoEndpoint, value);
        }

        private string _kustoDatabase = string.Empty;
        public string KustoDatabase
        {
            get => _kustoDatabase;
            set => SetProperty(ref _kustoDatabase, value);
        }

        public ObservableCollection<KustoQueryTemplate> KustoTemplates { get; } = new();

        private KustoQueryTemplate? _selectedKustoTemplate;
        public KustoQueryTemplate? SelectedKustoTemplate
        {
            get => _selectedKustoTemplate;
            set
            {
                if (SetProperty(ref _selectedKustoTemplate, value) && value != null)
                {
                    KustoQuery = value.Query;
                }
            }
        }

        // ============ LOCAL KQL DATABASE ============
        private string _localKqlQuery = string.Empty;
        public string LocalKqlQuery
        {
            get => _localKqlQuery;
            set => SetProperty(ref _localKqlQuery, value);
        }

        private DataTable? _localKqlResults;
        public DataTable? LocalKqlResults
        {
            get => _localKqlResults;
            set => SetProperty(ref _localKqlResults, value);
        }

        private string _translatedSql = string.Empty;
        public string TranslatedSql
        {
            get => _translatedSql;
            set => SetProperty(ref _translatedSql, value);
        }

        private string _localDbStatus = "Not initialized";
        public string LocalDbStatus
        {
            get => _localDbStatus;
            set => SetProperty(ref _localDbStatus, value);
        }

        private string _selectedLocalKqlCategory = "All";
        public string SelectedLocalKqlCategory
        {
            get => _selectedLocalKqlCategory;
            set
            {
                if (SetProperty(ref _selectedLocalKqlCategory, value))
                {
                    UpdateFilteredKqlTemplates();
                }
            }
        }

        public ObservableCollection<string> LocalKqlCategories { get; } = new() { "All" };
        public ObservableCollection<KqlQueryTemplate> LocalKqlTemplates { get; } = new();
        public ObservableCollection<KqlQueryTemplate> FilteredLocalKqlTemplates { get; } = new();

        private KqlQueryTemplate? _selectedLocalKqlTemplate;
        public KqlQueryTemplate? SelectedLocalKqlTemplate
        {
            get => _selectedLocalKqlTemplate;
            set
            {
                if (SetProperty(ref _selectedLocalKqlTemplate, value) && value != null)
                {
                    LocalKqlQuery = value.Query;
                }
            }
        }

        public ObservableCollection<string> AvailableTables { get; } = new();

        private string _selectedTable = string.Empty;
        public string SelectedTable
        {
            get => _selectedTable;
            set
            {
                if (SetProperty(ref _selectedTable, value) && !string.IsNullOrEmpty(value))
                {
                    _ = LoadTableColumnsAsync(value);
                }
            }
        }

        public ObservableCollection<string> TableColumns { get; } = new();

        private long _totalRecords;
        public long TotalRecords
        {
            get => _totalRecords;
            set => SetProperty(ref _totalRecords, value);
        }

        // ============ OLETOOLS/PDF ANALYZER ============
        private string _oletoolsPath = string.Empty;
        public string OletoolsPath
        {
            get => _oletoolsPath;
            set => SetProperty(ref _oletoolsPath, value);
        }

        private string _pdfParserPath = string.Empty;
        public string PdfParserPath
        {
            get => _pdfParserPath;
            set => SetProperty(ref _pdfParserPath, value);
        }

        private bool _runOlevba = true;
        public bool RunOlevba
        {
            get => _runOlevba;
            set => SetProperty(ref _runOlevba, value);
        }

        private bool _runMraptor = true;
        public bool RunMraptor
        {
            get => _runMraptor;
            set => SetProperty(ref _runMraptor, value);
        }

        private bool _runPdfParser = true;
        public bool RunPdfParser
        {
            get => _runPdfParser;
            set => SetProperty(ref _runPdfParser, value);
        }

        // ============ BULK EXTRACTOR ============
        private string _bulkExtractorPath = string.Empty;
        public string BulkExtractorPath
        {
            get => _bulkExtractorPath;
            set => SetProperty(ref _bulkExtractorPath, value);
        }

        private string _bulkExtractorInput = string.Empty;
        public string BulkExtractorInput
        {
            get => _bulkExtractorInput;
            set => SetProperty(ref _bulkExtractorInput, value);
        }

        private bool _extractEmails = true;
        public bool ExtractEmails
        {
            get => _extractEmails;
            set => SetProperty(ref _extractEmails, value);
        }

        private bool _extractUrls = true;
        public bool ExtractUrls
        {
            get => _extractUrls;
            set => SetProperty(ref _extractUrls, value);
        }

        private bool _extractCreditCards = false;
        public bool ExtractCreditCards
        {
            get => _extractCreditCards;
            set => SetProperty(ref _extractCreditCards, value);
        }

        private bool _extractIpAddresses = true;
        public bool ExtractIpAddresses
        {
            get => _extractIpAddresses;
            set => SetProperty(ref _extractIpAddresses, value);
        }

        // ============ OPENSEARCH INTEGRATION ============
        private string _openSearchUrl = "http://localhost:9200";
        public string OpenSearchUrl
        {
            get => _openSearchUrl;
            set => SetProperty(ref _openSearchUrl, value);
        }

        private string _indexName = "dfir-artifacts";
        public string IndexName
        {
            get => _indexName;
            set => SetProperty(ref _indexName, value);
        }

        private string _pipelineName = "dfir-volatility";
        public string PipelineName
        {
            get => _pipelineName;
            set => SetProperty(ref _pipelineName, value);
        }

        private bool _isOpenSearchConnected;
        public bool IsOpenSearchConnected
        {
            get => _isOpenSearchConnected;
            set
            {
                if (SetProperty(ref _isOpenSearchConnected, value))
                {
                    RaisePropertyChanged(nameof(OpenSearchConnectionStatus));
                }
            }
        }

        public string OpenSearchConnectionStatus => IsOpenSearchConnected ? "Status: ✅ Connected" : "Status: ❌ Not Connected";

        // ============ OSINT/METADATA ============
        private string _documentPath = string.Empty;
        public string DocumentPath
        {
            get => _documentPath;
            set => SetProperty(ref _documentPath, value);
        }

        private string _exifToolPath = string.Empty;
        public string ExifToolPath
        {
            get => _exifToolPath;
            set => SetProperty(ref _exifToolPath, value);
        }

        // ============ STATUS ============
        private bool _isRunning;
        public bool IsRunning
        {
            get => _isRunning;
            set => SetProperty(ref _isRunning, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _outputLog = string.Empty;
        public string OutputLog
        {
            get => _outputLog;
            set => SetProperty(ref _outputLog, value);
        }

        // ============ RESULTS ============
        public ObservableCollection<ForensicArtifact> Artifacts { get; } = new();
        public ObservableCollection<DocumentMetadata> ExtractedMetadata { get; } = new();
        public ObservableCollection<MalwareAnalysisResult> MalwareResults { get; } = new();
        public ObservableCollection<BulkExtractorResult> BulkExtractorResults { get; } = new();

        // ============ TASK SCHEDULER (DFIR) ============
        private string _schedulerTaskName = string.Empty;
        public string SchedulerTaskName
        {
            get => _schedulerTaskName;
            set => SetProperty(ref _schedulerTaskName, value);
        }

        private string _schedulerTaskFilter = string.Empty;
        public string SchedulerTaskFilter
        {
            get => _schedulerTaskFilter;
            set => SetProperty(ref _schedulerTaskFilter, value);
        }

        public ObservableCollection<ScheduledTaskInfo> ScheduledTasks { get; } = new();

        // ============ BROWSER FORENSICS ============
        private string _browserForensicsProfile = string.Empty;
        public string BrowserForensicsProfile
        {
            get => _browserForensicsProfile;
            set => SetProperty(ref _browserForensicsProfile, value);
        }

        private bool _extractBrowserHistory = true;
        public bool ExtractBrowserHistory
        {
            get => _extractBrowserHistory;
            set => SetProperty(ref _extractBrowserHistory, value);
        }

        private bool _extractBrowserCookies = true;
        public bool ExtractBrowserCookies
        {
            get => _extractBrowserCookies;
            set => SetProperty(ref _extractBrowserCookies, value);
        }

        private bool _extractBrowserDownloads = true;
        public bool ExtractBrowserDownloads
        {
            get => _extractBrowserDownloads;
            set => SetProperty(ref _extractBrowserDownloads, value);
        }

        private bool _extractBrowserPasswords = false;
        public bool ExtractBrowserPasswords
        {
            get => _extractBrowserPasswords;
            set => SetProperty(ref _extractBrowserPasswords, value);
        }

        public ObservableCollection<BrowserArtifact> BrowserArtifacts { get; } = new();

        // ============ IOC SCANNER ============
        private string _iocScanPath = string.Empty;
        public string IOCScanPath
        {
            get => _iocScanPath;
            set => SetProperty(ref _iocScanPath, value);
        }

        private string _iocFeedUrl = string.Empty;
        public string IOCFeedUrl
        {
            get => _iocFeedUrl;
            set => SetProperty(ref _iocFeedUrl, value);
        }

        private bool _scanIOCFiles = true;
        public bool ScanIOCFiles
        {
            get => _scanIOCFiles;
            set => SetProperty(ref _scanIOCFiles, value);
        }

        private bool _scanIOCRegistry = true;
        public bool ScanIOCRegistry
        {
            get => _scanIOCRegistry;
            set => SetProperty(ref _scanIOCRegistry, value);
        }

        private bool _scanIOCNetwork = true;
        public bool ScanIOCNetwork
        {
            get => _scanIOCNetwork;
            set => SetProperty(ref _scanIOCNetwork, value);
        }

        public ObservableCollection<IOCMatch> IOCMatches { get; } = new();

        // ============ REGISTRY DIFF ============
        public string RegistrySnapshotFolder => Path.Combine(
            PlatypusTools.UI.Services.SettingsManager.DataDirectory, "RegistrySnapshots");

        private string _registrySnapshot1Path = string.Empty;
        public string RegistrySnapshot1Path
        {
            get => _registrySnapshot1Path;
            set => SetProperty(ref _registrySnapshot1Path, value);
        }

        private string _registrySnapshot2Path = string.Empty;
        public string RegistrySnapshot2Path
        {
            get => _registrySnapshot2Path;
            set => SetProperty(ref _registrySnapshot2Path, value);
        }

        private string _registryHiveFilter = "HKLM";
        public string RegistryHiveFilter
        {
            get => _registryHiveFilter;
            set => SetProperty(ref _registryHiveFilter, value);
        }

        public ObservableCollection<string> RegistryHives { get; } = new() { "HKLM", "HKCU", "HKU", "HKCR", "HKCC" };
        public ObservableCollection<RegistryDiffEntry> RegistryDiffs { get; } = new();

        // ============ PCAP PARSER ============
        private string _pcapFilePath = string.Empty;
        public string PcapFilePath
        {
            get => _pcapFilePath;
            set => SetProperty(ref _pcapFilePath, value);
        }

        private bool _filterHttpTraffic = true;
        public bool FilterHttpTraffic
        {
            get => _filterHttpTraffic;
            set => SetProperty(ref _filterHttpTraffic, value);
        }

        private bool _filterDnsTraffic = true;
        public bool FilterDnsTraffic
        {
            get => _filterDnsTraffic;
            set => SetProperty(ref _filterDnsTraffic, value);
        }

        private bool _extractPcapFiles = false;
        public bool ExtractPcapFiles
        {
            get => _extractPcapFiles;
            set => SetProperty(ref _extractPcapFiles, value);
        }

        public ObservableCollection<NetworkPacketInfo> ParsedPackets { get; } = new();
        public ObservableCollection<NetworkConnectionInfo> NetworkConnections { get; } = new();

        #endregion

        #region Commands

        // Memory Acquisition
        public ICommand BrowseWinPmemCommand { get; }
        public ICommand BrowseDumpOutputCommand { get; }
        public ICommand AcquireMemoryDumpCommand { get; }
        
        // Magnet RAM Capture
        public ICommand BrowseMagnetRamCaptureCommand { get; }
        public ICommand DownloadMagnetRamCaptureCommand { get; }
        public ICommand AcquireMagnetRamCaptureCommand { get; }
        
        // ProcDump
        public ICommand BrowseProcDumpCommand { get; }
        public ICommand DownloadProcDumpCommand { get; }
        public ICommand RefreshProcessListCommand { get; }
        public ICommand AcquireProcDumpCommand { get; }

        // Memory Analysis
        public ICommand BrowseMemoryDumpCommand { get; }
        public ICommand BrowseVolatilityCommand { get; }
        public ICommand RunVolatilityAnalysisCommand { get; }

        // KAPE
        public ICommand BrowseKapeCommand { get; }
        public ICommand BrowseKapeTargetCommand { get; }
        public ICommand RunKapeCollectionCommand { get; }

        // Plaso
        public ICommand BrowsePlasoCommand { get; }
        public ICommand BrowseEvidenceCommand { get; }
        public ICommand RunPlasoTimelineCommand { get; }
        public ICommand ExportPlasoToOpenSearchCommand { get; }

        // Velociraptor
        public ICommand BrowseVelociraptorCommand { get; }
        public ICommand RunVelociraptorCollectCommand { get; }

        // Kusto
        public ICommand RunKustoQueryCommand { get; }
        public ICommand LoadKustoTemplateCommand { get; }

        // Local KQL Database
        public ICommand InitializeLocalDbCommand { get; }
        public ICommand ExecuteLocalKqlCommand { get; }
        public ICommand ClearLocalDbCommand { get; }
        public ICommand InsertTableQueryCommand { get; }
        public ICommand RefreshTablesCommand { get; }
        public ICommand ExportLocalResultsCommand { get; }

        // Plaso
        public ICommand DownloadPlasoCommand { get; }

        // Oletools/PDF
        public ICommand BrowseOletoolsCommand { get; }
        public ICommand BrowsePdfParserCommand { get; }
        public ICommand RunMalwareAnalysisCommand { get; }
        public ICommand DownloadOletoolsCommand { get; }
        public ICommand DownloadPdfParserCommand { get; }

        // Bulk Extractor
        public ICommand BrowseBulkExtractorCommand { get; }
        public ICommand BrowseBulkInputCommand { get; }
        public ICommand RunBulkExtractorCommand { get; }
        public ICommand DownloadBulkExtractorCommand { get; }

        // OpenSearch
        public ICommand TestOpenSearchCommand { get; }
        public ICommand CreatePipelinesCommand { get; }
        public ICommand CreateIndexTemplatesCommand { get; }
        public ICommand IngestToOpenSearchCommand { get; }

        // OSINT
        public ICommand BrowseDocumentsCommand { get; }
        public ICommand BrowseExifToolCommand { get; }
        public ICommand ExtractMetadataCommand { get; }

        // Tool Downloads
        public ICommand DownloadWinPmemCommand { get; }
        public ICommand DownloadVolatilityCommand { get; }
        public ICommand GetKapeCommand { get; }
        public ICommand DownloadExifToolCommand { get; }
        public ICommand DownloadVelociraptorCommand { get; }

        // General
        public ICommand CancelCommand { get; }
        public ICommand ClearLogCommand { get; }
        public ICommand ExportResultsCommand { get; }

        // Task Scheduler (DFIR)
        public ICommand ScanScheduledTasksCommand { get; }
        public ICommand ExportScheduledTasksCommand { get; }

        // Browser Forensics
        public ICommand ScanBrowserArtifactsCommand { get; }
        public ICommand BrowseBrowserProfileCommand { get; }
        public ICommand ExportBrowserArtifactsCommand { get; }

        // IOC Scanner
        public ICommand ScanForIOCsCommand { get; }
        public ICommand BrowseIOCPathCommand { get; }
        public ICommand LoadIOCFeedCommand { get; }
        public ICommand ExportIOCResultsCommand { get; }

        // Registry Diff
        public ICommand TakeRegistrySnapshotCommand { get; }
        public ICommand CompareRegistrySnapshotsCommand { get; }
        public ICommand BrowseSnapshot1Command { get; }
        public ICommand BrowseSnapshot2Command { get; }
        public ICommand ExportRegistryDiffCommand { get; }
        public ICommand OpenRegistrySnapshotFolderCommand { get; }

        // PCAP Parser
        public ICommand ParsePcapCommand { get; }
        public ICommand BrowsePcapFileCommand { get; }
        public ICommand ExportPcapResultsCommand { get; }

        #endregion

        public AdvancedForensicsViewModel()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatypusTools/3.2");

            // Initialize Kusto templates
            InitializeKustoTemplates();

            // Memory Acquisition Commands (WinPmem)
            BrowseWinPmemCommand = new RelayCommand(_ => BrowseFile("WinPmem Executable", "Executable|winpmem*.exe|All files|*.*", s => WinPmemPath = s));
            BrowseDumpOutputCommand = new RelayCommand(_ => BrowseFolder("Select output folder for memory dump", s => DumpOutputPath = s));
            AcquireMemoryDumpCommand = new AsyncRelayCommand(AcquireMemoryDumpAsync, () => !IsRunning);
            
            // Magnet RAM Capture Commands
            BrowseMagnetRamCaptureCommand = new RelayCommand(_ => BrowseFile("Magnet RAM Capture", "Executable|MagnetRAMCapture*.exe;MRC.exe|All files|*.*", s => MagnetRamCapturePath = s));
            DownloadMagnetRamCaptureCommand = new RelayCommand(_ => OpenUrl("https://www.magnetforensics.com/resources/magnet-ram-capture/"));
            AcquireMagnetRamCaptureCommand = new AsyncRelayCommand(AcquireMagnetRamCaptureAsync, () => !IsRunning);
            
            // ProcDump Commands
            BrowseProcDumpCommand = new RelayCommand(_ => BrowseFile("ProcDump Executable", "Executable|procdump*.exe|All files|*.*", s => ProcDumpPath = s));
            DownloadProcDumpCommand = new AsyncRelayCommand(DownloadProcDumpAsync, () => !IsRunning);
            RefreshProcessListCommand = new RelayCommand(_ => RefreshRunningProcesses());
            AcquireProcDumpCommand = new AsyncRelayCommand(AcquireProcDumpAsync, () => !IsRunning);

            // Memory Analysis Commands
            BrowseMemoryDumpCommand = new RelayCommand(_ => BrowseFile("Memory Dump", "Raw/DMP files|*.raw;*.dmp;*.mem;*.aff4|All files|*.*", s => MemoryDumpPath = s));
            BrowseVolatilityCommand = new RelayCommand(_ => BrowseFolder("Select Volatility 3 folder", s => VolatilityPath = s));
            RunVolatilityAnalysisCommand = new AsyncRelayCommand(RunVolatilityAnalysisAsync, () => !IsRunning);

            // KAPE Commands
            BrowseKapeCommand = new RelayCommand(_ => BrowseFile("KAPE Executable", "Executable|kape.exe|All files|*.*", s => KapePath = s));
            BrowseKapeTargetCommand = new RelayCommand(_ => BrowseFolder("Select target drive/folder", s => KapeTargetPath = s));
            RunKapeCollectionCommand = new AsyncRelayCommand(RunKapeCollectionAsync, () => !IsRunning);

            // Plaso Commands
            BrowsePlasoCommand = new RelayCommand(_ => BrowseFolder("Select Plaso installation folder", s => PlasoPath = s));
            BrowseEvidenceCommand = new RelayCommand(_ => BrowseFolder("Select evidence folder", s => PlasoEvidencePath = s));
            RunPlasoTimelineCommand = new AsyncRelayCommand(RunPlasoTimelineAsync, () => !IsRunning);
            ExportPlasoToOpenSearchCommand = new AsyncRelayCommand(ExportPlasoToOpenSearchAsync, () => !IsRunning);

            // Velociraptor Commands
            BrowseVelociraptorCommand = new RelayCommand(_ => BrowseFile("Velociraptor Executable", "Executable|velociraptor*.exe|All files|*.*", s => VelociraptorPath = s));
            RunVelociraptorCollectCommand = new AsyncRelayCommand(RunVelociraptorCollectAsync, () => !IsRunning);

            // Kusto Commands
            RunKustoQueryCommand = new AsyncRelayCommand(RunKustoQueryAsync, () => !IsRunning);
            LoadKustoTemplateCommand = new RelayCommand(_ => { if (SelectedKustoTemplate != null) KustoQuery = SelectedKustoTemplate.Query; });

            // Local KQL Database Commands
            InitializeLocalDbCommand = new AsyncRelayCommand(InitializeLocalKqlDatabaseAsync);
            ExecuteLocalKqlCommand = new AsyncRelayCommand(ExecuteLocalKqlQueryAsync, () => !IsRunning && _kqlEngineInitialized);
            ClearLocalDbCommand = new AsyncRelayCommand(ClearLocalDatabaseAsync, () => !IsRunning && _kqlEngineInitialized);
            InsertTableQueryCommand = new RelayCommand(InsertTableIntoQuery);
            RefreshTablesCommand = new AsyncRelayCommand(RefreshAvailableTablesAsync, () => _kqlEngineInitialized);
            ExportLocalResultsCommand = new AsyncRelayCommand(ExportLocalKqlResultsAsync, () => LocalKqlResults != null);

            // Initialize KQL cheat sheet templates
            InitializeLocalKqlTemplates();

            // Plaso Download Command
            DownloadPlasoCommand = new RelayCommand(_ => OpenUrl("https://github.com/log2timeline/plaso/releases"));

            // Oletools/PDF Commands
            BrowseOletoolsCommand = new RelayCommand(_ => BrowseFolder("Select oletools installation folder", s => OletoolsPath = s));
            BrowsePdfParserCommand = new RelayCommand(_ => BrowseFile("pdf-parser.py", "Python|*.py|All files|*.*", s => PdfParserPath = s));
            RunMalwareAnalysisCommand = new AsyncRelayCommand(RunMalwareAnalysisAsync, () => !IsRunning);
            DownloadOletoolsCommand = new AsyncRelayCommand(DownloadOletoolsAsync, () => !IsRunning);
            DownloadPdfParserCommand = new AsyncRelayCommand(DownloadPdfParserAsync, () => !IsRunning);

            // Bulk Extractor Commands
            BrowseBulkExtractorCommand = new RelayCommand(_ => BrowseFile("bulk_extractor Executable", "Executable|bulk_extractor*.exe|All files|*.*", s => BulkExtractorPath = s));
            BrowseBulkInputCommand = new RelayCommand(_ => BrowseFolder("Select input folder or disk image", s => BulkExtractorInput = s));
            RunBulkExtractorCommand = new AsyncRelayCommand(RunBulkExtractorAsync, () => !IsRunning);
            DownloadBulkExtractorCommand = new AsyncRelayCommand(DownloadBulkExtractorAsync, () => !IsRunning);

            // OpenSearch Commands
            TestOpenSearchCommand = new AsyncRelayCommand(TestOpenSearchConnectionAsync);
            CreatePipelinesCommand = new AsyncRelayCommand(CreateOpenSearchPipelinesAsync, () => !IsRunning);
            CreateIndexTemplatesCommand = new AsyncRelayCommand(CreateOpenSearchIndexTemplatesAsync, () => !IsRunning);
            IngestToOpenSearchCommand = new AsyncRelayCommand(IngestToOpenSearchAsync, () => !IsRunning);

            // OSINT Commands
            BrowseDocumentsCommand = new RelayCommand(_ => BrowseFolder("Select folder with documents", s => DocumentPath = s));
            BrowseExifToolCommand = new RelayCommand(_ => BrowseFile("ExifTool Executable", "Executable|exiftool.exe|All files|*.*", s => ExifToolPath = s));
            ExtractMetadataCommand = new AsyncRelayCommand(ExtractDocumentMetadataAsync, () => !IsRunning);

            // Tool Download Commands
            DownloadWinPmemCommand = new AsyncRelayCommand(DownloadWinPmemAsync, () => !IsRunning);
            DownloadVolatilityCommand = new RelayCommand(_ => OpenUrl("https://github.com/volatilityfoundation/volatility3/releases"));
            GetKapeCommand = new RelayCommand(_ => OpenUrl("https://www.kroll.com/en/services/cyber-risk/incident-response-litigation-support/kroll-artifact-parser-extractor-kape"));
            DownloadExifToolCommand = new AsyncRelayCommand(DownloadExifToolAsync, () => !IsRunning);
            DownloadVelociraptorCommand = new AsyncRelayCommand(DownloadVelociraptorAsync, () => !IsRunning);

            // General Commands
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsRunning);
            ClearLogCommand = new RelayCommand(_ => ClearAll());
            ExportResultsCommand = new AsyncRelayCommand(ExportResultsAsync);

            // Task Scheduler Commands
            ScanScheduledTasksCommand = new AsyncRelayCommand(ScanScheduledTasksAsync, () => !IsRunning);
            ExportScheduledTasksCommand = new AsyncRelayCommand(ExportScheduledTasksAsync, () => ScheduledTasks.Count > 0);

            // Browser Forensics Commands
            ScanBrowserArtifactsCommand = new AsyncRelayCommand(ScanBrowserArtifactsAsync, () => !IsRunning);
            BrowseBrowserProfileCommand = new RelayCommand(_ => BrowseFolder("Select browser profile folder", s => BrowserForensicsProfile = s));
            ExportBrowserArtifactsCommand = new AsyncRelayCommand(ExportBrowserArtifactsAsync, () => BrowserArtifacts.Count > 0);

            // IOC Scanner Commands
            ScanForIOCsCommand = new AsyncRelayCommand(ScanForIOCsAsync, () => !IsRunning);
            BrowseIOCPathCommand = new RelayCommand(_ => BrowseFolder("Select folder to scan for IOCs", s => IOCScanPath = s));
            LoadIOCFeedCommand = new AsyncRelayCommand(LoadIOCFeedAsync, () => !IsRunning);
            ExportIOCResultsCommand = new AsyncRelayCommand(ExportIOCResultsAsync, () => IOCMatches.Count > 0);

            // Registry Diff Commands
            TakeRegistrySnapshotCommand = new AsyncRelayCommand(TakeRegistrySnapshotAsync, () => !IsRunning);
            CompareRegistrySnapshotsCommand = new AsyncRelayCommand(CompareRegistrySnapshotsAsync, () => !IsRunning);
            BrowseSnapshot1Command = new RelayCommand(_ => BrowseFile("Registry Snapshot", "JSON|*.json|All files|*.*", s => RegistrySnapshot1Path = s));
            BrowseSnapshot2Command = new RelayCommand(_ => BrowseFile("Registry Snapshot", "JSON|*.json|All files|*.*", s => RegistrySnapshot2Path = s));
            ExportRegistryDiffCommand = new AsyncRelayCommand(ExportRegistryDiffAsync, () => RegistryDiffs.Count > 0);
            OpenRegistrySnapshotFolderCommand = new RelayCommand(_ =>
            {
                var folder = RegistrySnapshotFolder;
                if (!System.IO.Directory.Exists(folder))
                    System.IO.Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", folder);
            });

            // PCAP Parser Commands
            ParsePcapCommand = new AsyncRelayCommand(ParsePcapAsync, () => !IsRunning);
            BrowsePcapFileCommand = new RelayCommand(_ => BrowseFile("PCAP File", "PCAP|*.pcap;*.pcapng;*.cap|All files|*.*", s => PcapFilePath = s));
            ExportPcapResultsCommand = new AsyncRelayCommand(ExportPcapResultsAsync, () => ParsedPackets.Count > 0 || NetworkConnections.Count > 0);

            // Set default paths
            SetDefaultPaths();
        }

        #region Private Methods

        private void InitializeKustoTemplates()
        {
            KustoTemplates.Add(new KustoQueryTemplate
            {
                Name = "Suspicious Process Creation (4688)",
                Description = "Windows Security Event 4688 - Process creation with suspicious command lines",
                Query = @"SecurityEvent
| where EventID == 4688
| extend Account = tostring(Account),
         CommandLine = tostring(CommandLine),
         NewProcessName = tostring(NewProcessName),
         ParentProcessName = tostring(ParentProcessName)
| where CommandLine has_any (""-enc"", ""-EncodedCommand"", ""rundll32"", ""regsvr32"", ""powershell"", ""cmd /c"")
| project TimeGenerated, Computer, Account, NewProcessName, CommandLine, ParentProcessName
| order by TimeGenerated desc"
            });

            KustoTemplates.Add(new KustoQueryTemplate
            {
                Name = "Sysmon Process Creation (EID 1)",
                Description = "Sysmon Event ID 1 - Detailed process creation with hashes",
                Query = @"Event
| where EventID == 1 and Source == ""Microsoft-Windows-Sysmon""
| extend Image = tostring(EventData.Image),
         CommandLine = tostring(EventData.CommandLine),
         ParentImage = tostring(EventData.ParentImage),
         User = tostring(UserName)
| where CommandLine has_any (""-enc"", ""-EncodedCommand"", ""rundll32"", ""regsvr32"", ""powershell"")
| project TimeGenerated, Computer, User, Image, CommandLine, ParentImage
| order by TimeGenerated desc"
            });

            KustoTemplates.Add(new KustoQueryTemplate
            {
                Name = "PowerShell Suspicious Activity",
                Description = "PowerShell Operational log - Download/execution indicators",
                Query = @"Event
| where EventLog == ""Microsoft-Windows-PowerShell/Operational""
| extend PsMessage = tostring(RenderedDescription)
| where PsMessage has_any (""DownloadString"", ""FromBase64String"", ""IEX"", ""New-Object Net.WebClient"")
| project TimeGenerated, Computer, EventID, PsMessage
| order by TimeGenerated desc"
            });

            KustoTemplates.Add(new KustoQueryTemplate
            {
                Name = "Amcache to Process Correlation",
                Description = "Correlate Amcache presence with process creation by SHA1 hash",
                Query = @"let tStart = ago(30d);
Amcache_CL
| where TimeGenerated >= tStart
| project Host_s, FilePath_s, Sha1_s
| join kind=innerunique (
    ProcCreate_CL
    | where TimeGenerated >= tStart
    | project TimeGenerated, Host_s, Image_s, CommandLine_s, Sha1_s
) on Sha1_s
| order by TimeGenerated desc"
            });

            KustoTemplates.Add(new KustoQueryTemplate
            {
                Name = "Volatility Netscan to Process",
                Description = "Correlate Volatility network scan results with process creation",
                Query = @"VolNetscan_CL
| where TimeGenerated >= ago(7d)
| join kind=leftouter (
    ProcCreate_CL
    | summarize arg_max(TimeGenerated, *) by Host_s, Pid_d
) on Host_s, $left.Pid_d == $right.Pid_d
| project TimeGenerated, Host_s, LocalIP_s, LocalPort_d, RemoteIP_s, RemotePort_d, Pid_d, Image_s, CommandLine_s
| order by TimeGenerated desc"
            });

            KustoTemplates.Add(new KustoQueryTemplate
            {
                Name = "Failed Logon Attempts",
                Description = "Security Event 4625 - Failed logon attempts",
                Query = @"SecurityEvent
| where EventID == 4625
| summarize FailedAttempts = count() by TargetAccount, IpAddress, Computer, bin(TimeGenerated, 1h)
| where FailedAttempts > 5
| order by FailedAttempts desc"
            });

            KustoTemplates.Add(new KustoQueryTemplate
            {
                Name = "Lateral Movement Detection",
                Description = "Detect potential lateral movement via remote service creation",
                Query = @"SecurityEvent
| where EventID in (4624, 4648, 4672)
| where LogonType in (3, 10)
| summarize LogonCount = count() by TargetUserName, IpAddress, Computer, bin(TimeGenerated, 1h)
| where LogonCount > 10
| order by LogonCount desc"
            });
        }

        private void ClearAll()
        {
            OutputLog = string.Empty;
            Artifacts.Clear();
            ExtractedMetadata.Clear();
            MalwareResults.Clear();
            BulkExtractorResults.Clear();
        }

        private void SetDefaultPaths()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var forensicsBase = Path.Combine(appData, "PlatypusTools", "Forensics");
            var toolsBase = Path.Combine(appData, "PlatypusTools", "Tools");
            
            MemoryOutputPath = Path.Combine(forensicsBase, "MemoryAnalysis");
            KapeOutputPath = Path.Combine(forensicsBase, "KapeOutput");
            DumpOutputPath = Path.Combine(forensicsBase, "MemoryDumps");
            PlasoStorageFile = Path.Combine(forensicsBase, "plaso_case.plaso");

            // Auto-detect WinPmem in Tools folder
            var winpmemPath = Path.Combine(toolsBase, "winpmem_mini_x64.exe");
            if (File.Exists(winpmemPath))
                WinPmemPath = winpmemPath;
            else
            {
                // Check common locations
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var possiblePaths = new[]
                {
                    Path.Combine(programFiles, "WinPmem", "winpmem_mini_x64.exe"),
                    Path.Combine(Environment.CurrentDirectory, "winpmem_mini_x64.exe"),
                    @"C:\Tools\winpmem_mini_x64.exe"
                };
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        WinPmemPath = path;
                        break;
                    }
                }
            }

            // Auto-detect ExifTool (check flat and subfolder locations)
            var exiftoolPaths = new[]
            {
                Path.Combine(toolsBase, "exiftool.exe"),
                Path.Combine(toolsBase, "exiftool", "exiftool.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool.exe"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools", "exiftool", "exiftool.exe"),
            };
            foreach (var path in exiftoolPaths)
            {
                if (File.Exists(path))
                {
                    ExifToolPath = path;
                    break;
                }
            }

            // Auto-detect KAPE
            var kapePaths = new[]
            {
                @"C:\KAPE\kape.exe",
                @"C:\Tools\KAPE\kape.exe",
                Path.Combine(toolsBase, "KAPE", "kape.exe")
            };
            foreach (var path in kapePaths)
            {
                if (File.Exists(path))
                {
                    KapePath = path;
                    break;
                }
            }
        }

        private void OpenUrl(string url)
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Failed to open URL: {ex.Message}");
            }
        }

        private void BrowseFile(string title, string filter, Action<string> setter)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            if (dialog.ShowDialog() == true)
                setter(dialog.FileName);
        }

        private void BrowseFolder(string description, Action<string> setter)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = description
            };
            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                setter(dialog.SelectedPath);
        }

        private void AppendLog(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            OutputLog += $"[{timestamp}] {message}\n";
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
        }

        #endregion

        #region Tool Downloads

        private async Task DownloadWinPmemAsync()
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var toolsBase = Path.Combine(appData, "PlatypusTools", "Tools");
                Directory.CreateDirectory(toolsBase);

                var outputPath = Path.Combine(toolsBase, "winpmem.exe");

                AppendLog("📥 Downloading WinPmem from GitHub...");
                AppendLog("   Using latest Velocidex WinPmem 4.1 development version");
                StatusMessage = "Downloading WinPmem...";

                // WinPmem 4.1 dev release - works with modern Windows including Secure Boot
                // See: https://github.com/Velocidex/WinPmem/releases/tag/v4.1.dev1
                const string winpmemUrl = "https://github.com/Velocidex/WinPmem/releases/download/v4.1.dev1/winpmem64.exe";

                using var response = await _httpClient.GetAsync(winpmemUrl, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                await using var contentStream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token);
                await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        Progress = (int)(totalRead * 100 / totalBytes);
                        StatusMessage = $"Downloading WinPmem... {totalRead / 1024}KB / {totalBytes / 1024}KB";
                    }
                }

                WinPmemPath = outputPath;
                AppendLog($"✓ WinPmem downloaded successfully to: {outputPath}");
                StatusMessage = "WinPmem downloaded successfully";
            }
            catch (OperationCanceledException)
            {
                AppendLog("⚠ Download cancelled");
                StatusMessage = "Download cancelled";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Failed to download WinPmem: {ex.Message}");
                AppendLog("   Manual download: https://github.com/Velocidex/WinPmem/releases");
                StatusMessage = "Download failed - see log for details";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }
        
        /// <summary>
        /// Downloads ProcDump from Sysinternals.
        /// </summary>
        private async Task DownloadProcDumpAsync()
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var toolsBase = Path.Combine(appData, "PlatypusTools", "Tools");
                Directory.CreateDirectory(toolsBase);

                var zipPath = Path.Combine(toolsBase, "procdump.zip");
                var outputPath = Path.Combine(toolsBase, "procdump64.exe");

                AppendLog("📥 Downloading ProcDump from Sysinternals...");
                StatusMessage = "Downloading ProcDump...";

                // ProcDump from Sysinternals
                const string procdumpUrl = "https://download.sysinternals.com/files/Procdump.zip";

                using var response = await _httpClient.GetAsync(procdumpUrl, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                await using var contentStream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token);
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        Progress = (int)(totalRead * 100 / totalBytes);
                        StatusMessage = $"Downloading ProcDump... {totalRead / 1024}KB / {totalBytes / 1024}KB";
                    }
                }
                
                fileStream.Close();
                
                // Extract the zip
                AppendLog("Extracting ProcDump...");
                StatusMessage = "Extracting ProcDump...";
                
                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    var entry = archive.GetEntry("procdump64.exe") ?? archive.GetEntry("Procdump64.exe");
                    if (entry != null)
                    {
                        entry.ExtractToFile(outputPath, overwrite: true);
                    }
                    else
                    {
                        // Try extracting all and finding it
                        ZipFile.ExtractToDirectory(zipPath, toolsBase, overwriteFiles: true);
                    }
                }
                
                // Clean up zip
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ProcDumpPath = outputPath;
                AppendLog($"✓ ProcDump downloaded successfully to: {outputPath}");
                StatusMessage = "ProcDump downloaded successfully";
            }
            catch (OperationCanceledException)
            {
                AppendLog("⚠ Download cancelled");
                StatusMessage = "Download cancelled";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Failed to download ProcDump: {ex.Message}");
                AppendLog("   Manual download: https://learn.microsoft.com/en-us/sysinternals/downloads/procdump");
                StatusMessage = "Download failed - see log for details";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }
        
        /// <summary>
        /// Refreshes the list of running processes for ProcDump target selection.
        /// </summary>
        private void RefreshRunningProcesses()
        {
            RunningProcesses.Clear();
            
            try
            {
                var processes = Process.GetProcesses()
                    .Where(p => !string.IsNullOrEmpty(p.ProcessName))
                    .OrderBy(p => p.ProcessName)
                    .ToList();
                    
                foreach (var proc in processes)
                {
                    try
                    {
                        RunningProcesses.Add(new ProcessInfo
                        {
                            Id = proc.Id,
                            Name = proc.ProcessName,
                            WindowTitle = proc.MainWindowTitle ?? string.Empty,
                            MemoryMB = proc.WorkingSet64 / (1024 * 1024)
                        });
                    }
                    catch
                    {
                        // Skip processes we can't access
                    }
                }
                
                AppendLog($"✓ Found {RunningProcesses.Count} running processes");
                StatusMessage = $"Found {RunningProcesses.Count} processes";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error enumerating processes: {ex.Message}");
                StatusMessage = "Error getting process list";
            }
        }
        
        /// <summary>
        /// Acquires a memory dump using Magnet RAM Capture.
        /// </summary>
        private async Task AcquireMagnetRamCaptureAsync()
        {
            if (string.IsNullOrWhiteSpace(MagnetRamCapturePath) || !File.Exists(MagnetRamCapturePath))
            {
                AppendLog("═══════════════════════════════════════════════════════════════");
                AppendLog("⚠ Magnet RAM Capture not found");
                AppendLog("═══════════════════════════════════════════════════════════════");
                AppendLog("");
                AppendLog("Magnet RAM Capture is a FREE tool with a SIGNED driver that works");
                AppendLog("with Secure Boot and modern Windows security features.");
                AppendLog("");
                AppendLog("To get Magnet RAM Capture:");
                AppendLog("  1. Visit: https://www.magnetforensics.com/resources/magnet-ram-capture/");
                AppendLog("  2. Fill out the form (free registration required)");
                AppendLog("  3. Download and extract the ZIP file");
                AppendLog("  4. Use the Browse button to select MagnetRAMCapture.exe");
                AppendLog("");
                StatusMessage = "Please download Magnet RAM Capture first";
                return;
            }
            
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            
            try
            {
                // Check admin privileges
                bool isAdmin = new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    
                AppendLog($"═══════════════════════════════════════════════════════════════");
                AppendLog($"Starting Magnet RAM Capture Memory Acquisition...");
                AppendLog($"═══════════════════════════════════════════════════════════════");
                AppendLog($"Administrator privileges: {(isAdmin ? "✓ YES" : "✗ NO - REQUIRED!")}");
                
                if (!isAdmin)
                {
                    AppendLog("");
                    AppendLog("⚠ Magnet RAM Capture requires Administrator privileges.");
                    AppendLog("  Please restart PlatypusTools as Administrator.");
                    StatusMessage = "Administrator privileges required";
                    return;
                }
                
                // Create output folder
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var outputFolder = string.IsNullOrWhiteSpace(DumpOutputPath) 
                    ? Path.Combine(appData, "PlatypusTools", "Forensics", "MemoryDumps")
                    : DumpOutputPath;
                Directory.CreateDirectory(outputFolder);
                
                var hostname = Environment.MachineName;
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var outputFile = Path.Combine(outputFolder, $"{hostname}_{timestamp}_MagnetRAM.raw");
                
                AppendLog($"Tool Path: {MagnetRamCapturePath}");
                AppendLog($"Output File: {outputFile}");
                AppendLog("");
                AppendLog("Launching Magnet RAM Capture...");
                AppendLog("Note: Magnet RAM Capture will open its own window.");
                AppendLog("      Please select the output path and click 'Start' in the application.");
                
                // Launch Magnet RAM Capture (it has its own GUI)
                var psi = new ProcessStartInfo
                {
                    FileName = MagnetRamCapturePath,
                    UseShellExecute = true,
                    Verb = "runas"
                };
                
                Process.Start(psi);
                
                AppendLog("");
                AppendLog("✓ Magnet RAM Capture launched successfully");
                AppendLog("");
                AppendLog("After acquisition completes:");
                AppendLog("  1. The memory dump will be saved as a .raw file");
                AppendLog("  2. You can analyze it using Volatility in the Memory Analysis section");
                
                StatusMessage = "Magnet RAM Capture launched - complete acquisition in the app window";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error launching Magnet RAM Capture: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }
        
        /// <summary>
        /// Acquires a process memory dump using ProcDump.
        /// </summary>
        private async Task AcquireProcDumpAsync()
        {
            if (string.IsNullOrWhiteSpace(ProcDumpPath) || !File.Exists(ProcDumpPath))
            {
                AppendLog("⚠ ProcDump not found. Click 'Download' to get it from Sysinternals.");
                StatusMessage = "ProcDump not found - please download first";
                return;
            }
            
            if (string.IsNullOrWhiteSpace(SelectedProcessForDump))
            {
                AppendLog("⚠ Please select a process to dump or enter a process name/PID.");
                StatusMessage = "Please select a target process";
                return;
            }
            
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            
            try
            {
                // Check admin privileges
                bool isAdmin = new System.Security.Principal.WindowsPrincipal(
                    System.Security.Principal.WindowsIdentity.GetCurrent())
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                    
                AppendLog($"═══════════════════════════════════════════════════════════════");
                AppendLog($"Starting ProcDump Process Memory Acquisition...");
                AppendLog($"═══════════════════════════════════════════════════════════════");
                AppendLog($"Administrator privileges: {(isAdmin ? "✓ YES" : "⚠ NO (may limit dump access)")}");
                
                // Create output folder
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var outputFolder = string.IsNullOrWhiteSpace(DumpOutputPath) 
                    ? Path.Combine(appData, "PlatypusTools", "Forensics", "ProcessDumps")
                    : DumpOutputPath;
                Directory.CreateDirectory(outputFolder);
                
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var processTarget = SelectedProcessForDump.Trim();
                
                // Extract PID if the format is "Name (PID: xxx)"
                var pidMatch = System.Text.RegularExpressions.Regex.Match(processTarget, @"PID:\s*(\d+)");
                string targetArg;
                string safeName;
                
                if (pidMatch.Success)
                {
                    targetArg = pidMatch.Groups[1].Value;
                    safeName = processTarget.Split(' ')[0];
                }
                else if (int.TryParse(processTarget, out _))
                {
                    targetArg = processTarget;
                    safeName = $"PID_{processTarget}";
                }
                else
                {
                    targetArg = processTarget;
                    safeName = processTarget.Replace(" ", "_");
                }
                
                var outputFile = Path.Combine(outputFolder, $"{safeName}_{timestamp}.dmp");
                
                // Build ProcDump arguments
                // -ma = full dump (all memory)
                // -mm = mini dump
                // -accepteula = accept EULA automatically
                var dumpType = ProcDumpFullDump ? "-ma" : "-mm";
                var args = $"-accepteula {dumpType} {targetArg} \"{outputFile}\"";
                
                AppendLog($"Target: {processTarget}");
                AppendLog($"Dump Type: {(ProcDumpFullDump ? "Full Memory Dump (-ma)" : "Mini Dump (-mm)")}");
                AppendLog($"Output: {outputFile}");
                AppendLog($"Command: procdump64.exe {args}");
                AppendLog("");
                
                StatusMessage = $"Creating process dump for {safeName}...";
                
                var result = await RunProcessWithDetailedLoggingAsync(ProcDumpPath, args);
                
                if (File.Exists(outputFile))
                {
                    var fileInfo = new FileInfo(outputFile);
                    AppendLog($"═══════════════════════════════════════════════════════════════");
                    AppendLog($"✓ Process dump created successfully!");
                    AppendLog($"  Size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");
                    AppendLog($"  Path: {outputFile}");
                    AppendLog($"═══════════════════════════════════════════════════════════════");
                    AppendLog("");
                    AppendLog("You can analyze this dump with:");
                    AppendLog("  • WinDbg (Windows Debugger)");
                    AppendLog("  • Visual Studio");
                    AppendLog("  • Volatility (for full dumps)");
                    
                    MemoryDumpPath = outputFile;
                    StatusMessage = "Process dump created successfully";
                    
                    Artifacts.Add(new ForensicArtifact
                    {
                        Type = "Process Dump",
                        Name = $"ProcDump: {safeName}",
                        Source = Environment.MachineName,
                        OutputPath = outputFile,
                        Timestamp = DateTime.Now,
                        RecordCount = (int)(fileInfo.Length / 1024)
                    });
                }
                else
                {
                    AppendLog($"═══════════════════════════════════════════════════════════════");
                    AppendLog($"✗ Process dump creation FAILED");
                    AppendLog($"═══════════════════════════════════════════════════════════════");
                    
                    if (!string.IsNullOrEmpty(result.Output))
                    {
                        AppendLog("ProcDump output:");
                        foreach (var line in result.Output.Split('\n'))
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                AppendLog($"  {line.Trim()}");
                        }
                    }
                    
                    AppendLog("");
                    AppendLog("Common issues:");
                    AppendLog("  • Process not found (check name/PID)");
                    AppendLog("  • Access denied (run as Administrator)");
                    AppendLog("  • Protected process (some system processes can't be dumped)");
                    
                    StatusMessage = "Process dump failed - see log for details";
                }
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task DownloadExifToolAsync()
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Use the install directory for tools
                var toolsBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                Directory.CreateDirectory(toolsBase);

                var zipPath = Path.Combine(toolsBase, "exiftool.zip");
                var outputPath = Path.Combine(toolsBase, "exiftool.exe");

                AppendLog("📥 Downloading ExifTool...");
                StatusMessage = "Downloading ExifTool...";

                // ExifTool Windows executable
                const string exiftoolUrl = "https://exiftool.org/exiftool-12.76.zip";

                using var response = await _httpClient.GetAsync(exiftoolUrl, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? 0;
                await using var contentStream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token);
                await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                {
                    await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                    totalRead += bytesRead;

                    if (totalBytes > 0)
                    {
                        Progress = (int)(totalRead * 100 / totalBytes);
                        StatusMessage = $"Downloading ExifTool... {totalRead / 1024}KB / {totalBytes / 1024}KB";
                    }
                }

                fileStream.Close();

                // Extract the zip
                AppendLog("📦 Extracting ExifTool...");
                StatusMessage = "Extracting ExifTool...";

                System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, toolsBase, overwriteFiles: true);

                // Rename the exe (exiftool(-k).exe -> exiftool.exe)
                var extractedExe = Directory.GetFiles(toolsBase, "exiftool*.exe").FirstOrDefault();
                if (extractedExe != null && extractedExe != outputPath)
                {
                    if (File.Exists(outputPath))
                        File.Delete(outputPath);
                    File.Move(extractedExe, outputPath);
                }

                // Cleanup zip
                if (File.Exists(zipPath))
                    File.Delete(zipPath);

                ExifToolPath = outputPath;
                AppendLog($"✓ ExifTool downloaded and extracted to: {outputPath}");
                StatusMessage = "ExifTool downloaded successfully";
            }
            catch (OperationCanceledException)
            {
                AppendLog("⚠ Download cancelled");
                StatusMessage = "Download cancelled";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Failed to download ExifTool: {ex.Message}");
                AppendLog("   Manual download: https://exiftool.org");
                StatusMessage = "Download failed - see log for details";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        private async Task DownloadOletoolsAsync()
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                AppendLog("========================================");
                AppendLog("oletools Installation");
                AppendLog("========================================");
                AppendLog("");
                AppendLog("oletools is a Python package for analyzing Microsoft Office files.");
                AppendLog("");
                AppendLog("Installing via pip...");
                StatusMessage = "Installing oletools via pip...";

                var result = await RunProcessWithLoggingAsync("pip", "install -U oletools");

                if (result.Success)
                {
                    AppendLog("");
                    AppendLog("✓ oletools installed successfully!");
                    AppendLog("");
                    AppendLog("Available tools:");
                    AppendLog("  • olevba - Extract VBA macros from Office files");
                    AppendLog("  • mraptor - Macro Raptor detection");
                    AppendLog("  • oleid - Analyze OLE files");
                    AppendLog("  • oleobj - Extract embedded objects");
                    AppendLog("  • rtfobj - Analyze RTF files");
                    StatusMessage = "oletools installed successfully";

                    // Try to find oletools location
                    var pipShowResult = await RunProcessAsync("pip", "show oletools", null);
                    if (pipShowResult.Success && !string.IsNullOrEmpty(pipShowResult.Output))
                    {
                        var locationMatch = System.Text.RegularExpressions.Regex.Match(pipShowResult.Output, @"Location:\s*(.+)");
                        if (locationMatch.Success)
                        {
                            OletoolsPath = locationMatch.Groups[1].Value.Trim();
                            AppendLog($"");
                            AppendLog($"Installed at: {OletoolsPath}");
                        }
                    }
                }
                else
                {
                    AppendLog("");
                    AppendLog("✗ pip install failed. Trying alternative methods...");
                    AppendLog("");

                    // Try python -m pip
                    result = await RunProcessWithLoggingAsync("python", "-m pip install -U oletools");

                    if (result.Success)
                    {
                        AppendLog("");
                        AppendLog("✓ oletools installed successfully!");
                        StatusMessage = "oletools installed successfully";
                    }
                    else
                    {
                        AppendLog("");
                        AppendLog("✗ Installation failed.");
                        AppendLog("");
                        AppendLog("Manual installation options:");
                        AppendLog("  1. Install Python from https://python.org");
                        AppendLog("  2. Run: pip install oletools");
                        AppendLog("  3. Or download from: https://github.com/decalage2/oletools");
                        StatusMessage = "Installation failed - see log";

                        OpenUrl("https://github.com/decalage2/oletools");
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                StatusMessage = "Installation failed";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        private async Task DownloadBulkExtractorAsync()
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var toolsBase = Path.Combine(appData, "PlatypusTools", "Tools");
                Directory.CreateDirectory(toolsBase);

                AppendLog("========================================");
                AppendLog("bulk_extractor Download");
                AppendLog("========================================");
                AppendLog("");
                AppendLog("bulk_extractor extracts features from disk images/files:");
                AppendLog("  • Email addresses");
                AppendLog("  • URLs");
                AppendLog("  • Credit card numbers");
                AppendLog("  • Phone numbers");
                AppendLog("  • And more...");
                AppendLog("");

                // Check for latest release from GitHub
                const string releasesApiUrl = "https://api.github.com/repos/simsong/bulk_extractor/releases/latest";
                AppendLog("Checking for latest release...");
                StatusMessage = "Checking for latest bulk_extractor release...";

                _httpClient.DefaultRequestHeaders.Remove("User-Agent");
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatypusTools/3.2");

                var releaseResponse = await _httpClient.GetStringAsync(releasesApiUrl);
                var releaseMatch = System.Text.RegularExpressions.Regex.Match(releaseResponse, @"""tag_name""\s*:\s*""([^""]+)""");
                var tagName = releaseMatch.Success ? releaseMatch.Groups[1].Value : "v2.1.1";

                AppendLog($"Latest release: {tagName}");

                // Try to find Windows binary URL
                var assetMatch = System.Text.RegularExpressions.Regex.Match(releaseResponse, @"""browser_download_url""\s*:\s*""([^""]*windows[^""]*\.zip[^""]*)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (assetMatch.Success)
                {
                    var downloadUrl = assetMatch.Groups[1].Value;
                    AppendLog($"Download URL: {downloadUrl}");
                    AppendLog("");
                    AppendLog("📥 Downloading bulk_extractor...");
                    StatusMessage = "Downloading bulk_extractor...";

                    var zipPath = Path.Combine(toolsBase, "bulk_extractor.zip");

                    using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    await using var contentStream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token);
                    await using var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                        totalRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            Progress = (int)(totalRead * 100 / totalBytes);
                            StatusMessage = $"Downloading... {totalRead / 1024}KB / {totalBytes / 1024}KB";
                        }
                    }

                    fileStream.Close();

                    // Extract
                    AppendLog("📦 Extracting...");
                    StatusMessage = "Extracting bulk_extractor...";

                    var extractPath = Path.Combine(toolsBase, "bulk_extractor");
                    if (Directory.Exists(extractPath))
                        Directory.Delete(extractPath, true);

                    System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath);

                    // Find the executable
                    var exeFile = Directory.GetFiles(extractPath, "bulk_extractor*.exe", SearchOption.AllDirectories).FirstOrDefault();
                    if (exeFile != null)
                    {
                        BulkExtractorPath = exeFile;
                        AppendLog($"");
                        AppendLog($"✓ bulk_extractor installed at: {exeFile}");
                        StatusMessage = "bulk_extractor downloaded successfully";
                    }
                    else
                    {
                        AppendLog("⚠ Could not find bulk_extractor.exe after extraction");
                        StatusMessage = "Extraction issue - check manually";
                    }

                    // Cleanup
                    if (File.Exists(zipPath))
                        File.Delete(zipPath);
                }
                else
                {
                    // No Windows binary found, offer alternatives
                    AppendLog("");
                    AppendLog("⚠ No Windows binary found in latest release.");
                    AppendLog("");
                    AppendLog("Manual download options:");
                    AppendLog("  • GitHub Releases: https://github.com/simsong/bulk_extractor/releases");
                    AppendLog("  • Build from source: https://github.com/simsong/bulk_extractor");
                    AppendLog("");
                    AppendLog("Opening download page...");
                    StatusMessage = "Opening download page...";

                    OpenUrl("https://github.com/simsong/bulk_extractor/releases");
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("⚠ Download cancelled");
                StatusMessage = "Download cancelled";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                AppendLog("");
                AppendLog("Opening GitHub releases page for manual download...");
                OpenUrl("https://github.com/simsong/bulk_extractor/releases");
                StatusMessage = "Opening download page...";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        private async Task DownloadPdfParserAsync()
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var toolsBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                Directory.CreateDirectory(toolsBase);

                AppendLog("========================================");
                AppendLog("pdf-parser.py Download");
                AppendLog("========================================");
                AppendLog("");
                AppendLog("pdf-parser.py by Didier Stevens analyzes PDF files for:");
                AppendLog("  • JavaScript code");
                AppendLog("  • Embedded files");
                AppendLog("  • Launch actions");
                AppendLog("  • OpenAction triggers");
                AppendLog("  • Stream content");
                AppendLog("");

                const string pdfParserUrl = "https://raw.githubusercontent.com/DidierStevens/DidierStevensSuite/master/pdf-parser.py";
                var outputPath = Path.Combine(toolsBase, "pdf-parser.py");

                AppendLog($"Downloading from: {pdfParserUrl}");
                StatusMessage = "Downloading pdf-parser.py...";

                var content = await _httpClient.GetStringAsync(pdfParserUrl);
                await File.WriteAllTextAsync(outputPath, content);

                PdfParserPath = outputPath;
                AppendLog("");
                AppendLog($"✓ pdf-parser.py downloaded to: {outputPath}");
                AppendLog("");
                AppendLog("Usage: python pdf-parser.py [options] <pdf-file>");
                AppendLog("Common options:");
                AppendLog("  -a    Extract all objects");
                AppendLog("  -s    Search for strings");
                AppendLog("  -o    Select specific object");
                AppendLog("");
                AppendLog("Also available from Didier Stevens Suite:");
                AppendLog("  https://github.com/DidierStevens/DidierStevensSuite");
                StatusMessage = "pdf-parser.py downloaded successfully";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                AppendLog("");
                AppendLog("Opening GitHub page for manual download...");
                OpenUrl("https://github.com/DidierStevens/DidierStevensSuite");
                StatusMessage = "Opening download page...";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        private async Task DownloadVelociraptorAsync()
        {
            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var toolsBase = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Tools");
                Directory.CreateDirectory(toolsBase);

                AppendLog("========================================");
                AppendLog("Velociraptor Download & Setup Guide");
                AppendLog("========================================");
                AppendLog("");
                AppendLog("Velociraptor is a powerful endpoint visibility and");
                AppendLog("collection tool for DFIR and threat hunting.");
                AppendLog("");
                AppendLog("Capabilities:");
                AppendLog("  • Live forensic collection across endpoints");
                AppendLog("  • VQL (Velociraptor Query Language) for hunting");
                AppendLog("  • Artifact collection (KAPE-compatible)");
                AppendLog("  • Real-time monitoring and alerting");
                AppendLog("  • Fleet-wide deployment");
                AppendLog("");

                StatusMessage = "Checking for latest Velociraptor release...";
                const string releasesApiUrl = "https://api.github.com/repos/Velocidex/velociraptor/releases/latest";

                _httpClient.DefaultRequestHeaders.Remove("User-Agent");
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlatypusTools/3.2");

                var releaseResponse = await _httpClient.GetStringAsync(releasesApiUrl);
                var tagMatch = System.Text.RegularExpressions.Regex.Match(releaseResponse, @"""tag_name""\s*:\s*""([^""]+)""");
                var tagName = tagMatch.Success ? tagMatch.Groups[1].Value : "latest";

                AppendLog($"Latest version: {tagName}");

                // Find Windows amd64 binary
                var assetMatch = System.Text.RegularExpressions.Regex.Match(releaseResponse, 
                    @"""browser_download_url""\s*:\s*""([^""]*windows-amd64\.exe[^""]*)""", 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                if (assetMatch.Success)
                {
                    var downloadUrl = assetMatch.Groups[1].Value;
                    var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
                    var outputPath = Path.Combine(toolsBase, fileName);

                    AppendLog($"Downloading: {fileName}");
                    StatusMessage = "Downloading Velociraptor...";

                    using var response = await _httpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, _cancellationTokenSource.Token);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    await using var contentStream = await response.Content.ReadAsStreamAsync(_cancellationTokenSource.Token);
                    await using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    long totalRead = 0;
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cancellationTokenSource.Token)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead, _cancellationTokenSource.Token);
                        totalRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            Progress = (int)(totalRead * 100 / totalBytes);
                            StatusMessage = $"Downloading... {totalRead / (1024 * 1024)}MB / {totalBytes / (1024 * 1024)}MB";
                        }
                    }

                    fileStream.Close();

                    VelociraptorPath = outputPath;
                    AppendLog("");
                    AppendLog($"✓ Velociraptor downloaded to: {outputPath}");
                    AppendLog("");
                    AppendLog("========================================");
                    AppendLog("Quick Start Guide");
                    AppendLog("========================================");
                    AppendLog("");
                    AppendLog("1. OFFLINE COLLECTION (Single Machine):");
                    AppendLog("   velociraptor.exe artifacts collect Windows.KapeFiles.Targets --format json");
                    AppendLog("");
                    AppendLog("2. GENERATE CONFIG (Server Mode):");
                    AppendLog("   velociraptor.exe config generate -i");
                    AppendLog("");
                    AppendLog("3. START SERVER:");
                    AppendLog("   velociraptor.exe --config server.config.yaml frontend -v");
                    AppendLog("");
                    AppendLog("4. COMMON ARTIFACTS:");
                    AppendLog("   • Windows.KapeFiles.Targets - Full KAPE collection");
                    AppendLog("   • Windows.System.Pslist - Running processes");
                    AppendLog("   • Windows.Network.Netstat - Network connections");
                    AppendLog("   • Windows.EventLogs.Evtx - Event logs");
                    AppendLog("   • Windows.Forensics.SRUM - Resource usage");
                    AppendLog("   • Windows.Registry.AutoRuns - Persistence");
                    AppendLog("");
                    AppendLog("Documentation: https://docs.velociraptor.app/");
                    StatusMessage = "Velociraptor downloaded successfully";
                }
                else
                {
                    AppendLog("");
                    AppendLog("Could not find Windows binary in latest release.");
                    AppendLog("Opening GitHub releases page...");
                    OpenUrl("https://github.com/Velocidex/velociraptor/releases");
                    StatusMessage = "Opening download page...";
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("⚠ Download cancelled");
                StatusMessage = "Download cancelled";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                AppendLog("");
                AppendLog("Opening GitHub releases page for manual download...");
                OpenUrl("https://github.com/Velocidex/velociraptor/releases");
                StatusMessage = "Opening download page...";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        #endregion

        #region Memory Acquisition

        private async Task AcquireMemoryDumpAsync()
        {
            AppendLog("═══════════════════════════════════════════════════════════════");
            AppendLog("Starting Memory Acquisition Diagnostics...");
            AppendLog("═══════════════════════════════════════════════════════════════");
            
            // Check administrator privileges
            var isAdmin = new System.Security.Principal.WindowsPrincipal(
                System.Security.Principal.WindowsIdentity.GetCurrent())
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            
            AppendLog($"Administrator privileges: {(isAdmin ? "✓ YES" : "✗ NO")}");
            if (!isAdmin)
            {
                AppendLog("⚠ WARNING: Memory acquisition typically requires Administrator privileges!");
                AppendLog("  Please restart PlatypusTools as Administrator.");
            }
            
            // Check WinPmem path
            AppendLog($"WinPmem Path: {WinPmemPath}");
            AppendLog($"WinPmem Exists: {File.Exists(WinPmemPath)}");
            
            if (string.IsNullOrWhiteSpace(WinPmemPath) || !File.Exists(WinPmemPath))
            {
                StatusMessage = "WinPmem not found - downloading...";
                AppendLog("⚠ WinPmem not found. Attempting to download automatically...");
                
                // Attempt to download WinPmem
                await DownloadWinPmemAsync();
                
                // Check again after download
                if (string.IsNullOrWhiteSpace(WinPmemPath) || !File.Exists(WinPmemPath))
                {
                    StatusMessage = "WinPmem download failed";
                    AppendLog("✗ Failed to download WinPmem. Please download manually from: https://github.com/Velocidex/WinPmem");
                    AppendLog("  Direct download: https://github.com/Velocidex/WinPmem/releases");
                    return;
                }
            }
            
            // Verify WinPmem is a valid executable
            try
            {
                var fileInfo = new FileInfo(WinPmemPath);
                AppendLog($"WinPmem file size: {fileInfo.Length:N0} bytes");
                AppendLog($"WinPmem last modified: {fileInfo.LastWriteTime}");
                
                // Check if file is blocked by Windows
                var zoneId = Path.Combine(WinPmemPath + ":Zone.Identifier");
                if (File.Exists(zoneId))
                {
                    AppendLog("⚠ WARNING: WinPmem may be blocked by Windows (downloaded from internet)");
                    AppendLog("  Right-click the file → Properties → Check 'Unblock' → Apply");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"⚠ Could not read WinPmem file info: {ex.Message}");
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Validate output path
                AppendLog($"Output folder: {DumpOutputPath}");
                
                try
                {
                    Directory.CreateDirectory(DumpOutputPath);
                    AppendLog($"Output folder created/verified: ✓");
                    
                    // Test write permissions
                    var testFile = Path.Combine(DumpOutputPath, ".write_test");
                    await File.WriteAllTextAsync(testFile, "test");
                    File.Delete(testFile);
                    AppendLog($"Write permissions: ✓");
                }
                catch (Exception ex)
                {
                    AppendLog($"✗ Output folder error: {ex.Message}");
                    StatusMessage = $"Cannot write to output folder: {ex.Message}";
                    return;
                }
                
                // Check available disk space
                try
                {
                    var driveInfo = new DriveInfo(Path.GetPathRoot(DumpOutputPath) ?? "C:");
                    var freeSpaceGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0);
                    var totalMemoryGB = Environment.WorkingSet / (1024.0 * 1024.0 * 1024.0) * 10; // Rough estimate
                    
                    AppendLog($"Available disk space: {freeSpaceGB:F2} GB");
                    AppendLog($"System RAM (approx): {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024.0 * 1024.0 * 1024.0):F2} GB");
                    
                    if (freeSpaceGB < 8)
                    {
                        AppendLog("⚠ WARNING: Low disk space! Memory dumps can be very large (8-64+ GB)");
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"⚠ Could not check disk space: {ex.Message}");
                }
                
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var hostname = Environment.MachineName;
                var outputFile = Path.Combine(DumpOutputPath, $"{hostname}_{timestamp}.{DumpFormat}");

                AppendLog($"───────────────────────────────────────────────────────────────");
                AppendLog($"Starting WinPmem memory acquisition...");
                AppendLog($"Output file: {outputFile}");
                AppendLog($"Format: {DumpFormat}");
                AppendLog($"───────────────────────────────────────────────────────────────");

                StatusMessage = "Acquiring memory dump (this may take several minutes)...";

                // Detect WinPmem version type
                var winpmemType = await DetectWinPmemTypeAsync(WinPmemPath);
                AppendLog($"WinPmem type detected: {winpmemType}");
                
                string args;
                switch (winpmemType)
                {
                    case "go-winpmem":
                        // Go-based WinPmem (go-winpmem_amd64_*_signed.exe)
                        // Usage: go-winpmem [options] output_file
                        // Options: --compression snappy|s2|gzip|none
                        args = DumpFormat switch
                        {
                            "compressed" => $"--compression snappy \"{outputFile}\"",
                            _ => $"--compression none \"{outputFile}\""
                        };
                        break;
                        
                    case "winpmem-v4-format":
                        // WinPmem v4 builds with --format option
                        args = DumpFormat switch
                        {
                            "aff4" => $"--format map -o \"{outputFile}\"",
                            "crashdump" => $"--format crashdump -o \"{outputFile}\"",
                            _ => $"--format raw -o \"{outputFile}\""
                        };
                        break;
                    
                    case "winpmem-v4":
                    case "winpmem-v3":
                        // WinPmem v3.x/v4.x - simple syntax: winpmem.exe [option] output_path
                        // This includes v4.0.1, v4.1.dev1 etc.
                        AppendLog("Using WinPmem v4/v3 syntax: winpmem.exe output_path");
                        args = $"\"{outputFile}\"";
                        break;
                        
                    default:
                        // Legacy WinPmem syntax (v2.x and older)
                        AppendLog("Using legacy syntax: winpmem.exe output_path");
                        args = $"\"{outputFile}\"";
                        break;
                }
                
                AppendLog($"Command: {WinPmemPath} {args}");
                AppendLog($"Executing at: {DateTime.Now:HH:mm:ss}");

                var result = await RunProcessWithDetailedLoggingAsync(WinPmemPath, args);

                // If first attempt failed, try alternative syntax
                if (!result.Success && !File.Exists(outputFile))
                {
                    AppendLog("First attempt failed. Trying alternative command syntax...");
                    
                    // Try the opposite syntax style based on detected type
                    switch (winpmemType)
                    {
                        case "go-winpmem":
                            args = $"\"{outputFile}\"";  // Simpler positional arg
                            break;
                        case "winpmem-v4-format":
                            args = $"-o \"{outputFile}\"";  // Without --format
                            break;
                        default:
                            // For all other versions, just retry with simple output path
                            args = $"\"{outputFile}\"";
                            break;
                    }
                    
                    AppendLog($"Retry Command: {WinPmemPath} {args}");
                    result = await RunProcessWithDetailedLoggingAsync(WinPmemPath, args);
                }

                if (result.Success && File.Exists(outputFile))
                {
                    var fileInfo = new FileInfo(outputFile);
                    AppendLog($"═══════════════════════════════════════════════════════════════");
                    AppendLog($"✓ Memory dump acquired successfully!");
                    AppendLog($"  Size: {fileInfo.Length / (1024.0 * 1024.0 * 1024.0):F2} GB");
                    AppendLog($"  Path: {outputFile}");
                    AppendLog($"═══════════════════════════════════════════════════════════════");

                    MemoryDumpPath = outputFile;
                    StatusMessage = "Memory dump acquired successfully";

                    Artifacts.Add(new ForensicArtifact
                    {
                        Type = "Memory Dump",
                        Name = $"Live Memory Acquisition",
                        Source = hostname,
                        OutputPath = outputFile,
                        Timestamp = DateTime.Now,
                        RecordCount = (int)(fileInfo.Length / 1024)
                    });
                }
                else
                {
                    AppendLog($"═══════════════════════════════════════════════════════════════");
                    AppendLog($"✗ Memory acquisition FAILED");
                    AppendLog($"═══════════════════════════════════════════════════════════════");
                    
                    if (!string.IsNullOrWhiteSpace(result.Error))
                    {
                        AppendLog($"Error output:");
                        foreach (var line in result.Error.Split('\n'))
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                AppendLog($"  {line.Trim()}");
                        }
                    }
                    
                    if (!string.IsNullOrWhiteSpace(result.Output))
                    {
                        AppendLog($"Standard output:");
                        foreach (var line in result.Output.Split('\n'))
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                                AppendLog($"  {line.Trim()}");
                        }
                    }
                    
                    AppendLog($"Exit code: {result.ExitCode}");
                    
                    // Check for specific error codes in output
                    bool hasDriverError = result.Output.Contains("0x241") || 
                                          result.Output.Contains("StartService") ||
                                          result.Output.Contains("Cannot start the driver") ||
                                          result.Error.Contains("0x241");
                    
                    // Provide troubleshooting hints based on common errors
                    if (hasDriverError)
                    {
                        AppendLog($"");
                        AppendLog($"═══════════════════════════════════════════════════════════════");
                        AppendLog($"⚠ ERROR 0x241: Driver Signature Enforcement (DSE) Blocked");
                        AppendLog($"═══════════════════════════════════════════════════════════════");
                        AppendLog($"");
                        AppendLog($"Windows is blocking WinPmem's kernel driver because it's not");
                        AppendLog($"signed with a WHQL (Windows Hardware Quality Labs) certificate.");
                        AppendLog($"");
                        AppendLog($"This is a SECURITY FEATURE of modern Windows, not a bug.");
                        AppendLog($"");
                        AppendLog($"OPTIONS TO ACQUIRE MEMORY:");
                        AppendLog($"");
                        AppendLog($"  1. USE ALTERNATIVE TOOLS (Recommended):");
                        AppendLog($"     • Magnet RAM Capture (free, signed): https://magnetforensics.com/free-tools/");
                        AppendLog($"     • Belkasoft RAM Capture (free, signed): https://belkasoft.com/get");
                        AppendLog($"     • DumpIt by Comae (signed): https://github.com/comaeio/");
                        AppendLog($"");
                        AppendLog($"  2. DISABLE DSE TEMPORARILY (requires reboot):");
                        AppendLog($"     • Hold Shift + click Restart");
                        AppendLog($"     • Troubleshoot → Advanced → Startup Settings → Restart");
                        AppendLog($"     • Press 7 for 'Disable driver signature enforcement'");
                        AppendLog($"     • Run memory acquisition, then reboot normally");
                        AppendLog($"");
                        AppendLog($"  3. USE A FORENSIC BOOT ENVIRONMENT:");
                        AppendLog($"     • Boot from CAINE, Paladin, or Tsurugi Linux");
                        AppendLog($"     • Acquire memory without Windows restrictions");
                        AppendLog($"");
                        AppendLog($"  4. CHECK SYSTEM SECURITY SETTINGS:");
                        AppendLog($"     • Secure Boot: May block unsigned drivers");
                        AppendLog($"     • HVCI/Memory Integrity: Settings → Privacy & Security → ");
                        AppendLog($"       Windows Security → Device Security → Core Isolation");
                        AppendLog($"     • Antivirus/EDR: May quarantine WinPmem");
                    }
                    else if (result.Error.Contains("Access") || result.Error.Contains("denied") || result.ExitCode == 5)
                    {
                        AppendLog($"");
                        AppendLog($"TROUBLESHOOTING: Access Denied");
                        AppendLog($"  1. Run PlatypusTools as Administrator");
                        AppendLog($"  2. Disable antivirus temporarily (WinPmem may be flagged)");
                        AppendLog($"  3. Check if Secure Boot or Credential Guard is blocking kernel access");
                    }
                    else if (result.Error.Contains("driver") || result.Error.Contains("kernel"))
                    {
                        AppendLog($"");
                        AppendLog($"TROUBLESHOOTING: Driver/Kernel Issue");
                        AppendLog($"  1. Windows may be blocking unsigned kernel drivers");
                        AppendLog($"  2. Try disabling Driver Signature Enforcement temporarily");
                        AppendLog($"  3. Secure Boot may need to be disabled in BIOS");
                    }
                    else if (result.Error.Contains("file") || result.Error.Contains("path"))
                    {
                        AppendLog($"");
                        AppendLog($"TROUBLESHOOTING: File/Path Issue");
                        AppendLog($"  1. Ensure output path exists and is writable");
                        AppendLog($"  2. Try a different output location (e.g., C:\\Temp)");
                        AppendLog($"  3. Ensure sufficient disk space for memory dump");
                    }
                    else if (result.ExitCode == -1)
                    {
                        AppendLog($"");
                        AppendLog($"TROUBLESHOOTING: Exit Code -1 (Generic Failure)");
                        AppendLog($"  This usually indicates WinPmem couldn't load its kernel driver.");
                        AppendLog($"  Common causes:");
                        AppendLog($"  1. Driver Signature Enforcement (DSE) is blocking the driver");
                        AppendLog($"  2. Secure Boot is enabled - some WinPmem versions don't work with Secure Boot");
                        AppendLog($"  3. Hypervisor-Protected Code Integrity (HVCI) is blocking kernel access");
                        AppendLog($"  4. Antivirus/EDR is blocking the driver");
                        AppendLog($"");
                        AppendLog($"SOLUTIONS:");
                        AppendLog($"  Option 1: Use the 'Download' button to get the latest Velocidex WinPmem");
                        AppendLog($"            (newer version works better with modern Windows)");
                        AppendLog($"  Option 2: Temporarily disable Driver Signature Enforcement:");
                        AppendLog($"            - Hold Shift + click Restart");
                        AppendLog($"            - Troubleshoot → Advanced → Startup Settings → Restart");
                        AppendLog($"            - Press 7 for 'Disable driver signature enforcement'");
                        AppendLog($"  Option 3: Try DumpIt instead of WinPmem (different acquisition method)");
                        AppendLog($"  Option 4: Use a bootable forensics distro (e.g., CAINE, Paladin)");
                    }
                    
                    StatusMessage = "Memory acquisition failed - see log for details";
                }
            }
            catch (Exception ex)
            {
                AppendLog($"═══════════════════════════════════════════════════════════════");
                AppendLog($"✗ EXCEPTION during memory acquisition");
                AppendLog($"═══════════════════════════════════════════════════════════════");
                AppendLog($"Message: {ex.Message}");
                AppendLog($"Type: {ex.GetType().Name}");
                if (ex.InnerException != null)
                {
                    AppendLog($"Inner Exception: {ex.InnerException.Message}");
                }
                AppendLog($"Stack Trace:");
                foreach (var line in ex.StackTrace?.Split('\n') ?? Array.Empty<string>())
                {
                    AppendLog($"  {line.Trim()}");
                }
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }
        
        /// <summary>
        /// Enhanced process runner with detailed logging and real-time output capture.
        /// </summary>
        private async Task<(bool Success, string Output, string Error, int ExitCode)> RunProcessWithDetailedLoggingAsync(string fileName, string arguments)
        {
            var output = new System.Text.StringBuilder();
            var error = new System.Text.StringBuilder();
            var exitCode = -1;
            
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas" // Request elevation
                };
                
                AppendLog($"Creating process...");
                
                using var process = new Process { StartInfo = psi };
                
                // Capture output in real-time
                process.OutputDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        output.AppendLine(e.Data);
                        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            AppendLog($"[WinPmem] {e.Data}");
                        });
                    }
                };
                
                process.ErrorDataReceived += (s, e) =>
                {
                    if (e.Data != null)
                    {
                        error.AppendLine(e.Data);
                        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            AppendLog($"[WinPmem ERR] {e.Data}");
                        });
                    }
                };
                
                var started = process.Start();
                if (!started)
                {
                    AppendLog($"✗ Failed to start process");
                    return (false, "", "Failed to start process", -1);
                }
                
                AppendLog($"Process started (PID: {process.Id})");
                
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                
                // Wait with timeout (memory acquisition can take a while)
                var completed = await Task.Run(() => process.WaitForExit(600000)); // 10 minute timeout
                
                if (!completed)
                {
                    AppendLog($"⚠ Process timed out after 10 minutes");
                    try { process.Kill(); } catch { }
                    return (false, output.ToString(), "Process timed out after 10 minutes", -1);
                }
                
                exitCode = process.ExitCode;
                AppendLog($"Process exited with code: {exitCode}");
                
                return (exitCode == 0, output.ToString(), error.ToString(), exitCode);
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 740)
            {
                AppendLog($"✗ Elevation required (error 740)");
                AppendLog($"  The operation requires administrator privileges.");
                return (false, "", "Administrator privileges required. Please run PlatypusTools as Administrator.", 740);
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                AppendLog($"✗ Win32 Error: {ex.NativeErrorCode} - {ex.Message}");
                return (false, "", $"Win32 Error {ex.NativeErrorCode}: {ex.Message}", ex.NativeErrorCode);
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Exception: {ex.Message}");
                return (false, "", ex.Message, -1);
            }
        }
        
        /// <summary>
        /// Detects the WinPmem type: go-winpmem, velocidex-mini, or legacy.
        /// </summary>
        private async Task<string> DetectWinPmemTypeAsync(string winpmemPath)
        {
            try
            {
                // First check by filename
                var fileName = Path.GetFileName(winpmemPath).ToLowerInvariant();
                
                // go-winpmem_amd64_*.exe - the Go-based imager
                if (fileName.Contains("go-winpmem") || fileName.Contains("go_winpmem"))
                {
                    AppendLog($"Detected Go-based WinPmem by filename: {fileName}");
                    return "go-winpmem";
                }
                
                // Try running with -h to get more info - this is more reliable than filename
                var psi = new ProcessStartInfo
                {
                    FileName = winpmemPath,
                    Arguments = "-h",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(psi);
                if (process == null) return "legacy";
                
                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                
                // Wait briefly
                await Task.Run(() => process.WaitForExit(5000));
                
                var combinedOutput = (output + error).ToLowerInvariant();
                
                // Log what we found for debugging
                AppendLog($"WinPmem help output (first 500 chars): {combinedOutput.Substring(0, Math.Min(500, combinedOutput.Length))}");
                
                // Check VERSION NUMBER first - this is most reliable
                // v4.x and v3.x are newer versions (even if they still show scudette copyright)
                // v2.x and v1.x are legacy
                
                // Extract version using regex
                var versionMatch = System.Text.RegularExpressions.Regex.Match(combinedOutput, @"version\s+(\d+)\.(\d+)");
                if (versionMatch.Success)
                {
                    int majorVersion = int.Parse(versionMatch.Groups[1].Value);
                    int minorVersion = int.Parse(versionMatch.Groups[2].Value);
                    AppendLog($"Detected WinPmem version: {majorVersion}.{minorVersion}");
                    
                    if (majorVersion >= 4)
                    {
                        // v4.x uses simple syntax: winpmem.exe [option] output_path
                        // Note: Despite copyright showing scudette, v4.x is the modern version
                        AppendLog("Detected WinPmem v4.x (modern version with legacy syntax)");
                        return "winpmem-v4";
                    }
                    else if (majorVersion == 3)
                    {
                        AppendLog("Detected WinPmem v3.x");
                        return "winpmem-v3";
                    }
                    else
                    {
                        AppendLog($"Detected legacy WinPmem v{majorVersion}.x");
                        return "legacy";
                    }
                }
                
                // Fallback: check for specific features if version not found
                
                // Go-based WinPmem mentions compression options
                if (combinedOutput.Contains("--compression") || 
                    combinedOutput.Contains("snappy") ||
                    combinedOutput.Contains("go-winpmem"))
                {
                    return "go-winpmem";
                }
                
                // Check for --format option (some v4 builds have this)
                if (combinedOutput.Contains("--format"))
                {
                    AppendLog("Detected WinPmem with --format option");
                    return "winpmem-v4-format";
                }
                
                // Default to v4 style (modern) as that's what Download button provides
                AppendLog("Could not determine WinPmem version, assuming v4 syntax");
                return "winpmem-v4";
            }
            catch (Exception ex)
            {
                AppendLog($"WinPmem detection failed: {ex.Message}");
                // If detection fails, assume legacy version
                return "legacy";
            }
        }

        #endregion

        #region Volatility Analysis

        private async Task RunVolatilityAnalysisAsync()
        {
            if (string.IsNullOrWhiteSpace(MemoryDumpPath) || !File.Exists(MemoryDumpPath))
            {
                StatusMessage = "Please select a valid memory dump file";
                return;
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            Artifacts.Clear();

            try
            {
                Directory.CreateDirectory(MemoryOutputPath);
                AppendLog($"Starting Volatility 3 analysis on: {MemoryDumpPath}");

                var plugins = new[]
                {
                    (RunPsList, "windows.pslist", "Process List"),
                    (RunNetScan, "windows.netscan", "Network Connections"),
                    (RunMalfind, "windows.malfind", "Malware Detection"),
                    (RunDllList, "windows.dlllist", "DLL List"),
                    (RunHandles, "windows.handles", "Handles"),
                    (RunCmdline, "windows.cmdline", "Command Lines")
                };

                var enabledPlugins = plugins.Where(p => p.Item1).ToList();
                var completed = 0;

                foreach (var (_, plugin, name) in enabledPlugins)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested) break;

                    StatusMessage = $"Running {name}...";
                    AppendLog($"Executing plugin: {plugin}");

                    var outputFile = Path.Combine(MemoryOutputPath, $"vol_{plugin.Replace(".", "_")}.json");

                    // Run volatility (vol.py or vol.exe)
                    var volExe = Path.Combine(VolatilityPath, "vol.py");
                    if (!File.Exists(volExe))
                        volExe = Path.Combine(VolatilityPath, "vol.exe");

                    if (!string.IsNullOrEmpty(VolatilityPath) && (File.Exists(volExe) || Directory.Exists(VolatilityPath)))
                    {
                        var result = await RunProcessAsync(
                            "python3",
                            $"\"{volExe}\" -f \"{MemoryDumpPath}\" -r json {plugin}",
                            outputFile);

                        if (result.Success)
                        {
                            var recordCount = await CountJsonRecordsAsync(outputFile);
                            Artifacts.Add(new ForensicArtifact
                            {
                                Type = "Volatility",
                                Name = name,
                                Source = plugin,
                                OutputPath = outputFile,
                                Timestamp = DateTime.Now,
                                RecordCount = recordCount
                            });
                            AppendLog($"✓ {name}: {Artifacts.Last().RecordCount} records");
                        }
                        else
                        {
                            AppendLog($"✗ {name} failed: {result.Error}");
                        }
                    }
                    else
                    {
                        AppendLog($"⚠ Volatility not found. Simulating {plugin} output...");
                        // Create sample output for demo
                        await File.WriteAllTextAsync(outputFile, $"[{{\"plugin\":\"{plugin}\",\"status\":\"simulated\"}}]");
                        Artifacts.Add(new ForensicArtifact
                        {
                            Type = "Volatility (Simulated)",
                            Name = name,
                            Source = plugin,
                            OutputPath = outputFile,
                            Timestamp = DateTime.Now,
                            RecordCount = 1
                        });
                    }

                    completed++;
                    Progress = (completed * 100.0) / enabledPlugins.Count;
                }

                StatusMessage = $"Analysis complete: {Artifacts.Count} artifacts generated";
                AppendLog($"Volatility analysis finished. Output: {MemoryOutputPath}");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        #endregion

        #region KAPE Collection

        private async Task RunKapeCollectionAsync()
        {
            if (string.IsNullOrWhiteSpace(KapeTargetPath) || !Directory.Exists(KapeTargetPath))
            {
                StatusMessage = "Please select a valid target path";
                return;
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                Directory.CreateDirectory(KapeOutputPath);
                AppendLog($"========================================");
                AppendLog($"KAPE Artifact Collection Started");
                AppendLog($"========================================");
                AppendLog($"Target Path: {KapeTargetPath}");
                AppendLog($"Output Path: {KapeOutputPath}");
                AppendLog($"KAPE Path: {(string.IsNullOrEmpty(KapePath) ? "(not set - using manual collection)" : KapePath)}");
                AppendLog($"");

                // Check if KAPE.exe is available and use it
                if (!string.IsNullOrEmpty(KapePath) && File.Exists(KapePath))
                {
                    await RunKapeExeCollectionAsync();
                }
                else
                {
                    await RunManualCollectionAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppendLog($"✗ Fatal Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        private async Task RunKapeExeCollectionAsync()
        {
            AppendLog($"[MODE] Using KAPE.exe for artifact collection");
            AppendLog($"       KAPE uses raw disk access to bypass file locks");
            AppendLog($"");

            // Build target list from selected options
            var targetsList = new List<string>();
            if (CollectPrefetch) targetsList.Add("Prefetch");
            if (CollectAmcache) targetsList.Add("Amcache");
            if (CollectEventLogs) targetsList.Add("EventLogs");
            if (CollectMft) targetsList.Add("$MFT");
            if (CollectRegistry) targetsList.Add("RegistryHives");
            if (CollectSrum) targetsList.Add("SRUM");
            if (CollectShellBags) targetsList.Add("ShellBags");
            if (CollectJumpLists) targetsList.Add("JumpLists");
            if (CollectLnkFiles) targetsList.Add("LNKFiles");
            if (CollectShimcache) targetsList.Add("RegistryHives");
            if (CollectRecycleBin) targetsList.Add("RecycleBin");
            if (CollectUsnJrnl) targetsList.Add("$J");

            // Remove duplicates
            var uniqueTargets = targetsList.Distinct().ToList();
            var targetsArg = string.Join(",", uniqueTargets);

            AppendLog($"[TARGETS] Selected: {targetsArg}");
            AppendLog($"");

            // Get target drive letter from path
            var targetDrive = Path.GetPathRoot(KapeTargetPath)?.TrimEnd('\\') ?? "C:";

            // Build KAPE arguments
            // --tsource: Target source drive
            // --tdest: Target destination folder  
            // --target: Comma-separated target names
            // --tflush: Flush existing files in destination
            var kapeArgs = $"--tsource {targetDrive} --tdest \"{KapeOutputPath}\" --target \"{targetsArg}\" --tflush --debug";

            AppendLog($"[COMMAND] {KapePath}");
            AppendLog($"[ARGS] {kapeArgs}");
            AppendLog($"");
            AppendLog($"--- KAPE Output ---");

            StatusMessage = "Running KAPE collection...";

            var startInfo = new ProcessStartInfo
            {
                FileName = KapePath,
                Arguments = kapeArgs,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(KapePath) ?? ""
            };

            using var process = new Process { StartInfo = startInfo };

            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() => AppendLog(e.Data));
                }
            };

            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    System.Windows.Application.Current?.Dispatcher?.Invoke(() => AppendLog($"[STDERR] {e.Data}"));
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);

            AppendLog($"");
            AppendLog($"--- End KAPE Output ---");
            AppendLog($"");
            AppendLog($"[EXIT CODE] {process.ExitCode}");

            // Check output directory for results
            await ScanKapeOutputAsync();

            if (process.ExitCode == 0)
            {
                StatusMessage = $"KAPE collection complete: {Artifacts.Count} artifacts";
                AppendLog($"✓ KAPE collection completed successfully");
            }
            else
            {
                StatusMessage = $"KAPE exited with code {process.ExitCode}";
                AppendLog($"⚠ KAPE exited with non-zero code. Check output above for errors.");
                AppendLog($"");
                AppendLog($"Common issues:");
                AppendLog($"  • Run as Administrator for raw disk access");
                AppendLog($"  • Ensure target drive is accessible");
                AppendLog($"  • Check KAPE targets exist in Targets folder");
            }
        }

        private async Task ScanKapeOutputAsync()
        {
            await Task.Run(() =>
            {
                if (!Directory.Exists(KapeOutputPath)) return;

                var files = Directory.GetFiles(KapeOutputPath, "*", SearchOption.AllDirectories);
                var totalSize = files.Sum(f => new FileInfo(f).Length);

                System.Windows.Application.Current?.Dispatcher?.Invoke(() =>
                {
                    AppendLog($"");
                    AppendLog($"[OUTPUT SCAN] Found {files.Length} files ({FormatBytes(totalSize)})");

                    // Group by folder
                    var folders = files
                        .Select(f => Path.GetDirectoryName(f))
                        .Where(d => d != null)
                        .Distinct()
                        .Select(d => new
                        {
                            Path = d!,
                            Files = Directory.GetFiles(d!, "*", SearchOption.TopDirectoryOnly).Length
                        })
                        .OrderBy(x => x.Path);

                    foreach (var folder in folders)
                    {
                        var relativePath = Path.GetRelativePath(KapeOutputPath, folder.Path);
                        AppendLog($"  📁 {relativePath}: {folder.Files} files");

                        Artifacts.Add(new ForensicArtifact
                        {
                            Type = "KAPE Target",
                            Name = relativePath,
                            Source = KapeTargetPath,
                            OutputPath = folder.Path,
                            Timestamp = DateTime.Now,
                            RecordCount = folder.Files
                        });
                    }
                });
            });
        }

        private async Task RunManualCollectionAsync()
        {
            AppendLog($"[MODE] Manual file collection (KAPE.exe not available)");
            AppendLog($"");
            AppendLog($"⚠ WARNING: Manual collection CANNOT access locked system files!");
            AppendLog($"           The following files require KAPE or raw disk tools:");
            AppendLog($"           • $MFT (Master File Table)");
            AppendLog($"           • Registry hives (SYSTEM, SAM, SECURITY, SOFTWARE)");
            AppendLog($"           • $UsnJrnl (Change journal)");
            AppendLog($"           • Some event logs currently in use");
            AppendLog($"");
            AppendLog($"💡 TIP: Download KAPE from https://www.kroll.com/kape");
            AppendLog($"");

            var targets = new[]
            {
                (CollectPrefetch, "Prefetch", "Windows\\Prefetch"),
                (CollectAmcache, "Amcache", "Windows\\appcompat\\Programs\\Amcache.hve"),
                (CollectEventLogs, "EventLogs", "Windows\\System32\\winevt\\Logs"),
                (CollectMft, "$MFT", "$MFT"),
                (CollectRegistry, "Registry", "Windows\\System32\\config"),
                (CollectSrum, "SRUM", "Windows\\System32\\sru"),
                (CollectShellBags, "ShellBags", "Users"),
                (CollectJumpLists, "JumpLists", "Users"),
                (CollectLnkFiles, "LNK Files", "Users"),
                (CollectShimcache, "Shimcache", "Windows\\System32\\config\\SYSTEM"),
                (CollectRecycleBin, "RecycleBin", "$Recycle.Bin"),
                (CollectUsnJrnl, "$UsnJrnl", "$Extend\\$UsnJrnl")
            };

            var enabledTargets = targets.Where(t => t.Item1).ToList();
            var completed = 0;
            var successCount = 0;
            var failCount = 0;

            foreach (var (_, name, relativePath) in enabledTargets)
            {
                if (_cancellationTokenSource?.Token.IsCancellationRequested == true) break;

                StatusMessage = $"Collecting {name}...";

                var sourcePath = Path.Combine(KapeTargetPath, relativePath);
                var destPath = Path.Combine(KapeOutputPath, name);

                try
                {
                    if (File.Exists(sourcePath))
                    {
                        Directory.CreateDirectory(destPath);
                        await Task.Run(() => File.Copy(sourcePath, Path.Combine(destPath, Path.GetFileName(sourcePath)), true));
                        AppendLog($"✓ Collected: {name}");
                        successCount++;
                    }
                    else if (Directory.Exists(sourcePath))
                    {
                        var fileCount = await CopyDirectoryAsync(sourcePath, destPath);
                        if (fileCount > 0)
                        {
                            AppendLog($"✓ Collected: {name} ({fileCount} files)");
                            successCount++;
                        }
                        else
                        {
                            AppendLog($"⚠ Collected: {name} (0 files - may be access denied)");
                        }
                    }
                    else
                    {
                        AppendLog($"⚠ Not found: {relativePath}");
                    }

                    if (Directory.Exists(destPath) && Directory.GetFiles(destPath, "*", SearchOption.AllDirectories).Length > 0)
                    {
                        Artifacts.Add(new ForensicArtifact
                        {
                            Type = "Manual Collection",
                            Name = name,
                            Source = relativePath,
                            OutputPath = destPath,
                            Timestamp = DateTime.Now
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    AppendLog($"✗ Access denied: {name} (requires KAPE for raw disk access)");
                    failCount++;
                }
                catch (IOException ex) when (ex.HResult == -2147024864) // File in use
                {
                    AppendLog($"✗ File locked: {name} (requires KAPE for raw disk access)");
                    failCount++;
                }
                catch (Exception ex)
                {
                    AppendLog($"✗ Error collecting {name}: {ex.Message}");
                    failCount++;
                }

                completed++;
                Progress = (completed * 100.0) / enabledTargets.Count;
            }

            AppendLog($"");
            AppendLog($"========================================");
            AppendLog($"Collection Summary");
            AppendLog($"========================================");
            AppendLog($"Successful: {successCount}");
            AppendLog($"Failed: {failCount}");
            AppendLog($"Output: {KapeOutputPath}");
            
            StatusMessage = $"Collection complete: {Artifacts.Count} artifacts ({failCount} failed)";
            AppendLog($"");
            AppendLog($"KAPE collection finished.");
        }

        private async Task<int> CopyDirectoryAsync(string source, string dest)
        {
            return await Task.Run(() => CopyDirectoryRecursive(source, dest));
        }

        private int CopyDirectoryRecursive(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            var copiedCount = 0;

            foreach (var file in Directory.GetFiles(source))
            {
                try
                {
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                    copiedCount++;
                }
                catch { /* Skip locked files */ }
            }

            foreach (var dir in Directory.GetDirectories(source))
            {
                try
                {
                    copiedCount += CopyDirectoryRecursive(dir, Path.Combine(dest, Path.GetFileName(dir)));
                }
                catch { /* Skip inaccessible dirs */ }
            }

            return copiedCount;
        }

        #endregion

        #region Plaso Timeline

        private async Task RunPlasoTimelineAsync()
        {
            if (string.IsNullOrWhiteSpace(PlasoEvidencePath) || !Directory.Exists(PlasoEvidencePath))
            {
                StatusMessage = "Please select evidence folder";
                AppendLog("========================================");
                AppendLog("Plaso (log2timeline) - Super-Timeline Creator");
                AppendLog("========================================");
                AppendLog("⚠ Evidence folder not selected.");
                AppendLog("");
                AppendLog("Plaso creates forensic timelines from evidence sources.");
                AppendLog("Download: https://github.com/log2timeline/plaso/releases");
                AppendLog("");
                AppendLog("Installation options:");
                AppendLog("  • Windows: Download standalone ZIP from releases");
                AppendLog("  • Python: pip install plaso");
                AppendLog("  • Docker: docker pull log2timeline/plaso");
                return;
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var plasoDir = Path.GetDirectoryName(PlasoStorageFile);
                if (!string.IsNullOrEmpty(plasoDir)) Directory.CreateDirectory(plasoDir);

                AppendLog($"========================================");
                AppendLog($"Plaso Timeline Creation Started");
                AppendLog($"========================================");
                AppendLog($"Plaso Path: {(string.IsNullOrEmpty(PlasoPath) ? "(using system PATH)" : PlasoPath)}");
                AppendLog($"Evidence: {PlasoEvidencePath}");
                AppendLog($"Storage File: {PlasoStorageFile}");
                AppendLog($"");

                StatusMessage = "Creating timeline (this may take a long time)...";

                var log2timeline = Path.Combine(PlasoPath, "log2timeline.py");
                if (!File.Exists(log2timeline))
                {
                    // Try standalone exe
                    log2timeline = Path.Combine(PlasoPath, "log2timeline.exe");
                    if (!File.Exists(log2timeline))
                        log2timeline = "log2timeline"; // Try system PATH
                }

                AppendLog($"[COMMAND] {log2timeline}");
                AppendLog($"[ARGS] --storage-file \"{PlasoStorageFile}\" \"{PlasoEvidencePath}\"");
                AppendLog($"");
                AppendLog($"--- Plaso Output ---");

                var result = await RunProcessWithLoggingAsync(
                    log2timeline,
                    $"--storage-file \"{PlasoStorageFile}\" \"{PlasoEvidencePath}\"");

                AppendLog($"");
                AppendLog($"--- End Plaso Output ---");
                AppendLog($"");

                if (result.Success)
                {
                    AppendLog($"✓ Timeline created successfully!");
                    AppendLog($"  Storage file: {PlasoStorageFile}");

                    var fileInfo = new FileInfo(PlasoStorageFile);
                    if (fileInfo.Exists)
                    {
                        AppendLog($"  Size: {FormatBytes(fileInfo.Length)}");
                    }

                    Artifacts.Add(new ForensicArtifact
                    {
                        Type = "Plaso Timeline",
                        Name = "Super-Timeline",
                        Source = PlasoEvidencePath,
                        OutputPath = PlasoStorageFile,
                        Timestamp = DateTime.Now
                    });

                    StatusMessage = "Plaso timeline created";
                }
                else
                {
                    AppendLog($"✗ Plaso failed or not found.");
                    AppendLog($"");
                    AppendLog($"Error: {result.Error}");
                    AppendLog($"");
                    AppendLog($"Troubleshooting:");
                    AppendLog($"  • Ensure Plaso is installed");
                    AppendLog($"  • Click 'Download Plaso' to get installation page");
                    AppendLog($"  • Set Plaso Path to installation folder");
                    StatusMessage = "Plaso failed - see log for details";
                }
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task<(bool Success, string Output, string Error)> RunProcessWithLoggingAsync(string fileName, string arguments)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = new Process { StartInfo = startInfo };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() => AppendLog(e.Data));
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        System.Windows.Application.Current?.Dispatcher?.Invoke(() => AppendLog($"[STDERR] {e.Data}"));
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync(_cancellationTokenSource?.Token ?? CancellationToken.None);

                AppendLog($"[EXIT CODE] {process.ExitCode}");

                return (process.ExitCode == 0, outputBuilder.ToString(), errorBuilder.ToString());
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        private async Task ExportPlasoToOpenSearchAsync()
        {
            if (!File.Exists(PlasoStorageFile))
            {
                StatusMessage = "Create a Plaso timeline first";
                return;
            }

            IsRunning = true;
            try
            {
                AppendLog($"Exporting Plaso timeline to OpenSearch...");

                var psort = Path.Combine(PlasoPath, "psort.py");
                if (!File.Exists(psort))
                    psort = "psort.py";

                var timestamp = DateTime.Now.ToString("yyyy.MM");
                var indexName = $"dfir-plaso-{timestamp}";

                var result = await RunProcessAsync(
                    "python3",
                    $"\"{psort}\" -o opensearch --server {OpenSearchUrl} --index_name {indexName} \"{PlasoStorageFile}\"",
                    null);

                if (result.Success)
                {
                    AppendLog($"✓ Exported to OpenSearch index: {indexName}");
                    StatusMessage = "Plaso export complete";
                }
                else
                {
                    AppendLog($"✗ Export failed: {result.Error}");
                    StatusMessage = "Export failed";
                }
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
            }
        }

        #endregion

        #region Velociraptor

        private async Task RunVelociraptorCollectAsync()
        {
            if (string.IsNullOrWhiteSpace(VelociraptorPath) || !File.Exists(VelociraptorPath))
            {
                StatusMessage = "Please select Velociraptor executable";
                AppendLog("========================================");
                AppendLog("Velociraptor - Endpoint Visibility Tool");
                AppendLog("========================================");
                AppendLog("");
                AppendLog("⚠ Velociraptor executable not configured.");
                AppendLog("");
                AppendLog("Velociraptor enables:");
                AppendLog("  • Live forensic collection");
                AppendLog("  • VQL-based threat hunting");
                AppendLog("  • Fleet-wide artifact collection");
                AppendLog("  • Real-time monitoring");
                AppendLog("");
                AppendLog("💡 Click 'Download Velociraptor' to get the tool.");
                AppendLog("");
                AppendLog("Download: https://github.com/Velocidex/velociraptor/releases");
                return;
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var outputDir = Path.Combine(KapeOutputPath, "Velociraptor");
                Directory.CreateDirectory(outputDir);

                AppendLog($"========================================");
                AppendLog($"Velociraptor Artifact Collection");
                AppendLog($"========================================");
                AppendLog($"Executable: {VelociraptorPath}");
                AppendLog($"Artifact: {VelociraptorArtifact}");
                AppendLog($"Output Dir: {outputDir}");
                AppendLog($"");

                StatusMessage = $"Collecting {VelociraptorArtifact}...";

                var outputFile = Path.Combine(outputDir, $"velociraptor_{VelociraptorArtifact.Replace(".", "_")}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                // Run Velociraptor in offline collection mode
                var args = $"artifacts collect {VelociraptorArtifact} --format json";
                if (!string.IsNullOrEmpty(VelociraptorConfig) && File.Exists(VelociraptorConfig))
                {
                    args = $"--config \"{VelociraptorConfig}\" " + args;
                }

                AppendLog($"[COMMAND] {VelociraptorPath}");
                AppendLog($"[ARGS] {args}");
                AppendLog($"");
                AppendLog($"--- Velociraptor Output ---");

                var result = await RunProcessWithLoggingAsync(VelociraptorPath, args);

                AppendLog($"");
                AppendLog($"--- End Velociraptor Output ---");

                // Save output to file
                if (!string.IsNullOrEmpty(result.Output))
                {
                    await File.WriteAllTextAsync(outputFile, result.Output);
                }

                if (result.Success)
                {
                    var recordCount = await CountJsonRecordsAsync(outputFile);
                    AppendLog($"");
                    AppendLog($"✓ Collection complete: {VelociraptorArtifact}");
                    AppendLog($"  Records collected: {recordCount}");
                    AppendLog($"  Output file: {outputFile}");

                    Artifacts.Add(new ForensicArtifact
                    {
                        Type = "Velociraptor",
                        Name = VelociraptorArtifact,
                        Source = Environment.MachineName,
                        OutputPath = outputFile,
                        Timestamp = DateTime.Now,
                        RecordCount = recordCount
                    });

                    StatusMessage = $"Velociraptor collection complete: {recordCount} records";
                }
                else
                {
                    AppendLog($"");
                    AppendLog($"✗ Velociraptor collection failed.");
                    AppendLog($"Error: {result.Error}");
                    AppendLog($"");
                    AppendLog($"Troubleshooting:");
                    AppendLog($"  • Run as Administrator");
                    AppendLog($"  • Verify artifact name exists");
                    AppendLog($"  • Check Velociraptor version");
                    StatusMessage = "Velociraptor collection failed - see log";
                }
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        #endregion

        #region Kusto Queries

        private async Task RunKustoQueryAsync()
        {
            if (string.IsNullOrWhiteSpace(KustoQuery))
            {
                StatusMessage = "Please enter or select a Kusto query";
                return;
            }

            IsRunning = true;
            try
            {
                AppendLog("Executing Kusto query...");
                AppendLog($"Endpoint: {KustoEndpoint}");
                AppendLog($"Database: {KustoDatabase}");
                AppendLog($"Query:\n{KustoQuery}");

                if (!string.IsNullOrEmpty(KustoEndpoint) && !string.IsNullOrEmpty(KustoDatabase))
                {
                    // Real Kusto execution would require Azure.Data.Explorer SDK
                    // For now, we show the query and explain how to run it
                    AppendLog("\n⚠ Real-time Kusto execution requires Azure Data Explorer SDK.");
                    AppendLog("To run this query:");
                    AppendLog("1. Open Azure Data Explorer: https://dataexplorer.azure.com");
                    AppendLog($"2. Connect to cluster: {KustoEndpoint}");
                    AppendLog($"3. Select database: {KustoDatabase}");
                    AppendLog("4. Paste and run the query above");
                }
                else
                {
                    AppendLog("\n💡 Configure Kusto endpoint and database for direct query execution.");
                    AppendLog("Or copy this query to Azure Data Explorer / Log Analytics.");
                }

                StatusMessage = "Query ready - copy to Azure Data Explorer";
            }
            finally
            {
                IsRunning = false;
            }
            await Task.CompletedTask;
        }

        #endregion

        #region Local KQL Database

        private void InitializeLocalKqlTemplates()
        {
            // Load all templates from the cheat sheet
            var templates = KqlCheatSheetTemplates.GetAllTemplates();
            foreach (var template in templates)
            {
                LocalKqlTemplates.Add(template);
            }

            // Load categories
            LocalKqlCategories.Clear();
            LocalKqlCategories.Add("All");
            foreach (var category in KqlCheatSheetTemplates.GetCategories())
            {
                LocalKqlCategories.Add(category);
            }

            UpdateFilteredKqlTemplates();
        }

        private void UpdateFilteredKqlTemplates()
        {
            FilteredLocalKqlTemplates.Clear();
            
            var filtered = SelectedLocalKqlCategory == "All"
                ? LocalKqlTemplates
                : LocalKqlTemplates.Where(t => t.Category == SelectedLocalKqlCategory);

            foreach (var template in filtered)
            {
                FilteredLocalKqlTemplates.Add(template);
            }
        }

        private async Task InitializeLocalKqlDatabaseAsync()
        {
            try
            {
                LocalDbStatus = "Initializing...";
                AppendLog("Initializing local forensics database...");

                _kqlEngine = new ForensicsKqlEngine();
                await _kqlEngine.InitializeAsync();

                _kqlEngineInitialized = true;
                LocalDbStatus = "Connected";
                
                await RefreshAvailableTablesAsync();
                
                AppendLog("✅ Local KQL database initialized successfully");
                AppendLog($"   Database path: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PlatypusTools", "forensics_data.db")}");
                StatusMessage = "Local database ready";
            }
            catch (Exception ex)
            {
                LocalDbStatus = "Error";
                AppendLog($"❌ Failed to initialize database: {ex.Message}");
                StatusMessage = "Database initialization failed";
            }
        }

        private async Task RefreshAvailableTablesAsync()
        {
            if (_kqlEngine == null) return;

            try
            {
                AvailableTables.Clear();
                var tables = await _kqlEngine.GetTablesAsync();
                TotalRecords = 0;

                foreach (var table in tables.Where(t => !t.StartsWith("sqlite_")))
                {
                    AvailableTables.Add(table);
                    var count = await _kqlEngine.GetTableRecordCountAsync(table);
                    TotalRecords += count;
                }

                AppendLog($"📊 Available tables: {AvailableTables.Count}, Total records: {TotalRecords:N0}");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Error refreshing tables: {ex.Message}");
            }
        }

        private async Task LoadTableColumnsAsync(string tableName)
        {
            if (_kqlEngine == null) return;

            try
            {
                TableColumns.Clear();
                var columns = await _kqlEngine.GetTableColumnsAsync(tableName);
                foreach (var (name, type) in columns)
                {
                    TableColumns.Add($"{name} ({type})");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Error loading columns: {ex.Message}");
            }
        }

        private void InsertTableIntoQuery(object? parameter)
        {
            if (!string.IsNullOrEmpty(SelectedTable))
            {
                if (string.IsNullOrWhiteSpace(LocalKqlQuery))
                {
                    LocalKqlQuery = SelectedTable + "\n| take 100";
                }
                else
                {
                    LocalKqlQuery = SelectedTable + "\n" + LocalKqlQuery;
                }
            }
        }

        private async Task ExecuteLocalKqlQueryAsync()
        {
            if (_kqlEngine == null || string.IsNullOrWhiteSpace(LocalKqlQuery))
            {
                StatusMessage = "Please enter a KQL query";
                return;
            }

            IsRunning = true;
            try
            {
                AppendLog("═══════════════════════════════════════════");
                AppendLog("Executing local KQL query...");
                AppendLog($"Query:\n{LocalKqlQuery}");

                // Translate KQL to SQL and show it
                try
                {
                    TranslatedSql = _kqlEngine.TranslateKqlToSql(LocalKqlQuery);
                    AppendLog($"\nTranslated SQL:\n{TranslatedSql}");
                }
                catch (Exception ex)
                {
                    AppendLog($"⚠ KQL translation warning: {ex.Message}");
                }

                // Execute the query
                var stopwatch = Stopwatch.StartNew();
                LocalKqlResults = await _kqlEngine.ExecuteKqlAsync(LocalKqlQuery);
                stopwatch.Stop();

                AppendLog($"\n✅ Query completed in {stopwatch.ElapsedMilliseconds}ms");
                AppendLog($"   Rows returned: {LocalKqlResults.Rows.Count:N0}");
                AppendLog($"   Columns: {LocalKqlResults.Columns.Count}");

                StatusMessage = $"Query returned {LocalKqlResults.Rows.Count:N0} rows in {stopwatch.ElapsedMilliseconds}ms";
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Query failed: {ex.Message}");
                StatusMessage = "Query execution failed";
                LocalKqlResults = null;
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ClearLocalDatabaseAsync()
        {
            if (_kqlEngine == null) return;

            try
            {
                AppendLog("Clearing all data from local database...");
                await _kqlEngine.ClearAllDataAsync();
                await RefreshAvailableTablesAsync();
                LocalKqlResults = null;
                AppendLog("✅ Database cleared");
                StatusMessage = "Database cleared";
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Error clearing database: {ex.Message}");
            }
        }

        private async Task ExportLocalKqlResultsAsync()
        {
            if (LocalKqlResults == null || LocalKqlResults.Rows.Count == 0)
            {
                StatusMessage = "No results to export";
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export KQL Results",
                Filter = "CSV files|*.csv|JSON files|*.json",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        var rows = new List<Dictionary<string, object?>>();
                        foreach (DataRow row in LocalKqlResults.Rows)
                        {
                            var dict = new Dictionary<string, object?>();
                            foreach (DataColumn col in LocalKqlResults.Columns)
                            {
                                dict[col.ColumnName] = row[col] == DBNull.Value ? null : row[col];
                            }
                            rows.Add(dict);
                        }
                        await File.WriteAllTextAsync(dialog.FileName, 
                            JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        // Header
                        sb.AppendLine(string.Join(",", LocalKqlResults.Columns.Cast<DataColumn>().Select(c => $"\"{c.ColumnName}\"")));
                        // Data
                        foreach (DataRow row in LocalKqlResults.Rows)
                        {
                            sb.AppendLine(string.Join(",", row.ItemArray.Select(v => $"\"{v?.ToString()?.Replace("\"", "\"\"") ?? ""}\"")));
                        }
                        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                    }

                    AppendLog($"✅ Results exported to: {dialog.FileName}");
                    StatusMessage = $"Exported {LocalKqlResults.Rows.Count} rows";
                }
                catch (Exception ex)
                {
                    AppendLog($"❌ Export failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Store Volatility results in local database after analysis.
        /// </summary>
        public async Task StoreVolatilityResultsAsync(string plugin, string outputFile, string computer)
        {
            if (_kqlEngine == null || !_kqlEngineInitialized)
            {
                AppendLog("⚠ Local database not initialized - results not stored");
                return;
            }

            try
            {
                if (!File.Exists(outputFile)) return;

                var content = await File.ReadAllTextAsync(outputFile);
                var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                if (plugin.Contains("pslist", StringComparison.OrdinalIgnoreCase))
                {
                    var processes = new List<Dictionary<string, object?>>();
                    foreach (var line in lines.Skip(1)) // Skip header
                    {
                        var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            processes.Add(new Dictionary<string, object?>
                            {
                                ["PID"] = int.TryParse(parts[0], out var pid) ? pid : 0,
                                ["PPID"] = int.TryParse(parts[1], out var ppid) ? ppid : 0,
                                ["ProcessName"] = parts.Length > 2 ? parts[2] : "",
                                ["CommandLine"] = parts.Length > 3 ? parts[3] : ""
                            });
                        }
                    }
                    await _kqlEngine.InsertProcessesAsync(processes, $"volatility-{plugin}", computer);
                    AppendLog($"   📥 Stored {processes.Count} process records in local DB");
                }
                else if (plugin.Contains("netscan", StringComparison.OrdinalIgnoreCase))
                {
                    var connections = new List<Dictionary<string, object?>>();
                    foreach (var line in lines.Skip(1))
                    {
                        var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 5)
                        {
                            connections.Add(new Dictionary<string, object?>
                            {
                                ["LocalAddress"] = parts[0],
                                ["LocalPort"] = int.TryParse(parts[1], out var lp) ? lp : 0,
                                ["RemoteAddress"] = parts[2],
                                ["RemotePort"] = int.TryParse(parts[3], out var rp) ? rp : 0,
                                ["State"] = parts[4],
                                ["ProcessName"] = parts.Length > 5 ? parts[5] : ""
                            });
                        }
                    }
                    await _kqlEngine.InsertNetworkConnectionsAsync(connections, $"volatility-{plugin}", computer);
                    AppendLog($"   📥 Stored {connections.Count} network connection records in local DB");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"⚠ Failed to store results: {ex.Message}");
            }
        }

        /// <summary>
        /// Store artifact collection results in local database.
        /// </summary>
        public async Task StoreArtifactResultsAsync(string artifactType, string outputPath, string computer)
        {
            if (_kqlEngine == null || !_kqlEngineInitialized) return;

            try
            {
                var artifacts = new List<Dictionary<string, object?>>();
                
                // Store as generic artifacts for now
                artifacts.Add(new Dictionary<string, object?>
                {
                    ["Name"] = artifactType,
                    ["Value"] = outputPath,
                    ["Description"] = $"Collected {artifactType} artifact"
                });

                await _kqlEngine.InsertArtifactsAsync(artifactType, artifacts, "kape", computer);
                AppendLog($"   📥 Stored {artifactType} artifact reference in local DB");
            }
            catch (Exception ex)
            {
                AppendLog($"⚠ Failed to store artifact: {ex.Message}");
            }
        }

        #endregion

        #region Malware Analysis (oletools/PDF)

        private async Task RunMalwareAnalysisAsync()
        {
            if (string.IsNullOrWhiteSpace(DocumentPath) || !Directory.Exists(DocumentPath))
            {
                StatusMessage = "Please select a documents folder";
                AppendLog("========================================");
                AppendLog("Malware Analysis (oletools)");
                AppendLog("========================================");
                AppendLog("⚠ No documents folder selected.");
                AppendLog("");
                AppendLog("This tool analyzes Office documents and PDFs for:");
                AppendLog("  • VBA macros with suspicious code");
                AppendLog("  • Auto-execution triggers");
                AppendLog("  • Embedded objects and scripts");
                AppendLog("  • JavaScript in PDFs");
                AppendLog("");
                AppendLog("💡 Click 'Download oletools' to install the analysis tools.");
                return;
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            MalwareResults.Clear();

            try
            {
                AppendLog($"========================================");
                AppendLog($"Malware Analysis Started");
                AppendLog($"========================================");
                AppendLog($"oletools Path: {(string.IsNullOrEmpty(OletoolsPath) ? "(using system PATH)" : OletoolsPath)}");
                AppendLog($"Documents Path: {DocumentPath}");
                AppendLog($"");

                var files = Directory.GetFiles(DocumentPath, "*.*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var ext = Path.GetExtension(f).ToLowerInvariant();
                        return ext is ".doc" or ".docx" or ".docm" or ".xls" or ".xlsx" or ".xlsm"
                            or ".ppt" or ".pptx" or ".pptm" or ".pdf";
                    })
                    .ToList();

                AppendLog($"Found {files.Count} documents to analyze");
                AppendLog($"");
                var processed = 0;

                foreach (var file in files)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested) break;

                    StatusMessage = $"Analyzing: {Path.GetFileName(file)}";
                    AppendLog($"[{processed + 1}/{files.Count}] Analyzing: {Path.GetFileName(file)}");
                    
                    var ext = Path.GetExtension(file).ToLowerInvariant();

                    // Office documents - use oletools
                    if (ext is ".doc" or ".docx" or ".docm" or ".xls" or ".xlsx" or ".xlsm" or ".ppt" or ".pptx" or ".pptm")
                    {
                        await AnalyzeWithOletoolsAsync(file);
                    }
                    // PDF documents
                    else if (ext == ".pdf")
                    {
                        await AnalyzePdfAsync(file);
                    }

                    processed++;
                    Progress = (processed * 100.0) / files.Count;
                }

                var suspicious = MalwareResults.Count(r => r.IsSuspicious);
                
                AppendLog($"");
                AppendLog($"========================================");
                AppendLog($"Analysis Summary");
                AppendLog($"========================================");
                AppendLog($"Files analyzed: {processed}");
                AppendLog($"Suspicious files: {suspicious}");
                
                if (suspicious > 0)
                {
                    AppendLog($"");
                    AppendLog($"⚠ SUSPICIOUS FILES DETECTED:");
                    foreach (var sus in MalwareResults.Where(r => r.IsSuspicious))
                    {
                        AppendLog($"  • {sus.FileName}");
                        foreach (var indicator in sus.Indicators)
                        {
                            AppendLog($"    - {indicator}");
                        }
                    }
                }
                
                StatusMessage = $"Analysis complete: {suspicious} suspicious files found";
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        private async Task AnalyzeWithOletoolsAsync(string filePath)
        {
            var result = new MalwareAnalysisResult
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileType = "Office Document"
            };

            try
            {
                // olevba - VBA macro extraction and analysis
                if (RunOlevba)
                {
                    var olevba = Path.Combine(OletoolsPath, "olevba.py");
                    if (!File.Exists(olevba)) olevba = "olevba";

                    var vbaResult = await RunProcessAsync("python3", $"\"{olevba}\" -a \"{filePath}\"", null);
                    if (vbaResult.Success && !string.IsNullOrEmpty(vbaResult.Output))
                    {
                        result.HasMacros = vbaResult.Output.Contains("VBA MACRO") || vbaResult.Output.Contains("AutoOpen");
                        result.MacroDetails = vbaResult.Output;

                        // Check for suspicious indicators
                        if (vbaResult.Output.Contains("AutoOpen") || vbaResult.Output.Contains("Document_Open"))
                            result.Indicators.Add("Auto-execution trigger detected");
                        if (vbaResult.Output.Contains("Shell") || vbaResult.Output.Contains("WScript.Shell"))
                            result.Indicators.Add("Shell execution capability");
                        if (vbaResult.Output.Contains("PowerShell") || vbaResult.Output.Contains("powershell"))
                            result.Indicators.Add("PowerShell execution");
                        if (vbaResult.Output.Contains("DownloadFile") || vbaResult.Output.Contains("URLDownloadToFile"))
                            result.Indicators.Add("Download capability");
                        if (vbaResult.Output.Contains("CreateObject") && vbaResult.Output.Contains("XMLHTTP"))
                            result.Indicators.Add("HTTP request capability");
                    }
                }

                // mraptor - Macro Raptor detection
                if (RunMraptor)
                {
                    var mraptor = Path.Combine(OletoolsPath, "mraptor.py");
                    if (!File.Exists(mraptor)) mraptor = "mraptor";

                    var mraptorResult = await RunProcessAsync("python3", $"\"{mraptor}\" \"{filePath}\"", null);
                    if (mraptorResult.Success && mraptorResult.Output.Contains("SUSPICIOUS"))
                    {
                        result.IsSuspicious = true;
                        result.Indicators.Add("mraptor: SUSPICIOUS");
                    }
                }

                if (result.HasMacros || result.IsSuspicious || result.Indicators.Count > 0)
                {
                    result.IsSuspicious = result.IsSuspicious || result.Indicators.Count > 0;
                    MalwareResults.Add(result);
                    AppendLog($"⚠ {result.FileName}: {string.Join(", ", result.Indicators)}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"  Error analyzing {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        private async Task AnalyzePdfAsync(string filePath)
        {
            var result = new MalwareAnalysisResult
            {
                FileName = Path.GetFileName(filePath),
                FilePath = filePath,
                FileType = "PDF Document"
            };

            try
            {
                if (RunPdfParser && !string.IsNullOrEmpty(PdfParserPath) && File.Exists(PdfParserPath))
                {
                    var pdfResult = await RunProcessAsync("python3", $"\"{PdfParserPath}\" -a \"{filePath}\"", null);
                    if (pdfResult.Success && !string.IsNullOrEmpty(pdfResult.Output))
                    {
                        // Check for suspicious elements
                        if (pdfResult.Output.Contains("/JavaScript"))
                            result.Indicators.Add("Contains JavaScript");
                        if (pdfResult.Output.Contains("/JS"))
                            result.Indicators.Add("Contains JS action");
                        if (pdfResult.Output.Contains("/OpenAction"))
                            result.Indicators.Add("Has OpenAction (auto-execute)");
                        if (pdfResult.Output.Contains("/Launch"))
                            result.Indicators.Add("Has Launch action");
                        if (pdfResult.Output.Contains("/EmbeddedFile"))
                            result.Indicators.Add("Contains embedded files");
                        if (pdfResult.Output.Contains("/URI") || pdfResult.Output.Contains("/URL"))
                            result.Indicators.Add("Contains URLs");

                        result.MacroDetails = pdfResult.Output;
                    }
                }
                else
                {
                    // Basic PDF analysis without pdf-parser
                    var content = await File.ReadAllTextAsync(filePath);
                    if (content.Contains("/JavaScript")) result.Indicators.Add("Contains JavaScript");
                    if (content.Contains("/OpenAction")) result.Indicators.Add("Has OpenAction");
                    if (content.Contains("/Launch")) result.Indicators.Add("Has Launch action");
                }

                if (result.Indicators.Count > 0)
                {
                    result.IsSuspicious = true;
                    MalwareResults.Add(result);
                    AppendLog($"⚠ {result.FileName}: {string.Join(", ", result.Indicators)}");
                }
            }
            catch (Exception ex)
            {
                AppendLog($"  Error analyzing {Path.GetFileName(filePath)}: {ex.Message}");
            }
        }

        #endregion

        #region Bulk Extractor

        private async Task RunBulkExtractorAsync()
        {
            if (string.IsNullOrWhiteSpace(BulkExtractorInput))
            {
                StatusMessage = "Please select input folder or disk image";
                AppendLog("========================================");
                AppendLog("bulk_extractor - Feature Extraction Tool");
                AppendLog("========================================");
                AppendLog("⚠ No input selected.");
                AppendLog("");
                AppendLog("bulk_extractor extracts features from disk images:");
                AppendLog("  • Email addresses");
                AppendLog("  • URLs and domains");
                AppendLog("  • Credit card numbers");
                AppendLog("  • Phone numbers");
                AppendLog("  • IP addresses");
                AppendLog("");
                AppendLog("💡 Click 'Download bulk_extractor' to get the tool.");
                return;
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            BulkExtractorResults.Clear();

            try
            {
                var outputDir = Path.Combine(KapeOutputPath, "bulk_extractor_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(outputDir);

                AppendLog($"========================================");
                AppendLog($"bulk_extractor Scan Started");
                AppendLog($"========================================");
                AppendLog($"bulk_extractor Path: {(string.IsNullOrEmpty(BulkExtractorPath) ? "(using system PATH)" : BulkExtractorPath)}");
                AppendLog($"Input: {BulkExtractorInput}");
                AppendLog($"Output: {outputDir}");
                AppendLog($"");

                StatusMessage = "Running bulk_extractor (this may take a long time)...";

                // Build scanner list
                var scanners = new List<string>();
                if (ExtractEmails) scanners.Add("email");
                if (ExtractUrls) scanners.Add("url");
                if (ExtractCreditCards) scanners.Add("accts");
                if (ExtractIpAddresses) scanners.Add("net");

                AppendLog($"Enabled scanners: {string.Join(", ", scanners)}");
                AppendLog($"");

                var scannerArgs = scanners.Count > 0 ? $"-e {string.Join(" -e ", scanners)}" : "";

                var bulkExe = !string.IsNullOrEmpty(BulkExtractorPath) && File.Exists(BulkExtractorPath)
                    ? BulkExtractorPath
                    : "bulk_extractor";

                var args = $"{scannerArgs} -o \"{outputDir}\" \"{BulkExtractorInput}\"";
                AppendLog($"[COMMAND] {bulkExe}");
                AppendLog($"[ARGS] {args}");
                AppendLog($"");
                AppendLog($"--- bulk_extractor Output ---");

                var result = await RunProcessWithLoggingAsync(bulkExe, args);

                AppendLog($"");
                AppendLog($"--- End bulk_extractor Output ---");
                AppendLog($"");

                if (result.Success)
                {
                    // Parse results
                    await ParseBulkExtractorResultsAsync(outputDir);
                    
                    AppendLog($"========================================");
                    AppendLog($"Extraction Summary");
                    AppendLog($"========================================");
                    
                    var emailCount = BulkExtractorResults.Count(r => r.Type == "email");
                    var urlCount = BulkExtractorResults.Count(r => r.Type == "url");
                    var ipCount = BulkExtractorResults.Count(r => r.Type == "ip");
                    var ccnCount = BulkExtractorResults.Count(r => r.Type == "credit_card");
                    
                    AppendLog($"Emails found: {emailCount}");
                    AppendLog($"URLs found: {urlCount}");
                    AppendLog($"IP addresses: {ipCount}");
                    AppendLog($"Credit cards: {ccnCount}");
                    AppendLog($"Total findings: {BulkExtractorResults.Count}");
                    AppendLog($"Output folder: {outputDir}");
                    
                    StatusMessage = $"Extraction complete: {BulkExtractorResults.Count} findings";
                }
                else
                {
                    AppendLog($"✗ bulk_extractor failed or not found.");
                    AppendLog($"");
                    AppendLog($"Error: {result.Error}");
                    AppendLog($"");
                    AppendLog($"Troubleshooting:");
                    AppendLog($"  • Click 'Download' to install bulk_extractor");
                    AppendLog($"  • Or set path to bulk_extractor.exe manually");
                    StatusMessage = "bulk_extractor failed - see log";
                }

                Artifacts.Add(new ForensicArtifact
                {
                    Type = "bulk_extractor",
                    Name = "Feature Extraction",
                    Source = BulkExtractorInput,
                    OutputPath = outputDir,
                    Timestamp = DateTime.Now,
                    RecordCount = BulkExtractorResults.Count
                });
            }
            catch (Exception ex)
            {
                AppendLog($"✗ Error: {ex.Message}");
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ParseBulkExtractorResultsAsync(string outputDir)
        {
            var resultFiles = new[]
            {
                ("email.txt", "email"),
                ("url.txt", "url"),
                ("ip.txt", "ip"),
                ("ccn.txt", "credit_card"),
                ("telephone.txt", "phone")
            };

            foreach (var (filename, type) in resultFiles)
            {
                var filePath = Path.Combine(outputDir, filename);
                if (File.Exists(filePath))
                {
                    var lines = await File.ReadAllLinesAsync(filePath);
                    foreach (var line in lines.Take(1000)) // Limit for performance
                    {
                        if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
                        {
                            var parts = line.Split('\t');
                            BulkExtractorResults.Add(new BulkExtractorResult
                            {
                                Type = type,
                                Value = parts.Length > 1 ? parts[1] : parts[0],
                                Source = parts.Length > 0 ? parts[0] : "unknown"
                            });
                        }
                    }
                }
            }

            AppendLog($"  Emails: {BulkExtractorResults.Count(r => r.Type == "email")}");
            AppendLog($"  URLs: {BulkExtractorResults.Count(r => r.Type == "url")}");
            AppendLog($"  IPs: {BulkExtractorResults.Count(r => r.Type == "ip")}");
        }

        #endregion

        #region OpenSearch Integration

        private async Task TestOpenSearchConnectionAsync()
        {
            try
            {
                StatusMessage = "Testing OpenSearch connection...";
                var response = await _httpClient.GetAsync(OpenSearchUrl);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    IsOpenSearchConnected = true;
                    StatusMessage = "OpenSearch connected successfully";
                    AppendLog($"OpenSearch connected: {OpenSearchUrl}");
                    AppendLog(content);
                }
                else
                {
                    IsOpenSearchConnected = false;
                    StatusMessage = $"OpenSearch connection failed: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                IsOpenSearchConnected = false;
                StatusMessage = $"Connection failed: {ex.Message}";
                AppendLog($"OpenSearch error: {ex.Message}");
            }
        }

        private async Task CreateOpenSearchPipelinesAsync()
        {
            IsRunning = true;
            try
            {
                StatusMessage = "Creating OpenSearch pipelines...";

                var pipelines = new[]
                {
                    ("dfir-volatility", GetVolatilityPipeline()),
                    ("dfir-evtx", GetEvtxPipeline()),
                    ("dfir-amcache", GetAmcachePipeline()),
                    ("dfir-mftecmd", GetMftPipeline()),
                    ("osint-docmeta", GetDocMetaPipeline())
                };

                foreach (var (name, body) in pipelines)
                {
                    try
                    {
                        using var content = new StringContent(body, Encoding.UTF8, "application/json");
                        var response = await _httpClient.PutAsync($"{OpenSearchUrl}/_ingest/pipeline/{name}", content);
                        
                        if (response.IsSuccessStatusCode)
                            AppendLog($"✓ Created pipeline: {name}");
                        else
                            AppendLog($"✗ Failed to create {name}: {response.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"✗ Error creating {name}: {ex.Message}");
                    }
                }

                StatusMessage = "Pipelines created";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task CreateOpenSearchIndexTemplatesAsync()
        {
            IsRunning = true;
            try
            {
                StatusMessage = "Creating OpenSearch index templates...";

                var templates = new[]
                {
                    ("dfir-volatility", GetVolatilityIndexTemplate()),
                    ("dfir-evtx", GetEvtxIndexTemplate()),
                    ("dfir-plaso", GetPlasoIndexTemplate())
                };

                foreach (var (name, body) in templates)
                {
                    try
                    {
                        using var content = new StringContent(body, Encoding.UTF8, "application/json");
                        var response = await _httpClient.PutAsync($"{OpenSearchUrl}/_index_template/{name}", content);

                        if (response.IsSuccessStatusCode)
                            AppendLog($"✓ Created index template: {name}");
                        else
                            AppendLog($"✗ Failed to create template {name}: {response.StatusCode}");
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"✗ Error creating template {name}: {ex.Message}");
                    }
                }

                StatusMessage = "Index templates created";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task IngestToOpenSearchAsync()
        {
            if (Artifacts.Count == 0)
            {
                StatusMessage = "No artifacts to ingest. Run analysis first.";
                return;
            }

            IsRunning = true;
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy.MM");
                var targetIndex = $"{IndexName}-{timestamp}";

                StatusMessage = $"Ingesting to {targetIndex}...";
                var ingested = 0;

                foreach (var artifact in Artifacts.Where(a => a.OutputPath.EndsWith(".json")))
                {
                    if (!File.Exists(artifact.OutputPath)) continue;

                    try
                    {
                        var json = await File.ReadAllTextAsync(artifact.OutputPath);
                        var sb = new StringBuilder();

                        // Parse JSON array and create bulk request
                        using var doc = JsonDocument.Parse(json);
                        if (doc.RootElement.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var element in doc.RootElement.EnumerateArray())
                            {
                                sb.AppendLine($"{{\"index\":{{\"_index\":\"{targetIndex}\"}}}}");
                                sb.AppendLine(element.GetRawText());
                            }
                        }

                        if (sb.Length > 0)
                        {
                            using var content = new StringContent(sb.ToString(), Encoding.UTF8, "application/x-ndjson");
                            var response = await _httpClient.PostAsync(
                                $"{OpenSearchUrl}/{targetIndex}/_bulk?pipeline={PipelineName}",
                                content);

                            if (response.IsSuccessStatusCode)
                            {
                                AppendLog($"✓ Ingested: {artifact.Name}");
                                ingested++;
                            }
                            else
                            {
                                AppendLog($"✗ Failed to ingest {artifact.Name}: {response.StatusCode}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppendLog($"✗ Error ingesting {artifact.Name}: {ex.Message}");
                    }
                }

                StatusMessage = $"Ingested {ingested} artifacts to OpenSearch";
            }
            finally
            {
                IsRunning = false;
            }
        }

        #endregion

        #region OSINT/Metadata Extraction

        private async Task ExtractDocumentMetadataAsync()
        {
            if (string.IsNullOrWhiteSpace(DocumentPath) || !Directory.Exists(DocumentPath))
            {
                StatusMessage = "Please select a valid documents folder";
                return;
            }

            IsRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            ExtractedMetadata.Clear();

            try
            {
                AppendLog($"========================================");
                AppendLog($"Document Metadata Extraction");
                AppendLog($"========================================");
                AppendLog($"Folder: {DocumentPath}");
                AppendLog($"ExifTool: {(File.Exists(ExifToolPath) ? "Available" : "Not found - using basic extraction")}");
                AppendLog($"");

                var extensions = new[] { ".pdf", ".docx", ".doc", ".xlsx", ".xls", ".pptx", ".ppt", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".heic", ".raw", ".cr2", ".nef", ".arw" };
                var files = Directory.GetFiles(DocumentPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList();

                AppendLog($"Found {files.Count} files to analyze");
                AppendLog($"");
                var processed = 0;
                var totalMetadataFields = 0;

                foreach (var file in files)
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested) break;

                    StatusMessage = $"Extracting metadata: {Path.GetFileName(file)}";

                    var metadata = await ExtractMetadataFromFileAsync(file);
                    if (metadata != null)
                    {
                        ExtractedMetadata.Add(metadata);
                        totalMetadataFields += metadata.MetadataCount;
                    }

                    processed++;
                    Progress = (processed * 100.0) / files.Count;

                    if (processed % 50 == 0)
                        await Task.Delay(1); // Yield to UI
                }

                // Log detailed summary of findings
                AppendLog($"--- Extraction Summary ---");
                AppendLog($"Files processed: {processed}");
                AppendLog($"Successful extractions: {ExtractedMetadata.Count}");
                AppendLog($"Total metadata fields: {totalMetadataFields}");
                AppendLog($"");

                var authors = ExtractedMetadata.Where(m => !string.IsNullOrEmpty(m.Author))
                    .Select(m => m.Author).Distinct().ToList();
                var companies = ExtractedMetadata.Where(m => !string.IsNullOrEmpty(m.Company))
                    .Select(m => m.Company).Distinct().ToList();
                var creators = ExtractedMetadata.Where(m => !string.IsNullOrEmpty(m.Creator))
                    .Select(m => m.Creator).Distinct().ToList();
                var software = ExtractedMetadata.Where(m => !string.IsNullOrEmpty(m.Software))
                    .Select(m => m.Software).Distinct().ToList();
                var gpsLocations = ExtractedMetadata.Where(m => !string.IsNullOrEmpty(m.GPSPosition) || 
                    (!string.IsNullOrEmpty(m.GPSLatitude) && !string.IsNullOrEmpty(m.GPSLongitude)))
                    .ToList();

                AppendLog($"--- Author/Creator Analysis ---");
                if (authors.Any())
                    AppendLog($"Authors ({authors.Count}): {string.Join(", ", authors.Take(15))}");
                if (creators.Any())
                    AppendLog($"Creators ({creators.Count}): {string.Join(", ", creators.Take(15))}");
                if (companies.Any())
                    AppendLog($"Companies ({companies.Count}): {string.Join(", ", companies.Take(15))}");
                if (software.Any())
                    AppendLog($"Software ({software.Count}): {string.Join(", ", software.Take(15))}");
                    
                AppendLog($"");
                AppendLog($"--- GPS/Location Data ---");
                if (gpsLocations.Any())
                {
                    AppendLog($"⚠ Found {gpsLocations.Count} files with GPS coordinates!");
                    foreach (var loc in gpsLocations.Take(10))
                    {
                        var coords = !string.IsNullOrEmpty(loc.GPSPosition) ? loc.GPSPosition : $"{loc.GPSLatitude}, {loc.GPSLongitude}";
                        AppendLog($"  • {loc.FileName}: {coords}");
                    }
                    if (gpsLocations.Count > 10)
                        AppendLog($"  ... and {gpsLocations.Count - 10} more");
                }
                else
                {
                    AppendLog($"No GPS coordinates found in scanned files");
                }

                // Camera info
                var cameras = ExtractedMetadata.Where(m => !string.IsNullOrEmpty(m.CameraMake) || !string.IsNullOrEmpty(m.CameraModel))
                    .Select(m => $"{m.CameraMake} {m.CameraModel}".Trim()).Distinct().ToList();
                if (cameras.Any())
                {
                    AppendLog($"");
                    AppendLog($"--- Camera Models ---");
                    AppendLog($"Cameras ({cameras.Count}): {string.Join(", ", cameras.Take(10))}");
                }

                AppendLog($"");
                AppendLog($"✓ Metadata extraction complete");

                StatusMessage = $"Extracted {totalMetadataFields} metadata fields from {ExtractedMetadata.Count} documents";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
                AppendLog($"Error: {ex.Message}");
            }
            finally
            {
                IsRunning = false;
                Progress = 0;
            }
        }

        private async Task<DocumentMetadata?> ExtractMetadataFromFileAsync(string filePath)
        {
            try
            {
                var fileInfo = new FileInfo(filePath);
                var metadata = new DocumentMetadata
                {
                    FilePath = filePath,
                    FileName = fileInfo.Name,
                    FileSize = fileInfo.Length,
                    Created = fileInfo.CreationTime,
                    Modified = fileInfo.LastWriteTime
                };

                // Try ExifTool if available - get ALL metadata with -a flag (duplicates) and -G flag (group names)
                if (!string.IsNullOrEmpty(ExifToolPath) && File.Exists(ExifToolPath))
                {
                    // Use -j (JSON), -a (all tags including duplicates), -G (group names)
                    var result = await RunProcessAsync(ExifToolPath, $"-j -a -G \"{filePath}\"", null);
                    if (result.Success && !string.IsNullOrEmpty(result.Output))
                    {
                        try
                        {
                            using var doc = JsonDocument.Parse(result.Output);
                            var root = doc.RootElement[0];

                            // Iterate through ALL properties and store them
                            foreach (var property in root.EnumerateObject())
                            {
                                var key = property.Name;
                                var value = property.Value.ValueKind == JsonValueKind.String
                                    ? property.Value.GetString() ?? ""
                                    : property.Value.ToString();

                                // Store in AllMetadata dictionary
                                if (!string.IsNullOrEmpty(value) && value != "0" && value != "Unknown")
                                {
                                    metadata.AllMetadata[key] = value;
                                }

                                // Also populate specific properties for easy access
                                var keyLower = key.ToLowerInvariant();
                                
                                // Author/Creator fields
                                if (keyLower.Contains("author") && string.IsNullOrEmpty(metadata.Author))
                                    metadata.Author = value;
                                if (keyLower.Contains("creator") && !keyLower.Contains("creatortool") && string.IsNullOrEmpty(metadata.Creator))
                                    metadata.Creator = value;
                                if (keyLower.Contains("producer") && string.IsNullOrEmpty(metadata.Producer))
                                    metadata.Producer = value;
                                if (keyLower.Contains("company") && string.IsNullOrEmpty(metadata.Company))
                                    metadata.Company = value;
                                if ((keyLower == "title" || keyLower.EndsWith(":title")) && string.IsNullOrEmpty(metadata.Title))
                                    metadata.Title = value;
                                if (keyLower.Contains("software") && string.IsNullOrEmpty(metadata.Software))
                                    metadata.Software = value;
                                if (keyLower.Contains("creatortool") && string.IsNullOrEmpty(metadata.CreatorTool))
                                    metadata.CreatorTool = value;
                                if (keyLower.Contains("application") && string.IsNullOrEmpty(metadata.Application))
                                    metadata.Application = value;
                                    
                                // Subject/Description
                                if (keyLower.Contains("subject") && string.IsNullOrEmpty(metadata.Subject))
                                    metadata.Subject = value;
                                if (keyLower.Contains("description") && string.IsNullOrEmpty(metadata.Description))
                                    metadata.Description = value;
                                if (keyLower.Contains("comment") && string.IsNullOrEmpty(metadata.Comment))
                                    metadata.Comment = value;
                                if (keyLower.Contains("keywords") && string.IsNullOrEmpty(metadata.Keywords))
                                    metadata.Keywords = value;
                                if (keyLower.Contains("copyright") && string.IsNullOrEmpty(metadata.Copyright))
                                    metadata.Copyright = value;
                                    
                                // Last modified by
                                if (keyLower.Contains("lastmodifiedby") && string.IsNullOrEmpty(metadata.LastModifiedBy))
                                    metadata.LastModifiedBy = value;
                                    
                                // Dates
                                if ((keyLower == "createdate" || keyLower.EndsWith(":createdate")) && string.IsNullOrEmpty(metadata.CreateDate))
                                    metadata.CreateDate = value;
                                if ((keyLower == "modifydate" || keyLower.EndsWith(":modifydate")) && string.IsNullOrEmpty(metadata.ModifyDate))
                                    metadata.ModifyDate = value;
                                    
                                // File type info
                                if (keyLower == "mimetype" && string.IsNullOrEmpty(metadata.MimeType))
                                    metadata.MimeType = value;
                                if (keyLower == "filetype" && string.IsNullOrEmpty(metadata.FileType))
                                    metadata.FileType = value;
                                if (keyLower == "filetypeextension" && string.IsNullOrEmpty(metadata.FileTypeExtension))
                                    metadata.FileTypeExtension = value;
                                    
                                // Image dimensions
                                if ((keyLower == "imagewidth" || keyLower.EndsWith(":imagewidth")) && string.IsNullOrEmpty(metadata.ImageWidth))
                                    metadata.ImageWidth = value;
                                if ((keyLower == "imageheight" || keyLower.EndsWith(":imageheight")) && string.IsNullOrEmpty(metadata.ImageHeight))
                                    metadata.ImageHeight = value;
                                if (keyLower.Contains("colorspace") && string.IsNullOrEmpty(metadata.ColorSpace))
                                    metadata.ColorSpace = value;
                                if (keyLower.Contains("resolution") && string.IsNullOrEmpty(metadata.Resolution))
                                    metadata.Resolution = value;
                                    
                                // Camera/EXIF info
                                if (keyLower == "model" || keyLower.EndsWith(":model"))
                                    if (string.IsNullOrEmpty(metadata.CameraModel)) metadata.CameraModel = value;
                                if (keyLower == "make" || keyLower.EndsWith(":make"))
                                    if (string.IsNullOrEmpty(metadata.CameraMake)) metadata.CameraMake = value;
                                if (keyLower.Contains("exposuretime") && string.IsNullOrEmpty(metadata.ExposureTime))
                                    metadata.ExposureTime = value;
                                if (keyLower.Contains("fnumber") && string.IsNullOrEmpty(metadata.FNumber))
                                    metadata.FNumber = value;
                                if (keyLower == "iso" || keyLower.Contains(":iso"))
                                    if (string.IsNullOrEmpty(metadata.ISO)) metadata.ISO = value;
                                if (keyLower.Contains("focallength") && string.IsNullOrEmpty(metadata.FocalLength))
                                    metadata.FocalLength = value;
                                    
                                // GPS info
                                if (keyLower.Contains("gpslatitude") && !keyLower.Contains("ref") && string.IsNullOrEmpty(metadata.GPSLatitude))
                                    metadata.GPSLatitude = value;
                                if (keyLower.Contains("gpslongitude") && !keyLower.Contains("ref") && string.IsNullOrEmpty(metadata.GPSLongitude))
                                    metadata.GPSLongitude = value;
                                if (keyLower.Contains("gpsposition") && string.IsNullOrEmpty(metadata.GPSPosition))
                                    metadata.GPSPosition = value;
                                    
                                // PDF specific
                                if (keyLower.Contains("pdfversion") && string.IsNullOrEmpty(metadata.PDFVersion))
                                    metadata.PDFVersion = value;
                                if (keyLower == "pagecount" || keyLower.EndsWith(":pagecount"))
                                    if (string.IsNullOrEmpty(metadata.PageCount)) metadata.PageCount = value;
                                if (keyLower.Contains("linearized") && string.IsNullOrEmpty(metadata.Linearized))
                                    metadata.Linearized = value;
                                if (keyLower.Contains("encrypted") && string.IsNullOrEmpty(metadata.Encrypted))
                                    metadata.Encrypted = value;
                                    
                                // XMP
                                if (keyLower.Contains("xmptoolkit") && string.IsNullOrEmpty(metadata.XMPToolkit))
                                    metadata.XMPToolkit = value;
                                if (keyLower.Contains("documentid") && string.IsNullOrEmpty(metadata.DocumentID))
                                    metadata.DocumentID = value;
                                if (keyLower.Contains("instanceid") && string.IsNullOrEmpty(metadata.InstanceID))
                                    metadata.InstanceID = value;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    // Basic extraction without ExifTool
                    var ext = Path.GetExtension(filePath).ToLowerInvariant();
                    if (ext == ".docx" || ext == ".xlsx" || ext == ".pptx")
                    {
                        // Extract from Office Open XML
                        metadata = await ExtractOfficeMetadataAsync(filePath, metadata);
                    }
                }

                return metadata;
            }
            catch
            {
                return null;
            }
        }

        private async Task<DocumentMetadata> ExtractOfficeMetadataAsync(string filePath, DocumentMetadata metadata)
        {
            try
            {
                using var archive = System.IO.Compression.ZipFile.OpenRead(filePath);
                var coreEntry = archive.GetEntry("docProps/core.xml");
                if (coreEntry != null)
                {
                    using var stream = coreEntry.Open();
                    using var reader = new StreamReader(stream);
                    var xml = await reader.ReadToEndAsync();

                    // Simple XML parsing for metadata
                    metadata.Author = ExtractXmlValue(xml, "dc:creator");
                    metadata.Title = ExtractXmlValue(xml, "dc:title");
                    metadata.Company = ExtractXmlValue(xml, "cp:lastModifiedBy");
                }
            }
            catch { }
            return metadata;
        }

        private string? ExtractXmlValue(string xml, string tag)
        {
            var startTag = $"<{tag}>";
            var endTag = $"</{tag}>";
            var start = xml.IndexOf(startTag);
            var end = xml.IndexOf(endTag);
            if (start >= 0 && end > start)
            {
                return xml.Substring(start + startTag.Length, end - start - startTag.Length);
            }
            return null;
        }

        #endregion

        #region Export

        private async Task ExportResultsAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Results",
                Filter = "JSON|*.json|CSV|*.csv",
                FileName = $"forensics_export_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json"))
                    {
                        var export = new
                        {
                            ExportDate = DateTime.Now,
                            Artifacts = Artifacts,
                            ExtractedMetadata = ExtractedMetadata,
                            Log = OutputLog
                        };
                        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(dialog.FileName, json);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Type,Name,Source,OutputPath,Timestamp,RecordCount");
                        foreach (var a in Artifacts)
                        {
                            sb.AppendLine($"\"{a.Type}\",\"{a.Name}\",\"{a.Source}\",\"{a.OutputPath}\",\"{a.Timestamp}\",{a.RecordCount}");
                        }
                        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                    }

                    StatusMessage = $"Exported to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        #endregion

        #region Task Scheduler DFIR

        private async Task ScanScheduledTasksAsync()
        {
            try
            {
                IsRunning = true;
                StatusMessage = "Scanning Windows scheduled tasks...";
                ScheduledTasks.Clear();

                // Use schtasks.exe to query Windows Task Scheduler
                await Task.Run(() =>
                {
                    try
                    {
                        var filter = SchedulerTaskFilter?.ToLowerInvariant() ?? "";
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = "schtasks.exe",
                            Arguments = "/Query /FO CSV /V",
                            RedirectStandardOutput = true,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        
                        using var process = System.Diagnostics.Process.Start(psi);
                        if (process == null) return;
                        
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();
                        
                        var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        var isFirst = true;
                        
                        foreach (var line in lines)
                        {
                            if (isFirst) { isFirst = false; continue; } // Skip header
                            
                            var fields = ParseCsvLine(line);
                            if (fields.Length < 10) continue;
                            
                            var taskName = fields.Length > 1 ? fields[1] : "";
                            var nextRun = fields.Length > 2 ? fields[2] : "";
                            var status = fields.Length > 3 ? fields[3] : "";
                            var lastRun = fields.Length > 4 ? fields[4] : "";
                            var author = fields.Length > 7 ? fields[7] : "";
                            var taskToRun = fields.Length > 8 ? fields[8] : "";
                            
                            if (!string.IsNullOrEmpty(filter) && !taskName.ToLowerInvariant().Contains(filter))
                                continue;
                            
                            // Check for suspicious patterns
                            var isSuspicious = taskToRun.ToLowerInvariant().Contains("powershell") ||
                                               taskToRun.ToLowerInvariant().Contains("cmd /c") ||
                                               taskToRun.ToLowerInvariant().Contains("regsvr32") ||
                                               taskToRun.ToLowerInvariant().Contains("mshta");
                            
                            DateTime? lastRunTime = null;
                            DateTime? nextRunTime = null;
                            if (DateTime.TryParse(lastRun, out var lr)) lastRunTime = lr;
                            if (DateTime.TryParse(nextRun, out var nr)) nextRunTime = nr;
                            
                            App.Current?.Dispatcher?.Invoke(() =>
                            {
                                ScheduledTasks.Add(new ScheduledTaskInfo
                                {
                                    TaskName = Path.GetFileName(taskName),
                                    TaskPath = taskName,
                                    State = status,
                                    Actions = taskToRun,
                                    Triggers = nextRun,
                                    LastRunTime = lastRunTime,
                                    NextRunTime = nextRunTime,
                                    Author = author,
                                    IsSuspicious = isSuspicious
                                });
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        App.Current?.Dispatcher?.Invoke(() =>
                            OutputLog += $"[{DateTime.Now:HH:mm:ss}] Warning: {ex.Message}\n");
                    }
                });

                OutputLog += $"[{DateTime.Now:HH:mm:ss}] Found {ScheduledTasks.Count} scheduled tasks\n";
                StatusMessage = $"Found {ScheduledTasks.Count} scheduled tasks";

                Artifacts.Add(new ForensicArtifact
                {
                    Type = "ScheduledTasks",
                    Name = "Task Scheduler Scan",
                    Source = "System",
                    RecordCount = ScheduledTasks.Count,
                    Timestamp = DateTime.Now,
                    OutputPath = "Memory"
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning tasks: {ex.Message}";
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ExportScheduledTasksAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Scheduled Tasks",
                Filter = "JSON|*.json|CSV|*.csv",
                FileName = $"scheduled_tasks_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json"))
                    {
                        var json = JsonSerializer.Serialize(ScheduledTasks, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(dialog.FileName, json);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("TaskName,TaskPath,State,Actions,Author,IsSuspicious");
                        foreach (var t in ScheduledTasks)
                        {
                            sb.AppendLine($"\"{t.TaskName}\",\"{t.TaskPath}\",\"{t.State}\",\"{t.Actions}\",\"{t.Author}\",{t.IsSuspicious}");
                        }
                        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                    }
                    StatusMessage = $"Exported to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        #endregion

        #region Browser Forensics DFIR

        private async Task ScanBrowserArtifactsAsync()
        {
            try
            {
                IsRunning = true;
                StatusMessage = "Scanning browser artifacts...";
                BrowserArtifacts.Clear();

                var service = PlatypusTools.Core.Services.ServiceContainer.GetRequiredService<PlatypusTools.UI.Services.Forensics.BrowserForensicsService>();
                
                // Configure the service
                service.ExtractHistory = ExtractBrowserHistory;
                service.ExtractCookies = ExtractBrowserCookies;
                service.ExtractDownloads = ExtractBrowserDownloads;
                service.ExtractCredentials = ExtractBrowserPasswords;

                var result = await service.ExtractArtifactsAsync();

                // Map history entries
                foreach (var entry in result.History)
                {
                    BrowserArtifacts.Add(new BrowserArtifact
                    {
                        Browser = entry.Browser,
                        Type = "History",
                        Url = entry.Url,
                        Title = entry.Title,
                        Timestamp = entry.VisitTime,
                        Data = $"Visits: {entry.VisitCount}"
                    });
                }

                // Map downloads
                foreach (var download in result.Downloads)
                {
                    BrowserArtifacts.Add(new BrowserArtifact
                    {
                        Browser = download.Browser,
                        Type = "Download",
                        Url = download.Url,
                        Title = Path.GetFileName(download.TargetPath),
                        Timestamp = download.StartTime,
                        Data = download.TargetPath
                    });
                }

                // Map cookies
                foreach (var cookie in result.Cookies)
                {
                    BrowserArtifacts.Add(new BrowserArtifact
                    {
                        Browser = cookie.Browser,
                        Type = "Cookie",
                        Url = cookie.Host,
                        Title = cookie.Name,
                        Timestamp = cookie.ExpiryTime,
                        Data = cookie.Path
                    });
                }

                OutputLog += $"[{DateTime.Now:HH:mm:ss}] Extracted {BrowserArtifacts.Count} browser artifacts\n";
                if (result.Errors.Any())
                {
                    OutputLog += $"[{DateTime.Now:HH:mm:ss}] Warnings: {string.Join(", ", result.Errors)}\n";
                }
                StatusMessage = $"Found {BrowserArtifacts.Count} browser artifacts";

                Artifacts.Add(new ForensicArtifact
                {
                    Type = "BrowserForensics",
                    Name = "Browser Artifact Extraction",
                    Source = "Browsers",
                    RecordCount = BrowserArtifacts.Count,
                    Timestamp = DateTime.Now,
                    OutputPath = "Memory"
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning browsers: {ex.Message}";
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ExportBrowserArtifactsAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Browser Artifacts",
                Filter = "JSON|*.json|CSV|*.csv",
                FileName = $"browser_artifacts_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json"))
                    {
                        var json = JsonSerializer.Serialize(BrowserArtifacts, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(dialog.FileName, json);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("Browser,Type,Url,Title,Timestamp,Data");
                        foreach (var a in BrowserArtifacts)
                        {
                            sb.AppendLine($"\"{a.Browser}\",\"{a.Type}\",\"{a.Url}\",\"{a.Title}\",\"{a.Timestamp}\",\"{a.Data}\"");
                        }
                        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                    }
                    StatusMessage = $"Exported to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        #endregion

        #region IOC Scanner DFIR

        private async Task ScanForIOCsAsync()
        {
            try
            {
                IsRunning = true;
                StatusMessage = "Scanning for IOCs...";
                IOCMatches.Clear();

                var service = PlatypusTools.Core.Services.ServiceContainer.GetRequiredService<PlatypusTools.UI.Services.Forensics.IOCScannerService>();
                
                // Configure scan options
                service.ScanFileHashes = ScanIOCFiles;
                service.ScanFileContents = ScanIOCFiles;
                service.ScanNetworkIOCs = ScanIOCNetwork;

                var scanPath = string.IsNullOrWhiteSpace(IOCScanPath) 
                    ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) 
                    : IOCScanPath;

                var result = await service.ScanDirectoryAsync(scanPath);

                foreach (var match in result.Matches)
                {
                    IOCMatches.Add(new IOCMatch
                    {
                        IOCType = match.IOC.Type.ToString(),
                        IOCValue = match.IOC.Value,
                        MatchedIn = Path.GetFileName(match.MatchLocation),
                        MatchLocation = match.MatchLocation,
                        Severity = match.IOC.Severity.ToString(),
                        Source = match.IOC.Source
                    });
                }

                OutputLog += $"[{DateTime.Now:HH:mm:ss}] Scanned {result.FilesScanned} files, found {IOCMatches.Count} IOC matches\n";
                if (result.Errors.Any())
                {
                    OutputLog += $"[{DateTime.Now:HH:mm:ss}] Warnings: {string.Join(", ", result.Errors.Take(5))}\n";
                }
                StatusMessage = $"Found {IOCMatches.Count} IOC matches";

                Artifacts.Add(new ForensicArtifact
                {
                    Type = "IOCScanner",
                    Name = "IOC Scan",
                    Source = scanPath,
                    RecordCount = IOCMatches.Count,
                    Timestamp = DateTime.Now,
                    OutputPath = "Memory"
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error scanning IOCs: {ex.Message}";
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task LoadIOCFeedAsync()
        {
            try
            {
                IsRunning = true;
                StatusMessage = "Loading IOC feed...";

                if (string.IsNullOrWhiteSpace(IOCFeedUrl))
                {
                    StatusMessage = "Please enter a feed URL";
                    return;
                }

                var service = PlatypusTools.Core.Services.ServiceContainer.GetRequiredService<PlatypusTools.UI.Services.Forensics.IOCScannerService>();
                
                // Create a feed entry
                var feed = new PlatypusTools.UI.Services.Forensics.IOCFeed
                {
                    Name = "Custom Feed",
                    Url = IOCFeedUrl,
                    FeedType = PlatypusTools.UI.Services.Forensics.IOCFeedType.PlainText,
                    IsEnabled = true
                };
                
                var count = await service.UpdateFeedAsync(feed);

                OutputLog += $"[{DateTime.Now:HH:mm:ss}] Loaded {count} IOCs from feed\n";
                StatusMessage = $"Loaded {count} IOCs from feed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading feed: {ex.Message}";
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ExportIOCResultsAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export IOC Results",
                Filter = "JSON|*.json|CSV|*.csv",
                FileName = $"ioc_matches_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json"))
                    {
                        var json = JsonSerializer.Serialize(IOCMatches, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(dialog.FileName, json);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("IOCType,IOCValue,MatchedIn,MatchLocation,Severity,Source");
                        foreach (var m in IOCMatches)
                        {
                            sb.AppendLine($"\"{m.IOCType}\",\"{m.IOCValue}\",\"{m.MatchedIn}\",\"{m.MatchLocation}\",\"{m.Severity}\",\"{m.Source}\"");
                        }
                        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                    }
                    StatusMessage = $"Exported to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        #endregion

        #region Registry Diff DFIR

        private async Task TakeRegistrySnapshotAsync()
        {
            try
            {
                IsRunning = true;
                StatusMessage = "Taking registry snapshot...";

                var service = PlatypusTools.Core.Services.ServiceContainer.GetRequiredService<PlatypusTools.UI.Services.Forensics.RegistryDiffService>();
                
                // Set the hive to snapshot
                service.ScanHKLM = RegistryHiveFilter == "HKLM" || RegistryHiveFilter == "All";
                service.ScanHKCU = RegistryHiveFilter == "HKCU" || RegistryHiveFilter == "All";

                var snapshot = await service.CreateSnapshotAsync($"Snapshot_{DateTime.Now:yyyyMMdd_HHmmss}");
                
                // Save the snapshot
                await service.SaveSnapshotAsync(snapshot);
                
                RegistrySnapshot1Path = snapshot.Id;
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] Registry snapshot created: {snapshot.Name}\n";
                StatusMessage = $"Snapshot saved: {snapshot.Name}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error taking snapshot: {ex.Message}";
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task CompareRegistrySnapshotsAsync()
        {
            try
            {
                IsRunning = true;
                StatusMessage = "Comparing registry snapshots...";
                RegistryDiffs.Clear();

                if (string.IsNullOrWhiteSpace(RegistrySnapshot1Path) || string.IsNullOrWhiteSpace(RegistrySnapshot2Path))
                {
                    StatusMessage = "Please select both snapshot files";
                    return;
                }

                var service = PlatypusTools.Core.Services.ServiceContainer.GetRequiredService<PlatypusTools.UI.Services.Forensics.RegistryDiffService>();
                
                // Load snapshots
                var snapshot1 = await service.LoadSnapshotAsync(RegistrySnapshot1Path);
                var snapshot2 = await service.LoadSnapshotAsync(RegistrySnapshot2Path);
                
                if (snapshot1 == null || snapshot2 == null)
                {
                    StatusMessage = "Failed to load one or both snapshots";
                    return;
                }

                var result = service.Compare(snapshot1, snapshot2);

                foreach (var diff in result.Changes)
                {
                    RegistryDiffs.Add(new RegistryDiffEntry
                    {
                        ChangeType = diff.ChangeType.ToString(),
                        KeyPath = diff.KeyPath,
                        ValueName = diff.ValueName ?? "",
                        OldValue = diff.OldValue ?? "",
                        NewValue = diff.NewValue ?? "",
                        Timestamp = DateTime.Now
                    });
                }

                OutputLog += $"[{DateTime.Now:HH:mm:ss}] Found {RegistryDiffs.Count} registry differences\n";
                StatusMessage = $"Found {RegistryDiffs.Count} registry differences";

                Artifacts.Add(new ForensicArtifact
                {
                    Type = "RegistryDiff",
                    Name = "Registry Comparison",
                    Source = "Registry",
                    RecordCount = RegistryDiffs.Count,
                    Timestamp = DateTime.Now,
                    OutputPath = "Memory"
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error comparing snapshots: {ex.Message}";
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ExportRegistryDiffAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export Registry Diff",
                Filter = "JSON|*.json|CSV|*.csv",
                FileName = $"registry_diff_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json"))
                    {
                        var json = JsonSerializer.Serialize(RegistryDiffs, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(dialog.FileName, json);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("ChangeType,KeyPath,ValueName,OldValue,NewValue,Timestamp");
                        foreach (var d in RegistryDiffs)
                        {
                            sb.AppendLine($"\"{d.ChangeType}\",\"{d.KeyPath}\",\"{d.ValueName}\",\"{d.OldValue}\",\"{d.NewValue}\",\"{d.Timestamp}\"");
                        }
                        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                    }
                    StatusMessage = $"Exported to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        #endregion

        #region PCAP Parser DFIR

        private async Task ParsePcapAsync()
        {
            try
            {
                IsRunning = true;
                StatusMessage = "Parsing PCAP file...";
                ParsedPackets.Clear();
                NetworkConnections.Clear();

                if (string.IsNullOrWhiteSpace(PcapFilePath) || !File.Exists(PcapFilePath))
                {
                    StatusMessage = "Please select a valid PCAP file";
                    return;
                }

                var service = PlatypusTools.Core.Services.ServiceContainer.GetRequiredService<PlatypusTools.UI.Services.Forensics.PcapParserService>();
                
                // Configure parsing options
                service.ExtractHttp = FilterHttpTraffic;
                service.ExtractDns = FilterDnsTraffic;
                service.ExtractPayloads = ExtractPcapFiles;

                var result = await service.AnalyzeAsync(PcapFilePath);

                // Note: PcapAnalysisResult doesn't store raw packets for memory efficiency
                // We use HttpRequests and DNS records for the packet view
                var packetIndex = 1;
                foreach (var httpReq in result.HttpRequests.Take(1000))
                {
                    ParsedPackets.Add(new NetworkPacketInfo
                    {
                        PacketNumber = packetIndex++,
                        Timestamp = httpReq.Timestamp,
                        Protocol = "HTTP",
                        SourceIP = httpReq.SourceIP?.ToString() ?? "",
                        SourcePort = 0,
                        DestinationIP = httpReq.DestIP?.ToString() ?? "",
                        DestinationPort = 80,
                        Length = 0,
                        Info = $"{httpReq.Method} {httpReq.Host}{httpReq.Path}"
                    });
                }
                
                foreach (var dns in result.DnsRecords.Take(1000))
                {
                    ParsedPackets.Add(new NetworkPacketInfo
                    {
                        PacketNumber = packetIndex++,
                        Timestamp = dns.Timestamp,
                        Protocol = "DNS",
                        SourceIP = dns.SourceIP?.ToString() ?? "",
                        SourcePort = 0,
                        DestinationIP = dns.DestIP?.ToString() ?? "",
                        DestinationPort = 53,
                        Length = 0,
                        Info = $"{dns.RecordType} {dns.QueryName} -> {dns.Answer ?? "N/A"}"
                    });
                }

                // Map connections
                foreach (var conn in result.Connections)
                {
                    NetworkConnections.Add(new NetworkConnectionInfo
                    {
                        SourceIP = conn.SourceIP?.ToString() ?? "",
                        DestinationIP = conn.DestIP?.ToString() ?? "",
                        DestinationPort = conn.DestPort,
                        Protocol = conn.Protocol,
                        PacketCount = conn.PacketCount,
                        BytesTransferred = conn.BytesSent + conn.BytesReceived,
                        FirstSeen = conn.FirstSeen,
                        LastSeen = conn.LastSeen
                    });
                }

                OutputLog += $"[{DateTime.Now:HH:mm:ss}] Parsed {result.TotalPackets} packets, {NetworkConnections.Count} connections\n";
                if (result.Errors.Any())
                {
                    OutputLog += $"[{DateTime.Now:HH:mm:ss}] Warnings: {string.Join(", ", result.Errors.Take(3))}\n";
                }
                StatusMessage = $"Parsed {result.TotalPackets} packets, {NetworkConnections.Count} unique connections";

                Artifacts.Add(new ForensicArtifact
                {
                    Type = "PCAP",
                    Name = Path.GetFileName(PcapFilePath),
                    Source = PcapFilePath,
                    RecordCount = result.TotalPackets,
                    Timestamp = DateTime.Now,
                    OutputPath = "Memory"
                });
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error parsing PCAP: {ex.Message}";
                OutputLog += $"[{DateTime.Now:HH:mm:ss}] ERROR: {ex.Message}\n";
            }
            finally
            {
                IsRunning = false;
            }
        }

        private async Task ExportPcapResultsAsync()
        {
            var dialog = new SaveFileDialog
            {
                Title = "Export PCAP Analysis",
                Filter = "JSON|*.json|CSV|*.csv",
                FileName = $"pcap_analysis_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    if (dialog.FileName.EndsWith(".json"))
                    {
                        var export = new { Packets = ParsedPackets, Connections = NetworkConnections };
                        var json = JsonSerializer.Serialize(export, new JsonSerializerOptions { WriteIndented = true });
                        await File.WriteAllTextAsync(dialog.FileName, json);
                    }
                    else
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("SourceIP,DestinationIP,Port,Protocol,PacketCount,BytesTransferred");
                        foreach (var c in NetworkConnections)
                        {
                            sb.AppendLine($"\"{c.SourceIP}\",\"{c.DestinationIP}\",{c.DestinationPort},\"{c.Protocol}\",{c.PacketCount},{c.BytesTransferred}");
                        }
                        await File.WriteAllTextAsync(dialog.FileName, sb.ToString());
                    }
                    StatusMessage = $"Exported to: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Export failed: {ex.Message}";
                }
            }
        }

        #endregion

        #region Helpers

        private async Task<(bool Success, string Output, string Error)> RunProcessAsync(string fileName, string arguments, string? outputFile)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return (false, "", "Failed to start process");

                var output = await process.StandardOutput.ReadToEndAsync();
                var error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (outputFile != null && process.ExitCode == 0)
                {
                    await File.WriteAllTextAsync(outputFile, output);
                }

                return (process.ExitCode == 0, output, error);
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }

        /// <summary>
        /// Parse a CSV line handling quoted fields with embedded commas.
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var inQuotes = false;
            var current = new StringBuilder();
            
            for (int i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (c == ',' && !inQuotes)
                {
                    fields.Add(current.ToString().Trim('"'));
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
            
            fields.Add(current.ToString().Trim('"'));
            return fields.ToArray();
        }

        private async Task<int> CountJsonRecordsAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return 0;
                var json = await File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    return doc.RootElement.GetArrayLength();
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        private static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        #endregion

        #region Pipeline Definitions

        private string GetVolatilityPipeline() => @"{
  ""description"": ""Normalize Volatility outputs"",
  ""processors"": [
    { ""set"": { ""field"": ""artifact_category"", ""value"": ""memory"" } },
    { ""rename"": { ""field"": ""PID"", ""target_field"": ""process.pid"", ""ignore_failure"": true } },
    { ""rename"": { ""field"": ""PPID"", ""target_field"": ""process.parent.pid"", ""ignore_failure"": true } },
    { ""rename"": { ""field"": ""LocalIP"", ""target_field"": ""net.local.ip"", ""ignore_failure"": true } },
    { ""rename"": { ""field"": ""RemoteIP"", ""target_field"": ""net.remote.ip"", ""ignore_failure"": true } }
  ]
}";

        private string GetEvtxPipeline() => @"{
  ""description"": ""Normalize Windows Event Logs"",
  ""processors"": [
    { ""set"": { ""field"": ""artifact_category"", ""value"": ""windows_eventlog"" } },
    { ""rename"": { ""field"": ""Event.System.EventID"", ""target_field"": ""event.code"", ""ignore_failure"": true } },
    { ""rename"": { ""field"": ""Event.System.Channel"", ""target_field"": ""event.provider"", ""ignore_failure"": true } }
  ]
}";

        private string GetAmcachePipeline() => @"{
  ""description"": ""Normalize Amcache artifacts"",
  ""processors"": [
    { ""set"": { ""field"": ""artifact_category"", ""value"": ""windows_artifact"" } },
    { ""rename"": { ""field"": ""path"", ""target_field"": ""file.path"", ""ignore_failure"": true } },
    { ""rename"": { ""field"": ""sha1"", ""target_field"": ""file.hash.sha1"", ""ignore_failure"": true } }
  ]
}";

        private string GetMftPipeline() => @"{
  ""description"": ""Normalize MFT artifacts"",
  ""processors"": [
    { ""set"": { ""field"": ""artifact_category"", ""value"": ""filesystem"" } },
    { ""rename"": { ""field"": ""Path"", ""target_field"": ""file.path"", ""ignore_failure"": true } },
    { ""rename"": { ""field"": ""Created0x10"", ""target_field"": ""file.created"", ""ignore_failure"": true } }
  ]
}";

        private string GetDocMetaPipeline() => @"{
  ""description"": ""Normalize document metadata for OSINT"",
  ""processors"": [
    { ""set"": { ""field"": ""artifact_category"", ""value"": ""doc_metadata"" } },
    { ""rename"": { ""field"": ""Author"", ""target_field"": ""doc.author"", ""ignore_failure"": true } },
    { ""rename"": { ""field"": ""Company"", ""target_field"": ""doc.company"", ""ignore_failure"": true } }
  ]
}";

        private string GetVolatilityIndexTemplate() => @"{
  ""index_patterns"": [""dfir-vol-*""],
  ""template"": {
    ""settings"": { ""number_of_shards"": 1 },
    ""mappings"": {
      ""dynamic"": true,
      ""properties"": {
        ""@timestamp"": { ""type"": ""date"" },
        ""artifact_category"": { ""type"": ""keyword"" },
        ""artifact_type"": { ""type"": ""keyword"" },
        ""host.name"": { ""type"": ""keyword"" },
        ""process.pid"": { ""type"": ""long"" },
        ""process.parent.pid"": { ""type"": ""long"" },
        ""process.name"": { ""type"": ""keyword"" },
        ""process.executable"": { ""type"": ""text"" },
        ""net.local.ip"": { ""type"": ""ip"" },
        ""net.remote.ip"": { ""type"": ""ip"" },
        ""net.local.port"": { ""type"": ""integer"" },
        ""net.remote.port"": { ""type"": ""integer"" }
      }
    }
  }
}";

        private string GetEvtxIndexTemplate() => @"{
  ""index_patterns"": [""dfir-evtx-*""],
  ""template"": {
    ""settings"": { ""number_of_shards"": 1 },
    ""mappings"": {
      ""dynamic"": true,
      ""properties"": {
        ""@timestamp"": { ""type"": ""date"" },
        ""artifact_category"": { ""type"": ""keyword"" },
        ""event.code"": { ""type"": ""keyword"" },
        ""event.provider"": { ""type"": ""keyword"" },
        ""host.name"": { ""type"": ""keyword"" },
        ""user.name"": { ""type"": ""keyword"" },
        ""process.executable"": { ""type"": ""text"" },
        ""process.command_line"": { ""type"": ""text"" }
      }
    }
  }
}";

        private string GetPlasoIndexTemplate() => @"{
  ""index_patterns"": [""dfir-plaso-*""],
  ""template"": {
    ""settings"": { ""number_of_shards"": 1 },
    ""mappings"": {
      ""dynamic"": true,
      ""properties"": {
        ""@timestamp"": { ""type"": ""date"" },
        ""datetime"": { ""type"": ""date"" },
        ""timestamp_desc"": { ""type"": ""keyword"" },
        ""source_short"": { ""type"": ""keyword"" },
        ""source_long"": { ""type"": ""text"" },
        ""message"": { ""type"": ""text"" },
        ""parser"": { ""type"": ""keyword"" },
        ""display_name"": { ""type"": ""text"" },
        ""hostname"": { ""type"": ""keyword"" },
        ""username"": { ""type"": ""keyword"" }
      }
    }
  }
}";

        #endregion
    }

    #region Models

    public class ForensicArtifact
    {
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public string OutputPath { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int RecordCount { get; set; }
    }

    public class DocumentMetadata
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modified { get; set; }
        public string? Author { get; set; }
        public string? Title { get; set; }
        public string? Creator { get; set; }
        public string? Producer { get; set; }
        public string? Company { get; set; }
        public string? Software { get; set; }
        
        // Store ALL ExifTool metadata for detailed analysis
        public Dictionary<string, string> AllMetadata { get; set; } = new();
        
        // Additional common metadata fields extracted by ExifTool
        public string? Subject { get; set; }
        public string? Keywords { get; set; }
        public string? Description { get; set; }
        public string? Comment { get; set; }
        public string? Copyright { get; set; }
        public string? CreatorTool { get; set; }
        public string? LastModifiedBy { get; set; }
        public string? Application { get; set; }
        public string? CreateDate { get; set; }
        public string? ModifyDate { get; set; }
        public string? MimeType { get; set; }
        public string? FileType { get; set; }
        public string? FileTypeExtension { get; set; }
        
        // Image-specific metadata
        public string? ImageWidth { get; set; }
        public string? ImageHeight { get; set; }
        public string? ColorSpace { get; set; }
        public string? Resolution { get; set; }
        public string? CameraModel { get; set; }
        public string? CameraMake { get; set; }
        public string? ExposureTime { get; set; }
        public string? FNumber { get; set; }
        public string? ISO { get; set; }
        public string? FocalLength { get; set; }
        public string? GPSLatitude { get; set; }
        public string? GPSLongitude { get; set; }
        public string? GPSPosition { get; set; }
        
        // PDF-specific metadata
        public string? PDFVersion { get; set; }
        public string? PageCount { get; set; }
        public string? Linearized { get; set; }
        public string? Encrypted { get; set; }
        
        // XMP metadata
        public string? XMPToolkit { get; set; }
        public string? DocumentID { get; set; }
        public string? InstanceID { get; set; }
        
        // Returns the count of non-empty metadata fields
        public int MetadataCount => AllMetadata.Count;
    }

    public class KustoQueryTemplate
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
    }

    public class MalwareAnalysisResult
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileType { get; set; } = string.Empty;
        public bool HasMacros { get; set; }
        public bool IsSuspicious { get; set; }
        public string? MacroDetails { get; set; }
        public ObservableCollection<string> Indicators { get; } = new();
    }

    public class BulkExtractorResult
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }
    
    public class ProcessInfo
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string WindowTitle { get; set; } = string.Empty;
        public long MemoryMB { get; set; }
        
        public string DisplayName => string.IsNullOrEmpty(WindowTitle) 
            ? $"{Name} (PID: {Id}) - {MemoryMB} MB"
            : $"{Name} - {WindowTitle} (PID: {Id}) - {MemoryMB} MB";
            
        public override string ToString() => DisplayName;
    }

    // DFIR Model Classes
    public class ScheduledTaskInfo
    {
        public string TaskName { get; set; } = string.Empty;
        public string TaskPath { get; set; } = string.Empty;
        public string State { get; set; } = string.Empty;
        public string Actions { get; set; } = string.Empty;
        public string Triggers { get; set; } = string.Empty;
        public DateTime? LastRunTime { get; set; }
        public DateTime? NextRunTime { get; set; }
        public string Author { get; set; } = string.Empty;
        public bool IsSuspicious { get; set; }
    }

    public class BrowserArtifact
    {
        public string Browser { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
        public string Data { get; set; } = string.Empty;
    }

    public class IOCMatch
    {
        public string IOCType { get; set; } = string.Empty;
        public string IOCValue { get; set; } = string.Empty;
        public string MatchedIn { get; set; } = string.Empty;
        public string MatchLocation { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    public class RegistryDiffEntry
    {
        public string ChangeType { get; set; } = string.Empty;
        public string KeyPath { get; set; } = string.Empty;
        public string ValueName { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
    }

    public class NetworkPacketInfo
    {
        public int PacketNumber { get; set; }
        public DateTime Timestamp { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public string SourceIP { get; set; } = string.Empty;
        public int SourcePort { get; set; }
        public string DestinationIP { get; set; } = string.Empty;
        public int DestinationPort { get; set; }
        public int Length { get; set; }
        public string Info { get; set; } = string.Empty;
    }

    public class NetworkConnectionInfo
    {
        public string SourceIP { get; set; } = string.Empty;
        public string DestinationIP { get; set; } = string.Empty;
        public int DestinationPort { get; set; }
        public string Protocol { get; set; } = string.Empty;
        public int PacketCount { get; set; }
        public long BytesTransferred { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
    }

    #endregion
}
