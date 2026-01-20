using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class RestorePointViewModel : BindableBase
    {
        private int _sequenceNumber;
        public int SequenceNumber { get => _sequenceNumber; set => SetProperty(ref _sequenceNumber, value); }

        private string _description = string.Empty;
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private DateTime _creationTime;
        public DateTime CreationTime { get => _creationTime; set => SetProperty(ref _creationTime, value); }

        private string _restorePointType = string.Empty;
        public string RestorePointType { get => _restorePointType; set => SetProperty(ref _restorePointType, value); }

        private string _eventType = string.Empty;
        public string EventType { get => _eventType; set => SetProperty(ref _eventType, value); }
    }

    public class SystemRestoreViewModel : BindableBase
    {
        private readonly SystemRestoreService _systemRestoreService;

        public SystemRestoreViewModel()
        {
            _systemRestoreService = Services.ServiceLocator.SystemRestore;

            RefreshCommand = new RelayCommand(async _ => await RefreshAsync(), _ => !IsLoading);
            CreatePointCommand = new RelayCommand(async _ => await CreatePointAsync(), _ => !IsLoading);
            RestoreCommand = new RelayCommand(async _ => await RestoreAsync(), _ => SelectedPoint != null && !IsLoading);
            DeleteCommand = new RelayCommand(async _ => await DeletePointAsync(), _ => SelectedPoint != null && !IsLoading);
        }

        public ObservableCollection<RestorePointViewModel> RestorePoints { get; } = new();

        private RestorePointViewModel? _selectedPoint;
        public RestorePointViewModel? SelectedPoint 
        { 
            get => _selectedPoint; 
            set 
            { 
                SetProperty(ref _selectedPoint, value); 
                ((RelayCommand)RestoreCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            } 
        }

        private bool _isLoading;
        public bool IsLoading 
        { 
            get => _isLoading; 
            set 
            { 
                SetProperty(ref _isLoading, value); 
                ((RelayCommand)RefreshCommand).RaiseCanExecuteChanged();
                ((RelayCommand)CreatePointCommand).RaiseCanExecuteChanged();
                ((RelayCommand)RestoreCommand).RaiseCanExecuteChanged();
                ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            } 
        }

        private string _statusMessage = "Ready";
        public string StatusMessage { get => _statusMessage; set => SetProperty(ref _statusMessage, value); }

        private int _totalPoints;
        public int TotalPoints { get => _totalPoints; set => SetProperty(ref _totalPoints, value); }

        private string _newPointDescription = string.Empty;
        public string NewPointDescription { get => _newPointDescription; set => SetProperty(ref _newPointDescription, value); }

        public ICommand RefreshCommand { get; }
        public ICommand CreatePointCommand { get; }
        public ICommand RestoreCommand { get; }
        public ICommand DeleteCommand { get; }

        public async Task RefreshAsync()
        {
            IsLoading = true;
            StatusMessage = "Loading restore points...";
            RestorePoints.Clear();

            try
            {
                var points = await _systemRestoreService.GetRestorePoints();
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    foreach (var point in points)
                    {
                        RestorePoints.Add(new RestorePointViewModel
                        {
                            SequenceNumber = point.SequenceNumber,
                            Description = point.Description,
                            CreationTime = point.CreationTime,
                            RestorePointType = point.Type.ToString(),
                            EventType = point.Type.ToString()
                        });
                    }
                });

                TotalPoints = RestorePoints.Count;
                StatusMessage = $"Loaded {TotalPoints} restore points";
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

        public async Task CreatePointAsync()
        {
            if (string.IsNullOrWhiteSpace(NewPointDescription))
            {
                StatusMessage = "Please enter a description for the restore point";
                return;
            }

            var result = System.Windows.MessageBox.Show(
                $"Create restore point with description: '{NewPointDescription}'?",
                "Confirm Create",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                IsLoading = true;
                StatusMessage = "Creating restore point...";

                try
                {
                    await Task.Run(() => _systemRestoreService.CreateRestorePoint(NewPointDescription));
                    StatusMessage = $"Successfully created restore point: {NewPointDescription}";
                    NewPointDescription = string.Empty;
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error creating restore point: {ex.Message}";
                }
                finally
                {
                    IsLoading = false;
                }
            }
        }

        public async Task RestoreAsync()
        {
            if (SelectedPoint == null) return;

            var result = System.Windows.MessageBox.Show(
                $"WARNING: This will restore your system to the state at: {SelectedPoint.CreationTime:g}\n\n" +
                $"Description: {SelectedPoint.Description}\n\n" +
                "Your computer will restart. Are you sure you want to continue?",
                "Confirm System Restore",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                IsLoading = true;
                StatusMessage = "Initiating system restore...";

                try
                {
                    await Task.Run(() => _systemRestoreService.RestoreToPoint(SelectedPoint.SequenceNumber));
                    StatusMessage = "System restore initiated. System will restart shortly.";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error: {ex.Message}";
                    IsLoading = false;
                }
            }
        }

        public async Task DeletePointAsync()
        {
            if (SelectedPoint == null) return;

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete restore point:\n{SelectedPoint.Description}?",
                "Confirm Delete",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                try
                {
                    await Task.Run(() => _systemRestoreService.DeleteRestorePoint(SelectedPoint.SequenceNumber));
                    StatusMessage = $"Deleted restore point: {SelectedPoint.Description}";
                    await RefreshAsync();
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error deleting restore point: {ex.Message}";
                }
            }
        }
    }
}
