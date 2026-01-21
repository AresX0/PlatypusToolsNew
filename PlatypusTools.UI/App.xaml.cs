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

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            
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
            
            StartupProfiler.BeginPhase("Splash screen");

            // Show splash screen
            _splashScreen = new SplashScreenWindow();
            _splashScreen.Show();
            _splashScreen.UpdateStatus("Initializing...");

            try
            {
                // Check dependencies
                StartupProfiler.BeginPhase("Dependency check");
                _splashScreen.UpdateStatus("Checking dependencies...");
                await CheckDependenciesAsync();

                // If the first argument is a directory, expose it for viewmodels to pick up
                StartupProfiler.BeginPhase("Process arguments");
                if (e.Args != null && e.Args.Length > 0)
                {
                    var arg0 = e.Args[0];
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

                // Configure logging
                StartupProfiler.BeginPhase("Configure logging");
                _splashScreen.UpdateStatus("Configuring logging...");
                ConfigureLogging();

                // Reduced splash delay for faster startup (was 1500ms)
                await Task.Delay(500);

                // Create and show main window
                StartupProfiler.BeginPhase("Create main window");
                _splashScreen.UpdateStatus("Loading main window...");
                var mainWindow = new MainWindow();
                
                // Close splash and show main window
                _splashScreen.Close();
                _splashScreen = null;
                
                StartupProfiler.BeginPhase("Show main window");
                mainWindow.Show();
                StartupProfiler.Finish();
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
