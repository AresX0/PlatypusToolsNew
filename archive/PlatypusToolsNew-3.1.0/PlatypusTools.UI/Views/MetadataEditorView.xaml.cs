using System.Windows;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    public partial class MetadataEditorView : UserControl
    {
        public MetadataEditorView()
        {
            InitializeComponent();
            Loaded += MetadataEditorView_Loaded;
        }

        private void MetadataEditorView_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize both DataGrids for proper resizing
            if (this.FindName("MetadataGrid") is DataGrid metadataGrid)
            {
                metadataGrid.UpdateLayout();
                foreach (var column in metadataGrid.Columns)
                {
                    column.Width = double.NaN; // Auto-size
                }
                metadataGrid.UpdateLayout();
            }

            if (this.FindName("FolderFilesGrid") is DataGrid folderGrid)
            {
                folderGrid.UpdateLayout();
                foreach (var column in folderGrid.Columns)
                {
                    column.Width = double.NaN; // Auto-size
                }
                folderGrid.UpdateLayout();
            }
        }
    }
}
