using PlatypusTools.Core.Services;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// ViewModel for audio visualization
    /// 
    /// Features:
    /// - Real-time spectrum and waveform rendering
    /// - Preset selection and switching
    /// - Visualizer settings
    /// - Integration with audio player for live audio data
    /// </summary>
    public class AudioVisualizerViewModel : BindableBase
    {
        private readonly IAudioVisualizerService _visualizerService;

        public AudioVisualizerViewModel(IAudioVisualizerService visualizerService)
        {
            _visualizerService = visualizerService ?? new AudioVisualizerService();
            InitializePresets();
            InitializeCommands();
        }

        #region Properties

        private ObservableCollection<string> _availablePresets = new();
        public ObservableCollection<string> AvailablePresets
        {
            get => _availablePresets;
            set => SetProperty(ref _availablePresets, value);
        }

        private string _selectedPreset = "Default";
        public string SelectedPreset
        {
            get => _selectedPreset;
            set => SetProperty(ref _selectedPreset, value);
        }

        private bool _isVisualizerEnabled = true;
        public bool IsVisualizerEnabled
        {
            get => _isVisualizerEnabled;
            set
            {
                SetProperty(ref _isVisualizerEnabled, value);
                _visualizerService.SetEnabled(value);
            }
        }

        private float[] _spectrumData = new float[64];
        public float[] SpectrumData
        {
            get => _spectrumData;
            set => SetProperty(ref _spectrumData, value);
        }

        private float[] _waveformData = new float[2048];
        public float[] WaveformData
        {
            get => _waveformData;
            set => SetProperty(ref _waveformData, value);
        }

        private double _visualizerOpacity = 0.9;
        public double VisualizerOpacity
        {
            get => _visualizerOpacity;
            set => SetProperty(ref _visualizerOpacity, value);
        }

        private int _barCount = 32;
        public int BarCount
        {
            get => _barCount;
            set => SetProperty(ref _barCount, value);
        }

        #endregion

        #region Commands

        public ICommand ToggleVisualizerCommand { get; private set; }
        public ICommand SelectPresetCommand { get; private set; }

        private void InitializeCommands()
        {
            ToggleVisualizerCommand = new RelayCommand(_ => ToggleVisualizer());
            SelectPresetCommand = new RelayCommand(param => SelectPreset(param as string));
        }

        private void ToggleVisualizer()
        {
            IsVisualizerEnabled = !IsVisualizerEnabled;
        }

        private void SelectPreset(string presetName)
        {
            if (!string.IsNullOrEmpty(presetName))
            {
                _visualizerService.LoadPreset(presetName);
                SelectedPreset = presetName;
            }
        }

        #endregion

        #region Methods

        private void InitializePresets()
        {
            var presets = _visualizerService.GetAvailablePresets();
            foreach (var preset in presets)
            {
                AvailablePresets.Add(preset);
            }
        }

        /// <summary>
        /// Update visualizer with new audio data (called from audio player)
        /// </summary>
        public void UpdateAudioData(float[] samples, int length)
        {
            _visualizerService.UpdateAudioSamples(samples, length);
            
            // Update UI data
            SpectrumData = _visualizerService.GetSpectrumData();
            WaveformData = _visualizerService.GetWaveformData();
        }

        /// <summary>
        /// Initialize visualizer with audio parameters
        /// </summary>
        public void Initialize(int sampleRate, int channels, int bufferSize)
        {
            _visualizerService.Initialize(sampleRate, channels, bufferSize);
        }

        #endregion
    }
}

