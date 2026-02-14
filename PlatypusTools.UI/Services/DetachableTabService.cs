using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// IDEA-016: Multi-Window Support Service.
    /// Allows detaching tab content into separate floating windows for multi-monitor setups.
    /// Tracks detached windows and supports re-docking content back into the main window.
    /// </summary>
    public class DetachableTabService
    {
        private static readonly Lazy<DetachableTabService> _instance = new(() => new DetachableTabService());
        public static DetachableTabService Instance => _instance.Value;

        private readonly List<FloatingTabWindow> _floatingWindows = new();
        
        /// <summary>
        /// Currently open floating windows.
        /// </summary>
        public IReadOnlyList<FloatingTabWindow> FloatingWindows => _floatingWindows;

        /// <summary>
        /// Raised when a tab is detached into a floating window.
        /// </summary>
        public event EventHandler<FloatingTabWindow>? TabDetached;

        /// <summary>
        /// Raised when a floating window is re-docked.
        /// </summary>
        public event EventHandler<FloatingTabWindow>? TabRedocked;

        /// <summary>
        /// Detaches a TabItem from a TabControl into a new floating window.
        /// </summary>
        /// <param name="tabControl">The parent TabControl containing the tab.</param>
        /// <param name="tabItem">The TabItem to detach.</param>
        /// <returns>The created floating window, or null if detach failed.</returns>
        public FloatingTabWindow? DetachTab(TabControl tabControl, TabItem tabItem)
        {
            if (tabControl == null || tabItem == null) return null;

            try
            {
                var tabHeader = tabItem.Header?.ToString() ?? "Detached Tab";
                var content = tabItem.Content;

                if (content == null) return null;

                // Remove from original TabControl
                tabItem.Content = null; // Disconnect content first
                int originalIndex = tabControl.Items.IndexOf(tabItem);
                tabControl.Items.Remove(tabItem);

                // Select adjacent tab
                if (tabControl.Items.Count > 0)
                {
                    tabControl.SelectedIndex = Math.Min(originalIndex, tabControl.Items.Count - 1);
                }

                // Create floating window
                var floatingWindow = new FloatingTabWindow
                {
                    Title = $"PlatypusTools — {tabHeader}",
                    Content = new ContentControl { Content = content },
                    Width = 900,
                    Height = 650,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    Background = Application.Current.MainWindow?.Background,
                    // Store metadata for re-docking
                    OriginalTabControl = tabControl,
                    OriginalTabItem = tabItem,
                    OriginalIndex = originalIndex,
                    TabHeader = tabHeader,
                    DetachedContent = content
                };

                // Apply current theme resources
                foreach (var dict in Application.Current.Resources.MergedDictionaries)
                {
                    floatingWindow.Resources.MergedDictionaries.Add(dict);
                }

                // Set window chrome
                floatingWindow.Style = CreateFloatingWindowStyle();

                floatingWindow.Closed += (s, e) =>
                {
                    _floatingWindows.Remove(floatingWindow);
                    
                    // Re-dock content back to original TabControl if it still exists
                    if (floatingWindow.OriginalTabControl != null && 
                        floatingWindow.DetachedContent != null)
                    {
                        var newTab = floatingWindow.OriginalTabItem ?? new TabItem();
                        newTab.Header = floatingWindow.TabHeader;
                        newTab.Content = floatingWindow.DetachedContent;

                        var tc = floatingWindow.OriginalTabControl;
                        int insertIdx = Math.Min(floatingWindow.OriginalIndex, tc.Items.Count);
                        tc.Items.Insert(insertIdx, newTab);
                        tc.SelectedItem = newTab;

                        TabRedocked?.Invoke(this, floatingWindow);
                    }
                };

                _floatingWindows.Add(floatingWindow);
                floatingWindow.Show();

                TabDetached?.Invoke(this, floatingWindow);
                return floatingWindow;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"Failed to detach tab: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Detaches the currently selected tab from a TabControl.
        /// </summary>
        public FloatingTabWindow? DetachSelectedTab(TabControl tabControl)
        {
            if (tabControl?.SelectedItem is TabItem selectedTab)
            {
                return DetachTab(tabControl, selectedTab);
            }
            return null;
        }

        /// <summary>
        /// Closes all floating windows, re-docking their content.
        /// </summary>
        public void RedockAll()
        {
            foreach (var window in _floatingWindows.ToArray())
            {
                window.Close();
            }
        }

        /// <summary>
        /// Gets the count of floating windows.
        /// </summary>
        public int FloatingCount => _floatingWindows.Count;

        private static Style CreateFloatingWindowStyle()
        {
            var style = new Style(typeof(Window));
            style.Setters.Add(new Setter(Window.BackgroundProperty, 
                Application.Current.FindResource("WindowBackgroundBrush") as System.Windows.Media.Brush 
                ?? System.Windows.Media.Brushes.Black));
            style.Setters.Add(new Setter(Window.ForegroundProperty, 
                Application.Current.FindResource("WindowForegroundBrush") as System.Windows.Media.Brush 
                ?? System.Windows.Media.Brushes.White));
            style.Setters.Add(new Setter(Window.ResizeModeProperty, ResizeMode.CanResize));
            return style;
        }
    }

    /// <summary>
    /// A floating window containing detached tab content.
    /// Stores original location for re-docking when closed.
    /// </summary>
    public class FloatingTabWindow : Window
    {
        /// <summary>The original TabControl this tab came from.</summary>
        public TabControl? OriginalTabControl { get; set; }
        
        /// <summary>The original TabItem (for re-docking with same instance).</summary>
        public TabItem? OriginalTabItem { get; set; }
        
        /// <summary>Original position in the TabControl.</summary>
        public int OriginalIndex { get; set; }
        
        /// <summary>Tab header text for re-docking.</summary>
        public string TabHeader { get; set; } = string.Empty;
        
        /// <summary>The detached content (UserControl, etc.).</summary>
        public object? DetachedContent { get; set; }
    }
}
