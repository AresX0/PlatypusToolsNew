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
            UpdatePlaceholderVisibility();
            UpdateEmptyQueueVisibility();
            
            // Initialize audio visualizer
            InitializeVisualizer();
        }
    }
    
    private void InitializeVisualizer()
    {
        if (VisualizerControl?.DataContext is AudioVisualizerViewModel vizViewModel)
        {
            // Initialize with default audio parameters (44100 Hz, 2 channels, 2048 samples)
            vizViewModel.Initialize(44100, 2, 2048);
        }
        else if (VisualizerControl != null)
        {
            // Create and set AudioVisualizerViewModel as DataContext
            var service = new PlatypusTools.Core.Services.AudioVisualizerService();
            var vizVM = new AudioVisualizerViewModel(service);
            vizVM.Initialize(44100, 2, 2048);
            VisualizerControl.DataContext = vizVM;
        }
    }
    
    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AudioPlayerViewModel.SpectrumData))
        {
            // Old visualizer code is replaced by integrated AudioVisualizerView
            // Dispatcher.Invoke(() => UpdateVisualizer(_viewModel?.SpectrumData ?? Array.Empty<double>()));
            
            // Update the integrated visualizer view
            if (VisualizerControl?.DataContext is AudioVisualizerViewModel vizViewModel && _viewModel?.SpectrumData != null)
            {
                var spectrumFloats = _viewModel.SpectrumData.Select(x => (float)x).ToArray();
                vizViewModel.UpdateAudioData(spectrumFloats, spectrumFloats.Length);
            }
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.CurrentTrack))
        {
            Dispatcher.Invoke(UpdatePlaceholderVisibility);
        }
        else if (e.PropertyName == nameof(AudioPlayerViewModel.Queue) || 
                 (sender is AudioPlayerViewModel vm && vm.Queue.Count == 0))
        {
            Dispatcher.Invoke(UpdateEmptyQueueVisibility);
        }
    }
    
    private void UpdatePlaceholderVisibility()
    {
        NoTrackPlaceholder.Visibility = _viewModel?.CurrentTrack == null 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }
    
    private void UpdateEmptyQueueVisibility()
    {
        EmptyQueueMessage.Visibility = (_viewModel?.Queue.Count ?? 0) == 0 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }
    
    
    // Old visualizer drawing methods replaced by integrated AudioVisualizerView
    // These methods are deprecated and kept only for reference
}
