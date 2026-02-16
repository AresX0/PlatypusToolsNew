using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using PlatypusTools.Core.Models.Video;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class TransitionPickerWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public TransitionPickerWindow()
        {
            InitializeComponent();
            DataContext = this;
            LoadData();
        }

        #region Properties

        public ObservableCollection<TransitionCategoryItem> Categories { get; } = new();
        public ObservableCollection<TransitionItem> AllTransitions { get; } = new();
        public ObservableCollection<TransitionItem> FilteredTransitions { get; } = new();
        public ObservableCollection<string> EasingOptions { get; } = new() { "Linear", "Ease In", "Ease Out", "Ease In-Out" };

        private TransitionCategoryItem? _selectedCategory;
        public TransitionCategoryItem? SelectedCategory
        {
            get => _selectedCategory;
            set
            {
                _selectedCategory = value;
                OnPropertyChanged(nameof(SelectedCategory));
                FilterTransitions();
            }
        }

        private TransitionItem? _selectedTransition;
        public TransitionItem? SelectedTransition
        {
            get => _selectedTransition;
            set
            {
                // Clear previous selection visual
                foreach (var t in AllTransitions)
                    t.IsSelected = false;
                
                _selectedTransition = value;
                if (_selectedTransition != null)
                    _selectedTransition.IsSelected = true;
                
                OnPropertyChanged(nameof(SelectedTransition));
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        private double _duration = 0.5;
        public double Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(nameof(Duration)); }
        }

        private string _selectedEasing = "Ease In-Out";
        public string SelectedEasing
        {
            get => _selectedEasing;
            set { _selectedEasing = value; OnPropertyChanged(nameof(SelectedEasing)); }
        }

        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set
            {
                _searchText = value;
                OnPropertyChanged(nameof(SearchText));
                FilterTransitions();
            }
        }

        public bool HasSelection => SelectedTransition != null;

        /// <summary>
        /// The result transition after the user clicks Apply.
        /// </summary>
        public Transition? ResultTransition { get; private set; }

        #endregion

        #region Methods

        private void LoadData()
        {
            // Load categories
            Categories.Add(new TransitionCategoryItem("All", "📋", TransitionCategory.Fade));
            Categories.Add(new TransitionCategoryItem("Fade", "🌫️", TransitionCategory.Fade));
            Categories.Add(new TransitionCategoryItem("Wipe", "↔️", TransitionCategory.Wipe));
            Categories.Add(new TransitionCategoryItem("Slide", "➡️", TransitionCategory.Slide));
            Categories.Add(new TransitionCategoryItem("Zoom", "🔍", TransitionCategory.Zoom));
            Categories.Add(new TransitionCategoryItem("3D", "🎲", TransitionCategory.ThreeD));
            Categories.Add(new TransitionCategoryItem("Blur", "💨", TransitionCategory.Blur));

            // Load transitions
            var transitions = new List<(TransitionType type, string icon)>
            {
                (TransitionType.FadeIn, "🌅"),
                (TransitionType.FadeOut, "🌆"),
                (TransitionType.CrossDissolve, "✨"),
                (TransitionType.DipToBlack, "⬛"),
                (TransitionType.DipToWhite, "⬜"),
                (TransitionType.WipeLeft, "⬅️"),
                (TransitionType.WipeRight, "➡️"),
                (TransitionType.WipeUp, "⬆️"),
                (TransitionType.WipeDown, "⬇️"),
                (TransitionType.WipeCircle, "⭕"),
                (TransitionType.SlideLeft, "📤"),
                (TransitionType.SlideRight, "📥"),
                (TransitionType.SlideUp, "📈"),
                (TransitionType.SlideDown, "📉"),
                (TransitionType.Push, "👆"),
                (TransitionType.ZoomIn, "🔎"),
                (TransitionType.ZoomOut, "🔍"),
                (TransitionType.CrossZoom, "🎯"),
                (TransitionType.Cube, "🧊"),
                (TransitionType.Flip, "🔄"),
                (TransitionType.PageCurl, "📄"),
                (TransitionType.Rotate, "🔁"),
                (TransitionType.GaussianBlur, "💫"),
                (TransitionType.MotionBlur, "💨"),
                (TransitionType.RadialBlur, "🌀"),
            };

            foreach (var (type, icon) in transitions)
            {
                var transition = TransitionFactory.Create(type);
                AllTransitions.Add(new TransitionItem(transition, icon));
            }

            SelectedCategory = Categories.First();
        }

        private void FilterTransitions()
        {
            FilteredTransitions.Clear();

            foreach (var t in AllTransitions)
            {
                // Filter by category
                bool categoryMatch = SelectedCategory?.Name == "All" || t.Category == SelectedCategory?.Category;

                // Filter by search text
                bool searchMatch = string.IsNullOrWhiteSpace(SearchText) ||
                                   t.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

                if (categoryMatch && searchMatch)
                {
                    FilteredTransitions.Add(t);
                }
            }
        }

        private void Transition_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is TransitionItem item)
            {
                SelectedTransition = item;
            }
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTransition != null)
            {
                ResultTransition = new Transition
                {
                    Type = SelectedTransition.TransitionType,
                    Name = SelectedTransition.Name,
                    Category = SelectedTransition.Category,
                    Duration = TimeSpan.FromSeconds(Duration),
                    Easing = ParseEasing(SelectedEasing)
                };
                DialogResult = true;
                Close();
            }
        }

        private EasingType ParseEasing(string easing) => easing switch
        {
            "Linear" => EasingType.Linear,
            "Ease In" => EasingType.EaseIn,
            "Ease Out" => EasingType.EaseOut,
            "Ease In-Out" => EasingType.EaseInOut,
            _ => EasingType.EaseInOut
        };

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void PlayPreview_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTransition == null) return;
            
            // Reset state
            PreviewAfter.Opacity = 0;
            AfterTranslateTransform.X = 0;
            AfterTranslateTransform.Y = 0;
            AfterScaleTransform.ScaleX = 1;
            AfterScaleTransform.ScaleY = 1;
            
            var duration = TimeSpan.FromSeconds(Duration);
            var easeFunc = GetEasingFunction(SelectedEasing);
            
            var storyboard = new Storyboard();
            
            // Create animation based on transition type
            switch (SelectedTransition.TransitionType)
            {
                case TransitionType.FadeIn:
                case TransitionType.FadeOut:
                case TransitionType.CrossDissolve:
                case TransitionType.DipToBlack:
                case TransitionType.DipToWhite:
                    // Fade animation
                    var fadeAnim = new DoubleAnimation(0, 1, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(fadeAnim, PreviewAfter);
                    Storyboard.SetTargetProperty(fadeAnim, new PropertyPath(OpacityProperty));
                    storyboard.Children.Add(fadeAnim);
                    break;
                    
                case TransitionType.WipeLeft:
                case TransitionType.SlideLeft:
                case TransitionType.Push:
                    // Slide from right
                    PreviewAfter.Opacity = 1;
                    AfterTranslateTransform.X = 200;
                    var slideLeftAnim = new DoubleAnimation(200, 0, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(slideLeftAnim, PreviewAfter);
                    Storyboard.SetTargetProperty(slideLeftAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
                    storyboard.Children.Add(slideLeftAnim);
                    break;
                    
                case TransitionType.WipeRight:
                case TransitionType.SlideRight:
                    // Slide from left
                    PreviewAfter.Opacity = 1;
                    AfterTranslateTransform.X = -200;
                    var slideRightAnim = new DoubleAnimation(-200, 0, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(slideRightAnim, PreviewAfter);
                    Storyboard.SetTargetProperty(slideRightAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
                    storyboard.Children.Add(slideRightAnim);
                    break;
                    
                case TransitionType.WipeUp:
                case TransitionType.SlideUp:
                    // Slide from bottom
                    PreviewAfter.Opacity = 1;
                    AfterTranslateTransform.Y = 120;
                    var slideUpAnim = new DoubleAnimation(120, 0, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(slideUpAnim, PreviewAfter);
                    Storyboard.SetTargetProperty(slideUpAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
                    storyboard.Children.Add(slideUpAnim);
                    break;
                    
                case TransitionType.WipeDown:
                case TransitionType.SlideDown:
                    // Slide from top
                    PreviewAfter.Opacity = 1;
                    AfterTranslateTransform.Y = -120;
                    var slideDownAnim = new DoubleAnimation(-120, 0, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(slideDownAnim, PreviewAfter);
                    Storyboard.SetTargetProperty(slideDownAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
                    storyboard.Children.Add(slideDownAnim);
                    break;
                    
                case TransitionType.ZoomIn:
                case TransitionType.CrossZoom:
                    // Zoom in from center
                    PreviewAfter.Opacity = 1;
                    AfterScaleTransform.ScaleX = 0.1;
                    AfterScaleTransform.ScaleY = 0.1;
                    var zoomInX = new DoubleAnimation(0.1, 1, duration) { EasingFunction = easeFunc };
                    var zoomInY = new DoubleAnimation(0.1, 1, duration) { EasingFunction = easeFunc };
                    var zoomFade = new DoubleAnimation(0, 1, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(zoomInX, PreviewAfter);
                    Storyboard.SetTargetProperty(zoomInX, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
                    Storyboard.SetTarget(zoomInY, PreviewAfter);
                    Storyboard.SetTargetProperty(zoomInY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
                    storyboard.Children.Add(zoomInX);
                    storyboard.Children.Add(zoomInY);
                    break;
                    
                case TransitionType.ZoomOut:
                    // Zoom out from large
                    PreviewAfter.Opacity = 1;
                    AfterScaleTransform.ScaleX = 2;
                    AfterScaleTransform.ScaleY = 2;
                    var zoomOutX = new DoubleAnimation(2, 1, duration) { EasingFunction = easeFunc };
                    var zoomOutY = new DoubleAnimation(2, 1, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(zoomOutX, PreviewAfter);
                    Storyboard.SetTargetProperty(zoomOutX, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
                    Storyboard.SetTarget(zoomOutY, PreviewAfter);
                    Storyboard.SetTargetProperty(zoomOutY, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
                    storyboard.Children.Add(zoomOutX);
                    storyboard.Children.Add(zoomOutY);
                    break;
                    
                default:
                    // Default fade for other transitions
                    var defaultFade = new DoubleAnimation(0, 1, duration) { EasingFunction = easeFunc };
                    Storyboard.SetTarget(defaultFade, PreviewAfter);
                    Storyboard.SetTargetProperty(defaultFade, new PropertyPath(OpacityProperty));
                    storyboard.Children.Add(defaultFade);
                    break;
            }
            
            storyboard.Begin();
        }
        
        private IEasingFunction? GetEasingFunction(string easing)
        {
            return easing switch
            {
                "Ease In" => new CubicEase { EasingMode = EasingMode.EaseIn },
                "Ease Out" => new CubicEase { EasingMode = EasingMode.EaseOut },
                "Ease In-Out" => new CubicEase { EasingMode = EasingMode.EaseInOut },
                _ => null // Linear
            };
        }

        #endregion
    }

    #region Helper Classes

    public class TransitionCategoryItem
    {
        public string Name { get; }
        public string Icon { get; }
        public TransitionCategory Category { get; }

        public TransitionCategoryItem(string name, string icon, TransitionCategory category)
        {
            Name = name;
            Icon = icon;
            Category = category;
        }
    }

    public class TransitionItem : BindableBase
    {
        public string Name { get; }
        public string Icon { get; }
        public TransitionCategory Category { get; }
        public TransitionType TransitionType { get; }
        public string CategoryName => Category.ToString();

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
                OnPropertyChanged(nameof(BorderBrush));
                OnPropertyChanged(nameof(Background));
            }
        }

        public Brush BorderBrush => IsSelected ? Brushes.DodgerBlue : Brushes.LightGray;
        public Brush Background => IsSelected ? new SolidColorBrush(Color.FromRgb(235, 245, 255)) : Brushes.White;

        public TransitionItem(Transition transition, string icon)
        {
            Name = transition.Name;
            Icon = icon;
            Category = transition.Category;
            TransitionType = transition.Type;
        }
    }

    #endregion
}
