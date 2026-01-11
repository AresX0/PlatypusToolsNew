using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class BootableUSBViewModel : BindableBase
    {
        private readonly IBootableUSBService _service;
        private CancellationTokenSource? _cancellationTokenSource;

        public ObservableCollection<USBDrive> USBDrives { get; } = new();

        private string _isoPath = string.Empty;
        public string ISOPath
        {
            get => _isoPath;
            set
            {
                if (SetProperty(ref _isoPath, value))
                {
                    ((RelayCommand)CreateBootableUSBCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private USBDrive? _selectedDrive;
        public USBDrive? SelectedDrive
        {
            get => _selectedDrive;
            set
            {
                if (SetProperty(ref _selectedDrive, value))
                {
                    ((RelayCommand)CreateBootableUSBCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)FormatDriveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private string _fileSystem = "NTFS";
        public string FileSystem
        {
            get => _fileSystem;
            set => SetProperty(ref _fileSystem, value);
        }

        private string _volumeLabel = "BOOTABLE";
        public string VolumeLabel
        {
            get => _volumeLabel;
            set => SetProperty(ref _volumeLabel, value);
        }

        private BootMode _bootMode = BootMode.UEFI_GPT;
        public BootMode BootMode
        {
            get => _bootMode;
            set => SetProperty(ref _bootMode, value);
        }

        private bool _quickFormat = true;
        public bool QuickFormat
        {
            get => _quickFormat;
            set => SetProperty(ref _quickFormat, value);
        }

        private bool _verifyAfterWrite;
        public bool VerifyAfterWrite
        {
            get => _verifyAfterWrite;
            set => SetProperty(ref _verifyAfterWrite, value);
        }

        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set
            {
                if (SetProperty(ref _isProcessing, value))
                {
                    ((RelayCommand)CreateBootableUSBCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)FormatDriveCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)RefreshUSBCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)CancelCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _currentStage = string.Empty;
        public string CurrentStage
        {
            get => _currentStage;
            set => SetProperty(ref _currentStage, value);
        }

        private bool _isElevated;
        public bool IsElevated
        {
            get => _isElevated;
            set => SetProperty(ref _isElevated, value);
        }

        public ICommand BrowseISOCommand { get; }
        public ICommand RefreshUSBCommand { get; }
        public ICommand CreateBootableUSBCommand { get; }
        public ICommand FormatDriveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RequestElevationCommand { get; }

        public BootableUSBViewModel() : this(new BootableUSBService()) { }

        public BootableUSBViewModel(IBootableUSBService service)
        {
            _service = service;

            BrowseISOCommand = new RelayCommand(_ => BrowseISO());
            RefreshUSBCommand = new RelayCommand(async _ => await RefreshUSBDrivesAsync(), _ => !IsProcessing);
            CreateBootableUSBCommand = new RelayCommand(async _ => await CreateBootableUSBAsync(), _ => CanCreate());
            FormatDriveCommand = new RelayCommand(async _ => await FormatDriveAsync(), _ => SelectedDrive != null && !IsProcessing);
            CancelCommand = new RelayCommand(_ => Cancel(), _ => IsProcessing);
            RequestElevationCommand = new RelayCommand(_ => RequestElevation());

            IsElevated = _service.IsElevated();
            if (!IsElevated)
            {
                StatusMessage = "⚠️ Administrator privileges required. Click 'Request Elevation' to continue.";
            }
        }

        private bool CanCreate()
        {
            return !IsProcessing &&
                   !string.IsNullOrWhiteSpace(ISOPath) &&
                   File.Exists(ISOPath) &&
                   SelectedDrive != null &&
                   IsElevated;
        }

        private void BrowseISO()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "ISO Files (*.iso)|*.iso|All Files (*.*)|*.*",
                Title = "Select ISO File"
            };

            if (dialog.ShowDialog() == true)
            {
                ISOPath = dialog.FileName;
            }
        }

        private async Task RefreshUSBDrivesAsync()
        {
            IsProcessing = true;
            StatusMessage = "Detecting USB drives...";
            USBDrives.Clear();

            try
            {
                var drives = await _service.GetUSBDrives();
                
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var drive in drives)
                    {
                        USBDrives.Add(drive);
                    }
                });

                StatusMessage = $"Found {USBDrives.Count} USB drive(s)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private async Task CreateBootableUSBAsync()
        {
            if (SelectedDrive == null) return;

            var result = System.Windows.MessageBox.Show(
                $"WARNING: All data on {SelectedDrive.Caption} ({SelectedDrive.DriveLetter}) will be ERASED!\n\n" +
                $"Drive Size: {SelectedDrive.SizeFormatted}\n" +
                $"ISO: {Path.GetFileName(ISOPath)}\n\n" +
                $"Are you sure you want to continue?",
                "Confirm Bootable USB Creation",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            IsProcessing = true;
            Progress = 0;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var options = new BootableUSBOptions
                {
                    FileSystem = FileSystem,
                    VolumeLabel = VolumeLabel,
                    BootMode = BootMode,
                    QuickFormat = QuickFormat,
                    VerifyAfterWrite = VerifyAfterWrite
                };

                var progressReporter = new Progress<PlatypusTools.Core.Services.BootableUSBProgress>(p =>
                {
                    Progress = p.Percentage;
                    StatusMessage = p.Message;
                    CurrentStage = p.Stage;
                });

                var success = await _service.CreateBootableUSB(ISOPath, SelectedDrive, options, progressReporter, _cancellationTokenSource.Token);

                if (success)
                {
                    StatusMessage = "✅ Bootable USB created successfully!";
                    System.Windows.MessageBox.Show(
                        "Bootable USB created successfully!",
                        "Success",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Information);
                }
                else
                {
                    StatusMessage = "❌ Failed to create bootable USB";
                    System.Windows.MessageBox.Show(
                        "Failed to create bootable USB. Check the log for details.",
                        "Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (UnauthorizedAccessException)
            {
                StatusMessage = "❌ Administrator privileges required";
                System.Windows.MessageBox.Show(
                    "Administrator privileges are required to create a bootable USB.\n\nPlease click 'Request Elevation' and try again.",
                    "Elevation Required",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Cancelled by user";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Error creating bootable USB:\n\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task FormatDriveAsync()
        {
            if (SelectedDrive == null) return;

            var result = System.Windows.MessageBox.Show(
                $"WARNING: All data on {SelectedDrive.Caption} ({SelectedDrive.DriveLetter}) will be ERASED!\n\n" +
                $"Drive Size: {SelectedDrive.SizeFormatted}\n" +
                $"Format as: {FileSystem}\n" +
                $"Label: {VolumeLabel}\n\n" +
                $"Are you sure you want to continue?",
                "Confirm Format",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result != System.Windows.MessageBoxResult.Yes)
                return;

            IsProcessing = true;
            StatusMessage = "Formatting drive...";

            try
            {
                var success = await _service.FormatDrive(SelectedDrive, FileSystem, VolumeLabel, QuickFormat);

                if (success)
                {
                    StatusMessage = $"✅ Drive formatted as {FileSystem}";
                    await RefreshUSBDrivesAsync();
                }
                else
                {
                    StatusMessage = "❌ Failed to format drive";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Error: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Error formatting drive:\n\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                IsProcessing = false;
            }
        }

        private void Cancel()
        {
            _cancellationTokenSource?.Cancel();
            StatusMessage = "Cancelling...";
        }

        private void RequestElevation()
        {
            try
            {
                if (_service.RequestElevation())
                {
                    // The application will restart elevated, so close this instance
                    System.Windows.Application.Current.Shutdown();
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Failed to elevate privileges. Please run the application as Administrator.",
                        "Elevation Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error requesting elevation:\n\n{ex.Message}",
                    "Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
