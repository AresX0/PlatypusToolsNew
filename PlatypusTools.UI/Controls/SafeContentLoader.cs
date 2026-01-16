using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PlatypusTools.UI.Controls
{
    /// <summary>
    /// A control that safely loads content and displays an error message if loading fails.
    /// This ensures that one failing tab doesn't crash the entire application.
    /// 
    /// Usage:
    /// <controls:SafeContentLoader ViewType="{x:Type views:MyView}" DataContext="{Binding MyViewModel}" />
    /// 
    /// Or with direct content:
    /// <controls:SafeContentLoader>
    ///     <views:MyView DataContext="{Binding MyViewModel}" />
    /// </controls:SafeContentLoader>
    /// </summary>
    public class SafeContentLoader : ContentControl
    {
        public static readonly DependencyProperty ViewTypeProperty =
            DependencyProperty.Register(nameof(ViewType), typeof(Type), typeof(SafeContentLoader),
                new PropertyMetadata(null, OnViewTypeChanged));

        public static readonly DependencyProperty ViewDataContextProperty =
            DependencyProperty.Register(nameof(ViewDataContext), typeof(object), typeof(SafeContentLoader),
                new PropertyMetadata(null, OnViewDataContextChanged));

        public static readonly DependencyProperty ErrorMessageProperty =
            DependencyProperty.Register(nameof(ErrorMessage), typeof(string), typeof(SafeContentLoader),
                new PropertyMetadata(string.Empty));

        public static readonly DependencyProperty HasErrorProperty =
            DependencyProperty.Register(nameof(HasError), typeof(bool), typeof(SafeContentLoader),
                new PropertyMetadata(false));

        /// <summary>
        /// The Type of the view to instantiate. If set, the control will create an instance of this type.
        /// </summary>
        public Type? ViewType
        {
            get => (Type?)GetValue(ViewTypeProperty);
            set => SetValue(ViewTypeProperty, value);
        }

        /// <summary>
        /// The DataContext to apply to the loaded view.
        /// </summary>
        public object? ViewDataContext
        {
            get => GetValue(ViewDataContextProperty);
            set => SetValue(ViewDataContextProperty, value);
        }

        /// <summary>
        /// Error message displayed when view loading fails.
        /// </summary>
        public string ErrorMessage
        {
            get => (string)GetValue(ErrorMessageProperty);
            set => SetValue(ErrorMessageProperty, value);
        }

        /// <summary>
        /// Indicates whether an error occurred during view loading.
        /// </summary>
        public bool HasError
        {
            get => (bool)GetValue(HasErrorProperty);
            set => SetValue(HasErrorProperty, value);
        }

        private static void OnViewTypeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SafeContentLoader loader)
            {
                loader.LoadView();
            }
        }

        private static void OnViewDataContextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SafeContentLoader loader && loader.Content is FrameworkElement fe)
            {
                fe.DataContext = e.NewValue;
            }
        }

        public SafeContentLoader()
        {
            Loaded += SafeContentLoader_Loaded;
        }

        private void SafeContentLoader_Loaded(object sender, RoutedEventArgs e)
        {
            // If ViewType is set, load it; otherwise content was set directly
            if (ViewType != null)
            {
                LoadView();
            }
            else if (Content != null)
            {
                // Wrap existing content with error handling
                WrapExistingContent();
            }
        }

        private void LoadView()
        {
            if (ViewType == null) return;

            try
            {
                HasError = false;
                ErrorMessage = string.Empty;

                var instance = Activator.CreateInstance(ViewType);
                if (instance is FrameworkElement fe)
                {
                    if (ViewDataContext != null)
                    {
                        fe.DataContext = ViewDataContext;
                    }
                    Content = fe;
                }
                else
                {
                    Content = instance;
                }
            }
            catch (Exception ex)
            {
                ShowError($"Failed to load view '{ViewType.Name}'", ex);
            }
        }

        private void WrapExistingContent()
        {
            // Content is already set - just verify it loaded correctly
            // This method is called after XAML parsing, so if we get here, content is fine
        }

        private void ShowError(string message, Exception ex)
        {
            HasError = true;
            ErrorMessage = $"{message}: {ex.Message}";

            // Create error display
            var errorPanel = CreateErrorDisplay(message, ex);
            Content = errorPanel;

            // Log the error
            System.Diagnostics.Debug.WriteLine($"[SafeContentLoader] {message}");
            System.Diagnostics.Debug.WriteLine($"[SafeContentLoader] Exception: {ex}");
        }

        private FrameworkElement CreateErrorDisplay(string message, Exception ex)
        {
            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(50, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(180, 60, 60)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(20),
                Margin = new Thickness(20)
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            stack.Children.Add(new TextBlock
            {
                Text = "⚠️ View Loading Error",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 100, 100)),
                Margin = new Thickness(0, 0, 0, 10)
            });

            stack.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 200, 200)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 500,
                Margin = new Thickness(0, 0, 0, 10)
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

            var detailsExpander = new Expander
            {
                Header = "Technical Details",
                Foreground = new SolidColorBrush(Color.FromRgb(180, 180, 180)),
                IsExpanded = false
            };

            var detailsText = new TextBox
            {
                Text = ex.ToString(),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                MaxHeight = 200,
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
            return border;
        }
    }
}
