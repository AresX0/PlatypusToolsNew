using System;
using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.Controls
{
    public partial class QuickActionsBar : UserControl
    {
        public static readonly DependencyProperty CurrentPathProperty = 
            DependencyProperty.Register(nameof(CurrentPath), typeof(string), typeof(QuickActionsBar));
            
        public static readonly DependencyProperty SearchTextProperty = 
            DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(QuickActionsBar));

        public string? CurrentPath
        {
            get => (string?)GetValue(CurrentPathProperty);
            set => SetValue(CurrentPathProperty, value);
        }

        public string? SearchText
        {
            get => (string?)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        public event EventHandler? OpenRequested;
        public event EventHandler? RefreshRequested;
        public event EventHandler? UndoRequested;
        public event EventHandler? RedoRequested;
        public event EventHandler? SelectAllRequested;
        public event EventHandler? DeselectAllRequested;
        public event EventHandler<string>? SearchRequested;

        public QuickActionsBar()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void Open_Click(object sender, RoutedEventArgs e) => OpenRequested?.Invoke(this, EventArgs.Empty);
        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        private void Undo_Click(object sender, RoutedEventArgs e) => UndoRequested?.Invoke(this, EventArgs.Empty);
        private void Redo_Click(object sender, RoutedEventArgs e) => RedoRequested?.Invoke(this, EventArgs.Empty);
        private void SelectAll_Click(object sender, RoutedEventArgs e) => SelectAllRequested?.Invoke(this, EventArgs.Empty);
        private void DeselectAll_Click(object sender, RoutedEventArgs e) => DeselectAllRequested?.Invoke(this, EventArgs.Empty);
        
        private void Search_Click(object sender, RoutedEventArgs e)
        {
            SearchRequested?.Invoke(this, SearchText ?? "");
        }

        private void ClearSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchText = "";
            SearchRequested?.Invoke(this, "");
        }
    }
}
