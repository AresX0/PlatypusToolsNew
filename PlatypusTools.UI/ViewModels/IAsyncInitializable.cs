using System.Threading.Tasks;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Interface for ViewModels that require async initialization after construction.
    /// This allows heavy operations (file I/O, service discovery, data loading) to be
    /// deferred until the view is actually shown, improving startup time.
    /// </summary>
    public interface IAsyncInitializable
    {
        /// <summary>
        /// Gets whether the ViewModel has been initialized.
        /// </summary>
        bool IsInitialized { get; }

        /// <summary>
        /// Gets whether initialization is currently in progress.
        /// </summary>
        bool IsInitializing { get; }

        /// <summary>
        /// Performs async initialization. Should be called when the view is loaded.
        /// Safe to call multiple times - will only initialize once.
        /// </summary>
        Task InitializeAsync();
    }
}
