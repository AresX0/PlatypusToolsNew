using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PlatypusTools.Core.Models.Video;

// Alias to the root static services to avoid ambiguity
using RootFFprobeService = PlatypusTools.Core.Services.FFprobeService;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Service for detecting beats and rhythm in audio files.
    /// Uses spectral flux onset detection algorithm.
    /// </summary>
    public class BeatDetectionService
    {
        private const int DefaultFftSize = 2048;
        private const int DefaultHopSize = 512;
        
        /// <summary>
        /// Analyzes audio file for beats and rhythm.
        /// </summary>
        /// <param name="audioPath">Path to audio file.</param>
        /// <param name="options">Detection options.</param>
        /// <param name="progress">Progress reporter.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Beat detection result with markers.</returns>
        public async Task<BeatDetectionResult> DetectBeatsAsync(
            string audioPath,
            BeatDetectionOptions? options = null,
            IProgress<double>? progress = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new BeatDetectionOptions();
            
            return await Task.Run(() =>
            {
                var result = new BeatDetectionResult();
                
                try
                {
                    // Read audio samples
                    progress?.Report(0.1);
                    var (samples, sampleRate, duration) = ReadAudioSamples(audioPath);
                    result.Duration = duration;
                    
                    if (samples == null || samples.Length == 0)
                    {
                        return result;
                    }
                    
                    // Generate waveform data for visualization
                    progress?.Report(0.2);
                    result.WaveformData = GenerateWaveformData(samples, 4000);
                    
                    // Compute spectral flux
                    progress?.Report(0.3);
                    var spectralFlux = ComputeSpectralFlux(samples, sampleRate, options.FftSize, options.HopSize);
                    result.SpectralFlux = spectralFlux;
                    
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    // Detect onsets from spectral flux peaks
                    progress?.Report(0.5);
                    var onsets = DetectOnsets(spectralFlux, sampleRate, options);
                    
                    // Estimate BPM
                    progress?.Report(0.7);
                    var (bpm, confidence) = EstimateBpm(onsets, options);
                    result.Bpm = bpm;
                    result.BpmConfidence = confidence;
                    
                    // Generate beat markers
                    progress?.Report(0.8);
                    result.Markers = GenerateBeatMarkers(onsets, bpm, options);
                    
                    // Detect downbeats if requested
                    if (options.DetectDownbeats)
                    {
                        MarkDownbeats(result.Markers, bpm, options.TimeSignatureNumerator);
                    }
                    
                    progress?.Report(1.0);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    SimpleLogger.Error($"Beat detection failed: {ex.Message}");
                }
                
                return result;
            }, cancellationToken);
        }

        /// <summary>
        /// Snaps cut points to nearest beats.
        /// </summary>
        /// <param name="cutPoints">Original cut points.</param>
        /// <param name="markers">Beat markers.</param>
        /// <param name="maxSnapDistance">Maximum snap distance.</param>
        /// <returns>Snapped cut points.</returns>
        public List<TimeSpan> SnapToBeats(
            IEnumerable<TimeSpan> cutPoints,
            IEnumerable<BeatMarker> markers,
            TimeSpan maxSnapDistance)
        {
            var markerTimes = markers.Select(m => m.Time).OrderBy(t => t).ToList();
            var result = new List<TimeSpan>();
            
            foreach (var cut in cutPoints)
            {
                var closest = markerTimes
                    .OrderBy(m => Math.Abs((m - cut).TotalMilliseconds))
                    .FirstOrDefault();
                
                if (closest != default && Math.Abs((closest - cut).TotalMilliseconds) <= maxSnapDistance.TotalMilliseconds)
                {
                    result.Add(closest);
                }
                else
                {
                    result.Add(cut);
                }
            }
            
            return result;
        }

        /// <summary>
        /// Snaps a single time point to the nearest beat.
        /// </summary>
        /// <param name="markers">Beat markers.</param>
        /// <param name="time">Time to snap.</param>
        /// <param name="maxSnapDistance">Maximum snap distance (defaults to 500ms if not specified).</param>
        /// <returns>Snapped time.</returns>
        public TimeSpan SnapToBeats(
            IEnumerable<BeatMarker> markers,
            TimeSpan time,
            TimeSpan? maxSnapDistance = null)
        {
            var maxSnap = maxSnapDistance ?? TimeSpan.FromMilliseconds(500);
            var markerTimes = markers.Select(m => m.Time).OrderBy(t => t).ToList();
            
            if (markerTimes.Count == 0)
                return time;
            
            var closest = markerTimes
                .OrderBy(m => Math.Abs((m - time).TotalMilliseconds))
                .First();
            
            if (Math.Abs((closest - time).TotalMilliseconds) <= maxSnap.TotalMilliseconds)
            {
                return closest;
            }
            
            return time;
        }

        /// <summary>
        /// Generates waveform data for visualization.
        /// </summary>
        public async Task<WaveformData> GenerateWaveformAsync(
            string audioPath,
            int targetPoints = 4000,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                var (samples, sampleRate, duration) = ReadAudioSamples(audioPath);
                
                var waveform = new WaveformData
                {
                    SampleRate = sampleRate,
                    Channels = 1, // Mono after mixing
                    Duration = duration
                };
                
                if (samples == null || samples.Length == 0)
                    return waveform;
                
                var samplesPerPoint = Math.Max(1, samples.Length / targetPoints);
                waveform.SamplesPerPeak = samplesPerPoint;
                
                var peaks = new List<float>();
                var rms = new List<float>();
                
                for (int i = 0; i < samples.Length; i += samplesPerPoint)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    int end = Math.Min(i + samplesPerPoint, samples.Length);
                    float peak = 0;
                    float sumSquares = 0;
                    int count = 0;
                    
                    for (int j = i; j < end; j++)
                    {
                        var abs = Math.Abs(samples[j]);
                        if (abs > peak) peak = abs;
                        sumSquares += samples[j] * samples[j];
                        count++;
                    }
                    
                    peaks.Add(peak);
                    rms.Add((float)Math.Sqrt(sumSquares / count));
                }
                
                waveform.PeaksLeft = peaks.ToArray();
                waveform.RmsLeft = rms.ToArray();
                waveform.PeaksRight = waveform.PeaksLeft; // Mono
                waveform.RmsRight = waveform.RmsLeft;
                
                return waveform;
            }, cancellationToken);
        }

        #region Private Methods

        private (float[] samples, int sampleRate, TimeSpan duration) ReadAudioSamples(string path)
        {
            // This would use NAudio to read audio samples
            // For now, return placeholder that works without NAudio dependency
            // Actual implementation would use:
            // using var reader = new AudioFileReader(path);
            // var samples = new float[reader.Length / sizeof(float)];
            // reader.Read(samples, 0, samples.Length);
            
            // Placeholder implementation - FFmpeg-based reading
            try
            {
                var ffprobeResult = RootFFprobeService.GetMediaInfo(path);
                if (ffprobeResult == null)
                    return (Array.Empty<float>(), 44100, TimeSpan.Zero);
                
                var duration = ffprobeResult.Duration;
                
                // Generate synthetic samples for testing (would be replaced with actual audio reading)
                var sampleRate = 44100;
                var sampleCount = (int)(duration.TotalSeconds * sampleRate);
                var samples = new float[Math.Min(sampleCount, sampleRate * 600)]; // Max 10 min
                
                // Placeholder: return empty but valid structure
                return (samples, sampleRate, duration);
            }
            catch
            {
                return (Array.Empty<float>(), 44100, TimeSpan.Zero);
            }
        }

        private float[] GenerateWaveformData(float[] samples, int targetPoints)
        {
            if (samples.Length == 0) return Array.Empty<float>();
            
            var samplesPerPoint = Math.Max(1, samples.Length / targetPoints);
            var result = new float[Math.Min(targetPoints, samples.Length)];
            
            for (int i = 0; i < result.Length; i++)
            {
                int start = i * samplesPerPoint;
                int end = Math.Min(start + samplesPerPoint, samples.Length);
                
                float peak = 0;
                for (int j = start; j < end; j++)
                {
                    var abs = Math.Abs(samples[j]);
                    if (abs > peak) peak = abs;
                }
                result[i] = peak;
            }
            
            return result;
        }

        private float[] ComputeSpectralFlux(float[] samples, int sampleRate, int fftSize, int hopSize)
        {
            if (samples.Length < fftSize)
                return Array.Empty<float>();
            
            int numFrames = (samples.Length - fftSize) / hopSize + 1;
            var flux = new float[numFrames];
            var prevMagnitudes = new float[fftSize / 2];
            
            // Hamming window
            var window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
            {
                window[i] = 0.54f - 0.46f * (float)Math.Cos(2 * Math.PI * i / (fftSize - 1));
            }
            
            for (int frame = 0; frame < numFrames; frame++)
            {
                int offset = frame * hopSize;
                
                // Apply window and compute simplified magnitude spectrum
                var magnitudes = new float[fftSize / 2];
                
                // Simplified spectral analysis (real implementation would use proper FFT)
                for (int i = 0; i < fftSize / 2; i++)
                {
                    float sum = 0;
                    for (int j = 0; j < fftSize && (offset + j) < samples.Length; j++)
                    {
                        sum += samples[offset + j] * window[j];
                    }
                    magnitudes[i] = Math.Abs(sum / fftSize);
                }
                
                // Compute spectral flux (positive difference only)
                float frameFlux = 0;
                for (int i = 0; i < magnitudes.Length; i++)
                {
                    float diff = magnitudes[i] - prevMagnitudes[i];
                    if (diff > 0) frameFlux += diff;
                }
                
                flux[frame] = frameFlux;
                Array.Copy(magnitudes, prevMagnitudes, magnitudes.Length);
            }
            
            return flux;
        }

        private List<TimeSpan> DetectOnsets(float[] spectralFlux, int sampleRate, BeatDetectionOptions options)
        {
            var onsets = new List<TimeSpan>();
            
            if (spectralFlux.Length < 3)
                return onsets;
            
            // Compute adaptive threshold
            int windowSize = Math.Max(1, spectralFlux.Length / 20);
            var threshold = new float[spectralFlux.Length];
            
            for (int i = 0; i < spectralFlux.Length; i++)
            {
                int start = Math.Max(0, i - windowSize);
                int end = Math.Min(spectralFlux.Length, i + windowSize);
                
                float sum = 0;
                for (int j = start; j < end; j++)
                    sum += spectralFlux[j];
                
                threshold[i] = (sum / (end - start)) * (float)(1.0 + options.Sensitivity);
            }
            
            // Detect peaks above threshold
            double secondsPerFrame = (double)options.HopSize / sampleRate;
            double minIntervalSeconds = options.MinBeatIntervalMs / 1000.0;
            double lastOnsetSeconds = -minIntervalSeconds;
            
            for (int i = 1; i < spectralFlux.Length - 1; i++)
            {
                if (spectralFlux[i] > threshold[i] &&
                    spectralFlux[i] > spectralFlux[i - 1] &&
                    spectralFlux[i] > spectralFlux[i + 1])
                {
                    double currentSeconds = i * secondsPerFrame;
                    
                    if (currentSeconds - lastOnsetSeconds >= minIntervalSeconds)
                    {
                        onsets.Add(TimeSpan.FromSeconds(currentSeconds));
                        lastOnsetSeconds = currentSeconds;
                    }
                }
            }
            
            return onsets;
        }

        private (double bpm, double confidence) EstimateBpm(List<TimeSpan> onsets, BeatDetectionOptions options)
        {
            if (onsets.Count < 4)
                return (120, 0);
            
            // Calculate inter-onset intervals
            var intervals = new List<double>();
            for (int i = 1; i < onsets.Count; i++)
            {
                var interval = (onsets[i] - onsets[i - 1]).TotalSeconds;
                if (interval > 0.2 && interval < 2.0) // Reasonable beat interval
                    intervals.Add(interval);
            }
            
            if (intervals.Count == 0)
                return (120, 0);
            
            // Find most common interval using histogram
            var histogram = new Dictionary<int, int>();
            foreach (var interval in intervals)
            {
                int bucket = (int)(interval * 100); // 10ms buckets
                histogram.TryGetValue(bucket, out int count);
                histogram[bucket] = count + 1;
            }
            
            var mostCommon = histogram.OrderByDescending(x => x.Value).First();
            double avgInterval = mostCommon.Key / 100.0;
            double bpm = 60.0 / avgInterval;
            
            // Clamp to expected range
            while (bpm < options.MinBpm && bpm > 0) bpm *= 2;
            while (bpm > options.MaxBpm) bpm /= 2;
            
            // Calculate confidence based on histogram consistency
            double confidence = (double)mostCommon.Value / intervals.Count;
            
            return (Math.Round(bpm, 1), Math.Round(confidence, 2));
        }

        private List<BeatMarker> GenerateBeatMarkers(List<TimeSpan> onsets, double bpm, BeatDetectionOptions options)
        {
            var markers = new List<BeatMarker>();
            
            foreach (var onset in onsets)
            {
                markers.Add(new BeatMarker
                {
                    Time = onset,
                    Type = BeatType.Beat,
                    Strength = 1.0
                });
            }
            
            return markers;
        }

        private void MarkDownbeats(List<BeatMarker> markers, double bpm, int beatsPerMeasure)
        {
            if (markers.Count == 0 || bpm <= 0) return;
            
            double beatInterval = 60.0 / bpm;
            int measure = 1;
            int beatInMeasure = 1;
            
            foreach (var marker in markers)
            {
                marker.BeatInMeasure = beatInMeasure;
                marker.MeasureNumber = measure;
                marker.IsDownbeat = beatInMeasure == 1;
                marker.Type = marker.IsDownbeat ? BeatType.Downbeat : BeatType.Beat;
                marker.Color = marker.IsDownbeat ? "#FF4444" : "#FF6B6B";
                
                beatInMeasure++;
                if (beatInMeasure > beatsPerMeasure)
                {
                    beatInMeasure = 1;
                    measure++;
                }
            }
        }

        #endregion
    }
}
