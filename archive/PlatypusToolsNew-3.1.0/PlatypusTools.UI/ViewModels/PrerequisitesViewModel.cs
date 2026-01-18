using PlatypusTools.Core.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    public class PrerequisitesViewModel : BindableBase
    {
        private readonly IPrerequisiteCheckerService _prerequisiteCheckerService;

        public PrerequisitesViewModel(IPrerequisiteCheckerService prerequisiteCheckerService)
        {
            _prerequisiteCheckerService = prerequisiteCheckerService;
            InitializeCommands();
        }

        #region Properties

        private ObservableCollection<PrerequisiteInfo> _missingPrerequisites = new();
        public ObservableCollection<PrerequisiteInfo> MissingPrerequisites
        {
            get => _missingPrerequisites;
            set => SetProperty(ref _missingPrerequisites, value);
        }

        private bool _hasPrerequisitesAvailable = true;
        public bool HasPrerequisitesAvailable
        {
            get => _hasPrerequisitesAvailable;
            set => SetProperty(ref _hasPrerequisitesAvailable, value);
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        #endregion

        #region Commands

        public ICommand CheckPrerequisitesCommand { get; private set; } = null!;
        public ICommand OpenDownloadLinkCommand { get; private set; } = null!;
        public ICommand ContinueWithoutPrerequisitesCommand { get; private set; } = null!;

        private void InitializeCommands()
        {
            CheckPrerequisitesCommand = new AsyncRelayCommand(CheckPrerequisitesAsync);
            OpenDownloadLinkCommand = new RelayCommand(param => OpenDownloadLink((param as PrerequisiteInfo)!));
            ContinueWithoutPrerequisitesCommand = new RelayCommand(_ => ContinueWithoutPrerequisites());
        }

        #endregion

        #region Methods

        private async Task CheckPrerequisitesAsync()
        {
            IsLoading = true;
            try
            {
                var missing = await _prerequisiteCheckerService.GetMissingPrerequisitesAsync();
                
                MissingPrerequisites.Clear();
                foreach (var prerequisite in missing)
                {
                    MissingPrerequisites.Add(prerequisite);
                }

                HasPrerequisitesAvailable = MissingPrerequisites.Count == 0;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to check prerequisites: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OpenDownloadLink(PrerequisiteInfo prerequisite)
        {
            if (prerequisite == null) return;

            string? url = prerequisite.DownloadUrlWindows ?? prerequisite.DownloadUrl;
            
            if (string.IsNullOrEmpty(url)) return;
            
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback: Try to open with default browser
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd",
                        Arguments = $"/c start {url}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch { }
            }
        }

        private void ContinueWithoutPrerequisites()
        {
            // User wants to proceed anyway
            // This will be handled by the main window
        }

        #endregion
    }
}

