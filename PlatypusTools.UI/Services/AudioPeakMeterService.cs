using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace PlatypusTools.UI.Services
{
    /// <summary>
    /// Audio peak meter data for visualization.
    /// </summary>
    public class PeakMeterData : INotifyPropertyChanged
    {
        private float _leftPeak;
        private float _rightPeak;
        private float _leftRMS;
        private float _rightRMS;
        private float _leftPeakHold;
        private float _rightPeakHold;
        private double _leftDB;
        private double _rightDB;
        private double _leftRMSDB;
        private double _rightRMSDB;
        private bool _isClipping;

        /// <summary>
        /// Left channel peak level (0.0 to 1.0).
        /// </summary>
        public float LeftPeak
        {
            get => _leftPeak;
            set { _leftPeak = value; OnPropertyChanged(); LeftDB = LinearToDecibels(value); }
        }

        /// <summary>
        /// Right channel peak level (0.0 to 1.0).
        /// </summary>
        public float RightPeak
        {
            get => _rightPeak;
            set { _rightPeak = value; OnPropertyChanged(); RightDB = LinearToDecibels(value); }
        }

        /// <summary>
        /// Left channel RMS level (0.0 to 1.0).
        /// </summary>
        public float LeftRMS
        {
            get => _leftRMS;
            set { _leftRMS = value; OnPropertyChanged(); LeftRMSDB = LinearToDecibels(value); }
        }

        /// <summary>
        /// Right channel RMS level (0.0 to 1.0).
        /// </summary>
        public float RightRMS
        {
            get => _rightRMS;
            set { _rightRMS = value; OnPropertyChanged(); RightRMSDB = LinearToDecibels(value); }
        }

        /// <summary>
        /// Left channel peak hold (for VU meter peak indicator).
        /// </summary>
        public float LeftPeakHold
        {
            get => _leftPeakHold;
            set { _leftPeakHold = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Right channel peak hold.
        /// </summary>
        public float RightPeakHold
        {
            get => _rightPeakHold;
            set { _rightPeakHold = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Left channel level in decibels.
        /// </summary>
        public double LeftDB
        {
            get => _leftDB;
            private set { _leftDB = value; OnPropertyChanged(); OnPropertyChanged(nameof(LeftDBText)); }
        }

        /// <summary>
        /// Right channel level in decibels.
        /// </summary>
        public double RightDB
        {
            get => _rightDB;
            private set { _rightDB = value; OnPropertyChanged(); OnPropertyChanged(nameof(RightDBText)); }
        }

        /// <summary>
        /// Left channel RMS in decibels.
        /// </summary>
        public double LeftRMSDB
        {
            get => _leftRMSDB;
            private set { _leftRMSDB = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Right channel RMS in decibels.
        /// </summary>
        public double RightRMSDB
        {
            get => _rightRMSDB;
            private set { _rightRMSDB = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Whether audio is clipping (exceeding 0 dB).
        /// </summary>
        public bool IsClipping
        {
            get => _isClipping;
            set { _isClipping = value; OnPropertyChanged(); }
        }

        public string LeftDBText => _leftDB > -60 ? $"{_leftDB:F1} dB" : "-∞ dB";
        public string RightDBText => _rightDB > -60 ? $"{_rightDB:F1} dB" : "-∞ dB";

        /// <summary>
        /// Mono peak (average of left and right).
        /// </summary>
        public float MonoPeak => (LeftPeak + RightPeak) / 2f;

        /// <summary>
        /// Mono RMS (average of left and right).
        /// </summary>
        public float MonoRMS => (LeftRMS + RightRMS) / 2f;

        private static double LinearToDecibels(float linear)
        {
            if (linear <= 0) return -96.0;
            return 20.0 * Math.Log10(linear);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Service for real-time audio peak metering.
    /// </summary>
    public class AudioPeakMeterService : IDisposable
    {
        private readonly Timer _peakHoldTimer;
        private readonly PeakMeterData _data = new();
        private float _leftPeakHoldValue;
        private float _rightPeakHoldValue;
        private DateTime _leftPeakHoldTime;
        private DateTime _rightPeakHoldTime;
        private readonly TimeSpan _peakHoldDuration = TimeSpan.FromSeconds(2);
        private readonly float _falloffRate = 0.95f; // Per update
        private bool _isMonitoring;

        public PeakMeterData Data => _data;

        public event EventHandler<PeakMeterData>? PeakUpdated;

        public AudioPeakMeterService()
        {
            _peakHoldTimer = new Timer(UpdatePeakHold, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <summary>
        /// Starts monitoring audio levels.
        /// </summary>
        public void StartMonitoring()
        {
            _isMonitoring = true;
            _peakHoldTimer.Change(0, 50); // Update every 50ms
        }

        /// <summary>
        /// Stops monitoring audio levels.
        /// </summary>
        public void StopMonitoring()
        {
            _isMonitoring = false;
            _peakHoldTimer.Change(Timeout.Infinite, Timeout.Infinite);
            Reset();
        }

        /// <summary>
        /// Updates the meter with new sample data.
        /// </summary>
        /// <param name="leftSamples">Left channel samples.</param>
        /// <param name="rightSamples">Right channel samples (or same as left for mono).</param>
        public void UpdateFromSamples(float[] leftSamples, float[] rightSamples)
        {
            if (!_isMonitoring) return;

            // Calculate peak
            float leftPeak = 0, rightPeak = 0;
            float leftSum = 0, rightSum = 0;

            for (int i = 0; i < leftSamples.Length; i++)
            {
                var absLeft = Math.Abs(leftSamples[i]);
                var absRight = Math.Abs(rightSamples[i]);

                if (absLeft > leftPeak) leftPeak = absLeft;
                if (absRight > rightPeak) rightPeak = absRight;

                leftSum += leftSamples[i] * leftSamples[i];
                rightSum += rightSamples[i] * rightSamples[i];
            }

            // Calculate RMS
            float leftRMS = (float)Math.Sqrt(leftSum / leftSamples.Length);
            float rightRMS = (float)Math.Sqrt(rightSum / rightSamples.Length);

            // Update data
            _data.LeftPeak = leftPeak;
            _data.RightPeak = rightPeak;
            _data.LeftRMS = leftRMS;
            _data.RightRMS = rightRMS;
            _data.IsClipping = leftPeak >= 1.0f || rightPeak >= 1.0f;

            // Update peak hold
            var now = DateTime.Now;
            if (leftPeak > _leftPeakHoldValue)
            {
                _leftPeakHoldValue = leftPeak;
                _leftPeakHoldTime = now;
            }
            if (rightPeak > _rightPeakHoldValue)
            {
                _rightPeakHoldValue = rightPeak;
                _rightPeakHoldTime = now;
            }

            _data.LeftPeakHold = _leftPeakHoldValue;
            _data.RightPeakHold = _rightPeakHoldValue;

            PeakUpdated?.Invoke(this, _data);
        }

        /// <summary>
        /// Updates the meter from a WaveProvider buffer.
        /// </summary>
        public void UpdateFromBuffer(byte[] buffer, int bytesRead, WaveFormat format)
        {
            if (!_isMonitoring || bytesRead == 0) return;

            int channels = format.Channels;
            int bytesPerSample = format.BitsPerSample / 8;
            int sampleCount = bytesRead / (channels * bytesPerSample);

            float[] leftSamples = new float[sampleCount];
            float[] rightSamples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                int offset = i * channels * bytesPerSample;
                leftSamples[i] = BytesToFloat(buffer, offset, format);
                rightSamples[i] = channels > 1 
                    ? BytesToFloat(buffer, offset + bytesPerSample, format)
                    : leftSamples[i];
            }

            UpdateFromSamples(leftSamples, rightSamples);
        }

        private static float BytesToFloat(byte[] buffer, int offset, WaveFormat format)
        {
            if (format.BitsPerSample == 16)
            {
                short sample = BitConverter.ToInt16(buffer, offset);
                return sample / 32768f;
            }
            else if (format.BitsPerSample == 32)
            {
                if (format.Encoding == WaveFormatEncoding.IeeeFloat)
                {
                    return BitConverter.ToSingle(buffer, offset);
                }
                else
                {
                    int sample = BitConverter.ToInt32(buffer, offset);
                    return sample / (float)int.MaxValue;
                }
            }
            else if (format.BitsPerSample == 24)
            {
                int sample = buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16);
                if ((sample & 0x800000) != 0) sample |= unchecked((int)0xFF000000);
                return sample / 8388608f;
            }
            return 0f;
        }

        private void UpdatePeakHold(object? state)
        {
            var now = DateTime.Now;

            // Decay peak hold after duration
            if (now - _leftPeakHoldTime > _peakHoldDuration)
            {
                _leftPeakHoldValue *= _falloffRate;
                if (_leftPeakHoldValue < 0.001f) _leftPeakHoldValue = 0;
            }
            if (now - _rightPeakHoldTime > _peakHoldDuration)
            {
                _rightPeakHoldValue *= _falloffRate;
                if (_rightPeakHoldValue < 0.001f) _rightPeakHoldValue = 0;
            }

            _data.LeftPeakHold = _leftPeakHoldValue;
            _data.RightPeakHold = _rightPeakHoldValue;
        }

        /// <summary>
        /// Resets the meter to zero.
        /// </summary>
        public void Reset()
        {
            _data.LeftPeak = 0;
            _data.RightPeak = 0;
            _data.LeftRMS = 0;
            _data.RightRMS = 0;
            _data.LeftPeakHold = 0;
            _data.RightPeakHold = 0;
            _data.IsClipping = false;
            _leftPeakHoldValue = 0;
            _rightPeakHoldValue = 0;
        }

        public void Dispose()
        {
            _peakHoldTimer.Dispose();
        }
    }
}
