using PlatypusTools.Core.Services;
using PlatypusTools.UI.Views;
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

            // Show splash screen
            _splashScreen = new SplashScreenWindow();
            _splashScreen.Show();
            _splashScreen.UpdateStatus("Initializing...");

            try
            {
                // Check dependencies
                _splashScreen.UpdateStatus("Checking dependencies...");
                await CheckDependenciesAsync();

                // If the first argument is a directory, expose it for viewmodels to pick up
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
                _splashScreen.UpdateStatus("Configuring logging...");
                ConfigureLogging();

                // Small delay to show splash screen
                await Task.Delay(1500);

                // Create and show main window
                _splashScreen.UpdateStatus("Loading main window...");
                var mainWindow = new MainWindow();
                
                // Close splash and show main window
                _splashScreen.Close();
                _splashScreen = null;
                
                mainWindow.Show();
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
            if (!string.IsNullOrWhiteSpace(cfg.LogLevel) && Enum.TryParse<LogLevel>(cfg.LogLevel, true, out var lvl))
            {
                SimpleLogger.MinLevel = lvl;
            }

            SimpleLogger.Info($"Application starting. LogFile='{SimpleLogger.LogFile}', MinLevel={SimpleLogger.MinLevel}");
        }

        private async Task CheckDependenciesAsync()
        {
            try
            {
                var checker = new DependencyCheckerService();
                var result = await checker.CheckAllDependenciesAsync();

                if (!result.AllDependenciesMet)
                {
                    var message = result.GetMissingDependenciesMessage();
                    message += "\n\nSome features may not work without these dependencies.\n\nDo you want to continue anyway?";

                    var msgResult = MessageBox.Show(message, "Missing Dependencies", MessageBoxButton.YesNo, MessageBoxImage.Warning);
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
