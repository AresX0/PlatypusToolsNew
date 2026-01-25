using System;
using System.IO;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class SplashScreenWindow : Window
    {
        public SplashScreenWindow()
        {
            InitializeComponent();
            Loaded += SplashScreenWindow_Loaded;
        }

        private void SplashScreenWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Try to find the intro video file (new branding)
                var baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var videoPath = Path.Combine(baseDir, "Assets", "PlatypusToolsIntro.mp4");

                // Fallback to old video if new one not found
                if (!File.Exists(videoPath))
                {
                    videoPath = Path.Combine(baseDir, "Assets", "platypus_swimming.mp4");
                }
                
                if (!File.Exists(videoPath))
                {
                    // Try alternate locations
                    videoPath = Path.Combine(baseDir, "PlatypusToolsIntro.mp4");
                }
                
                if (!File.Exists(videoPath))
                {
                    videoPath = Path.Combine(baseDir, "platypus_swimming.mp4");
                }

                if (File.Exists(videoPath))
                {
                    VideoPlayer.Source = new Uri(videoPath, UriKind.Absolute);
                    System.Diagnostics.Debug.WriteLine($"Splash video loaded: {videoPath}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("No splash video found");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load splash video: {ex.Message}");
            }
        }

        private void VideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            // Loop the video
            VideoPlayer.Position = TimeSpan.Zero;
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
