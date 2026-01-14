using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace PlatypusTools.UI.Views
{
    public partial class RecentCleanupView : UserControl
    {
        public RecentCleanupView()
        {
            InitializeComponent();
            Loaded += RecentCleanupView_Loaded;
        }

        private void RecentCleanupView_Loaded(object sender, RoutedEventArgs e)
        {
            // Force layout measurement so column resizers are interactive even when empty
            if (this.FindName("ResultsGrid") is DataGrid grid)
            {
                // Force full layout pass
                grid.UpdateLayout();
                
                // Make all columns auto-size first to establish proper geometry
                foreach (var column in grid.Columns)
                {
                    column.Width = double.NaN; // Auto-size
                }
                
                // Force another layout pass after column updates
                grid.UpdateLayout();
            }
        }
    }
}