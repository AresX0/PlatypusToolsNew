using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// ViewModel for the Intune Win32 Content Prep Tool GUI.
/// Wraps IntuneWinAppUtil.exe to create .intunewin packages for Microsoft Intune deployment.
/// </summary>
public class IntunePackagerViewModel : BindableBase
{
    private string _sourceFolder = string.Empty;
    private string _setupFile = string.Empty;
    private string _outputFolder = string.Empty;
    private string _catalogFolder = string.Empty;
    private bool _includeCatalog;
    private bool _quietMode = true;
    private bool _isPackaging;
    private string _statusMessage = "Ready";
    private string _toolPath = string.Empty;
    private bool _toolAvailable;
    private string _toolVersion = string.Empty;
    private CancellationTokenSource? _cancellationTokenSource;

    private const string ToolExeName = "IntuneWinAppUtil.exe";
    private const string GitHubReleaseUrl = "https://raw.githubusercontent.com/microsoft/Microsoft-Win32-Content-Prep-Tool/master/IntuneWinAppUtil.exe";

    public IntunePackagerViewModel()
    {
        BrowseSourceFolderCommand = new RelayCommand(_ => BrowseSourceFolder());
        BrowseSetupFileCommand = new RelayCommand(_ => BrowseSetupFile());
        BrowseOutputFolderCommand = new RelayCommand(_ => BrowseOutputFolder());
        BrowseCatalogFolderCommand = new RelayCommand(_ => BrowseCatalogFolder());
        BrowseToolPathCommand = new RelayCommand(_ => BrowseToolPath());
        CreatePackageCommand = new AsyncRelayCommand(CreatePackageAsync, () => CanCreatePackage && !IsPackaging);
        CancelCommand = new RelayCommand(_ => Cancel(), _ => IsPackaging);
        DownloadToolCommand = new AsyncRelayCommand(DownloadToolAsync, () => !IsPackaging);
        CheckToolVersionCommand = new AsyncRelayCommand(CheckToolVersionAsync, () => !string.IsNullOrEmpty(ToolPath) && !IsPackaging);
        OpenToolFolderCommand = new RelayCommand(_ => OpenToolFolder(), _ => !string.IsNullOrEmpty(ToolPath));
        OpenGitHubCommand = new RelayCommand(_ => OpenGitHub());

        // Try to find the tool automatically
        FindToolAutomatically();
    }

    #region Properties

    public string SourceFolder
    {
        get => _sourceFolder;
        set
        {
            _sourceFolder = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanCreatePackage));
            ((AsyncRelayCommand)CreatePackageCommand).RaiseCanExecuteChanged();
        }
    }

    public string SetupFile
    {
        get => _setupFile;
        set
        {
            _setupFile = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanCreatePackage));
            ((AsyncRelayCommand)CreatePackageCommand).RaiseCanExecuteChanged();
        }
    }

    public string OutputFolder
    {
        get => _outputFolder;
        set
        {
            _outputFolder = value;
            RaisePropertyChanged();
            RaisePropertyChanged(nameof(CanCreatePackage));
            ((AsyncRelayCommand)CreatePackageCommand).RaiseCanExecuteChanged();
        }
    }

    public string CatalogFolder
    {
        get => _catalogFolder;
        set { _catalogFolder = value; RaisePropertyChanged(); }
    }

    public bool IncludeCatalog
    {
        get => _includeCatalog;
        set { _includeCatalog = value; RaisePropertyChanged(); }
    }

    public bool QuietMode
    {
        get => _quietMode;
        set { _quietMode = value; RaisePropertyChanged(); }
    }

    public bool IsPackaging
    {
        get => _isPackaging;
        private set
        {
            _isPackaging = value;
            RaisePropertyChanged();
            ((AsyncRelayCommand)CreatePackageCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)DownloadToolCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)CheckToolVersionCommand).RaiseCanExecuteChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; RaisePropertyChanged(); }
    }

    public string ToolPath
    {
        get => _toolPath;
        set
        {
            _toolPath = value;
            RaisePropertyChanged();
            ToolAvailable = File.Exists(value);
            RaisePropertyChanged(nameof(CanCreatePackage));
            ((AsyncRelayCommand)CreatePackageCommand).RaiseCanExecuteChanged();
            ((AsyncRelayCommand)CheckToolVersionCommand).RaiseCanExecuteChanged();
            ((RelayCommand)OpenToolFolderCommand).RaiseCanExecuteChanged();
        }
    }

    public bool ToolAvailable
    {
        get => _toolAvailable;
        private set { _toolAvailable = value; RaisePropertyChanged(); }
    }

    public string ToolVersion
    {
        get => _toolVersion;
        set { _toolVersion = value; RaisePropertyChanged(); }
    }

    public bool CanCreatePackage =>
        !string.IsNullOrWhiteSpace(SourceFolder) &&
        Directory.Exists(SourceFolder) &&
        !string.IsNullOrWhiteSpace(SetupFile) &&
        File.Exists(SetupFile) &&
        !string.IsNullOrWhiteSpace(OutputFolder) &&
        ToolAvailable;

    public ObservableCollection<string> OutputLog { get; } = new();

    #endregion

    #region Commands

    public ICommand BrowseSourceFolderCommand { get; }
    public ICommand BrowseSetupFileCommand { get; }
    public ICommand BrowseOutputFolderCommand { get; }
    public ICommand BrowseCatalogFolderCommand { get; }
    public ICommand BrowseToolPathCommand { get; }
    public ICommand CreatePackageCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DownloadToolCommand { get; }
    public ICommand CheckToolVersionCommand { get; }
    public ICommand OpenToolFolderCommand { get; }
    public ICommand OpenGitHubCommand { get; }

    #endregion

    #region Methods

    private void FindToolAutomatically()
    {
        // Check common locations
        var possiblePaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ToolExeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IntuneWinAppUtil", ToolExeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "IntuneWinAppUtil", ToolExeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntuneWinAppUtil", ToolExeName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", ToolExeName),
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                ToolPath = path;
                StatusMessage = $"Tool found: {Path.GetFileName(path)}";
                _ = CheckToolVersionAsync();
                return;
            }
        }

        // Check PATH environment variable
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(pathEnv))
        {
            foreach (var dir in pathEnv.Split(';'))
            {
                var fullPath = Path.Combine(dir.Trim(), ToolExeName);
                if (File.Exists(fullPath))
                {
                    ToolPath = fullPath;
                    StatusMessage = $"Tool found in PATH: {Path.GetFileName(fullPath)}";
                    _ = CheckToolVersionAsync();
                    return;
                }
            }
        }

        StatusMessage = "IntuneWinAppUtil.exe not found. Please download or locate the tool.";
    }

    private void BrowseSourceFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Setup Folder (containing all setup files)",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            SourceFolder = dialog.SelectedPath;
            
            // Auto-detect setup file if not set
            if (string.IsNullOrEmpty(SetupFile) || !File.Exists(SetupFile))
            {
                var msiFiles = Directory.GetFiles(SourceFolder, "*.msi");
                if (msiFiles.Length > 0)
                {
                    SetupFile = msiFiles[0];
                }
                else
                {
                    var exeFiles = Directory.GetFiles(SourceFolder, "setup*.exe");
                    if (exeFiles.Length > 0)
                    {
                        SetupFile = exeFiles[0];
                    }
                    else
                    {
                        exeFiles = Directory.GetFiles(SourceFolder, "*.exe");
                        if (exeFiles.Length > 0)
                        {
                            SetupFile = exeFiles[0];
                        }
                    }
                }
            }
        }
    }

    private void BrowseSetupFile()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select Setup File",
            Filter = "Setup Files|*.exe;*.msi|Executable|*.exe|MSI Installer|*.msi|All Files|*.*",
            InitialDirectory = !string.IsNullOrEmpty(SourceFolder) ? SourceFolder : null
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            SetupFile = dialog.FileName;
            
            // Auto-set source folder if not set
            if (string.IsNullOrEmpty(SourceFolder))
            {
                SourceFolder = Path.GetDirectoryName(SetupFile) ?? string.Empty;
            }
        }
    }

    private void BrowseOutputFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Output Folder for .intunewin file",
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            OutputFolder = dialog.SelectedPath;
        }
    }

    private void BrowseCatalogFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Catalog Folder (for Windows 10 S mode)",
            ShowNewFolderButton = false
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            CatalogFolder = dialog.SelectedPath;
            IncludeCatalog = true;
        }
    }

    private void BrowseToolPath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Locate IntuneWinAppUtil.exe",
            Filter = "IntuneWinAppUtil|IntuneWinAppUtil.exe|All Executables|*.exe",
            FileName = ToolExeName
        };

        if (dialog.ShowDialog() == DialogResult.OK)
        {
            ToolPath = dialog.FileName;
            _ = CheckToolVersionAsync();
        }
    }

    private async Task CheckToolVersionAsync()
    {
        if (string.IsNullOrEmpty(ToolPath) || !File.Exists(ToolPath))
        {
            ToolVersion = string.Empty;
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ToolPath,
                Arguments = "-v",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                ToolVersion = output.Trim();
                if (!string.IsNullOrEmpty(ToolVersion))
                {
                    StatusMessage = $"Tool ready - {ToolVersion}";
                }
            }
        }
        catch (Exception ex)
        {
            SimpleLogger.Warn($"Could not get tool version: {ex.Message}");
            ToolVersion = "Version unknown";
        }
    }

    private async Task DownloadToolAsync()
    {
        IsPackaging = true;
        StatusMessage = "Downloading IntuneWinAppUtil.exe from GitHub...";
        OutputLog.Clear();
        OutputLog.Add("Downloading from Microsoft GitHub repository...");
        OutputLog.Add(GitHubReleaseUrl);

        try
        {
            // Download to app directory
            var targetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ToolExeName);

            var httpClient = HttpClientFactory.Download;
            
            var response = await httpClient.GetAsync(GitHubReleaseUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1;
            await using var stream = await response.Content.ReadAsStreamAsync();
            await using var fileStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[8192];
            long downloadedBytes = 0;
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, bytesRead);
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (double)downloadedBytes / totalBytes * 100;
                    StatusMessage = $"Downloading... {progress:F1}% ({downloadedBytes / 1024:N0} KB / {totalBytes / 1024:N0} KB)";
                }
            }

            ToolPath = targetPath;
            OutputLog.Add($"Download complete: {targetPath}");
            OutputLog.Add($"File size: {new FileInfo(targetPath).Length:N0} bytes");
            StatusMessage = "Download complete!";
            
            await CheckToolVersionAsync();
        }
        catch (Exception ex)
        {
            OutputLog.Add($"Error: {ex.Message}");
            StatusMessage = $"Download failed: {ex.Message}";
            SimpleLogger.Error($"Failed to download IntuneWinAppUtil: {ex.Message}");
        }
        finally
        {
            IsPackaging = false;
        }
    }

    private async Task CreatePackageAsync()
    {
        if (!CanCreatePackage) return;

        IsPackaging = true;
        _cancellationTokenSource = new CancellationTokenSource();
        OutputLog.Clear();
        StatusMessage = "Creating .intunewin package...";

        try
        {
            // Build command line arguments
            var args = $"-c \"{SourceFolder}\" -s \"{SetupFile}\" -o \"{OutputFolder}\"";
            
            if (IncludeCatalog && !string.IsNullOrWhiteSpace(CatalogFolder) && Directory.Exists(CatalogFolder))
            {
                args += $" -a \"{CatalogFolder}\"";
            }

            if (QuietMode)
            {
                args += " -q";
            }

            OutputLog.Add($"Tool: {ToolPath}");
            OutputLog.Add($"Arguments: {args}");
            OutputLog.Add(new string('-', 60));

            var startInfo = new ProcessStartInfo
            {
                FileName = ToolPath,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = SourceFolder
            };

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // Read output asynchronously
            var outputTask = ReadOutputAsync(process.StandardOutput, _cancellationTokenSource.Token);
            var errorTask = ReadOutputAsync(process.StandardError, _cancellationTokenSource.Token);

            await process.WaitForExitAsync(_cancellationTokenSource.Token);
            await Task.WhenAll(outputTask, errorTask);

            if (process.ExitCode == 0)
            {
                StatusMessage = "Package created successfully!";
                OutputLog.Add(new string('-', 60));
                OutputLog.Add("✓ Package created successfully!");
                
                // Find the output file
                var expectedOutput = Path.Combine(OutputFolder, Path.GetFileNameWithoutExtension(SetupFile) + ".intunewin");
                if (File.Exists(expectedOutput))
                {
                    OutputLog.Add($"Output: {expectedOutput}");
                    OutputLog.Add($"Size: {new FileInfo(expectedOutput).Length:N0} bytes");
                }
            }
            else
            {
                StatusMessage = $"Packaging failed (exit code: {process.ExitCode})";
                OutputLog.Add($"✗ Process exited with code: {process.ExitCode}");
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Packaging cancelled";
            OutputLog.Add("Operation cancelled by user");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OutputLog.Add($"Error: {ex.Message}");
            SimpleLogger.Error($"Intune packaging failed: {ex.Message}");
        }
        finally
        {
            IsPackaging = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    private async Task ReadOutputAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        try
        {
            while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync();
                if (line != null)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        OutputLog.Add(line);
                    });
                }
            }
        }
        catch { }
    }

    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
    }

    private void OpenToolFolder()
    {
        if (!string.IsNullOrEmpty(ToolPath) && File.Exists(ToolPath))
        {
            var folder = Path.GetDirectoryName(ToolPath);
            if (!string.IsNullOrEmpty(folder))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{ToolPath}\"",
                    UseShellExecute = true
                });
            }
        }
    }

    private void OpenGitHub()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool",
            UseShellExecute = true
        });
    }

    #endregion
}
