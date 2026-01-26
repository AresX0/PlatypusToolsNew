using PlatypusTools.Core.Services;
using PlatypusTools.UI.Views;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.UI.Utilities;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace PlatypusTools.UI
{
    public partial class App : Application
    {
        private SplashScreenWindow? _splashScreen;
        private string[]? _startupArgs;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _startupArgs = e.Args;
            
            // IMMEDIATELY show splash screen with video - this is the first thing that happens
            // No async, no delays - get that video playing NOW
            _splashScreen = new SplashScreenWindow();
            _splashScreen.Show();
            
            // Force the window to render before continuing
            _splashScreen.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            
            // Now kick off the async initialization in the background
            // The video will loop while everything loads
            Dispatcher.BeginInvoke(new Action(async () => await InitializeApplicationAsync()));
        }
        
        private async Task InitializeApplicationAsync()
        {
            // Start startup profiling
            StartupProfiler.Start();
            StartupProfiler.BeginPhase("Exception handlers");

            // Register global unhandled exception handlers
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                SimpleLogger.Error($"Unhandled AppDomain exception: {ex?.ToString() ?? "Unknown"}");
            };
            DispatcherUnhandledException += (s, args) =>
            {
                SimpleLogger.Error($"Unhandled Dispatcher exception: {args.Exception}");
                args.Handled = true; // Prevent crash; log and continue if possible
            };
            TaskScheduler.UnobservedTaskException += (s, args) =>
            {
                SimpleLogger.Error($"Unobserved Task exception: {args.Exception}");
                args.SetObserved();
            };
            
            // Add global Loaded handler to apply theme backgrounds to UserControl content
            EventManager.RegisterClassHandler(
                typeof(System.Windows.Controls.UserControl),
                FrameworkElement.LoadedEvent,
                new RoutedEventHandler(OnUserControlLoaded));

            try
            {
                _splashScreen?.UpdateStatus("Initializing...");

                // If the first argument is a directory, expose it for viewmodels to pick up
                StartupProfiler.BeginPhase("Process arguments");
                if (_startupArgs != null && _startupArgs.Length > 0)
                {
                    var arg0 = _startupArgs[0];
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(arg0) && System.IO.Directory.Exists(arg0))
                        {
                            Application.Current.Properties["InitialTargetDir"] = arg0;
                            SimpleLogger.Info($"InitialTargetDir set from args: {arg0}");
                        }
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Warn($"Failed to set InitialTargetDir from args '{arg0}': {ex.Message}");
                    }
                }

                // Configure logging (fast - just sets file paths)
                StartupProfiler.BeginPhase("Configure logging");
                ConfigureLogging();

                // Load theme BEFORE creating main window to ensure resources are available
                StartupProfiler.BeginPhase("Load theme");
                _splashScreen?.UpdateStatus("Loading theme...");
                LoadInitialTheme();

                // Create and show main window IMMEDIATELY - don't wait for dependency checks
                StartupProfiler.BeginPhase("Create main window");
                _splashScreen?.UpdateStatus("Loading...");
                var mainWindow = new MainWindow();
                
                // Close splash and show main window
                _splashScreen?.Close();
                _splashScreen = null;
                
                StartupProfiler.BeginPhase("Show main window");
                mainWindow.Show();
                StartupProfiler.Finish();
                
                // Run dependency checks in background AFTER main window is shown
                // This doesn't block startup - user can start using the app immediately
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000); // Wait for app to fully load
                    await CheckDependenciesInBackgroundAsync();
                });
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Startup failed: {ex.Message}");
                _splashScreen?.Close();
                MessageBox.Show($"Failed to start application: {ex.Message}", "Startup Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown();
            }
        }

        /// <summary>
        /// Loads the initial theme from settings before creating the main window.
        /// This ensures all DynamicResource bindings resolve correctly.
        /// </summary>
        private void LoadInitialTheme()
        {
            try
            {
                var settings = Services.SettingsManager.Current;
                var theme = settings?.Theme ?? Services.ThemeManager.Light;
                SimpleLogger.Debug($"App: Loading initial theme '{theme}'");
                Services.ThemeManager.ApplyTheme(theme);
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"App: Failed to load initial theme: {ex.Message}");
                // Apply default light theme as fallback
                Services.ThemeManager.ApplyTheme(Services.ThemeManager.Light);
            }
        }

        /// <summary>
        /// Global handler that applies theme background to UserControl content panels.
        /// This ensures theme backgrounds propagate into UserControls, which don't inherit
        /// implicit styles from Application.Resources by default in WPF.
        /// </summary>
        private static void OnUserControlLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.UserControl uc && uc.Content is System.Windows.Controls.Panel panel)
            {
                // Only set if not already set (don't override intentional backgrounds)
                if (panel.Background == null || panel.Background == System.Windows.Media.Brushes.Transparent)
                {
                    panel.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "WindowBackgroundBrush");
                }
            }
        }

        private void ConfigureLogging()
        {
            var cfg = AppConfig.Load();

            // Determine log file: prefer configured, otherwise use %LOCALAPPDATA%\PlatypusTools\logs\platypustools.log
            if (!string.IsNullOrWhiteSpace(cfg.LogFile))
            {
                try
                {
                    var dir = Path.GetDirectoryName(cfg.LogFile) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    Directory.CreateDirectory(dir);
                    SimpleLogger.LogFile = cfg.LogFile;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to use configured log file '{cfg.LogFile}': {ex.Message}");
                }
            }

            if (string.IsNullOrWhiteSpace(SimpleLogger.LogFile))
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var logDir = Path.Combine(local, "PlatypusTools", "logs");
                Directory.CreateDirectory(logDir);
                SimpleLogger.LogFile = Path.Combine(logDir, "platypustools.log");
            }

            // Set minimum log level from config if provided
            if (!string.IsNullOrWhiteSpace(cfg.LogLevel) && Enum.TryParse<Core.Services.LogLevel>(cfg.LogLevel, true, out var lvl))
            {
                SimpleLogger.MinLevel = lvl;
            }

            // For debugging crashes during development, force verbose logging (Debug)
            // Remove or revert this line for production builds.
            SimpleLogger.MinLevel = Core.Services.LogLevel.Debug;

            SimpleLogger.Info($"Application starting. LogFile='{SimpleLogger.LogFile}', MinLevel={SimpleLogger.MinLevel}");
        }

        /// <summary>
        /// Background dependency check that runs after main window is shown.
        /// Does not block startup - just logs missing dependencies.
        /// </summary>
        private async Task CheckDependenciesInBackgroundAsync()
        {
            try
            {
                // Run all checks in parallel for speed
                var checkerTask = Task.Run(async () =>
                {
                    var checker = new PrerequisiteCheckerService();
                    return await checker.GetMissingPrerequisitesAsync();
                });
                
                var legacyTask = Task.Run(async () =>
                {
                    var legacyChecker = new DependencyCheckerService();
                    return await legacyChecker.CheckAllDependenciesAsync();
                });

                await Task.WhenAll(checkerTask, legacyTask);

                var missing = await checkerTask;
                var legacyResult = await legacyTask;

                if (missing.Count > 0)
                {
                    var names = string.Join(", ", missing.ConvertAll(p => p.DisplayName));
                    SimpleLogger.Info($"Optional tools not found: {names}");
                }

                if (!legacyResult.WebView2Installed)
                {
                    SimpleLogger.Info("WebView2 not installed - help viewer will have limited functionality");
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"Background dependency check failed: {ex.Message}");
            }
        }

        private async Task CheckDependenciesAsync()
        {
            try
            {
                var checker = new PrerequisiteCheckerService();
                var missing = await checker.GetMissingPrerequisitesAsync();

                if (missing.Count > 0)
                {
                    var prereqWindow = new PrerequisitesWindow
                    {
                        DataContext = new PrerequisitesViewModel(checker)
                    };

                    // Close splash before showing prerequisites window
                    _splashScreen?.Close();
                    _splashScreen = null;

                    var result = prereqWindow.ShowDialog();
                    
                    // If user clicked "Continue Anyway", we proceed
                    // If they closed the window, we shutdown
                    if (result == false)
                    {
                        Current.Shutdown();
                        return;
                    }
                }

                // Legacy DependencyCheckerService for other checks (WebView2, etc.)
                var legacyChecker = new DependencyCheckerService();
                var legacyResult = await legacyChecker.CheckAllDependenciesAsync();

                if (!legacyResult.WebView2Installed)
                {
                    var message = "WebView2 Runtime is required for help and certain features.\n\nDo you want to continue anyway?";
                    var msgResult = MessageBox.Show(message, "Missing WebView2", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (msgResult == MessageBoxResult.No)
                    {
                        Current.Shutdown();
                    }
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"Dependency check failed: {ex.Message}");
            }
        }
    }
}
