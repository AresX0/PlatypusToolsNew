using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

/// <summary>
/// Audio Player view with integrated visualizer supporting multiple visualization modes.
/// </summary>
public partial class AudioPlayerView : UserControl
{
    private AudioPlayerViewModel? _viewModel;
    
    public AudioPlayerView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }
    
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Initialize DataGrid columns for proper resizing
        if (this.FindName("LibraryTrackGrid") is DataGrid grid)
        {
            grid.UpdateLayout();
            foreach (var column in grid.Columns)
            {
                column.Width = double.NaN; // Auto-size
            }
            grid.UpdateLayout();
        }
        
        _viewModel = DataContext as AudioPlayerViewModel;
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            
            // Initialize audio visualizer
            InitializeVisualizer();
        }
    }
    
    private void InitializeVisualizer()
    {
        if (VisualizerControl != null)
        {
            // No special initialization needed - visualizer renders on its own
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioPlayerViewModel.SpectrumData))
        {
            // Feed spectrum data to the native visualizer
            if (VisualizerControl != null && _viewModel?.SpectrumData != null)
            {
                var specData = _viewModel.SpectrumData;
                string mode = _viewModel.VisualizerModeIndex switch
                {
                    0 => "Bars",
                    1 => "Mirror",
                    2 => "Waveform",
                    3 => "Circular",
                    _ => "Bars"
                };
                
                VisualizerControl.UpdateSpectrumData(specData, mode, _viewModel.BarCount);
            }
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.CurrentTrack))
        {
            // Track changed, visualizer will update automatically
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.Queue))
        {
            // Queue changed
        }
    }
    
// Old visualizer drawing methods replaced by integrated AudioVisualizerView
    // These methods are deprecated and kept only for reference
    
    /// <summary>
    /// Removes all selected tracks from the queue (multi-select support).
    /// </summary>
    private void OnRemoveSelectedClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        
        var selectedTracks = QueueListBox.SelectedItems
            .Cast<PlatypusTools.Core.Models.Audio.AudioTrack>()
            .ToList();
        
        if (selectedTracks.Count == 0)
        {
            MessageBox.Show("No tracks selected. Use Ctrl+Click or Shift+Click to select multiple tracks.",
                            "No Selection", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        foreach (var track in selectedTracks)
        {
            _viewModel.RemoveFromQueueCommand.Execute(track);
        }
    }
    
    /// <summary>
    /// Clears the entire queue.
    /// </summary>
    private void OnClearQueueClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel == null) return;
        
        if (_viewModel.Queue.Count == 0)
        {
            MessageBox.Show("Queue is already empty.", "Clear Queue", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var result = MessageBox.Show($"Remove all {_viewModel.Queue.Count} tracks from the queue?",
                                     "Clear Queue", MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ClearQueueCommand.Execute(null);
        }
    }
}