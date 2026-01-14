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
        Loaded += AudioLibraryView_Loaded;
    }

    private void AudioLibraryView_Loaded(object sender, RoutedEventArgs e)
    {
        // Force layout measurement so column resizers are interactive even when empty
        if (this.FindName("TrackDataGrid") is DataGrid grid)
        {
            grid.UpdateLayout();
            // Make all columns auto-size first to establish proper geometry
            foreach (var column in grid.Columns)
            {
                column.Width = double.NaN; // Auto-size
            }
            grid.UpdateLayout();
        }
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
