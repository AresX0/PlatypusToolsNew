using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    public partial class CveSearchView : UserControl
    {
        public CveSearchView()
        {
            InitializeComponent();
            if (DataContext == null)
            {
                DataContext = new CveSearchViewModel();
            }
        }
    }
}
