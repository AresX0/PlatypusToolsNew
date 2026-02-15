using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PlatypusTools.UI.ViewModels;
using PlatypusTools.UI.Services;
using PlatypusTools.UI.Interop;

namespace PlatypusTools.UI
{
    public partial class MainWindow : Window
    {
        private bool _glassInitialized = false;
        private bool _forceExit = false; // True when exiting from tray or force close
        
        // Fullscreen mode state
        private bool _isFullScreen = false;
        private WindowState _previousWindowState;
        private WindowStyle _previousWindowStyle;
        private ResizeMode _previousResizeMode;
        private Rect _previousBounds;
        
        public MainWindow()
        {
            InitializeComponent();
            
            // Set up keyboard shortcuts for fullscreen
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;
            
            // Hook up closing for cleanup and exit prompt
            this.Closing += MainWindow_Closing;
            this.Closed += MainWindow_Closed;
            
            // SourceInitialized fires when window handle is ready - required for DWM effects
            this.SourceInitialized += (s, e) =>
            {
                // Ensure window has a handle before applying glass
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != System.IntPtr.Zero)
                {
                    InitializeGlassTheme();
                }
            };
            
            this.Loaded += async (s, e) =>
            {
                StatusBarViewModel.Instance.Reset();
                RefreshRecentWorkspacesMenu();
                
                // Retry glass initialization if not done yet
                if (!_glassInitialized)
                {
                    InitializeGlassTheme();
                }
                
                // Restore session state (IDEA-009)
                RestoreSessionState();
                
                // Restore auto-hide tab bar state
                RestoreAutoHideState();
                
                // Show dependency setup on first run or if dependencies are missing and user hasn't opted out
                await CheckAndShowDependencySetupAsync();
            };
            
            // Subscribe to workspace changes
            RecentWorkspacesService.Instance.WorkspacesChanged += (s, e) => RefreshRecentWorkspacesMenu();
            
            // Initialize system tray
            InitializeSystemTray();
        }
        
        /// <summary>
        /// Initializes the system tray icon and handlers.
        /// </summary>
        private void InitializeSystemTray()
        {
            try
            {
                var trayService = SystemTrayService.Instance;
                trayService.Initialize(this);
                
                // Handle exit request from tray
                trayService.ExitRequested += (s, e) =>
                {
                    _forceExit = true;
                    this.Close();
                };
                
                PlatypusTools.Core.Services.SimpleLogger.Info("System tray initialized");
            }
            catch (System.Exception ex)
            {
                PlatypusTools.Core.Services.SimpleLogger.Error($"Error initializing system tray: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Checks if dependencies are missing and shows setup dialog if needed.
        /// </summary>
        private async System.Threading.Tasks.Task CheckAndShowDependencySetupAsync()
        {
            try
            {
                var settings = SettingsManager.Current;
                
                // If user has opted out of the prompt, skip
                if (settings.HasSeenDependencyPrompt)
                    return;
                
                // Check if any dependencies are missing
                var checker = new PlatypusTools.Core.Services.DependencyCheckerService();
                var result = await checker.CheckAllDependenciesAsync();
                
                // If all dependencies are met, mark as seen and skip
                if (result.AllDependenciesMet)
                {
                    settings.HasSeenDependencyPrompt = true;
                    SettingsManager.SaveCurrent();
                    return;
                }
                
                // Show the dependency setup window
                var setupWindow = new Views.DependencySetupWindow();
                setupWindow.Owner = this;
                setupWindow.ShowDialog();
            }
            catch (System.Exception ex)
            {
                PlatypusTools.Core.Services.SimpleLogger.Error($"Error checking dependencies: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Initializes glass theme effects based on saved settings.
        /// </summary>
        private void InitializeGlassTheme()
        {
            if (_glassInitialized)
                return;
                
            try
            {
                // Verify window has a handle
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == System.IntPtr.Zero)
                {
                    PlatypusTools.Core.Services.SimpleLogger.Warn("Glass init: Window handle not ready");
                    return;
                }
                
                _glassInitialized = true;
                
                // Register this window with the theme manager
                PlatypusTools.Core.Services.SimpleLogger.Debug("Glass init: Calling SetMainWindow");
                ThemeManager.Instance.SetMainWindow(this);
                
                // Apply saved glass settings
                var settings = SettingsManager.Current;
                PlatypusTools.Core.Services.SimpleLogger.Debug($"Glass init: settings.GlassEnabled={settings?.GlassEnabled}, IsGlassSupported={ThemeManager.Instance.IsGlassSupported}");
                if (settings != null && settings.GlassEnabled && ThemeManager.Instance.IsGlassSupported)
                {
                    PlatypusTools.Core.Services.SimpleLogger.Debug($"Glass init: Applying glass level {settings.GlassLevel}");
                    ThemeManager.Instance.GlassLevel = settings.GlassLevel;
                    ThemeManager.Instance.IsGlassEnabled = true;
                }
                else
                {
                    PlatypusTools.Core.Services.SimpleLogger.Debug($"Glass init: Not applying - Enabled={settings?.GlassEnabled}, Supported={ThemeManager.Instance.IsGlassSupported}");
                }
            }
            catch (System.Exception ex)
            {
                PlatypusTools.Core.Services.SimpleLogger.Error($"Error initializing glass theme: {ex.Message}");
            }
        }
        
        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            // Save session state before showing dialog (IDEA-009)
            SaveSessionState();
            
            // If force exit (from tray or programmatic), skip dialog
            if (_forceExit)
                return;
                
            // Show custom exit dialog with clear options
            var dialog = new Window
            {
                Title = "Exit PlatypusTools",
                Width = 460,
                Height = 140,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                ShowInTaskbar = false
            };
            
            var mainPanel = new StackPanel { Margin = new Thickness(15) };
            mainPanel.Children.Add(new TextBlock 
            { 
                Text = "What would you like to do?", 
                FontSize = 14, 
                Margin = new Thickness(0, 0, 0, 15),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            var exitBtn = new Button { Content = "Exit Application", MinWidth = 110, Height = 30, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) };
            var trayBtn = new Button { Content = "Minimize to Tray", MinWidth = 110, Height = 30, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) };
            var minimizeBtn = new Button { Content = "Minimize", MinWidth = 80, Height = 30, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) };
            var cancelBtn = new Button { Content = "Cancel", MinWidth = 70, Height = 30, Margin = new Thickness(4), Padding = new Thickness(8, 4, 8, 4) };
            
            string? choice = null;
            exitBtn.Click += (s, args) => { choice = "exit"; dialog.Close(); };
            trayBtn.Click += (s, args) => { choice = "tray"; dialog.Close(); };
            minimizeBtn.Click += (s, args) => { choice = "minimize"; dialog.Close(); };
            cancelBtn.Click += (s, args) => { choice = "cancel"; dialog.Close(); };
            
            buttonPanel.Children.Add(exitBtn);
            buttonPanel.Children.Add(trayBtn);
            buttonPanel.Children.Add(minimizeBtn);
            buttonPanel.Children.Add(cancelBtn);
            mainPanel.Children.Add(buttonPanel);
            dialog.Content = mainPanel;
            
            dialog.ShowDialog();
            
            switch (choice)
            {
                case "exit":
                    // User wants to exit - let the close proceed
                    break;
                    
                case "tray":
                    // Minimize to system tray
                    e.Cancel = true;
                    SystemTrayService.Instance.MinimizeToTray();
                    break;
                    
                case "minimize":
                    // Minimize to taskbar instead of closing
                    e.Cancel = true;
                    this.WindowState = WindowState.Minimized;
                    break;
                    
                case "cancel":
                default:
                    // Cancel the close
                    e.Cancel = true;
                    break;
            }
        }
        
        private void MainWindow_Closed(object? sender, System.EventArgs e)
        {
            // Clean up system tray
            try
            {
                SystemTrayService.Instance.Dispose();
            }
            catch { }
            
            // Force complete shutdown - kill all processes
            try
            {
                // Shutdown the application completely
                Application.Current.Shutdown();
                
                // Force kill the process to ensure complete cleanup
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            }
            catch
            {
                // Ignore errors during shutdown
            }
        }

        #region Session Restore (IDEA-009)

        /// <summary>
        /// Saves the current session state (selected tabs, window position/size) to settings.
        /// </summary>
        private void SaveSessionState()
        {
            try
            {
                var settings = SettingsManager.Current;
                
                // Save main tab selection
                settings.LastSelectedMainTabIndex = MainTabControl.SelectedIndex;
                
                // Save sub-tab selections for each main tab that has a nested TabControl
                var subIndices = new Dictionary<string, int>();
                for (int i = 0; i < MainTabControl.Items.Count; i++)
                {
                    if (MainTabControl.Items[i] is TabItem mainTab && mainTab.Content is TabControl subTab)
                    {
                        string key = mainTab.Header?.ToString() ?? $"Tab{i}";
                        subIndices[key] = subTab.SelectedIndex;
                    }
                }
                settings.LastSelectedSubTabIndices = subIndices;
                
                // Save window position and size (only if not minimized)
                if (this.WindowState != WindowState.Minimized)
                {
                    settings.WindowWidth = this.ActualWidth;
                    settings.WindowHeight = this.ActualHeight;
                    settings.WindowTop = this.Top;
                    settings.WindowLeft = this.Left;
                    settings.WindowStateValue = this.WindowState == WindowState.Maximized ? 2 : 0;
                }
                
                SettingsManager.SaveCurrent();
                PlatypusTools.Core.Services.SimpleLogger.Debug("Session state saved successfully");
            }
            catch (System.Exception ex)
            {
                PlatypusTools.Core.Services.SimpleLogger.Error($"Error saving session state: {ex.Message}");
            }
        }

        /// <summary>
        /// Restores the last session state (selected tabs, window position/size) from settings.
        /// </summary>
        private void RestoreSessionState()
        {
            try
            {
                var settings = SettingsManager.Current;
                if (!settings.RestoreLastSession) return;
                
                // Restore window position and size
                if (settings.WindowWidth > 100 && settings.WindowHeight > 100)
                {
                    // Ensure window is within screen bounds
                    var screen = System.Windows.SystemParameters.WorkArea;
                    double left = double.IsNaN(settings.WindowLeft) ? this.Left : settings.WindowLeft;
                    double top = double.IsNaN(settings.WindowTop) ? this.Top : settings.WindowTop;
                    
                    // Clamp to screen area
                    if (left + settings.WindowWidth > screen.Right + 50) left = screen.Right - settings.WindowWidth;
                    if (top + settings.WindowHeight > screen.Bottom + 50) top = screen.Bottom - settings.WindowHeight;
                    if (left < screen.Left - 50) left = screen.Left;
                    if (top < screen.Top - 50) top = screen.Top;
                    
                    this.Width = settings.WindowWidth;
                    this.Height = settings.WindowHeight;
                    this.Left = left;
                    this.Top = top;
                }
                
                // Restore maximized state
                if (settings.WindowStateValue == 2)
                {
                    this.WindowState = WindowState.Maximized;
                }
                
                // Restore main tab selection
                if (settings.LastSelectedMainTabIndex >= 0 && 
                    settings.LastSelectedMainTabIndex < MainTabControl.Items.Count)
                {
                    MainTabControl.SelectedIndex = settings.LastSelectedMainTabIndex;
                }
                
                // Restore sub-tab selections
                if (settings.LastSelectedSubTabIndices.Count > 0)
                {
                    for (int i = 0; i < MainTabControl.Items.Count; i++)
                    {
                        if (MainTabControl.Items[i] is TabItem mainTab && mainTab.Content is TabControl subTab)
                        {
                            string key = mainTab.Header?.ToString() ?? $"Tab{i}";
                            if (settings.LastSelectedSubTabIndices.TryGetValue(key, out int subIndex) &&
                                subIndex >= 0 && subIndex < subTab.Items.Count)
                            {
                                subTab.SelectedIndex = subIndex;
                            }
                        }
                    }
                }
                
                PlatypusTools.Core.Services.SimpleLogger.Debug("Session state restored successfully");
            }
            catch (System.Exception ex)
            {
                PlatypusTools.Core.Services.SimpleLogger.Error($"Error restoring session state: {ex.Message}");
            }
        }

        #endregion
        
        /// <summary>
        /// Handles keyboard shortcuts for fullscreen mode and command palette.
        /// Ctrl+Shift+P - Command Palette
        /// F11 - Toggle fullscreen
        /// ESC - Exit fullscreen
        /// Ctrl+Shift+A - Unlock AD Security Analyzer
        /// </summary>
        private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            var mods = System.Windows.Input.Keyboard.Modifiers;
            var ctrlOnly = mods == System.Windows.Input.ModifierKeys.Control;
            var ctrlShift = mods == (System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift);

            // Ctrl+Shift+P - Command Palette
            if (e.Key == System.Windows.Input.Key.P && ctrlShift)
            {
                Services.CommandService.Instance.ShowCommandPalette(this);
                e.Handled = true;
            }
            // Ctrl+Shift+A - Unlock AD Security Analyzer
            else if (e.Key == System.Windows.Input.Key.A && ctrlShift)
            {
                ShowAdSecurityUnlockDialog();
                e.Handled = true;
            }
            // IDEA-010: Ctrl+Z - Undo last operation
            else if (e.Key == System.Windows.Input.Key.Z && ctrlOnly)
            {
                _ = Services.UndoRedoService.Instance.UndoAsync();
                e.Handled = true;
            }
            // IDEA-010: Ctrl+Y - Redo
            else if (e.Key == System.Windows.Input.Key.Y && ctrlOnly)
            {
                _ = Services.UndoRedoService.Instance.RedoAsync();
                e.Handled = true;
            }
            // IDEA-010: Ctrl+1 through Ctrl+9 - Switch to main tab by number
            else if (ctrlOnly && e.Key >= System.Windows.Input.Key.D1 && e.Key <= System.Windows.Input.Key.D9)
            {
                var index = e.Key - System.Windows.Input.Key.D1;
                if (MainTabControl != null && index < MainTabControl.Items.Count)
                {
                    MainTabControl.SelectedIndex = index;
                    e.Handled = true;
                }
            }
            // IDEA-010: Ctrl+, - Open Settings
            else if (e.Key == System.Windows.Input.Key.OemComma && ctrlOnly)
            {
                try
                {
                    var settingsWindow = new Views.SettingsWindow { Owner = this };
                    settingsWindow.ShowDialog();
                }
                catch { /* ignore if already open */ }
                e.Handled = true;
            }
            // IDEA-002: Ctrl+K - Global Search / Spotlight (same as command palette)
            else if (e.Key == System.Windows.Input.Key.K && ctrlOnly)
            {
                Services.CommandService.Instance.ShowCommandPalette(this);
                e.Handled = true;
            }
            // Ctrl+H - Toggle auto-hide navigation tabs
            else if (e.Key == System.Windows.Input.Key.H && ctrlOnly)
            {
                ToggleAutoHideNavigation();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.F11)
            {
                ToggleFullScreen();
                e.Handled = true;
            }
            else if (e.Key == System.Windows.Input.Key.Escape && _isFullScreen)
            {
                ExitFullScreen();
                e.Handled = true;
            }
        }

        /// <summary>
        /// Shows the AD Security Analyzer unlock dialog.
        /// </summary>
        private void ShowAdSecurityUnlockDialog()
        {
            var dialog = new Views.AdSecurityUnlockDialog
            {
                Owner = this
            };
            
            if (dialog.ShowDialog() == true && dialog.WasUnlocked)
            {
                // Tab visibility is refreshed by the dialog
                // Optionally navigate to the tab
                StatusBarViewModel.Instance.StatusMessage = "AD Security Analyzer unlocked";
            }
        }
        
        /// <summary>
        /// Toggles between fullscreen and windowed mode.
        /// </summary>
        public void ToggleFullScreen()
        {
            if (_isFullScreen)
            {
                ExitFullScreen();
            }
            else
            {
                EnterFullScreen();
            }
        }
        
        /// <summary>
        /// Enters fullscreen mode, hiding title bar and taskbar.
        /// </summary>
        public void EnterFullScreen()
        {
            if (_isFullScreen) return;
            
            // Save current state
            _previousWindowState = this.WindowState;
            _previousWindowStyle = this.WindowStyle;
            _previousResizeMode = this.ResizeMode;
            _previousBounds = new Rect(this.Left, this.Top, this.Width, this.Height);
            
            // Enter fullscreen
            this.WindowState = WindowState.Normal; // Must be normal first to change WindowStyle
            this.WindowStyle = WindowStyle.None;
            this.ResizeMode = ResizeMode.NoResize;
            this.WindowState = WindowState.Maximized;
            
            // Ensure window covers taskbar
            this.Topmost = true;
            this.Topmost = false; // Reset topmost but keep fullscreen
            
            _isFullScreen = true;
            
            // Show brief notification
            StatusBarViewModel.Instance.StatusMessage = "Fullscreen mode - Press ESC or F11 to exit";
        }
        
        /// <summary>
        /// Exits fullscreen mode and restores previous window state.
        /// </summary>
        public void ExitFullScreen()
        {
            if (!_isFullScreen) return;
            
            // Restore previous state
            this.WindowState = WindowState.Normal;
            this.WindowStyle = _previousWindowStyle;
            this.ResizeMode = _previousResizeMode;
            
            // Restore bounds
            this.Left = _previousBounds.Left;
            this.Top = _previousBounds.Top;
            this.Width = _previousBounds.Width;
            this.Height = _previousBounds.Height;
            
            // Restore window state
            this.WindowState = _previousWindowState;
            
            _isFullScreen = false;
            
            StatusBarViewModel.Instance.StatusMessage = "Ready";
        }
        
        /// <summary>
        /// Gets whether the window is currently in fullscreen mode.
        /// </summary>
        public bool IsFullScreen => _isFullScreen;
        
        /// <summary>
        /// Menu click handler for fullscreen toggle.
        /// </summary>
        private void ToggleFullScreen_Click(object sender, RoutedEventArgs e)
        {
            ToggleFullScreen();
        }

        #region Auto-Hide Navigation

        private bool _isNavigationHidden = false;

        /// <summary>
        /// Toggles auto-hide mode for all navigation tab strips.
        /// When hidden, a thin reveal bar appears at the top that shows tabs on hover.
        /// </summary>
        public void ToggleAutoHideNavigation()
        {
            _isNavigationHidden = !_isNavigationHidden;
            ApplyAutoHideNavigation(_isNavigationHidden);

            // Save to settings
            var settings = SettingsManager.Current;
            settings.AutoHideNavigation = _isNavigationHidden;
            SettingsManager.Save(settings);

            StatusBarViewModel.Instance.StatusMessage = _isNavigationHidden
                ? "Navigation hidden - hover top edge or press Ctrl+H to show"
                : "Navigation visible";
        }

        /// <summary>
        /// Applies or removes auto-hide on all TabPanels in the visual tree.
        /// </summary>
        private void ApplyAutoHideNavigation(bool hide)
        {
            _isNavigationHidden = hide;

            // Toggle visibility of the tab strip (TabPanel) in MainTabControl
            // and the header bar, making more room for content
            if (TabRevealBar != null)
                TabRevealBar.Visibility = hide ? Visibility.Visible : Visibility.Collapsed;

            if (HeaderBorder != null)
                HeaderBorder.Visibility = hide ? Visibility.Collapsed : Visibility.Visible;

            // Walk visual tree and collapse all TabPanels
            SetAllTabPanelsVisibility(MainTabControl, hide ? Visibility.Collapsed : Visibility.Visible);
        }

        /// <summary>
        /// Recursively sets visibility on all TabPanel elements within a parent.
        /// </summary>
        private void SetAllTabPanelsVisibility(DependencyObject? parent, Visibility visibility)
        {
            if (parent == null) return;

            int childCount = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < childCount; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is System.Windows.Controls.Primitives.TabPanel tabPanel)
                {
                    tabPanel.Visibility = visibility;
                }
                SetAllTabPanelsVisibility(child, visibility);
            }
        }

        /// <summary>
        /// Temporarily reveals navigation tabs when hovering the reveal bar.
        /// </summary>
        private void TabRevealBar_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isNavigationHidden)
            {
                // Temporarily show tab panels and header
                if (HeaderBorder != null)
                    HeaderBorder.Visibility = Visibility.Visible;
                SetAllTabPanelsVisibility(MainTabControl, Visibility.Visible);
            }
        }

        /// <summary>
        /// Re-hides navigation when mouse leaves the navigation area.
        /// Uses delayed hide to prevent flickering.
        /// </summary>
        private System.Windows.Threading.DispatcherTimer? _autoHideTimer;
        
        private void NavigationArea_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isNavigationHidden) return;

            // Delay the hide to prevent flickering when moving between elements
            _autoHideTimer?.Stop();
            _autoHideTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(400)
            };
            _autoHideTimer.Tick += (s, args) =>
            {
                _autoHideTimer?.Stop();
                if (_isNavigationHidden && !IsMouseOverNavigationArea())
                {
                    if (HeaderBorder != null)
                        HeaderBorder.Visibility = Visibility.Collapsed;
                    SetAllTabPanelsVisibility(MainTabControl, Visibility.Collapsed);
                }
            };
            _autoHideTimer.Start();
        }

        private void NavigationArea_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            // Cancel pending hide when mouse re-enters navigation
            _autoHideTimer?.Stop();
        }

        /// <summary>
        /// Checks if the mouse is currently over the navigation area (reveal bar, header, or tab panels).
        /// </summary>
        private bool IsMouseOverNavigationArea()
        {
            if (TabRevealBar?.IsMouseOver == true) return true;
            if (HeaderBorder?.IsMouseOver == true) return true;
            if (MainTabControl?.IsMouseOver == true)
            {
                // Check if mouse is in the top portion (tab strip area)
                var pos = System.Windows.Input.Mouse.GetPosition(MainTabControl);
                if (pos.Y < 40) return true; // Tab strip is roughly 30-40px tall
            }
            return false;
        }

        /// <summary>
        /// Restores auto-hide state from settings on load.
        /// </summary>
        private void RestoreAutoHideState()
        {
            var settings = SettingsManager.Current;
            if (settings.AutoHideNavigation)
            {
                // Delay to ensure visual tree is loaded
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    ApplyAutoHideNavigation(true);
                });
            }
        }

        /// <summary>
        /// Menu/button click handler for auto-hide navigation toggle.
        /// </summary>
        private void ToggleAutoHideNavigation_Click(object sender, RoutedEventArgs e)
        {
            ToggleAutoHideNavigation();
        }

        #endregion

        private void RefreshRecentWorkspacesMenu()
        {
            if (RecentWorkspacesMenu == null) return;
            
            RecentWorkspacesMenu.Items.Clear();
            
            var recentWorkspaces = RecentWorkspacesService.Instance.RecentWorkspaces;
            
            if (recentWorkspaces.Count == 0)
            {
                var placeholder = new MenuItem
                {
                    Header = "(No recent workspaces)",
                    IsEnabled = false
                };
                RecentWorkspacesMenu.Items.Add(placeholder);
            }
            else
            {
                int index = 1;
                foreach (var workspace in recentWorkspaces.Take(10))
                {
                    var menuItem = new MenuItem
                    {
                        Header = $"_{index}. {workspace.Name}",
                        ToolTip = workspace.Path,
                        Tag = workspace.Path
                    };
                    menuItem.Click += RecentWorkspaceItem_Click;
                    RecentWorkspacesMenu.Items.Add(menuItem);
                    index++;
                }
                
                if (recentWorkspaces.Count > 10)
                {
                    RecentWorkspacesMenu.Items.Add(new Separator());
                    var moreItem = new MenuItem
                    {
                        Header = "More...",
                        Command = (DataContext as MainWindowViewModel)?.OpenRecentWorkspacesCommand
                    };
                    RecentWorkspacesMenu.Items.Add(moreItem);
                }
            }
        }

        private void RecentWorkspaceItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string path)
            {
                var vm = DataContext as MainWindowViewModel;
                if (vm != null)
                {
                    vm.SelectedFolder = path;
                    RecentWorkspacesService.Instance.AddWorkspace(path);
                }
            }
        }

        private void ClearRecentList_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all recent workspaces?",
                "Clear Recent Workspaces",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                RecentWorkspacesService.Instance.Clear();
                RefreshRecentWorkspacesMenu();
            }
        }

        private void ValidateDataContext_Click(object sender, RoutedEventArgs e)
        {
            var report = DataContextValidator.ValidateMainWindow(this);
            MessageBox.Show(report, "DataContext Validation", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UnlockAdSecurity_Click(object sender, RoutedEventArgs e)
        {
            ShowAdSecurityUnlockDialog();
        }

        private void ShowFileCleanerHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#file-cleaner");
        }

        private void ShowVideoConverterHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#media-conversion");
        }

        private void ShowVideoCombinerHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#media-conversion");
        }

        private void ShowDuplicatesHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#duplicates");
        }

        private void ShowSystemCleanerHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#cleanup");
        }

        private void ShowFolderHiderHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#security");
        }

        private void ShowRobocopyHelp(object sender, RoutedEventArgs e)
        {
            OpenHelpSection("#robocopy");
        }

        private void OpenHelpSection(string section)
        {
            try
            {
                var helpWindow = new Views.HelpWindow();
                helpWindow.Owner = this;
                helpWindow.Show();
                // WebView2 will handle the navigation to section via URL fragment
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening help: {ex.Message}", "Help Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowChangelog(object sender, RoutedEventArgs e)
        {
            var win = new Views.ChangelogWindow { Owner = this };
            win.ShowDialog();
        }

        private void ShowAbout(object sender, RoutedEventArgs e)
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            MessageBox.Show(
                "PLATYPUSTOOLS - Advanced File Management Suite\n\n" +
                $"Version: {version}\n" +
                "Built with: .NET 10 / WPF\n\n" +
                "FEATURES:\n" +
                "• File Cleaner & Batch Renamer\n" +
                "• Video/Image Converter & Processor\n" +
                "• Duplicate File Finder\n" +
                "• System Cleanup Tools\n" +
                "• Privacy & Security Tools\n" +
                "• Metadata Editor\n\n" +
                "Originally developed as PowerShell scripts,\n" +
                "now rebuilt as a modern Windows application.\n\n" +
                "© 2026 PlatypusTools Project",
                "About PlatypusTools",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        private void OpenLogViewer(object sender, RoutedEventArgs e)
        {
            try
            {
                var logWindow = new Views.LogViewerWindow();
                logWindow.Owner = this;
                logWindow.Show();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error opening log viewer: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void CheckForUpdates(object sender, RoutedEventArgs e)
        {
            try
            {
                var updateService = Services.UpdateService.Instance;
                var updateInfo = await updateService.CheckForUpdatesAsync();
                
                if (updateInfo != null)
                {
                    var updateWindow = new Views.UpdateWindow(updateInfo);
                    updateWindow.Owner = this;
                    updateWindow.ShowDialog();
                }
                else
                {
                    MessageBox.Show("You are running the latest version.", "No Updates", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error checking for updates: {ex.Message}", "Update Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void BackupSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PlatypusTools Backup (*.ptbackup)|*.ptbackup|All files (*.*)|*.*",
                    FileName = Services.SettingsBackupService.Instance.GetDefaultBackupFileName()
                };
                
                if (dlg.ShowDialog() == true)
                {
                    var success = await Services.SettingsBackupService.Instance.CreateBackupAsync(dlg.FileName);
                    if (success)
                    {
                        MessageBox.Show("Settings backed up successfully.", "Backup Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Failed to create backup.", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error backing up settings: {ex.Message}", "Backup Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void RestoreSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "PlatypusTools Backup (*.ptbackup)|*.ptbackup|All files (*.*)|*.*"
                };
                
                if (dlg.ShowDialog() == true)
                {
                    var result = MessageBox.Show(
                        "Restoring settings will overwrite your current configuration.\n\nA backup of your current settings will be created first.\n\nContinue?",
                        "Restore Settings",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        var success = await Services.SettingsBackupService.Instance.RestoreBackupAsync(dlg.FileName);
                        if (success)
                        {
                            MessageBox.Show("Settings restored successfully.\n\nPlease restart the application for changes to take effect.", "Restore Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                        else
                        {
                            MessageBox.Show("Failed to restore settings.", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error restoring settings: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    
        private void OpenSettings(object sender, RoutedEventArgs e)
        {
            try
            {
                var settingsWindow = new Views.SettingsWindow();
                settingsWindow.Owner = this;
                settingsWindow.ShowDialog();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Error opening settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}

