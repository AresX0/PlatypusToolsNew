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

        private void SystemRestoreView_Loaded(object sender, RoutedEventArgs e)
        {
            this.UpdateLayout();
            
            // Make all columns auto-size first to establish proper geometry
            if (this.FindName("DataGrid") is DataGrid grid)
            {
                grid.UpdateLayout();
                foreach (var column in grid.Columns)
                {
                    column.Width = double.NaN; // Auto-size
                }
                grid.UpdateLayout();
            }
        }
    }
}
