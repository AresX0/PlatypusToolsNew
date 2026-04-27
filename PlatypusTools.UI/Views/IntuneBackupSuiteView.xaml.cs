using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

/// <summary>
/// Interaction logic for IntuneBackupSuiteView.xaml
/// </summary>
public partial class IntuneBackupSuiteView : UserControl
{
    public IntuneBackupSuiteView()
    {
        InitializeComponent();

        if (DataContext == null)
        {
            DataContext = new IntuneBackupSuiteViewModel();
        }
    }
}
