using Microsoft.Extensions.DependencyInjection;
using PlatypusTools.Core.Services;
using PlatypusTools.Core.Services.Remote;
using PlatypusTools.UI.Views;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.UI.Utilities;
using PlatypusTools.UI.Services;
using PlatypusTools.UI.Services.RemoteServer;
using PlatypusTools.UI.Windows;
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
        private PlatypusRemoteServer? _remoteServer;

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
            
            // PRE-GUI SYSTEM REQUIREMENTS CHECK
            // Quick critical check first (fast - no WMI queries)
            if (!SystemRequirementsService.QuickCheck(out string failureReason))
            {
                MessageBox.Show(
                    $"PlatypusTools cannot run on this system.\n\n{failureReason}",
                    "System Requirements Not Met",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown();
                return;
            }
            
            // Full requirements check (shows window if issues found or first run)
            if (ShouldShowRequirementsCheck())
            {
                var requirementsService = new SystemRequirementsService();
                var result = requirementsService.CheckRequirements();
                
                // Only show window if there are issues OR it's a first run
                if (!result.MeetsRecommendedRequirements || IsFirstRun())
                {
                    var requirementsWindow = new SystemRequirementsWindow(result);
                    requirementsWindow.ShowDialog();
                    
                    if (!requirementsWindow.UserWantsToContinue)
                    {
                        Shutdown();
                        return;
                    }
                    
                    // Save "Don't show again" preference
                    if (requirementsWindow.DontShowAgain)
                    {
                        SettingsManager.Current.SkipSystemRequirementsCheck = true;
                        SettingsManager.SaveCurrent();
                    }
                    
                    // Mark that we've shown the check
                    MarkRequirementsCheckShown();
                }
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

        protected override void OnExit(ExitEventArgs e)
        {
            // Stop the Remote Server on shutdown
            try
            {
                _remoteServer?.Dispose();
            }
            catch
            {
                // Ignore disposal errors during shutdown
            }

            base.OnExit(e);
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
        
        #region System Requirements Check Helpers
        
        /// <summary>
        /// Determines if we should show the full requirements check window.
        /// </summary>
        private bool ShouldShowRequirementsCheck()
        {
            try
            {
                // Check if user has permanently dismissed the check
                var settings = SettingsManager.Current;
                return !settings.SkipSystemRequirementsCheck;
            }
            catch
            {
                return true; // On error, show the check
            }
        }
        
        /// <summary>
        /// Checks if this is the first run of the application.
        /// </summary>
        private bool IsFirstRun()
        {
            try
            {
                var flagFile = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PlatypusTools",
                    ".requirements_checked");
                return !File.Exists(flagFile);
            }
            catch
            {
                return true;
            }
        }
        
        /// <summary>
        /// Marks that the requirements check has been shown.
        /// </summary>
        private void MarkRequirementsCheckShown()
        {
            try
            {
                var folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PlatypusTools");
                Directory.CreateDirectory(folder);
                
                var flagFile = Path.Combine(folder, ".requirements_checked");
                File.WriteAllText(flagFile, DateTime.Now.ToString("o"));
            }
            catch
            {
                // Ignore - not critical
            }
        }
        
        #endregion
        
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

            // Initialize the dependency injection container
            StartupProfiler.BeginPhase("DI Container");
            InitializeServiceContainer();

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

                // Install bundled fonts silently BEFORE theme loading (on background thread to avoid GUI freeze)
                StartupProfiler.BeginPhase("Install fonts");
                _splashScreen?.UpdateStatus("Installing fonts...");
                await Task.Run(() => EnsureFontsInstalled());

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

                // Start Platypus Remote Server if enabled
                await StartRemoteServerIfEnabledAsync();
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

        /// <summary>
        /// Silently installs all bundled theme fonts (per-user) on first run.
        /// Copies font files to %LOCALAPPDATA%\Microsoft\Windows\Fonts and registers in HKCU.
        /// This prevents freezes when themes reference fonts that aren't installed.
        /// </summary>
        private void EnsureFontsInstalled()
        {
            try
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                var localFontsFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Microsoft", "Windows", "Fonts");

                // Font definitions: (display name, search file names, registry name)
                var fonts = new[]
                {
                    ("Okuda", new[] { "Okuda.otf" }, "Okuda"),
                    ("Okuda Bold", new[] { "Okuda Bold.otf" }, "Okuda Bold"),
                    ("Okuda Italic", new[] { "Okuda Italic.otf" }, "Okuda Italic"),
                    ("Okuda Bold Italic", new[] { "Okuda Bold Italic.otf" }, "Okuda Bold Italic"),
                    ("Monofonto", new[] { "monofonto.otf", "monofonto rg.otf" }, "Monofonto"),
                    ("Overseer", new[] { "Overseer.otf" }, "Overseer"),
                    ("Overseer Bold", new[] { "Overseer Bold.otf" }, "Overseer Bold"),
                    ("Overseer Italic", new[] { "Overseer Italic.otf" }, "Overseer Italic"),
                    ("Overseer Bold Italic", new[] { "Overseer Bold Italic.otf" }, "Overseer Bold Italic"),
                    ("Klingon", new[] { "KlingonFont.ttf", "klingon font.ttf" }, "klingon font"),
                };

                // Search directories for font files
                var searchDirs = new[]
                {
                    Path.Combine(appDir, "Assets"),
                    Path.Combine(appDir, "Fonts"),
                    appDir
                };

                bool anyInstalled = false;

                foreach (var (displayName, fileNames, registryName) in fonts)
                {
                    try
                    {
                        // Find the font file
                        string? sourcePath = null;
                        foreach (var dir in searchDirs)
                        {
                            if (!Directory.Exists(dir)) continue;
                            foreach (var fileName in fileNames)
                            {
                                var candidate = Path.Combine(dir, fileName);
                                if (File.Exists(candidate))
                                {
                                    sourcePath = candidate;
                                    break;
                                }
                            }
                            if (sourcePath != null) break;
                        }

                        if (sourcePath == null) continue; // Font not bundled, skip

                        var destFileName = Path.GetFileName(sourcePath);
                        var destPath = Path.Combine(localFontsFolder, destFileName);

                        // Skip if already installed
                        if (File.Exists(destPath)) continue;

                        // Ensure destination directory exists
                        if (!Directory.Exists(localFontsFolder))
                            Directory.CreateDirectory(localFontsFolder);

                        // Copy font file
                        File.Copy(sourcePath, destPath, true);

                        // Register in user registry
                        using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts", true))
                        {
                            key?.SetValue($"{registryName} (TrueType)", destPath);
                        }

                        // Make font available immediately in this session
                        Views.NativeMethods.AddFontResource(destPath);
                        anyInstalled = true;

                        SimpleLogger.Debug($"App: Auto-installed font '{displayName}' from {sourcePath}");
                    }
                    catch (Exception ex)
                    {
                        SimpleLogger.Warn($"App: Failed to auto-install font '{displayName}': {ex.Message}");
                        // Continue with other fonts — don't let one failure stop the rest
                    }
                }

                if (anyInstalled)
                {
                    // Broadcast font change so WPF picks up newly installed fonts
                    Views.NativeMethods.SendMessage(
                        Views.NativeMethods.HWND_BROADCAST,
                        Views.NativeMethods.WM_FONTCHANGE,
                        IntPtr.Zero, IntPtr.Zero);
                    SimpleLogger.Info("App: Font installation complete, WM_FONTCHANGE broadcast sent");
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"App: Font auto-installation failed: {ex.Message}");
                // Non-fatal — app continues with fallback fonts
            }
        }

        /// <summary>
        /// Initializes the dependency injection service container.
        /// Registers both Core and UI services.
        /// </summary>
        private void InitializeServiceContainer()
        {
            try
            {
                ServiceContainer.Initialize(services =>
                {
                    // Register UI-specific services
                    services.AddSingleton<Services.ThemeManager>();
                    services.AddSingleton<Services.KeyboardShortcutService>();
                    services.AddSingleton<Services.RecentWorkspacesService>();
                    services.AddSingleton<Services.UpdateService>();
                    services.AddSingleton<Services.PluginService>();
                    services.AddSingleton<Services.LoggingService>();
                    services.AddSingleton<Services.ToastNotificationService>();
                    services.AddSingleton<Services.CommandService>();
                    services.AddSingleton<Services.EnhancedAudioPlayerService>();
                    services.AddSingleton<Services.AudioStreamingService>();
                    services.AddSingleton<Services.TabVisibilityService>();
                    
                    // Register UI forensics services
                    services.AddSingleton<Services.Forensics.IOCScannerService>();
                    services.AddSingleton<Services.Forensics.YaraService>();
                    services.AddSingleton<Services.Forensics.PcapParserService>();
                    services.AddSingleton<Services.Forensics.BrowserForensicsService>();
                    services.AddSingleton<Services.Forensics.RegistryDiffService>();
                    services.AddSingleton<Services.Forensics.TaskSchedulerService>();
                });
                SimpleLogger.Info("ServiceContainer initialized successfully.");
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to initialize ServiceContainer: {ex.Message}");
                // Non-fatal - fall back to static Instance patterns
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

        #region Platypus Remote Server

        /// <summary>
        /// Gets the Remote Server instance for external access (e.g., from settings UI).
        /// </summary>
        public static PlatypusRemoteServer? RemoteServer => ((App)Current)._remoteServer;

        /// <summary>
        /// Starts the Remote Server if auto-start is enabled in settings.
        /// </summary>
        private async Task StartRemoteServerIfEnabledAsync()
        {
            try
            {
                var settings = SettingsManager.Current;
                if (settings.RemoteServerEnabled && settings.RemoteServerAutoStart)
                {
                    await StartRemoteServerAsync();
                }
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"Failed to auto-start Remote Server: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts the Platypus Remote Server.
        /// </summary>
        public async Task StartRemoteServerAsync()
        {
            if (_remoteServer?.IsRunning == true)
            {
                SimpleLogger.Info("Remote Server already running");
                return;
            }

            try
            {
                var settings = SettingsManager.Current;
                _remoteServer = new PlatypusRemoteServer(settings.RemoteServerPort);
                PlatypusRemoteServer.Current = _remoteServer; // Make available for SystemTrayService
                
                _remoteServer.LogMessage += (s, msg) => SimpleLogger.Info(msg);
                _remoteServer.ServerStateChanged += (s, running) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        SimpleLogger.Info($"Remote Server state changed: {(running ? "Running" : "Stopped")}");
                    });
                };

                // Register the audio player bridge so remote clients can control playback
                RegisterAudioPlayerBridge();

                await _remoteServer.StartAsync();
                SimpleLogger.Info($"Platypus Remote Server started at {_remoteServer.ServerUrl}");
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to start Remote Server: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Registers the audio player bridge for remote control.
        /// </summary>
        private void RegisterAudioPlayerBridge()
        {
            try
            {
                // Get the ViewModel from MainWindow
                EnhancedAudioPlayerViewModel? viewModel = null;
                if (MainWindow?.DataContext is MainWindowViewModel mainVm)
                {
                    viewModel = mainVm.EnhancedAudioPlayer;
                }

                // Create and register the provider
                var provider = new AudioPlayerProviderImpl(
                    EnhancedAudioPlayerService.Instance,
                    viewModel
                );
                AudioPlayerBridge.RegisterProvider(provider);
                SimpleLogger.Info("Audio player bridge registered for remote control");
            }
            catch (Exception ex)
            {
                SimpleLogger.Warn($"Failed to register audio player bridge: {ex.Message}");
            }
        }

        /// <summary>
        /// Stops the Platypus Remote Server.
        /// </summary>
        public async Task StopRemoteServerAsync()
        {
            if (_remoteServer == null || !_remoteServer.IsRunning)
                return;

            try
            {
                // Unregister the audio player bridge
                AudioPlayerBridge.UnregisterProvider();
                
                await _remoteServer.StopAsync();
                SimpleLogger.Info("Platypus Remote Server stopped");
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to stop Remote Server: {ex.Message}");
            }
        }

        #endregion
    }
}
