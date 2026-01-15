using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class SystemRestoreView : UserControl
    {
        public SystemRestoreView()
        {
            InitializeComponent();
            Loaded += SystemRestoreView_Loaded;
        }

        private async void SystemRestoreView_Loaded(object sender, RoutedEventArgs e)
        {
            this.UpdateLayout();
            if (DataContext is ViewModels.SystemRestoreViewModel vm)
            {
                await vm.RefreshAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SystemRestoreViewModel vm)
            {
                await vm.RefreshAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnCreatePointClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SystemRestoreViewModel vm)
            {
                vm.NewPointDescription = NewPointNameTextBox.Text;
                await vm.CreatePointAsync();
                RefreshUI(vm);
            }
        }
        
        private async void OnRestoreClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SystemRestoreViewModel vm)
            {
                await vm.RestoreAsync();
            }
        }
        
        private async void OnDeleteClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.SystemRestoreViewModel vm)
            {
                await vm.DeletePointAsync();
                RefreshUI(vm);
            }
        }
        
        private void RefreshUI(ViewModels.SystemRestoreViewModel vm)
        {
            TotalPointsText.Text = vm.TotalPoints.ToString();
            RestorePointsDataGrid.ItemsSource = null;
            RestorePointsDataGrid.ItemsSource = vm.RestorePoints;
        }
    }
}
