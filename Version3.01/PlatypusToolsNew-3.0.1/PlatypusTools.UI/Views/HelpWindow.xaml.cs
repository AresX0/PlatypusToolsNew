using System;
using System.IO;
using System.Windows;

namespace PlatypusTools.UI.Views
{
    public partial class HelpWindow : Window
    {
        public HelpWindow()
        {
            InitializeComponent();
            Loaded += HelpWindow_Loaded;
        }

        private async void HelpWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                await webView.EnsureCoreWebView2Async(null);
                
                // Try multiple possible locations for the help file
                string[] possiblePaths = new[]
                {
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "PlatypusTools_Help.html"),
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PlatypusTools_Help.html"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PlatypusUtils", "PlatypusTools_Help.html")
                };

                string? helpFile = null;
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        helpFile = path;
                        break;
                    }
                }

                if (helpFile != null && webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Navigate(new Uri(helpFile).AbsoluteUri);
                }
                else
                {
                    // Fallback: Show embedded basic help
                    webView.NavigateToString(GetFallbackHelp());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading help: {ex.Message}", "Help Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                webView.NavigateToString(GetFallbackHelp());
            }
        }

        private string GetFallbackHelp()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <meta charset='UTF-8'>
    <title>PlatypusTools Help</title>
    <style>
        body { font-family: Segoe UI, Arial; padding: 20px; background: #f8f9fa; }
        .container { max-width: 900px; margin: 0 auto; background: white; padding: 30px; border-radius: 8px; }
        h1 { color: #2d5aa8; border-bottom: 3px solid #5c8dd6; padding-bottom: 10px; }
        h2 { color: #2d5aa8; margin-top: 30px; }
        .section { margin-bottom: 25px; }
    </style>
</head>
<body>
    <div class='container'>
        <h1>🦆 PlatypusTools Help</h1>
        <p><em>Note: Full help documentation not found. Please ensure PlatypusTools_Help.html is in the Assets folder.</em></p>
        
        <div class='section'>
            <h2>Quick Start</h2>
            <p>PlatypusTools provides comprehensive file management and media processing capabilities:</p>
            <ul>
                <li><strong>File Cleaner:</strong> Batch rename, organize, and clean files</li>
                <li><strong>Media Conversion:</strong> Convert videos and images</li>
                <li><strong>Duplicates:</strong> Find and remove duplicate files</li>
                <li><strong>Cleanup:</strong> System maintenance tools</li>
                <li><strong>Security:</strong> Hide folders and manage permissions</li>
                <li><strong>Metadata:</strong> View and edit file metadata</li>
            </ul>
        </div>
        
        <div class='section'>
            <h2>Getting Help</h2>
            <p>Use the individual tool help buttons (?) for context-specific guidance.</p>
            <p>Full documentation: Ensure PlatypusTools_Help.html is installed with the application.</p>
        </div>
    </div>
</body>
</html>";
        }
    }
}
