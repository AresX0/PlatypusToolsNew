using System;
using Microsoft.Extensions.DependencyInjection;
using PlatypusTools.Core.Services.Abstractions;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Provides dependency injection container configuration and access.
    /// This is the central hub for service registration and resolution.
    /// </summary>
    /// <remarks>
    /// Usage:
    /// <code>
    /// // In App.xaml.cs OnStartup:
    /// ServiceContainer.Initialize(services => {
    ///     // Register additional UI services here
    ///     services.AddSingleton&lt;IMyService, MyService&gt;();
    /// });
    /// 
    /// // To resolve services:
    /// var service = ServiceContainer.GetService&lt;IVideoConverterService&gt;();
    /// </code>
    /// </remarks>
    public static class ServiceContainer
    {
        private static IServiceProvider? _provider;
        private static readonly object _lock = new();
        private static bool _isInitialized;

        /// <summary>
        /// Gets the service provider instance. Returns null if not initialized.
        /// </summary>
        public static IServiceProvider? Provider => _provider;

        /// <summary>
        /// Gets whether the container has been initialized.
        /// </summary>
        public static bool IsInitialized => _isInitialized;

        /// <summary>
        /// Initializes the dependency injection container with core services.
        /// Call this once during application startup.
        /// </summary>
        /// <param name="configureServices">Optional callback to register additional services.</param>
        public static void Initialize(Action<IServiceCollection>? configureServices = null)
        {
            lock (_lock)
            {
                if (_isInitialized)
                {
                    SimpleLogger.Warn("ServiceContainer.Initialize called multiple times. Ignoring subsequent calls.");
                    return;
                }

                var services = new ServiceCollection();

                // Register Core services
                RegisterCoreServices(services);

                // Allow caller to register additional services
                configureServices?.Invoke(services);

                // Build the provider
                _provider = services.BuildServiceProvider();
                _isInitialized = true;

                SimpleLogger.Info("ServiceContainer initialized with DI container.");
            }
        }

        /// <summary>
        /// Gets a service of the specified type.
        /// </summary>
        /// <typeparam name="T">The service type to resolve.</typeparam>
        /// <returns>The service instance, or null if not registered.</returns>
        public static T? GetService<T>() where T : class
        {
            if (!_isInitialized || _provider == null)
            {
                SimpleLogger.Warn($"ServiceContainer not initialized. Cannot resolve {typeof(T).Name}.");
                return null;
            }

            return _provider.GetService<T>();
        }

        /// <summary>
        /// Gets a required service of the specified type.
        /// </summary>
        /// <typeparam name="T">The service type to resolve.</typeparam>
        /// <returns>The service instance.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the service is not registered.</exception>
        public static T GetRequiredService<T>() where T : class
        {
            if (!_isInitialized || _provider == null)
            {
                throw new InvalidOperationException(
                    $"ServiceContainer not initialized. Call Initialize() before resolving {typeof(T).Name}.");
            }

            return _provider.GetRequiredService<T>();
        }

        /// <summary>
        /// Creates a new scope for scoped services.
        /// </summary>
        /// <returns>A new service scope.</returns>
        public static IServiceScope CreateScope()
        {
            if (!_isInitialized || _provider == null)
            {
                throw new InvalidOperationException("ServiceContainer not initialized.");
            }

            return _provider.CreateScope();
        }

        /// <summary>
        /// Registers core services that are available in all applications.
        /// Note: FFmpegService and FFprobeService are static classes and accessed directly.
        /// </summary>
        private static void RegisterCoreServices(IServiceCollection services)
        {
            // Video services - Singleton (stateless, reusable)
            // Note: FFmpegService and FFprobeService are static classes, not DI-compatible
            services.AddSingleton<VideoConverterService>();
            services.AddSingleton<VideoCombinerService>();
            services.AddSingleton<UpscalerService>();

            // File services - Singleton
            services.AddSingleton<FileRenamerService>();
            services.AddSingleton<DiskSpaceAnalyzerService>();

            // System services - Singleton
            services.AddSingleton<ProcessManagerService>();
            services.AddSingleton<ScheduledTasksService>();
            services.AddSingleton<StartupManagerService>();
            services.AddSingleton<SystemRestoreService>();

            // Document services - Singleton
            services.AddSingleton<PdfService>();

            // Metadata services - Singleton
            services.AddSingleton<MetadataExtractorService>();

            // Image services - Singleton
            services.AddSingleton<ImageSimilarityService>();
            services.AddSingleton<ImageResizerService>();
            services.AddSingleton<BatchUpscaleService>();

            // Archive services - Singleton
            services.AddSingleton<ArchiveService>();

            // Cleanup services - Singleton
            services.AddSingleton<DiskCleanupService>();
            services.AddSingleton<PrivacyCleanerService>();

            // Core forensics services - Singleton
            services.AddSingleton<ForensicsAnalyzerService>();
            
            // Cloud services - Singleton
            services.AddSingleton<CloudSyncService>();

            SimpleLogger.Debug("Core services registered with DI container.");
        }

        /// <summary>
        /// Resets the container (primarily for testing).
        /// </summary>
        internal static void Reset()
        {
            lock (_lock)
            {
                if (_provider is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                _provider = null;
                _isInitialized = false;
            }
        }
    }
}
