using System.Windows;
using System.Windows.Controls;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

public partial class PrivacyCleanerView : UserControl
{
    public PrivacyCleanerView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Selects all data source category checkboxes.
    /// </summary>
    private void OnSelectAllCategoriesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrivacyCleanerViewModel vm)
        {
            // Browsers
            vm.BrowserChrome = true;
            vm.BrowserEdge = true;
            vm.BrowserFirefox = true;
            vm.BrowserBrave = true;
            
            // Cloud Services
            vm.CloudOneDrive = true;
            vm.CloudGoogle = true;
            vm.CloudDropbox = true;
            vm.CloudiCloud = true;
            
            // Windows Identity
            vm.WindowsRecentDocs = true;
            vm.WindowsJumpLists = true;
            vm.WindowsExplorerHistory = true;
            vm.WindowsClipboard = true;
            
            // Application Data
            vm.ApplicationOffice = true;
            vm.ApplicationAdobe = true;
            vm.ApplicationMediaPlayers = true;
        }
    }

    /// <summary>
    /// Clears all data source category checkboxes.
    /// </summary>
    private void OnClearAllCategoriesClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is PrivacyCleanerViewModel vm)
        {
            // Browsers
            vm.BrowserChrome = false;
            vm.BrowserEdge = false;
            vm.BrowserFirefox = false;
            vm.BrowserBrave = false;
            
            // Cloud Services
            vm.CloudOneDrive = false;
            vm.CloudGoogle = false;
            vm.CloudDropbox = false;
            vm.CloudiCloud = false;
            
            // Windows Identity
            vm.WindowsRecentDocs = false;
            vm.WindowsJumpLists = false;
            vm.WindowsExplorerHistory = false;
            vm.WindowsClipboard = false;
            
            // Application Data
            vm.ApplicationOffice = false;
            vm.ApplicationAdobe = false;
            vm.ApplicationMediaPlayers = false;
        }
    }
}
