using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class DiskHealthViewModel : BindableBase
    {
        private readonly DiskHealthService _service = new();
        private CancellationTokenSource? _cts;

        public DiskHealthViewModel()
        {
            Disks = new ObservableCollection<DiskHealthInfo>();
            RefreshCommand = new RelayCommand(async _ => await RefreshAsync());
            CopyInfoCommand = new RelayCommand(_ => CopyDiskInfo(), _ => SelectedDisk != null);

            // Auto-load on creation
            _ = RefreshAsync();
        }

        public ObservableCollection<DiskHealthInfo> Disks { get; }

        private DiskHealthInfo? _selectedDisk;
        public DiskHealthInfo? SelectedDisk
        {
            get => _selectedDisk;
            set
            {
                if (SetProperty(ref _selectedDisk, value))
                {
                    RaisePropertyChanged(nameof(DiskDetails));
                }
            }
        }

        private string _statusMessage = "";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private bool _isScanning;
        public bool IsScanning { get => _isScanning; set => SetProperty(ref _isScanning, value); }

        public string DiskDetails => SelectedDisk != null ? FormatDiskDetails(SelectedDisk) : "";

        public ICommand RefreshCommand { get; }
        public ICommand CopyInfoCommand { get; }

        private async Task RefreshAsync()
        {
            if (IsScanning) return;

            try
            {
                IsScanning = true;
                StatusMessage = "Scanning disks...";
                _cts?.Cancel();
                _cts = new CancellationTokenSource();

                var disks = await _service.GetDiskHealthAsync(_cts.Token);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Disks.Clear();
                    foreach (var disk in disks)
                        Disks.Add(disk);
                });

                StatusMessage = $"Found {disks.Count} disk(s)";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = "Scan cancelled.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
            }
        }

        private void CopyDiskInfo()
        {
            if (SelectedDisk == null) return;
            var details = FormatDiskDetails(SelectedDisk);
            try
            {
                System.Windows.Clipboard.SetText(details);
                StatusMessage = "Disk info copied to clipboard.";
            }
            catch { }
        }

        private static string FormatDiskDetails(DiskHealthInfo disk)
        {
            return $"""
                Model: {disk.Model}
                Device ID: {disk.DeviceId}
                Serial: {disk.SerialNumber}
                Size: {disk.SizeDisplay}
                Type: {disk.MediaTypeEnum} ({disk.BusType})
                Interface: {disk.InterfaceType}
                Firmware: {disk.FirmwareRevision}
                Partitions: {disk.Partitions}
                Health: {disk.HealthStatus}
                Status: {disk.Status}
                Operational: {disk.OperationalStatus}
                Temperature: {disk.TemperatureDisplay}
                SMART Available: {disk.SmartAvailable}
                Predict Failure: {disk.PredictFailure}
                Spindle Speed: {(disk.SpindleSpeed > 0 ? $"{disk.SpindleSpeed} RPM" : "N/A (SSD)")}
                """;
        }
    }
}
