using System;
using System.Diagnostics;
using System.Windows.Controls;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for PlexBackupView.xaml
    /// </summary>
    public partial class PlexBackupView : UserControl
    {
        public PlexBackupView()
        {
            Debug.WriteLine("[PlexBackupView] Constructor started");
            try
            {
                Debug.WriteLine("[PlexBackupView] Calling InitializeComponent");
                InitializeComponent();
                Debug.WriteLine("[PlexBackupView] InitializeComponent completed successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PlexBackupView] ERROR in InitializeComponent: {ex}");
                throw; // Re-throw to let LazyTabContent handle it
            }
            Debug.WriteLine("[PlexBackupView] Constructor completed");
        }
    }
}
