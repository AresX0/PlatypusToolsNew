using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a beat marker detected from audio analysis.
    /// </summary>
    public class BeatMarker
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Time position of the beat.
        /// </summary>
        public TimeSpan Time { get; set; }
        
        /// <summary>
        /// Strength/intensity of the beat (0-1).
        /// </summary>
        public double Strength { get; set; } = 1.0;
        
        /// <summary>
        /// Type of beat marker.
        /// </summary>
        public BeatType Type { get; set; } = BeatType.Beat;
        
        /// <summary>
        /// Whether this is a downbeat (first beat of a measure).
        /// </summary>
        public bool IsDownbeat { get; set; }
        
        /// <summary>
        /// Beat number within the measure (1-based).
        /// </summary>
        public int BeatInMeasure { get; set; } = 1;
        
        /// <summary>
        /// Measure number (1-based).
        /// </summary>
        public int MeasureNumber { get; set; } = 1;
        
        /// <summary>
        /// Whether this marker is manually placed.
        /// </summary>
        public bool IsManual { get; set; }
        
        /// <summary>
        /// Color for visualization.
        /// </summary>
        public string Color { get; set; } = "#FF6B6B";
    }

    /// <summary>
    /// Types of beat markers.
    /// </summary>
    public enum BeatType
    {
        Beat,
        Downbeat,
        Onset,
        Silence,
        SceneChange,
        Custom
    }

    /// <summary>
    /// Result of audio beat detection analysis.
    /// </summary>
    public class BeatDetectionResult
    {
        /// <summary>
        /// Whether detection was successful.
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// Error message if detection failed.
        /// </summary>
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// Detected BPM (beats per minute).
        /// </summary>
        public double Bpm { get; set; }
        
        /// <summary>
        /// Confidence of BPM detection (0-1).
        /// </summary>
        public double BpmConfidence { get; set; }
        
        /// <summary>
        /// Detected time signature numerator (e.g., 4 for 4/4).
        /// </summary>
        public int TimeSignatureNumerator { get; set; } = 4;
        
        /// <summary>
        /// Detected time signature denominator (e.g., 4 for 4/4).
        /// </summary>
        public int TimeSignatureDenominator { get; set; } = 4;
        
        /// <summary>
        /// All detected beat markers.
        /// </summary>
        public List<BeatMarker> Markers { get; set; } = new();

        /// <summary>
        /// Alias for Markers property.
        /// </summary>
        public List<BeatMarker> BeatMarkers
        {
            get => Markers;
            set => Markers = value;
        }
        
        /// <summary>
        /// Duration of the analyzed audio.
        /// </summary>
        public TimeSpan Duration { get; set; }
        
        /// <summary>
        /// Waveform data for visualization (normalized samples).
        /// </summary>
        public float[]? WaveformData { get; set; }
        
        /// <summary>
        /// Spectral flux values used for onset detection.
        /// </summary>
        public float[]? SpectralFlux { get; set; }
    }

    /// <summary>
    /// Options for beat detection analysis.
    /// </summary>
    public class BeatDetectionOptions
    {
        /// <summary>
        /// Sensitivity for onset detection (0-1, higher = more beats detected).
        /// </summary>
        public double Sensitivity { get; set; } = 0.5;
        
        /// <summary>
        /// Minimum time between beats in milliseconds.
        /// </summary>
        public int MinBeatIntervalMs { get; set; } = 200;
        
        /// <summary>
        /// Expected BPM range minimum.
        /// </summary>
        public double MinBpm { get; set; } = 60;
        
        /// <summary>
        /// Expected BPM range maximum.
        /// </summary>
        public double MaxBpm { get; set; } = 200;

        /// <summary>
        /// Time signature numerator (e.g., 4 for 4/4).
        /// </summary>
        public int TimeSignatureNumerator { get; set; } = 4;
        
        /// <summary>
        /// Whether to detect downbeats.
        /// </summary>
        public bool DetectDownbeats { get; set; } = true;
        
        /// <summary>
        /// FFT size for spectral analysis.
        /// </summary>
        public int FftSize { get; set; } = 2048;
        
        /// <summary>
        /// Hop size for spectral analysis.
        /// </summary>
        public int HopSize { get; set; } = 512;
    }

    /// <summary>
    /// Audio waveform data for visualization.
    /// </summary>
    public class WaveformData
    {
        /// <summary>
        /// Sample rate of the audio.
        /// </summary>
        public int SampleRate { get; set; }
        
        /// <summary>
        /// Number of channels.
        /// </summary>
        public int Channels { get; set; }
        
        /// <summary>
        /// Duration of the audio.
        /// </summary>
        public TimeSpan Duration { get; set; }
        
        /// <summary>
        /// Peak values for left channel (downsampled for display).
        /// </summary>
        public float[] PeaksLeft { get; set; } = Array.Empty<float>();
        
        /// <summary>
        /// Peak values for right channel (downsampled for display).
        /// </summary>
        public float[] PeaksRight { get; set; } = Array.Empty<float>();
        
        /// <summary>
        /// RMS values for left channel.
        /// </summary>
        public float[] RmsLeft { get; set; } = Array.Empty<float>();
        
        /// <summary>
        /// RMS values for right channel.
        /// </summary>
        public float[] RmsRight { get; set; } = Array.Empty<float>();
        
        /// <summary>
        /// Samples per peak value (for time mapping).
        /// </summary>
        public int SamplesPerPeak { get; set; }
    }
}
