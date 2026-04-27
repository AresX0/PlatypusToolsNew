using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// ViewModel that exposes backupINtune GUI and CLI capabilities from within PlatypusTools.
/// </summary>
public class IntuneBackupSuiteViewModel : BindableBase
{
    private const string DefaultProjectRoot = "C:\\Projects\\backupINtune";

    private string _projectRoot = DefaultProjectRoot;
    private string _loginUpn = string.Empty;
    private string _tenantId = string.Empty;
    private string _exportFolderA = @"C:\IntuneSettings\TenantA";
    private string _exportFolderB = @"C:\IntuneSettings\TenantB";
    private string _selectedExportRoot = @"C:\IntuneSettings";
    private string _sourceExportFolder = @"C:\IntuneSettings\SourceTenant";
    private string _targetExportFolder = @"C:\IntuneSettings\TargetTenant";
    private string _outputFolder = @"C:\IntuneSettings\_Compare";
    private string _baselineFolder = @"C:\IntuneSettings\Security Baselines";
    private int _baselineIndex = 1;
    private string _importPrefix = "Imported";
    private string _csvOutputPath = @"C:\IntuneSettings\_SettingsExport\IntuneSettings.csv";
    private string _customInputLines = string.Empty;
    private bool _includeAssignments = true;
    private bool _includeApps = true;
    private bool _expandCatalog = true;
    private bool _generateImportScript;
    private bool _confirmDestructive;
    private string _statusMessage = "Ready";
    private bool _isBusy;
    private Process? _runningProcess;

    public IntuneBackupSuiteViewModel()
    {
        BrowseProjectRootCommand = new RelayCommand(_ => BrowseProjectRoot());
        RefreshStatusCommand = new RelayCommand(_ => RefreshStatus());
        OpenProjectFolderCommand = new RelayCommand(_ => OpenFolder(ProjectRoot), _ => Directory.Exists(ProjectRoot));
        OpenDocsFolderCommand = new RelayCommand(_ => OpenFolder(DocsPath), _ => Directory.Exists(DocsPath));
        OpenExportFolderCommand = new RelayCommand(_ => OpenFolder(ExportRootPath), _ => Directory.Exists(ExportRootPath));
        OpenReadmeCommand = new RelayCommand(_ => OpenFile(ReadmePath), _ => File.Exists(ReadmePath));

        RunFullBackupCommand = new AsyncRelayCommand(RunFullBackupAsync, () => !IsBusy && ScriptExists);
        RunCompareExportsCommand = new AsyncRelayCommand(RunCompareExportsAsync, () => !IsBusy && ScriptExists);
        RunGenerateReimportCommand = new AsyncRelayCommand(RunGenerateReimportAsync, () => !IsBusy && ScriptExists);
        RunBaselineCompareCommand = new AsyncRelayCommand(RunBaselineCompareAsync, () => !IsBusy && ScriptExists);
        RunImportBaselineCommand = new AsyncRelayCommand(RunImportBaselineAsync, () => !IsBusy && ScriptExists);
        RunCrossTenantCompareCommand = new AsyncRelayCommand(RunCrossTenantCompareAsync, () => !IsBusy && ScriptExists);
        RunCrossTenantImportCommand = new AsyncRelayCommand(RunCrossTenantImportAsync, () => !IsBusy && ScriptExists);
        RunExportSettingsCsvCommand = new AsyncRelayCommand(RunExportSettingsCsvAsync, () => !IsBusy && ScriptExists);
        RunCustomInputCommand = new AsyncRelayCommand(RunCustomInputAsync, () => !IsBusy && ScriptExists);
        CancelCommand = new RelayCommand(_ => CancelRunningOperation(), _ => IsBusy && _runningProcess != null && !_runningProcess.HasExited);

        RefreshStatus();
    }

    public string ProjectRoot
    {
        get => _projectRoot;
        set
        {
            if (SetProperty(ref _projectRoot, value))
            {
                RaisePathPropertiesChanged();
                RefreshStatus();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public string LoginUpn
    {
        get => _loginUpn;
        set => SetProperty(ref _loginUpn, value);
    }

    public string TenantId
    {
        get => _tenantId;
        set => SetProperty(ref _tenantId, value);
    }

    public string ExportFolderA
    {
        get => _exportFolderA;
        set => SetProperty(ref _exportFolderA, value);
    }

    public string ExportFolderB
    {
        get => _exportFolderB;
        set => SetProperty(ref _exportFolderB, value);
    }

    public string SelectedExportRoot
    {
        get => _selectedExportRoot;
        set => SetProperty(ref _selectedExportRoot, value);
    }

    public string SourceExportFolder
    {
        get => _sourceExportFolder;
        set => SetProperty(ref _sourceExportFolder, value);
    }

    public string TargetExportFolder
    {
        get => _targetExportFolder;
        set => SetProperty(ref _targetExportFolder, value);
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set => SetProperty(ref _outputFolder, value);
    }

    public string BaselineFolder
    {
        get => _baselineFolder;
        set => SetProperty(ref _baselineFolder, value);
    }

    public int BaselineIndex
    {
        get => _baselineIndex;
        set => SetProperty(ref _baselineIndex, value < 1 ? 1 : value);
    }

    public string ImportPrefix
    {
        get => _importPrefix;
        set => SetProperty(ref _importPrefix, value);
    }

    public string CsvOutputPath
    {
        get => _csvOutputPath;
        set => SetProperty(ref _csvOutputPath, value);
    }

    public string CustomInputLines
    {
        get => _customInputLines;
        set => SetProperty(ref _customInputLines, value);
    }

    public bool IncludeAssignments
    {
        get => _includeAssignments;
        set => SetProperty(ref _includeAssignments, value);
    }

    public bool IncludeApps
    {
        get => _includeApps;
        set => SetProperty(ref _includeApps, value);
    }

    public bool ExpandCatalog
    {
        get => _expandCatalog;
        set => SetProperty(ref _expandCatalog, value);
    }

    public bool GenerateImportScript
    {
        get => _generateImportScript;
        set => SetProperty(ref _generateImportScript, value);
    }

    public bool ConfirmDestructive
    {
        get => _confirmDestructive;
        set => SetProperty(ref _confirmDestructive, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                ((AsyncRelayCommand)RunFullBackupCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunCompareExportsCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunGenerateReimportCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunBaselineCompareCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunImportBaselineCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunCrossTenantCompareCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunCrossTenantImportCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunExportSettingsCsvCommand).RaiseCanExecuteChanged();
                ((AsyncRelayCommand)RunCustomInputCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string LauncherScriptPath => Path.Combine(ProjectRoot, "Launch-IntuneBackup.ps1");
    public string IntuneBackupScriptPath => Path.Combine(ProjectRoot, "IntuneBackup.ps1");
    public string GuiProjectPath => Path.Combine(ProjectRoot, "IntuneBackupGUI", "IntuneBackupGUI.csproj");
    public string GuiExeReleasePath => Path.Combine(ProjectRoot, "IntuneBackupGUI", "bin", "Release", "net10.0-windows", "IntuneBackupGUI.exe");
    public string GuiExeDebugPath => Path.Combine(ProjectRoot, "IntuneBackupGUI", "bin", "Debug", "net10.0-windows", "IntuneBackupGUI.exe");
    public string GuiExePublishPath => Path.Combine(ProjectRoot, "publish", "IntuneBackupGUI.exe");
    public string DocsPath => Path.Combine(ProjectRoot, "docs");
    public string ReadmePath => Path.Combine(ProjectRoot, "README.md");
    public string ExportRootPath => @"C:\IntuneSettings";

    public bool LauncherExists => File.Exists(LauncherScriptPath);
    public bool ScriptExists => File.Exists(IntuneBackupScriptPath);
    public bool GuiProjectExists => File.Exists(GuiProjectPath);

    public ObservableCollection<string> OutputLog { get; } = new();

    public ICommand BrowseProjectRootCommand { get; }
    public ICommand RefreshStatusCommand { get; }
    public ICommand OpenProjectFolderCommand { get; }
    public ICommand OpenDocsFolderCommand { get; }
    public ICommand OpenExportFolderCommand { get; }
    public ICommand OpenReadmeCommand { get; }
    public ICommand RunFullBackupCommand { get; }
    public ICommand RunCompareExportsCommand { get; }
    public ICommand RunGenerateReimportCommand { get; }
    public ICommand RunBaselineCompareCommand { get; }
    public ICommand RunImportBaselineCommand { get; }
    public ICommand RunCrossTenantCompareCommand { get; }
    public ICommand RunCrossTenantImportCommand { get; }
    public ICommand RunExportSettingsCsvCommand { get; }
    public ICommand RunCustomInputCommand { get; }
    public ICommand CancelCommand { get; }

    private void BrowseProjectRoot()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select backupINtune project root",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(ProjectRoot) ? ProjectRoot : DefaultProjectRoot
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            ProjectRoot = dialog.SelectedPath;
        }
    }

    private void RefreshStatus()
    {
        if (!Directory.Exists(ProjectRoot))
        {
            StatusMessage = "Project path not found.";
            AddLog($"Missing project folder: {ProjectRoot}");
            return;
        }

        var missing = 0;
        if (!LauncherExists) missing++;
        if (!ScriptExists) missing++;
        if (!GuiProjectExists) missing++;

        StatusMessage = missing == 0
            ? "Project detected. Ready to launch GUI/CLI and run backup operations."
            : $"Project loaded with {missing} missing component(s).";

        ((RelayCommand)OpenProjectFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)OpenDocsFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)OpenExportFolderCommand).RaiseCanExecuteChanged();
        ((RelayCommand)OpenReadmeCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunFullBackupCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunCompareExportsCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunGenerateReimportCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunBaselineCompareCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunImportBaselineCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunCrossTenantCompareCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunCrossTenantImportCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunExportSettingsCsvCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)RunCustomInputCommand).RaiseCanExecuteChanged();

        AddLog(StatusMessage);
    }

    private async Task RunFullBackupAsync()
    {
        if (!IsValidTenantId(TenantId))
        {
            StatusMessage = "Tenant ID is required and must be a valid GUID.";
            AddLog(StatusMessage);
            return;
        }

        var input = new[]
        {
            LoginUpn,
            "1",
            TenantId,
            ToYesNo(IncludeAssignments),
            ToYesNo(IncludeApps),
            ToYesNo(GenerateImportScript),
            ToYesNo(ExpandCatalog),
            "Y"
        };

        await ExecuteScriptWithInputsAsync("Full Export Backup", input);
    }

    private async Task RunCompareExportsAsync()
    {
        var outFolder = string.IsNullOrWhiteSpace(OutputFolder) ? @"C:\IntuneSettings\_Compare" : OutputFolder;
        var input = new[]
        {
            LoginUpn,
            "2",
            ExportFolderA,
            ExportFolderB,
            outFolder
        };

        await ExecuteScriptWithInputsAsync("Compare Two Exports", input);
    }

    private async Task RunGenerateReimportAsync()
    {
        var input = new[]
        {
            LoginUpn,
            "3",
            SelectedExportRoot
        };

        await ExecuteScriptWithInputsAsync("Generate Re-import Script", input);
    }

    private async Task RunBaselineCompareAsync()
    {
        var outFolder = string.IsNullOrWhiteSpace(OutputFolder) ? @"C:\IntuneSettings\_BaselineCompare" : OutputFolder;
        var input = new[]
        {
            LoginUpn,
            "4",
            "1",
            BaselineFolder,
            BaselineIndex.ToString(),
            "1",
            outFolder,
            SelectedExportRoot
        };

        await ExecuteScriptWithInputsAsync("Baseline Compare", input);
    }

    private async Task RunImportBaselineAsync()
    {
        if (!IsValidTenantId(TenantId))
        {
            StatusMessage = "Tenant ID is required and must be a valid GUID.";
            AddLog(StatusMessage);
            return;
        }

        var input = new[]
        {
            LoginUpn,
            "5",
            BaselineFolder,
            BaselineIndex.ToString(),
            TenantId,
            string.IsNullOrWhiteSpace(ImportPrefix) ? "Imported" : ImportPrefix,
            ToYesNo(ConfirmDestructive)
        };

        await ExecuteScriptWithInputsAsync("Import Baseline to Intune", input);
    }

    private async Task RunCrossTenantCompareAsync()
    {
        var outFolder = string.IsNullOrWhiteSpace(OutputFolder) ? @"C:\IntuneSettings\_CrossTenantCompare" : OutputFolder;
        var input = new[]
        {
            LoginUpn,
            "6",
            SourceExportFolder,
            TargetExportFolder,
            outFolder
        };

        await ExecuteScriptWithInputsAsync("Cross-Tenant Compare", input);
    }

    private async Task RunCrossTenantImportAsync()
    {
        if (!IsValidTenantId(TenantId))
        {
            StatusMessage = "Tenant ID is required and must be a valid GUID.";
            AddLog(StatusMessage);
            return;
        }

        var input = new[]
        {
            LoginUpn,
            "7",
            SourceExportFolder,
            TenantId,
            "A",
            ToYesNo(ConfirmDestructive)
        };

        await ExecuteScriptWithInputsAsync("Cross-Tenant Import", input);
    }

    private async Task RunExportSettingsCsvAsync()
    {
        var csvPath = string.IsNullOrWhiteSpace(CsvOutputPath)
            ? @"C:\IntuneSettings\_SettingsExport\IntuneSettings.csv"
            : CsvOutputPath;

        var input = new[]
        {
            LoginUpn,
            "8",
            "1",
            SelectedExportRoot,
            "1",
            csvPath
        };

        await ExecuteScriptWithInputsAsync("Export Settings CSV", input);
    }

    private async Task RunCustomInputAsync()
    {
        var lines = ParseCustomInputs();
        if (lines.Count == 0)
        {
            StatusMessage = "Enter one input line per prompt in the custom input box.";
            AddLog(StatusMessage);
            return;
        }

        await ExecuteScriptWithInputsAsync("Custom Script Run", lines);
    }

    private async Task ExecuteScriptWithInputsAsync(string operationName, IReadOnlyList<string> inputs)
    {
        if (!File.Exists(IntuneBackupScriptPath))
        {
            StatusMessage = "IntuneBackup.ps1 not found in project root.";
            AddLog(StatusMessage);
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = $"Running {operationName}...";
            AddLog($"Starting integrated run: {operationName}");

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{IntuneBackupScriptPath}\"",
                WorkingDirectory = ProjectRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            _runningProcess = process;
            process.Start();

            var stdOutTask = Task.Run(async () =>
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line)) AddLog(line!);
                }
            });

            var stdErrTask = Task.Run(async () =>
            {
                while (!process.StandardError.EndOfStream)
                {
                    var line = await process.StandardError.ReadLineAsync();
                    if (!string.IsNullOrWhiteSpace(line)) AddLog($"ERR: {line}");
                }
            });

            foreach (var line in inputs)
            {
                await process.StandardInput.WriteLineAsync(line ?? string.Empty);
            }

            process.StandardInput.Close();

            await Task.WhenAll(stdOutTask, stdErrTask, process.WaitForExitAsync());

            if (process.ExitCode == 0)
            {
                StatusMessage = $"{operationName} completed.";
                AddLog(StatusMessage);
            }
            else
            {
                StatusMessage = $"{operationName} failed with exit code {process.ExitCode}.";
                AddLog(StatusMessage);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"{operationName} failed.";
            AddLog($"Execution error: {ex.Message}");
        }
        finally
        {
            _runningProcess = null;
            IsBusy = false;
            RefreshStatus();
        }
    }

    private void CancelRunningOperation()
    {
        try
        {
            if (_runningProcess != null && !_runningProcess.HasExited)
            {
                _runningProcess.Kill(true);
                StatusMessage = "Operation cancelled.";
                AddLog(StatusMessage);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to cancel operation.";
            AddLog($"Cancel error: {ex.Message}");
        }
    }

    private void OpenFolder(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                StatusMessage = "Folder not found.";
                AddLog($"Missing folder: {path}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to open folder.";
            AddLog($"Folder open error: {ex.Message}");
        }
    }

    private void OpenFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                StatusMessage = "File not found.";
                AddLog($"Missing file: {path}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = "Failed to open file.";
            AddLog($"File open error: {ex.Message}");
        }
    }

    private void RaisePathPropertiesChanged()
    {
        RaisePropertyChanged(nameof(LauncherScriptPath));
        RaisePropertyChanged(nameof(IntuneBackupScriptPath));
        RaisePropertyChanged(nameof(GuiProjectPath));
        RaisePropertyChanged(nameof(GuiExeReleasePath));
        RaisePropertyChanged(nameof(GuiExeDebugPath));
        RaisePropertyChanged(nameof(GuiExePublishPath));
        RaisePropertyChanged(nameof(DocsPath));
        RaisePropertyChanged(nameof(ReadmePath));
        RaisePropertyChanged(nameof(LauncherExists));
        RaisePropertyChanged(nameof(ScriptExists));
        RaisePropertyChanged(nameof(GuiProjectExists));
    }

    private static bool IsValidTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) return false;
        return Guid.TryParse(tenantId, out _);
    }

    private static string ToYesNo(bool value) => value ? "Y" : "N";

    private List<string> ParseCustomInputs()
    {
        var list = new List<string>();
        using var reader = new StringReader(CustomInputLines ?? string.Empty);
        while (true)
        {
            var line = reader.ReadLine();
            if (line == null) break;
            list.Add(line);
        }

        return list;
    }

    private void AddLog(string message)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
        App.Current?.Dispatcher?.Invoke(() =>
        {
            OutputLog.Add(line);
            while (OutputLog.Count > 500)
            {
                OutputLog.RemoveAt(0);
            }
        });
    }
}
