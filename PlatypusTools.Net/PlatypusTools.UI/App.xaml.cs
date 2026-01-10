using PlatypusTools.Core.Services;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace PlatypusTools.UI
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            // Check dependencies first
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

            base.OnStartup(e);

            // Load app config and configure logging
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
                    // If we fail to use configured path, fallback to default
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