using System.Windows;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Update notification and download window.
    /// </summary>
    public partial class UpdateWindow : Window
    {
        public UpdateWindow()
        {
            InitializeComponent();
        }

        public UpdateWindow(PlatypusTools.UI.Services.UpdateInfo updateInfo) : this()
        {
            if (DataContext is ViewModels.UpdateViewModel vm)
            {
                vm.UpdateAvailable = updateInfo;
            }
        }
    }
}
