using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using PlatypusTools.Core.Models.Video;

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

    public class TransitionItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

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
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(BorderBrush)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Background)));
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
