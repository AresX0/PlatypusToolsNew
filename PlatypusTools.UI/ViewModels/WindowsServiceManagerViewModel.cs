using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.ViewModels
{
    public class WindowsServiceManagerViewModel : BindableBase
    {
        private readonly WindowsServiceManagerService _service = new();

        public WindowsServiceManagerViewModel()
        {
            Services = new ObservableCollection<WindowsServiceManagerService.ServiceInfo>();

            RefreshCommand = new RelayCommand(async _ => await LoadServicesAsync());
            StartCommand = new RelayCommand(async _ => await StartServiceAsync(), _ => CanStart);
            StopCommand = new RelayCommand(async _ => await StopServiceAsync(), _ => CanStop);
            RestartCommand = new RelayCommand(async _ => await RestartServiceAsync(), _ => CanStop);
            PauseCommand = new RelayCommand(async _ => await PauseServiceAsync(), _ => CanPause);
            SetStartupTypeCommand = new RelayCommand(async _ => await SetStartupTypeAsync());
            CopyServiceNameCommand = new RelayCommand(_ => CopyServiceName(), _ => SelectedService != null);
            OpenServicesFolderCommand = new RelayCommand(_ => OpenServicesFolder());
            ExportListCommand = new RelayCommand(async _ => await ExportListAsync());

            _ = LoadServicesAsync();
        }

        private ObservableCollection<WindowsServiceManagerService.ServiceInfo> _services = null!;
        public ObservableCollection<WindowsServiceManagerService.ServiceInfo> Services
        {
            get => _services;
            set => SetProperty(ref _services, value);
        }

        private WindowsServiceManagerService.ServiceInfo? _selectedService;
        public WindowsServiceManagerService.ServiceInfo? SelectedService
        {
            get => _selectedService;
            set
            {
                SetProperty(ref _selectedService, value);
                OnPropertyChanged(nameof(CanStart));
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(CanPause));
            }
        }

        private string _searchText = "";
        public string SearchText
        {
            get => _searchText;
            set
            {
                SetProperty(ref _searchText, value);
                _ = LoadServicesAsync();
            }
        }

        private string _filterStatus = "All";
        public string FilterStatus
        {
            get => _filterStatus;
            set
            {
                SetProperty(ref _filterStatus, value);
                _ = LoadServicesAsync();
            }
        }

        private string _filterStartType = "All";
        public string FilterStartType
        {
            get => _filterStartType;
            set
            {
                SetProperty(ref _filterStartType, value);
                _ = LoadServicesAsync();
            }
        }

        private bool _isLoading;
        public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

        private string _statusMessage = "Loading services...";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private string _newStartupType = "Automatic";
        public string NewStartupType { get => _newStartupType; set => SetProperty(ref _newStartupType, value); }

        public bool CanStart => SelectedService != null && SelectedService.Status != "Running";
        public bool CanStop => SelectedService != null && SelectedService.Status == "Running";
        public bool CanPause => SelectedService != null && SelectedService.Status == "Running";

        public ICommand RefreshCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand RestartCommand { get; }
        public ICommand PauseCommand { get; }
        public ICommand SetStartupTypeCommand { get; }
        public ICommand CopyServiceNameCommand { get; }
        public ICommand OpenServicesFolderCommand { get; }
        public ICommand ExportListCommand { get; }

        private async Task LoadServicesAsync()
        {
            IsLoading = true;
            try
            {
                var all = string.IsNullOrEmpty(SearchText)
                    ? _service.GetAllServices()
                    : _service.Search(SearchText);

                if (FilterStatus != "All")
                    all = all.Where(s => s.Status == FilterStatus).ToList();
                if (FilterStartType != "All")
                    all = all.Where(s => s.StartType.Contains(FilterStartType, StringComparison.OrdinalIgnoreCase)).ToList();

                Services.Clear();
                foreach (var s in all.OrderBy(s => s.DisplayName))
                    Services.Add(s);

                StatusMessage = $"{Services.Count} services";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task StartServiceAsync()
        {
            if (SelectedService == null) return;
            StatusMessage = $"Starting {SelectedService.DisplayName}...";
            var result = await _service.StartServiceAsync(SelectedService.Name);
            StatusMessage = result ? $"Started {SelectedService.DisplayName}" : $"Failed to start {SelectedService.DisplayName}";
            await LoadServicesAsync();
        }

        private async Task StopServiceAsync()
        {
            if (SelectedService == null) return;
            StatusMessage = $"Stopping {SelectedService.DisplayName}...";
            var result = await _service.StopServiceAsync(SelectedService.Name);
            StatusMessage = result ? $"Stopped {SelectedService.DisplayName}" : $"Failed to stop {SelectedService.DisplayName}";
            await LoadServicesAsync();
        }

        private async Task RestartServiceAsync()
        {
            if (SelectedService == null) return;
            StatusMessage = $"Restarting {SelectedService.DisplayName}...";
            var result = await _service.RestartServiceAsync(SelectedService.Name);
            StatusMessage = result ? $"Restarted {SelectedService.DisplayName}" : $"Failed to restart {SelectedService.DisplayName}";
            await LoadServicesAsync();
        }

        private async Task PauseServiceAsync()
        {
            if (SelectedService == null) return;
            StatusMessage = $"Pausing {SelectedService.DisplayName}...";
            var result = await _service.PauseServiceAsync(SelectedService.Name);
            StatusMessage = result ? $"Paused {SelectedService.DisplayName}" : $"Failed to pause {SelectedService.DisplayName}";
            await LoadServicesAsync();
        }

        private async Task SetStartupTypeAsync()
        {
            if (SelectedService == null) return;
            _service.SetStartupType(SelectedService.Name, NewStartupType);
            StatusMessage = $"Set {SelectedService.DisplayName} startup type to {NewStartupType}";
            await LoadServicesAsync();
        }

        private void CopyServiceName()
        {
            if (SelectedService != null)
                System.Windows.Clipboard.SetText(SelectedService.Name);
        }

        private void OpenServicesFolder()
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "services.msc",
                UseShellExecute = true
            });
        }

        private async Task ExportListAsync()
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "CSV|*.csv|Text|*.txt",
                FileName = $"services_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (dlg.ShowDialog() == true)
            {
                var lines = new System.Collections.Generic.List<string>
                {
                    "Name,DisplayName,Status,StartType,Account,Path"
                };
                foreach (var s in Services)
                {
                    lines.Add($"\"{s.Name}\",\"{s.DisplayName}\",\"{s.Status}\",\"{s.StartType}\",\"{s.Account}\",\"{s.Path?.Replace("\"", "\"\"")}\"");
                }
                await System.IO.File.WriteAllLinesAsync(dlg.FileName, lines);
                StatusMessage = $"Exported {Services.Count} services to {dlg.FileName}";
            }
        }
    }
}
