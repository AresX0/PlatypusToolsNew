using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Service that provides tab visibility state and notifies the UI when visibility changes.
    /// Implements INotifyPropertyChanged for WPF binding support.
    /// </summary>
    public class TabVisibilityService : INotifyPropertyChanged
    {
        private static TabVisibilityService? _instance;
        public static TabVisibilityService Instance => _instance ??= new TabVisibilityService();

        private Dictionary<string, Visibility> _tabVisibility = new();
        
        public event PropertyChangedEventHandler? PropertyChanged;
        
        /// <summary>
        /// Event raised when any tab's visibility changes.
        /// </summary>
        public event EventHandler? VisibilityChanged;

        private TabVisibilityService()
        {
            // Subscribe to settings changes
            SettingsManager.Current.TabVisibilityChanged += OnSettingsTabVisibilityChanged;
            
            // Initialize from current settings
            RefreshFromSettings();
        }

        private void OnSettingsTabVisibilityChanged(object? sender, string tabKey)
        {
            RefreshFromSettings();
        }

        /// <summary>
        /// Refreshes all tab visibility states from current settings.
        /// Call this after settings are saved.
        /// </summary>
        public void RefreshFromSettings()
        {
            var settings = SettingsManager.Current;
            var tabDefinitions = SettingsManager.GetAllTabDefinitions();
            
            foreach (var tab in tabDefinitions)
            {
                var isVisible = settings.IsTabVisible(tab.Key);
                _tabVisibility[tab.Key] = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Notify all properties changed
            OnPropertyChanged(string.Empty);
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gets the visibility for a specific tab key.
        /// </summary>
        public Visibility GetVisibility(string tabKey)
        {
            if (string.IsNullOrEmpty(tabKey)) return Visibility.Visible;
            return _tabVisibility.TryGetValue(tabKey, out var visibility) ? visibility : Visibility.Visible;
        }

        // Individual tab visibility properties for direct binding
        // File Management
        public Visibility FileManagement => GetVisibility("FileManagement");
        public Visibility FileCleaner => GetVisibility("FileManagement.FileCleaner");
        public Visibility Duplicates => GetVisibility("FileManagement.Duplicates");
        public Visibility EmptyFolderScanner => GetVisibility("FileManagement.EmptyFolderScanner");

        // Multimedia
        public Visibility Multimedia => GetVisibility("Multimedia");
        public Visibility AudioPlayer => GetVisibility("Multimedia.Audio.AudioPlayer");
        public Visibility EnhancedAudioPlayer => GetVisibility("Multimedia.Audio.EnhancedAudioPlayer");
        public Visibility AudioTrim => GetVisibility("Multimedia.Audio.AudioTrim");
        public Visibility ImageEdit => GetVisibility("Multimedia.Image.ImageEdit");
        public Visibility ImageConverter => GetVisibility("Multimedia.Image.ImageConverter");
        public Visibility ImageResizer => GetVisibility("Multimedia.Image.ImageResizer");
        public Visibility IconConverter => GetVisibility("Multimedia.Image.IconConverter");
        public Visibility BatchWatermark => GetVisibility("Multimedia.Image.BatchWatermark");
        public Visibility ImageScaler => GetVisibility("Multimedia.Image.ImageScaler");
        public Visibility Model3DEditor => GetVisibility("Multimedia.Image.Model3DEditor");
        public Visibility VideoPlayer => GetVisibility("Multimedia.Video.VideoPlayer");
        public Visibility VideoEditor => GetVisibility("Multimedia.Video.VideoEditor");
        public Visibility Upscaler => GetVisibility("Multimedia.Video.Upscaler");
        public Visibility VideoCombiner => GetVisibility("Multimedia.Video.VideoCombiner");
        public Visibility VideoConverter => GetVisibility("Multimedia.Video.VideoConverter");
        public Visibility MediaLibrary => GetVisibility("Multimedia.MediaLibrary");
        public Visibility ExternalTools => GetVisibility("Multimedia.ExternalTools");

        // System
        public Visibility System => GetVisibility("System");
        public Visibility DiskCleanup => GetVisibility("System.DiskCleanup");
        public Visibility PrivacyCleaner => GetVisibility("System.PrivacyCleaner");
        public Visibility RecentCleaner => GetVisibility("System.RecentCleaner");
        public Visibility StartupManager => GetVisibility("System.StartupManager");
        public Visibility ProcessManager => GetVisibility("System.ProcessManager");
        public Visibility RegistryCleaner => GetVisibility("System.RegistryCleaner");
        public Visibility ScheduledTasks => GetVisibility("System.ScheduledTasks");
        public Visibility SystemRestore => GetVisibility("System.SystemRestore");

        // Security
        public Visibility Security => GetVisibility("Security");
        public Visibility FolderHider => GetVisibility("Security.FolderHider");
        public Visibility SystemAudit => GetVisibility("Security.SystemAudit");
        public Visibility ForensicsAnalyzer => GetVisibility("Security.ForensicsAnalyzer");

        // Metadata
        public Visibility Metadata => GetVisibility("Metadata");

        // Tools
        public Visibility Tools => GetVisibility("Tools");
        public Visibility WebsiteDownloader => GetVisibility("Tools.WebsiteDownloader");
        public Visibility FileAnalyzer => GetVisibility("Tools.FileAnalyzer");
        public Visibility DiskSpaceAnalyzer => GetVisibility("Tools.DiskSpaceAnalyzer");
        public Visibility NetworkTools => GetVisibility("Tools.NetworkTools");
        public Visibility ArchiveManager => GetVisibility("Tools.ArchiveManager");
        public Visibility PdfTools => GetVisibility("Tools.PdfTools");
        public Visibility Screenshot => GetVisibility("Tools.Screenshot");
        public Visibility BootableUSB => GetVisibility("Tools.BootableUSB");
        public Visibility PluginManager => GetVisibility("Tools.PluginManager");
        public Visibility FtpClient => GetVisibility("Tools.FtpClient");
        public Visibility TerminalClient => GetVisibility("Tools.TerminalClient");
        public Visibility SimpleBrowser => GetVisibility("Tools.SimpleBrowser");

        // FileManagement - Robocopy
        public Visibility Robocopy => GetVisibility("FileManagement.Robocopy");

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
