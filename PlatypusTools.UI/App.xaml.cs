using PlatypusTools.Core.Services;
using PlatypusTools.UI.Views;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.UI.Utilities;
using PlatypusTools.UI.Services;
using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
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
            
            // Check for screensaver mode command-line arguments
            if (_startupArgs != null && _startupArgs.Length > 0)
            {
                string arg = _startupArgs[0].ToLowerInvariant();
                
                // /s - Run screensaver in full screen
                if (arg == "/s" || arg == "-s")
                {
                    RunAsScreensaver();
                    return;
                }
                
                // /c or /c:hwnd - Show configuration dialog
                if (arg.StartsWith("/c") || arg.StartsWith("-c"))
                {
                    ShowScreensaverConfig();
                    return;
                }
                
                // /p hwnd - Preview mode (show in small preview window)
                if (arg == "/p" || arg == "-p")
                {
                    // For preview, we just exit - preview in a tiny window isn't practical
                    Shutdown();
                    return;
                }
            }
            
            // Check if we need to run as admin but aren't elevated
            if (ShouldElevate())
            {
                RestartAsAdmin();
                return;
            }
            
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
        
        /// <summary>
        /// Runs the application in screensaver mode (fullscreen visualizer).
        /// </summary>
        private void RunAsScreensaver()
        {
            // Load screensaver settings
            var settings = ScreensaverSettings.Load();
            
            // Create and show the screensaver window
            var screensaverWindow = new ScreensaverWindow(settings.VisualizerMode, settings.ColorSchemeIndex);
            screensaverWindow.Show();
        }
        
        /// <summary>
        /// Shows the screensaver configuration dialog.
        /// </summary>
        private void ShowScreensaverConfig()
        {
            var configWindow = new ScreensaverConfigWindow();
            configWindow.ShowDialog();
            Shutdown();
        }
        
        /// <summary>
        /// Checks if the application should run elevated based on settings and current state.
        /// </summary>
        private bool ShouldElevate()
        {
            try
            {
                // Check if already running as admin
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                    return false; // Already elevated
                
                // Check if admin mode is requested in settings
                var settings = SettingsManager.Current;
                return settings.RequireAdminRights;
            }
            catch
            {
                return false; // On error, don't try to elevate
            }
        }
        
        /// <summary>
        /// Restarts the application with administrator privileges.
        /// </summary>
        private void RestartAsAdmin()
        {
            try
            {
                var exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Environment.ProcessPath;
                }
                
                if (!string.IsNullOrEmpty(exePath))
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        Verb = "runas", // Request elevation
                        Arguments = string.Join(" ", _startupArgs ?? Array.Empty<string>())
                    };
                    
                    Process.Start(startInfo);
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled UAC prompt - continue without elevation
                // Re-show splash and continue normally
                _splashScreen = new SplashScreenWindow();
                _splashScreen.Show();
                _splashScreen.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                Dispatcher.BeginInvoke(new Action(async () => await InitializeApplicationAsync()));
                return;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to restart as admin: {ex.Message}");
                // Continue without elevation
                _splashScreen = new SplashScreenWindow();
                _splashScreen.Show();
                _splashScreen.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                Dispatcher.BeginInvoke(new Action(async () => await InitializeApplicationAsync()));
                return;
            }
            
            // Exit this instance since we launched an elevated one
            Shutdown();
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
                
                // Apply font settings after window content is loaded
                mainWindow.Loaded += (s, args) => ApplyFontSettingsFromConfig(Services.SettingsManager.Current);
                
                // Ensure splash screen shows for at least 5 seconds to display the platypus video
                StartupProfiler.BeginPhase("Minimum splash display");
                _splashScreen?.UpdateStatus("Almost ready...");
                var elapsed = StartupProfiler.TotalElapsed;
                var minimumSplashTime = TimeSpan.FromSeconds(5);
                if (elapsed < minimumSplashTime)
                {
                    var remaining = minimumSplashTime - elapsed;
                    await Task.Delay(remaining);
                }
                
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
                
                // Apply font settings
                ApplyFontSettingsFromConfig(settings);
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"App: Failed to load initial theme: {ex.Message}");
                // Apply default light theme as fallback
                Services.ThemeManager.ApplyTheme(Services.ThemeManager.Light);
            }
        }
        
        /// <summary>
        /// Applies font settings from configuration at startup.
        /// </summary>
        private void ApplyFontSettingsFromConfig(Services.AppSettings? settings)
        {
            if (settings == null) return;
            
            try
            {
                var fontFamily = settings.CustomFontFamily;
                var fontScale = settings.FontScale;
                
                // Store font scale in resources
                if (Resources.Contains("GlobalFontScale"))
                {
                    Resources["GlobalFontScale"] = fontScale;
                }
                else
                {
                    Resources.Add("GlobalFontScale", fontScale);
                }
                
                // Apply custom font family if not "Default"
                System.Windows.Media.FontFamily? ff = null;
                if (fontFamily != "Default" && !string.IsNullOrEmpty(fontFamily))
                {
                    ff = new System.Windows.Media.FontFamily(fontFamily);
                    if (Resources.Contains("GlobalFontFamily"))
                    {
                        Resources["GlobalFontFamily"] = ff;
                    }
                    else
                    {
                        Resources.Add("GlobalFontFamily", ff);
                    }
                    
                    // Override all theme-specific font resources so the custom font takes effect
                    var fontKeys = new[] { "LcarsFont", "LcarsHeaderFont", "PipBoyFont", "PipBoyHeaderFont", 
                                           "PipBoyDisplayFont", "KlingonFontFamily", "KlingonDisplayFont" };
                    foreach (var key in fontKeys)
                    {
                        if (Resources.Contains(key))
                        {
                            Resources[key] = ff;
                        }
                    }
                }
                
                // Apply scale transform to main window content when available
                if (MainWindow != null)
                {
                    if (MainWindow.Content is System.Windows.FrameworkElement content)
                    {
                        content.LayoutTransform = new System.Windows.Media.ScaleTransform(fontScale, fontScale);
                    }
                    if (ff != null)
                    {
                        MainWindow.FontFamily = ff;
                    }
                }
                
                SimpleLogger.Debug($"App: Applied font settings - Family={fontFamily}, Scale={fontScale}x");
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"App: Failed to apply font settings: {ex.Message}");
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
