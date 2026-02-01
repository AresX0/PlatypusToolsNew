using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for IntunePackagerView.xaml
    /// GUI wrapper for Microsoft Win32 Content Prep Tool (IntuneWinAppUtil.exe)
    /// </summary>
    public partial class IntunePackagerView : UserControl
    {
        public IntunePackagerView()
        {
            InitializeComponent();
            
            // Set DataContext if not already bound
            if (DataContext == null)
            {
                DataContext = new IntunePackagerViewModel();
            }
        }
    }
}
