using System;
using System.IO;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class SplashScreenWindow : Window
    {
        private string? _videoPath;
        
        public SplashScreenWindow()
        {
            InitializeComponent();
            
            // Find video path IMMEDIATELY in constructor so it's ready when window loads
            FindVideoPath();
            
            // Start loading video as soon as window is initialized
            if (!string.IsNullOrEmpty(_videoPath))
            {
                VideoPlayer.Source = new Uri(_videoPath, UriKind.Absolute);
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
                    if (File.Exists(path))
                    {
                        _videoPath = path;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to find splash video: {ex.Message}");
            }
        }

        private void SplashScreenWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // If video wasn't set in constructor, try again
            if (VideoPlayer.Source == null && !string.IsNullOrEmpty(_videoPath))
            {
                VideoPlayer.Source = new Uri(_videoPath, UriKind.Absolute);
            }
            
            // Force video to start playing immediately
            VideoPlayer.Play();
            
            if (!string.IsNullOrEmpty(_videoPath))
            {
                System.Diagnostics.Debug.WriteLine($"Splash video loaded: {_videoPath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("No splash video found");
            }
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the video continuously until splash screen is closed
            VideoPlayer.Position = TimeSpan.Zero;
            VideoPlayer.Play();
        }
        
        private void VideoPlayer_MediaOpened(object sender, RoutedEventArgs e)
        {
            // Video is ready - ensure it starts playing
            VideoPlayer.Play();
        }

        public void UpdateStatus(string message)
        {
            if (LoadingText != null)
            {
                _ = Dispatcher.InvokeAsync(() => LoadingText.Text = message);
            }
        }
    }
}
