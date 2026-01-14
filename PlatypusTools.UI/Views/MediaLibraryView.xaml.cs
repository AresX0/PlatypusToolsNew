using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class MediaLibraryView : UserControl
    {
        public MediaLibraryView()
        {
            InitializeComponent();
            Loaded += MediaLibraryView_Loaded;
        }

        private void MediaLibraryView_Loaded(object sender, RoutedEventArgs e)
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
