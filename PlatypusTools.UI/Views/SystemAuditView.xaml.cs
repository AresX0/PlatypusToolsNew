using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class SystemAuditView : UserControl
    {
        public SystemAuditView()
        {
            InitializeComponent();
            Loaded += SystemAuditView_Loaded;
        }

        private void SystemAuditView_Loaded(object sender, RoutedEventArgs e)
        {
            // Force layout measurement so column resizers are interactive even when empty
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
