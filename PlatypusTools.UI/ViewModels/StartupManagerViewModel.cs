using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Utilities;

namespace PlatypusTools.UI.ViewModels
{
    public class StartupManagerViewModel : BindableBase
    {
        private readonly StartupManagerService _service;

        public StartupManagerViewModel()
        {
            // Debug entry
            SimpleLogger.Debug("Initializing StartupManagerViewModel");
            
            try
            {
                _service = Services.ServiceLocator.StartupManager;
                StartupItems = new ObservableCollection<StartupItemViewModel>();

            RefreshCommand = new RelayCommand(_ => Refresh());
            DisableSelectedCommand = new RelayCommand(_ => DisableSelected(), _ => StartupItems.Any(i => i.IsSelected));
            EnableSelectedCommand = new RelayCommand(_ => EnableSelected(), _ => StartupItems.Any(i => i.IsSelected));
            DeleteSelectedCommand = new RelayCommand(_ => DeleteSelected(), _ => StartupItems.Any(i => i.IsSelected));
            OpenLocationCommand = new RelayCommand(obj => OpenLocation(obj as StartupItemViewModel));
            SelectAllCommand = new RelayCommand(_ => SelectAll());
            SelectNoneCommand = new RelayCommand(_ => SelectNone());

            // Don't auto-refresh in constructor - let user click Refresh button
            // This prevents crashes during initialization
            StatusMessage = "Click Refresh to load startup items";
            
                SimpleLogger.Debug("StartupManagerViewModel initialized successfully");
                // Debug exit
            }
            catch (Exception ex)
            {
                SimpleLogger.Error("StartupManagerViewModel constructor" + " - " + ex.Message);
                StatusMessage = $"Error initializing: {ex.Message}";
                throw;
            }
        }

        public ObservableCollection<StartupItemViewModel> StartupItems { get; }

        private string _statusMessage = "Ready";
        public string StatusMessage
        {
            get => _statusMessage;
            set
            {
                _statusMessage = value;
                RaisePropertyChanged();
            }
        }

        public ICommand RefreshCommand { get; }
        public ICommand DisableSelectedCommand { get; }
        public ICommand EnableSelectedCommand { get; }
        public ICommand DeleteSelectedCommand { get; }
        public ICommand OpenLocationCommand { get; }
        public ICommand SelectAllCommand { get; }
        public ICommand SelectNoneCommand { get; }

        private void Refresh()
        {
            // Debug entry
            SimpleLogger.Debug("Starting startup items refresh");
            
            try
            {
                StartupItems.Clear();
                StatusMessage = "Loading startup items...";

                var items = _service.GetStartupItems();
                SimpleLogger.Debug($"GetStartupItems returned {items?.Count ?? 0} items");
                
                if (items == null || items.Count == 0)
                {
                    SimpleLogger.Debug("No startup items found");
                    StatusMessage = "No startup items found";
                    return;
                }

                foreach (var item in items)
                {
                    try
                    {
                        StartupItems.Add(new StartupItemViewModel(item));
                    }
                    catch (Exception itemEx)
                    {
                        SimpleLogger.Error($"Adding startup item {item.Name}" + " - " + itemEx.Message);
                    }
                }

                SimpleLogger.Debug($"Successfully loaded {StartupItems.Count} startup items");
                StatusMessage = $"Loaded {StartupItems.Count} startup items";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading startup items: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Full error: {ex}");
            }
            finally
            {
                RaiseCommandsCanExecuteChanged();
            }
        }

        private void DisableSelected()
        {
            var selected = StartupItems.Where(i => i.IsSelected).ToList();
            int successCount = 0;

            foreach (var item in selected)
            {
                if (_service.DisableStartupItem(item.Item))
                {
                    item.Item.IsEnabled = false;
                    successCount++;
                }
            }

            StatusMessage = $"Disabled {successCount} of {selected.Count} selected items";
            Refresh();
        }

        private void EnableSelected()
        {
            var selected = StartupItems.Where(i => i.IsSelected).ToList();
            int successCount = 0;

            foreach (var item in selected)
            {
                if (_service.EnableStartupItem(item.Item, item.Command))
                {
                    item.Item.IsEnabled = true;
                    successCount++;
                }
            }

            StatusMessage = $"Enabled {successCount} of {selected.Count} selected items";
            Refresh();
        }

        private void DeleteSelected()
        {
            var selected = StartupItems.Where(i => i.IsSelected).ToList();
            int successCount = 0;

            foreach (var item in selected)
            {
                if (_service.DeleteStartupItem(item.Item))
                {
                    successCount++;
                }
            }

            StatusMessage = $"Deleted {successCount} of {selected.Count} selected items";
            Refresh();
        }

        private void OpenLocation(StartupItemViewModel? item)
        {
            if (item == null) return;

            try
            {
                if (item.Type.Contains("Startup Folder"))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer",
                        Arguments = $"/select,\"{item.Command}\"",
                        UseShellExecute = true
                    });
                }
                else if (item.Type == "Registry" || item.Type == "RunOnce")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "regedit",
                        UseShellExecute = true
                    });
                    StatusMessage = $"Opening Registry Editor. Navigate to: {item.Location}";
                }
                else if (item.Type == "Scheduled Task")
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "taskschd.msc",
                        UseShellExecute = true
                    });
                }
            }
            catch { }
        }

        private void SelectAll()
        {
            foreach (var item in StartupItems)
                item.IsSelected = true;
            RaiseCommandsCanExecuteChanged();
        }

        private void SelectNone()
        {
            foreach (var item in StartupItems)
                item.IsSelected = false;
            RaiseCommandsCanExecuteChanged();
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            ((RelayCommand)DisableSelectedCommand).RaiseCanExecuteChanged();
            ((RelayCommand)EnableSelectedCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteSelectedCommand).RaiseCanExecuteChanged();
        }
    }

    public class StartupItemViewModel : BindableBase
    {
        public StartupItemViewModel(StartupItem item)
        {
            Item = item;
        }

        public StartupItem Item { get; }

        public string Name => Item.Name;
        public string Command => Item.Command;
        public string Location => Item.Location;
        public string Type => Item.Type;
        public bool IsEnabled => Item.IsEnabled;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                RaisePropertyChanged();
            }
        }
    }
}

