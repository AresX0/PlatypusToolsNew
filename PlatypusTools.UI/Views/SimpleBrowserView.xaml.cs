using Microsoft.Web.WebView2.Core;
using System;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Views
{
    public partial class SimpleBrowserView : UserControl
    {
        private bool _isInitialized;

        public SimpleBrowserView()
        {
            InitializeComponent();
            InitializeWebView();
            DataContextChanged += OnDataContextChanged;
        }

        private async void InitializeWebView()
        {
            if (_isInitialized) return;

            try
            {
                // Use shared WebView2 environment to prevent conflicts
                var env = await WebView2EnvironmentService.GetSharedEnvironmentAsync();
                await webView.EnsureCoreWebView2Async(env);

                // Configure WebView2 for memory efficiency
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsZoomControlEnabled = true;

                // Wire up events
                webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                webView.CoreWebView2.SourceChanged += OnSourceChanged;

                _isInitialized = true;

                // Navigate to default page after initialization
                webView.CoreWebView2.Navigate("https://www.google.com");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize browser: {ex.Message}\n\nPlease ensure WebView2 Runtime is installed.",
                    "Browser Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is ViewModels.SimpleBrowserViewModel vm)
            {
                vm.NavigateToUrlRequested += OnNavigateToUrlRequested;
                vm.GoBack += () => { if (webView.CanGoBack) webView.GoBack(); };
                vm.GoForward += () => { if (webView.CanGoForward) webView.GoForward(); };
                vm.Refresh += () => webView.Reload();
                vm.Stop += () => webView.Stop();
            }

            if (e.OldValue is ViewModels.SimpleBrowserViewModel oldVm)
            {
                oldVm.NavigateToUrlRequested -= OnNavigateToUrlRequested;
            }
        }

        private void OnNavigateToUrlRequested(string url)
        {
            try
            {
                if (webView.CoreWebView2 != null)
                {
                    webView.CoreWebView2.Navigate(url);
                }
                else
                {
                    webView.Source = new Uri(url);
                }
            }
            catch { }
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            if (DataContext is ViewModels.SimpleBrowserViewModel vm)
            {
                vm.OnNavigationStarted();
            }
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (DataContext is ViewModels.SimpleBrowserViewModel vm && webView.CoreWebView2 != null)
            {
                vm.OnNavigationCompleted(
                    webView.CoreWebView2.Source,
                    webView.CoreWebView2.DocumentTitle);
                
                vm.CanGoBack = webView.CanGoBack;
                vm.CanGoForward = webView.CanGoForward;
            }
        }

        private void OnSourceChanged(object? sender, CoreWebView2SourceChangedEventArgs e)
        {
            if (DataContext is ViewModels.SimpleBrowserViewModel vm && webView.CoreWebView2 != null)
            {
                vm.CurrentUrl = webView.CoreWebView2.Source;
            }
        }
    }
}
