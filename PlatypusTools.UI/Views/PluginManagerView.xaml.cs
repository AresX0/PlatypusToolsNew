using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class PluginManagerView : UserControl
    {
        public PluginManagerView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Trigger async initialization when view is first loaded
            if (DataContext is IAsyncInitializable asyncInit && !asyncInit.IsInitialized)
            {
                await asyncInit.InitializeAsync();
            }
        }
    }
}
