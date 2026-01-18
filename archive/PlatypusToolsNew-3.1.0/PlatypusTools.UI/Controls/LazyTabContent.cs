using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// Lazy-loads a view when the tab becomes visible for the first time.
    /// Provides error isolation so one failing view doesn't crash the entire app.
    /// 
    /// Usage in XAML:
    /// <TabItem Header="My Tab">
    ///     <controls:LazyTabContent ViewType="{x:Type views:MyView}" 
    ///                               ViewDataContext="{Binding MyViewModel}" />
    /// </TabItem>
    /// 
    /// Benefits:
    /// - Views are only created when first viewed (faster startup)
    /// - Errors in one tab don't prevent other tabs from loading
    /// - Error messages are shown inline instead of crashing the app
    /// </summary>
    public class LazyTabContent : ContentControl
    {
        private bool _isLoaded;
        private bool _isLoading;

        public static readonly DependencyProperty ViewTypeProperty =
            DependencyProperty.Register(nameof(ViewType), typeof(Type), typeof(LazyTabContent),
                new PropertyMetadata(null));

        public static readonly DependencyProperty ViewDataContextProperty =
            DependencyProperty.Register(nameof(ViewDataContext), typeof(object), typeof(LazyTabContent),
                new PropertyMetadata(null));

        /// <summary>
        /// The Type of the UserControl/View to instantiate when this tab becomes visible.
        /// </summary>
        public Type? ViewType
        {
            get => (Type?)GetValue(ViewTypeProperty);
            set => SetValue(ViewTypeProperty, value);
        }

        /// <summary>
        /// The DataContext to assign to the view once loaded.
        /// </summary>
        public object? ViewDataContext
        {
            get => GetValue(ViewDataContextProperty);
            set => SetValue(ViewDataContextProperty, value);
        }

        public LazyTabContent()
        {
            // Show loading placeholder initially
            Content = CreateLoadingPlaceholder();
            
            // Subscribe to visibility changes
            IsVisibleChanged += OnVisibilityChanged;
        }

        private void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if ((bool)e.NewValue && !_isLoaded && !_isLoading)
            {
                LoadViewAsync();
            }
        }

        private async void LoadViewAsync()
        {
            if (_isLoaded || _isLoading || ViewType == null) return;

            _isLoading = true;

            try
            {
                // Small delay to allow UI to render loading state
                await System.Threading.Tasks.Task.Delay(10);

                // Create view on UI thread
                Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        var view = Activator.CreateInstance(ViewType);
                        
                        if (view is FrameworkElement fe)
                        {
                            if (ViewDataContext != null)
                            {
                                fe.DataContext = ViewDataContext;
                            }
                            
                            // Wrap in ScrollViewer for consistency
                            var scrollViewer = new ScrollViewer
                            {
                                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                                Content = fe
                            };
                            
                            Content = scrollViewer;
                        }
                        else
                        {
                            Content = view;
                        }
                        
                        _isLoaded = true;
                    }
                    catch (Exception ex)
                    {
                        Content = CreateErrorDisplay(ex);
                        _isLoaded = true; // Mark as loaded so we don't retry
                    }
                });
            }
            catch (Exception ex)
            {
                Content = CreateErrorDisplay(ex);
                _isLoaded = true;
            }
            finally
            {
                _isLoading = false;
            }
        }

        private FrameworkElement CreateLoadingPlaceholder()
        {
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            stack.Children.Add(new TextBlock
            {
                Text = "⏳",
                FontSize = 32,
                HorizontalAlignment = HorizontalAlignment.Center
            });

            stack.Children.Add(new TextBlock
            {
                Text = "Loading...",
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.Gray),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 10, 0, 0)
            });

            return stack;
        }

        private FrameworkElement CreateErrorDisplay(Exception ex)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(50, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(20),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = "⚠️ Failed to Load View",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            stack.Children.Add(new TextBlock
            {
                Text = $"View: {ViewType?.Name ?? "Unknown"}",
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 200)),
                Margin = new Thickness(0, 0, 0, 5)
            });

            stack.Children.Add(new TextBlock
            {
                Text = ex.Message,
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(200, 150, 150)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Margin = new Thickness(0, 0, 0, 15)
            });

            // Add retry button
            var retryButton = new Button
            {
                Content = "🔄 Retry",
                Padding = new Thickness(15, 8, 15, 8),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            retryButton.Click += (s, e) =>
            {
                _isLoaded = false;
                _isLoading = false;
                Content = CreateLoadingPlaceholder();
                LoadViewAsync();
            };
            stack.Children.Add(retryButton);

            // Add expandable technical details
            var detailsExpander = new Expander
            {
                Header = "Technical Details",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                IsExpanded = false,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var detailsText = new TextBox
            {
                Text = ex.ToString(),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 200,
                MaxWidth = 500,
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                BorderThickness = new Thickness(0),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            detailsExpander.Content = detailsText;
            stack.Children.Add(detailsExpander);

            border.Child = stack;

            // Log error for diagnostics
            System.Diagnostics.Debug.WriteLine($"[LazyTabContent] Failed to load {ViewType?.Name}: {ex}");

            return border;
        }
    }
}
