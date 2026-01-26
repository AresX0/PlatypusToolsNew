using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace PlatypusTools.UI.Views
{
    public partial class SplashScreenWindow : Window
    {
        private string? _videoPath;
        private DispatcherTimer? _retryTimer;
        private int _retryCount = 0;
        
        public SplashScreenWindow()
        {
            InitializeComponent();
            
            // Find video path IMMEDIATELY in constructor so it's ready when window loads
            FindVideoPath();
            
            // Start loading video as soon as window is initialized
            if (!string.IsNullOrEmpty(_videoPath))
            {
                try
                {
                    VideoPlayer.Source = new Uri(_videoPath, UriKind.Absolute);
                    System.Diagnostics.Debug.WriteLine($"Splash: Video source set to {_videoPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Splash: Failed to set video source: {ex.Message}");
                }
            }
            
            Loaded += SplashScreenWindow_Loaded;
        }
        
        private void FindVideoPath()
        {
            try
            {
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                
                // Try multiple paths in priority order
                string[] possiblePaths = 
                {
                    Path.Combine(baseDir, "Assets", "PlatypusToolsIntro.mp4"),
                    Path.Combine(baseDir, "Assets", "platypus_swimming.mp4"),
                    Path.Combine(baseDir, "PlatypusToolsIntro.mp4"),
                    Path.Combine(baseDir, "platypus_swimming.mp4")
                };
                
                foreach (var path in possiblePaths)
                {
                    System.Diagnostics.Debug.WriteLine($"Splash: Checking path {path}");
                    if (File.Exists(path))
                    {
                        _videoPath = path;
                        System.Diagnostics.Debug.WriteLine($"Splash: Found video at {path}");
                        return;
                    }
                }
                
                System.Diagnostics.Debug.WriteLine("Splash: No video file found in any location");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Splash: Failed to find video: {ex.Message}");
            }
        }

        private void SplashScreenWindow_Loaded(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("Splash: Window loaded");
            
            // If video wasn't set in constructor, try again
            if (VideoPlayer.Source == null && !string.IsNullOrEmpty(_videoPath))
            {
                try
                {
                    VideoPlayer.Source = new Uri(_videoPath, UriKind.Absolute);
                }
                catch { }
            }
            
            // Force video to start playing immediately
            try
            {
                VideoPlayer.Play();
                System.Diagnostics.Debug.WriteLine("Splash: Called Play()");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Splash: Play() failed: {ex.Message}");
            }
            
            // Set up a retry timer in case video doesn't start
            _retryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _retryTimer.Tick += RetryTimer_Tick;
            _retryTimer.Start();
        }
        
        private void RetryTimer_Tick(object? sender, EventArgs e)
        {
            _retryCount++;
            
            // Try to restart video if it's not playing
            if (VideoPlayer.Source != null)
            {
                try
                {
                    VideoPlayer.Play();
                }
                catch { }
            }
            
            // Stop retrying after 5 attempts
            if (_retryCount >= 5)
            {
                _retryTimer?.Stop();
            }
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the video continuously until splash screen is closed
            System.Diagnostics.Debug.WriteLine("Splash: Video ended, looping");
            VideoPlayer.Position = TimeSpan.Zero;
            VideoPlayer.Play();
        }
        
        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Video is ready - ensure it starts playing
            System.Diagnostics.Debug.WriteLine("Splash: MediaOpened fired");
            _retryTimer?.Stop(); // Video is working, stop retry timer
            VideoPlayer.Play();
        }

        public void UpdateStatus(string message)
        {
            if (LoadingText != null)
            {
                _ = Dispatcher.InvokeAsync(() => LoadingText.Text = message);
            }
        }
        
        protected override void OnClosed(EventArgs e)
        {
            _retryTimer?.Stop();
            _retryTimer = null;
            try
            {
                VideoPlayer.Stop();
                VideoPlayer.Source = null;
            }
            catch { }
            base.OnClosed(e);
        }
    }
}
