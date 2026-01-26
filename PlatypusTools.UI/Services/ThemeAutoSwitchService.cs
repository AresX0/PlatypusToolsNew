using System;
using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Win32;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service for automatically switching themes based on Windows system theme.
    /// Monitors registry for theme changes and applies them at runtime.
    /// </summary>
    public class ThemeAutoSwitchService : IDisposable
    {
        private static readonly Lazy<ThemeAutoSwitchService> _instance = new(() => new ThemeAutoSwitchService());
        public static ThemeAutoSwitchService Instance => _instance.Value;

        private bool _isMonitoring;
        private bool _disposed;
        private const string ThemeRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
        private const string ThemeRegistryKey = "AppsUseLightTheme";

        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

        #region Win32 Interop

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int RegNotifyChangeKeyValue(
            IntPtr hKey,
            bool bWatchSubtree,
            RegNotifyChange dwNotifyFilter,
            IntPtr hEvent,
            bool fAsynchronous);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [Flags]
        private enum RegNotifyChange : uint
        {
            Name = 0x1,
            Attributes = 0x2,
            LastSet = 0x4,
            Security = 0x8
        }

        private const uint WAIT_OBJECT_0 = 0;
        private const uint WAIT_TIMEOUT = 0x102;
        private const uint INFINITE = 0xFFFFFFFF;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets whether dark mode is currently enabled in Windows.
        /// </summary>
        public bool IsSystemDarkMode
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(ThemeRegistryPath);
                    var value = key?.GetValue(ThemeRegistryKey);
                    return value is int intValue && intValue == 0;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Gets whether auto-switch is enabled in app settings.
        /// </summary>
        public bool IsAutoSwitchEnabled
        {
            get
            {
                var settings = SettingsManager.Current;
                // Check if theme is set to "System" to enable auto-switch
                return settings?.Theme?.Equals("System", StringComparison.OrdinalIgnoreCase) ?? false;
            }
        }

        /// <summary>
        /// Gets or sets whether to monitor for theme changes.
        /// </summary>
        public bool IsMonitoring => _isMonitoring;

        #endregion

        #region Public Methods

        /// <summary>
        /// Starts monitoring Windows theme changes.
        /// </summary>
        public void StartMonitoring()
        {
            if (_isMonitoring)
                return;

            _isMonitoring = true;

            // Apply current system theme immediately
            if (IsAutoSwitchEnabled)
            {
                ApplySystemTheme();
            }

            // Start background monitor
            System.Threading.Tasks.Task.Run(() => MonitorThemeChanges());
        }

        /// <summary>
        /// Stops monitoring theme changes.
        /// </summary>
        public void StopMonitoring()
        {
            _isMonitoring = false;
        }

        /// <summary>
        /// Applies the current Windows theme to the application.
        /// </summary>
        public void ApplySystemTheme()
        {
            var isDark = IsSystemDarkMode;
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ApplyTheme(isDark ? "Dark" : "Light");
            });
        }

        /// <summary>
        /// Applies a specific theme by name.
        /// </summary>
        public void ApplyTheme(string themeName)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                try
                {
                    var app = Application.Current;
                    if (app == null) return;

                    // Remove existing theme dictionaries
                    for (int i = app.Resources.MergedDictionaries.Count - 1; i >= 0; i--)
                    {
                        var dict = app.Resources.MergedDictionaries[i];
                        if (dict.Source != null && (dict.Source.ToString().Contains("Light.xaml") || dict.Source.ToString().Contains("Dark.xaml")))
                        {
                            app.Resources.MergedDictionaries.RemoveAt(i);
                        }
                    }

                    // Add new theme
                    var themeUri = new Uri($"pack://application:,,,/Themes/{themeName}.xaml", UriKind.Absolute);
                    var themeDictionary = new ResourceDictionary { Source = themeUri };
                    app.Resources.MergedDictionaries.Add(themeDictionary);

                    // Update settings
                    var settings = SettingsManager.Current;
                    if (settings != null)
                    {
                        settings.Theme = themeName;
                        SettingsManager.SaveCurrent();
                    }

                    ThemeChanged?.Invoke(this, new ThemeChangedEventArgs
                    {
                        ThemeName = themeName,
                        IsDarkMode = themeName.Equals("Dark", StringComparison.OrdinalIgnoreCase),
                        Source = ThemeChangeSource.System
                    });
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Toggles between light and dark themes.
        /// </summary>
        public void ToggleTheme()
        {
            var settings = SettingsManager.Current;
            var currentTheme = settings?.Theme ?? "Light";
            ApplyTheme(currentTheme == "Dark" ? "Light" : "Dark");
        }

        /// <summary>
        /// Gets all available theme names.
        /// </summary>
        public string[] GetAvailableThemes()
        {
            return new[] { "Light", "Dark", "Glass", "LCARS" };
        }

        #endregion

        #region Private Methods

        private void MonitorThemeChanges()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ThemeRegistryPath, false);
                if (key == null)
                    return;

                var hEvent = CreateEvent(IntPtr.Zero, true, false, string.Empty);
                if (hEvent == IntPtr.Zero)
                    return;

                try
                {
                    bool? lastKnownDarkMode = null;

                    while (_isMonitoring && !_disposed)
                    {
                        // Register for notification
                        var result = RegNotifyChangeKeyValue(
                            key.Handle.DangerousGetHandle(),
                            false,
                            RegNotifyChange.LastSet,
                            hEvent,
                            true);

                        if (result != 0)
                            break;

                        // Wait for change (with timeout for graceful shutdown)
                        var waitResult = WaitForSingleObject(hEvent, 1000);

                        if (waitResult == WAIT_OBJECT_0)
                        {
                            // Theme changed
                            var isDark = IsSystemDarkMode;

                            if (lastKnownDarkMode != isDark)
                            {
                                lastKnownDarkMode = isDark;

                                if (IsAutoSwitchEnabled)
                                {
                                    ApplySystemTheme();
                                }
                            }
                        }
                    }
                }
                finally
                {
                    CloseHandle(hEvent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Theme monitoring error: {ex.Message}");
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _isMonitoring = false;
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    #region Event Args

    public class ThemeChangedEventArgs : EventArgs
    {
        public string ThemeName { get; set; } = string.Empty;
        public bool IsDarkMode { get; set; }
        public ThemeChangeSource Source { get; set; }
    }

    public enum ThemeChangeSource
    {
        User,
        System,
        Startup
    }

    #endregion
}
