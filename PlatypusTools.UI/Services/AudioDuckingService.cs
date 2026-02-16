using System;
using System.Runtime.CompilerServices;
using NAudio.Wave;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Audio ducking configuration.
    /// </summary>
    public class DuckingConfig : BindableBase
    {
        private bool _isEnabled = true;
        private float _threshold = -20f; // dB
        private float _reduction = -12f; // dB
        private float _attackMs = 50f;
        private float _holdMs = 100f;
        private float _releaseMs = 300f;
        private float _ratio = 4f;

        /// <summary>
        /// Whether ducking is enabled.
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Threshold level in dB above which ducking activates.
        /// </summary>
        public float ThresholdDB
        {
            get => _threshold;
            set { _threshold = Math.Clamp(value, -60f, 0f); OnPropertyChanged(); }
        }

        /// <summary>
        /// Amount of gain reduction in dB when ducking is active.
        /// </summary>
        public float ReductionDB
        {
            get => _reduction;
            set { _reduction = Math.Clamp(value, -40f, 0f); OnPropertyChanged(); }
        }

        /// <summary>
        /// Attack time in milliseconds.
        /// </summary>
        public float AttackMs
        {
            get => _attackMs;
            set { _attackMs = Math.Clamp(value, 1f, 500f); OnPropertyChanged(); }
        }

        /// <summary>
        /// Hold time in milliseconds.
        /// </summary>
        public float HoldMs
        {
            get => _holdMs;
            set { _holdMs = Math.Clamp(value, 0f, 1000f); OnPropertyChanged(); }
        }

        /// <summary>
        /// Release time in milliseconds.
        /// </summary>
        public float ReleaseMs
        {
            get => _releaseMs;
            set { _releaseMs = Math.Clamp(value, 10f, 2000f); OnPropertyChanged(); }
        }

        /// <summary>
        /// Compression ratio (4:1 means for every 4dB above threshold, output increases 1dB).
        /// </summary>
        public float Ratio
        {
            get => _ratio;
            set { _ratio = Math.Clamp(value, 1f, 20f); OnPropertyChanged(); }
        }

        /// <summary>
        /// Threshold as linear value.
        /// </summary>
        public float ThresholdLinear => (float)Math.Pow(10, _threshold / 20.0);

        /// <summary>
        /// Reduction as linear gain multiplier.
        /// </summary>
        public float ReductionLinear => (float)Math.Pow(10, _reduction / 20.0);
    }

    /// <summary>
    /// ISampleProvider that implements audio ducking.
    /// Automatically lowers the volume of music when voice is detected.
    /// </summary>
    public class AudioDuckingSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly ISampleProvider _sidechain;
        private readonly DuckingConfig _config;
        
        private float _currentGain = 1.0f;
        private float _targetGain = 1.0f;
        private float _envelope;
        private int _holdCounter;
        private float _attackCoeff;
        private float _releaseCoeff;

        /// <summary>
        /// Creates an audio ducking provider.
        /// </summary>
        /// <param name="source">The audio source to duck (e.g., music).</param>
        /// <param name="sidechain">The audio source that triggers ducking (e.g., voice).</param>
        /// <param name="config">Ducking configuration.</param>
        public AudioDuckingSampleProvider(ISampleProvider source, ISampleProvider sidechain, DuckingConfig? config = null)
        {
            _source = source;
            _sidechain = sidechain;
            _config = config ?? new DuckingConfig();

            if (_source.WaveFormat.SampleRate != _sidechain.WaveFormat.SampleRate)
                throw new ArgumentException("Source and sidechain must have the same sample rate.");

            UpdateCoefficients();
            _config.PropertyChanged += (s, e) => UpdateCoefficients();
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        /// <summary>
        /// Gets the current ducking configuration.
        /// </summary>
        public DuckingConfig Config => _config;

        /// <summary>
        /// Gets the current gain reduction in dB.
        /// </summary>
        public double CurrentGainReductionDB => 20.0 * Math.Log10(_currentGain);

        /// <summary>
        /// Gets whether ducking is currently active.
        /// </summary>
        public bool IsDucking => _currentGain < 0.99f;

        private void UpdateCoefficients()
        {
            var sampleRate = WaveFormat.SampleRate;
            _attackCoeff = (float)Math.Exp(-1.0 / (sampleRate * _config.AttackMs / 1000.0));
            _releaseCoeff = (float)Math.Exp(-1.0 / (sampleRate * _config.ReleaseMs / 1000.0));
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // Read from source
            int samplesRead = _source.Read(buffer, offset, count);
            if (samplesRead == 0) return 0;

            if (!_config.IsEnabled)
                return samplesRead;

            // Read sidechain signal
            float[] sidechainBuffer = new float[count];
            int sidechainRead = _sidechain.Read(sidechainBuffer, 0, count);

            // Process each sample
            int channels = WaveFormat.Channels;
            for (int i = 0; i < samplesRead; i += channels)
            {
                // Detect sidechain level (peak of all channels)
                float sidechainPeak = 0;
                for (int ch = 0; ch < channels && i + ch < sidechainRead; ch++)
                {
                    sidechainPeak = Math.Max(sidechainPeak, Math.Abs(sidechainBuffer[i + ch]));
                }

                // Envelope follower
                if (sidechainPeak > _envelope)
                {
                    _envelope = _attackCoeff * _envelope + (1 - _attackCoeff) * sidechainPeak;
                }
                else
                {
                    _envelope = _releaseCoeff * _envelope + (1 - _releaseCoeff) * sidechainPeak;
                }

                // Determine if we should duck
                if (_envelope > _config.ThresholdLinear)
                {
                    _targetGain = _config.ReductionLinear;
                    _holdCounter = (int)(WaveFormat.SampleRate * _config.HoldMs / 1000.0);
                }
                else if (_holdCounter > 0)
                {
                    _holdCounter--;
                }
                else
                {
                    _targetGain = 1.0f;
                }

                // Smooth gain transitions
                if (_currentGain < _targetGain)
                {
                    // Release (restore volume)
                    _currentGain = _releaseCoeff * _currentGain + (1 - _releaseCoeff) * _targetGain;
                }
                else
                {
                    // Attack (reduce volume)
                    _currentGain = _attackCoeff * _currentGain + (1 - _attackCoeff) * _targetGain;
                }

                // Apply gain to all channels
                for (int ch = 0; ch < channels && offset + i + ch < buffer.Length; ch++)
                {
                    buffer[offset + i + ch] *= _currentGain;
                }
            }

            return samplesRead;
        }
    }

    /// <summary>
    /// Service for managing audio ducking in the application.
    /// </summary>
    public class AudioDuckingService : BindableBase
    {
        private static readonly Lazy<AudioDuckingService> _instance = new(() => new AudioDuckingService());
        public static AudioDuckingService Instance => _instance.Value;

        private readonly DuckingConfig _globalConfig = new();
        private bool _isDuckingActive;
        private double _currentReduction;

        public event EventHandler<bool>? DuckingStateChanged;

        private AudioDuckingService() { }

        /// <summary>
        /// Global ducking configuration.
        /// </summary>
        public DuckingConfig GlobalConfig => _globalConfig;

        /// <summary>
        /// Whether ducking is currently active.
        /// </summary>
        public bool IsDuckingActive
        {
            get => _isDuckingActive;
            private set
            {
                if (_isDuckingActive != value)
                {
                    _isDuckingActive = value;
                    OnPropertyChanged();
                    DuckingStateChanged?.Invoke(this, value);
                }
            }
        }

        /// <summary>
        /// Current gain reduction in dB.
        /// </summary>
        public double CurrentReductionDB
        {
            get => _currentReduction;
            private set
            {
                _currentReduction = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Creates a ducking provider for the given audio sources.
        /// </summary>
        /// <param name="musicSource">The music source to be ducked.</param>
        /// <param name="voiceSource">The voice source that triggers ducking.</param>
        /// <param name="customConfig">Optional custom configuration (uses global config if null).</param>
        public AudioDuckingSampleProvider CreateDuckingProvider(
            ISampleProvider musicSource, 
            ISampleProvider voiceSource,
            DuckingConfig? customConfig = null)
        {
            var config = customConfig ?? _globalConfig;
            var provider = new AudioDuckingSampleProvider(musicSource, voiceSource, config);
            return provider;
        }

        /// <summary>
        /// Updates ducking state from an active provider.
        /// </summary>
        public void UpdateState(AudioDuckingSampleProvider provider)
        {
            IsDuckingActive = provider.IsDucking;
            CurrentReductionDB = provider.CurrentGainReductionDB;
        }

        /// <summary>
        /// Applies a preset ducking configuration.
        /// </summary>
        public void ApplyPreset(string presetName)
        {
            switch (presetName.ToLowerInvariant())
            {
                case "gentle":
                    _globalConfig.ThresholdDB = -25f;
                    _globalConfig.ReductionDB = -6f;
                    _globalConfig.AttackMs = 100f;
                    _globalConfig.ReleaseMs = 500f;
                    break;
                case "moderate":
                    _globalConfig.ThresholdDB = -20f;
                    _globalConfig.ReductionDB = -12f;
                    _globalConfig.AttackMs = 50f;
                    _globalConfig.ReleaseMs = 300f;
                    break;
                case "aggressive":
                    _globalConfig.ThresholdDB = -15f;
                    _globalConfig.ReductionDB = -20f;
                    _globalConfig.AttackMs = 20f;
                    _globalConfig.ReleaseMs = 200f;
                    break;
                case "podcast":
                    _globalConfig.ThresholdDB = -22f;
                    _globalConfig.ReductionDB = -15f;
                    _globalConfig.AttackMs = 30f;
                    _globalConfig.HoldMs = 150f;
                    _globalConfig.ReleaseMs = 400f;
                    break;
            }
        }
    }
}
