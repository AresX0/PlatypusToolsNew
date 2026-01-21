using System.Windows;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class StagingWindow : Window
    {
        public StagingWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Trigger async initialization when window is first loaded
            if (DataContext is IAsyncInitializable asyncInit && !asyncInit.IsInitialized)
            {
                await asyncInit.InitializeAsync();
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}