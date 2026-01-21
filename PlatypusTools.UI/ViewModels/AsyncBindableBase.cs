using System;
using System.Threading.Tasks;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Base class for ViewModels that require async initialization.
    /// Extends BindableBase with async initialization support.
    /// </summary>
    public abstract class AsyncBindableBase : BindableBase, IAsyncInitializable
    {
        private bool _isInitialized;
        private bool _isInitializing;
        private readonly object _initLock = new();

        /// <summary>
        /// Gets whether the ViewModel has been initialized.
        /// </summary>
        public bool IsInitialized
        {
            get => _isInitialized;
            private set => SetProperty(ref _isInitialized, value);
        }

        /// <summary>
        /// Gets whether initialization is currently in progress.
        /// </summary>
        public bool IsInitializing
        {
            get => _isInitializing;
            private set => SetProperty(ref _isInitializing, value);
        }

        /// <summary>
        /// Performs async initialization. Safe to call multiple times - will only initialize once.
        /// </summary>
        public async Task InitializeAsync()
        {
            // Quick check without lock
            if (_isInitialized || _isInitializing)
                return;

            // Thread-safe check and set
            lock (_initLock)
            {
                if (_isInitialized || _isInitializing)
                    return;
                IsInitializing = true;
            }

            try
            {
                await OnInitializeAsync();
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                OnInitializationError(ex);
            }
            finally
            {
                IsInitializing = false;
            }
        }

        /// <summary>
        /// Override this method to perform async initialization logic.
        /// Called only once when InitializeAsync is first invoked.
        /// </summary>
        protected abstract Task OnInitializeAsync();

        /// <summary>
        /// Called when initialization fails. Override to customize error handling.
        /// Default implementation logs the error.
        /// </summary>
        /// <param name="ex">The exception that occurred during initialization.</param>
        protected virtual void OnInitializationError(Exception ex)
        {
            try
            {
                PlatypusTools.Core.Services.SimpleLogger.Error($"{GetType().Name} initialization failed: {ex.Message}");
            }
            catch { }
        }

        /// <summary>
        /// Resets the initialization state, allowing InitializeAsync to run again.
        /// Use with caution - typically for refresh scenarios.
        /// </summary>
        protected void ResetInitialization()
        {
            lock (_initLock)
            {
                _isInitialized = false;
                _isInitializing = false;
            }
            RaisePropertyChanged(nameof(IsInitialized));
            RaisePropertyChanged(nameof(IsInitializing));
        }
    }
}
