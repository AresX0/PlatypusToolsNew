using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.Core.Models.Audio;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

/// <summary>
/// Interaction logic for AudioLibraryView.xaml
/// </summary>
public partial class AudioLibraryView : UserControl
{
    public AudioLibraryView()
    {
        InitializeComponent();
    }
    
    private void TrackRow_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row && row.DataContext is AudioTrack track)
        {
            if (DataContext is AudioLibraryViewModel vm && vm.PlayTrackCommand.CanExecute(track))
            {
                vm.PlayTrackCommand.Execute(track);
            }
        }
    }
}
