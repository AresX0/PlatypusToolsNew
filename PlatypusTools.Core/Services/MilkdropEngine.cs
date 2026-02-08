using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Pure C# Milkdrop-style music visualization engine.
    /// Implements the core Milkdrop2 rendering pipeline:
    /// 1. Beat detection + FFT analysis
    /// 2. Per-frame equations (global vars updated each frame)
    /// 3. Per-vertex equations (motion grid warping)
    /// 4. Waveform rendering
    /// 5. Decay/feedback compositing
    /// 
    /// Renders to a pixel buffer (uint[]) that can be displayed via WriteableBitmap.
    /// Licensed under BSD (same as original Milkdrop2).
    /// </summary>
    public class MilkdropEngine : IDisposable
    {
        // --- Pixel buffer ---
        private uint[] _pixels = Array.Empty<uint>();
        private uint[] _prevFrame = Array.Empty<uint>();
        private uint[] _motionOutput = Array.Empty<uint>(); // Reused each frame
        private int _width;
        private int _height;

        // --- Motion grid ---
        private int _gridWidth = 48;
        private int _gridHeight = 36;
        private double[,] _gridX = new double[0, 0]; // Warped X coords
        private double[,] _gridY = new double[0, 0]; // Warped Y coords

        // --- Audio data ---
        private readonly double[] _spectrumLeft = new double[512];
        private readonly double[] _spectrumRight = new double[512];
        private readonly float[] _waveformLeft = new float[512];
        private readonly float[] _waveformRight = new float[512];

        // --- Beat detection ---
        private double _bass;
        private double _mid;
        private double _treb;
        private double _bassAtt;
        private double _midAtt;
        private double _trebAtt;
        private double _vol;
        private double _volAtt;
        private readonly double[] _bassHistory = new double[8];
        private readonly double[] _midHistory = new double[8];
        private readonly double[] _trebHistory = new double[8];
        private int _beatHistoryIndex;
        private bool _isBeatDetected;

        // --- Per-frame variables (Milkdrop standard) ---
        private double _time;
        private double _fps = 30;
        private int _frame;
        private double _progress; // 0-1 within current preset
        private readonly Random _random = new();

        // --- Current preset ---
        private MilkdropPreset _preset = MilkdropPreset.CreateDefault();
        private MilkdropPreset? _blendFromPreset;
        private double _blendProgress;
        private bool _isBlending;
        private const double BlendDuration = 2.0; // seconds

        // --- Per-frame computed values (from preset equations) ---
        private double _decay;
        private double _zoom;
        private double _zoomExp;
        private double _rot;
        private double _warp;
        private double _cx;
        private double _cy;
        private double _dx;
        private double _dy;
        private double _sx;
        private double _sy;
        private double _wave_r;
        private double _wave_g;
        private double _wave_b;
        private double _wave_a;
        private double _ob_r;
        private double _ob_g;
        private double _ob_b;
        private double _ob_a;
        private double _ob_size;
        private double _ib_r;
        private double _ib_g;
        private double _ib_b;
        private double _ib_a;
        private double _ib_size;
        private double _mv_x;
        private double _mv_y;
        private double _mv_dx;
        private double _mv_dy;
        private double _mv_l;
        private double _mv_r;
        private double _mv_g;
        private double _mv_b;
        private double _mv_a;

        // --- Q variables (custom per-preset communication) ---
        private readonly double[] _q = new double[32];

        // --- Waveform drawing mode ---
        private int _waveMode;
        private bool _additiveWaves;
        private bool _waveDots;
        private bool _waveThick;

        // --- Sensitivity (driven by UI slider) ---
        private double _sensitivity = 1.0;

        public int Width => _width;
        public int Height => _height;
        public uint[] Pixels => _pixels;

        /// <summary>
        /// Sets the audio sensitivity multiplier (0.1 = very subtle, 2.0 = very aggressive).
        /// Tied to the UI sensitivity slider.
        /// </summary>
        public void SetSensitivity(double sensitivity) => _sensitivity = Math.Clamp(sensitivity, 0.1, 3.0);
        public double Sensitivity => _sensitivity;

        /// <summary>
        /// Initialize engine with target dimensions.
        /// </summary>
        public void Initialize(int width, int height)
        {
            _width = Math.Max(width, 64);
            _height = Math.Max(height, 48);
            _pixels = new uint[_width * _height];
            _prevFrame = new uint[_width * _height];
            _motionOutput = new uint[_width * _height];
            _time = 0;
            _frame = 0;

            InitializeGrid();
        }

        /// <summary>
        /// Resize the render target.
        /// </summary>
        public void Resize(int width, int height)
        {
            if (width == _width && height == _height) return;
            Initialize(width, height);
        }

        private void InitializeGrid()
        {
            _gridX = new double[_gridHeight + 1, _gridWidth + 1];
            _gridY = new double[_gridHeight + 1, _gridWidth + 1];

            for (int j = 0; j <= _gridHeight; j++)
            {
                for (int i = 0; i <= _gridWidth; i++)
                {
                    _gridX[j, i] = (double)i / _gridWidth;
                    _gridY[j, i] = (double)j / _gridHeight;
                }
            }
        }

        /// <summary>
        /// Feed PCM audio data to the engine (interleaved stereo float samples, -1 to 1).
        /// </summary>
        public void AddPCMData(float[] samples, int sampleCount, bool isStereo)
        {
            int channels = isStereo ? 2 : 1;
            int waveLen = Math.Min(sampleCount / channels, 512);

            for (int i = 0; i < waveLen; i++)
            {
                if (isStereo)
                {
                    _waveformLeft[i] = samples[i * 2];
                    _waveformRight[i] = samples[i * 2 + 1];
                }
                else
                {
                    _waveformLeft[i] = samples[i];
                    _waveformRight[i] = samples[i];
                }
            }
        }

        /// <summary>
        /// Feed FFT spectrum data (magnitudes 0-1, 512 bands).
        /// </summary>
        public void AddSpectrumData(double[] spectrum)
        {
            int len = Math.Min(spectrum.Length, 512);
            Array.Copy(spectrum, _spectrumLeft, len);
            Array.Copy(spectrum, _spectrumRight, len);
        }

        /// <summary>
        /// Load a preset.
        /// </summary>
        public void LoadPreset(MilkdropPreset preset, bool smoothTransition = true)
        {
            if (smoothTransition && _preset != null)
            {
                _blendFromPreset = _preset;
                _blendProgress = 0;
                _isBlending = true;
            }

            _preset = preset;
            _progress = 0;
        }

        /// <summary>
        /// Load a random built-in preset.
        /// </summary>
        public void LoadRandomPreset(bool smoothTransition = true)
        {
            LoadPreset(MilkdropPreset.CreateRandom(_random), smoothTransition);
        }

        /// <summary>
        /// Render one frame. Call this at your target FPS (e.g. 30Hz).
        /// Returns the pixel buffer (BGRA format, uint[]).
        /// </summary>
        public uint[] RenderFrame(double deltaTime)
        {
            if (_pixels.Length == 0) return _pixels;

            _time += deltaTime;
            _frame++;
            _fps = 1.0 / Math.Max(deltaTime, 0.001);
            _progress = Math.Min(_progress + deltaTime / 30.0, 1.0);

            // Handle preset blending
            if (_isBlending)
            {
                _blendProgress += deltaTime / BlendDuration;
                if (_blendProgress >= 1.0)
                {
                    _isBlending = false;
                    _blendFromPreset = null;
                    _blendProgress = 1.0;
                }
            }

            // 1. Analyze audio / beat detection
            AnalyzeAudio();

            // 2. Run per-frame equations
            RunPerFrameEquations();

            // 3. Save current frame as previous
            Array.Copy(_pixels, _prevFrame, _pixels.Length);

            // 4. Apply motion (warp previous frame via grid)
            ApplyMotion();

            // 5. Apply decay/darkening
            ApplyDecay();

            // 5b. Apply color tint wash (injects color into the background)
            ApplyColorTint();

            // 6. Draw outer/inner borders
            DrawBorders();

            // 7. Draw motion vectors
            DrawMotionVectors();

            // 8. Draw waveform
            DrawWaveform();

            // 8b. Beat flash — bright burst of color on detected beats
            if (_isBeatDetected)
                ApplyBeatFlash();

            // 9. Draw additional shapes (darken center, etc.)
            DrawDarkenCenter();

            return _pixels;
        }

        #region Audio Analysis

        private void AnalyzeAudio()
        {
            // Compute bass, mid, treble from spectrum
            double bassSum = 0, midSum = 0, trebSum = 0;
            int bassCount = 0, midCount = 0, trebCount = 0;

            for (int i = 0; i < 512; i++)
            {
                double val = _spectrumLeft[i];
                if (i < 50) { bassSum += val; bassCount++; }
                else if (i < 200) { midSum += val; midCount++; }
                else { trebSum += val; trebCount++; }
            }

            _bass = bassCount > 0 ? bassSum / bassCount : 0;
            _mid = midCount > 0 ? midSum / midCount : 0;
            _treb = trebCount > 0 ? trebSum / trebCount : 0;

            // Scale up for responsiveness, modulated by UI sensitivity slider
            // sensitivity 0.1 = barely reactive, 1.0 = normal, 2.0 = very punchy
            double audioScale = 4.0 + _sensitivity * 6.0; // Range: 4.6 to 16.0
            double audioMax = 1.5 + _sensitivity * 2.0;   // Range: 1.7 to 5.5
            _bass = Math.Min(_bass * audioScale, audioMax);
            _mid = Math.Min(_mid * audioScale, audioMax);
            _treb = Math.Min(_treb * audioScale, audioMax);

            // Attenuated (smoothed) values - tracking speed tied to sensitivity
            double smoothFactor = 0.15 + _sensitivity * 0.15; // Range: 0.165 to 0.45
            _bassAtt = _bassAtt * (1.0 - smoothFactor) + _bass * smoothFactor;
            _midAtt = _midAtt * (1.0 - smoothFactor) + _mid * smoothFactor;
            _trebAtt = _trebAtt * (1.0 - smoothFactor) + _treb * smoothFactor;

            // Volume
            double volSum = 0;
            for (int i = 0; i < 512; i++)
            {
                volSum += Math.Abs(_waveformLeft[i]) + Math.Abs(_waveformRight[i]);
            }
            _vol = volSum / 512.0 * (0.5 + _sensitivity); // Volume scaled by sensitivity
            double volSmooth = 0.15 + _sensitivity * 0.1;
            _volAtt = _volAtt * (1.0 - volSmooth) + _vol * volSmooth;

            // Beat detection with history
            _bassHistory[_beatHistoryIndex] = _bass;
            _midHistory[_beatHistoryIndex] = _mid;
            _trebHistory[_beatHistoryIndex] = _treb;
            _beatHistoryIndex = (_beatHistoryIndex + 1) % _bassHistory.Length;

            double bassAvg = 0;
            for (int i = 0; i < _bassHistory.Length; i++) bassAvg += _bassHistory[i];
            bassAvg /= _bassHistory.Length;

            // Beat detection - lower threshold at higher sensitivity for more beats
            double beatThreshold = 1.4 - _sensitivity * 0.15; // 1.385 to 1.1
            double beatFloor = 0.08 - _sensitivity * 0.02;    // 0.078 to 0.04
            _isBeatDetected = _bass > bassAvg * beatThreshold && _bass > Math.Max(0.02, beatFloor);
        }

        #endregion

        #region Per-Frame Equations

        private void RunPerFrameEquations()
        {
            var p = _preset;

            // Initialize from preset defaults
            _decay = p.Decay;
            _zoom = p.Zoom;
            _zoomExp = p.ZoomExponent;
            _rot = p.Rotation;
            _warp = p.WarpAmount;
            _cx = p.CenterX;
            _cy = p.CenterY;
            _dx = p.TranslateX;
            _dy = p.TranslateY;
            _sx = p.StretchX;
            _sy = p.StretchY;

            // Wave colors
            _wave_r = p.WaveR;
            _wave_g = p.WaveG;
            _wave_b = p.WaveB;
            _wave_a = p.WaveA;

            // Borders
            _ob_r = p.OuterBorderR;
            _ob_g = p.OuterBorderG;
            _ob_b = p.OuterBorderB;
            _ob_a = p.OuterBorderA;
            _ob_size = p.OuterBorderSize;
            _ib_r = p.InnerBorderR;
            _ib_g = p.InnerBorderG;
            _ib_b = p.InnerBorderB;
            _ib_a = p.InnerBorderA;
            _ib_size = p.InnerBorderSize;

            // Motion vectors
            _mv_x = p.MotionVectorsX;
            _mv_y = p.MotionVectorsY;
            _mv_dx = p.MotionVectorsDX;
            _mv_dy = p.MotionVectorsDY;
            _mv_l = p.MotionVectorsLength;
            _mv_r = p.MotionVectorsR;
            _mv_g = p.MotionVectorsG;
            _mv_b = p.MotionVectorsB;
            _mv_a = p.MotionVectorsA;

            _waveMode = p.WaveMode;
            _additiveWaves = p.AdditiveWaves;
            _waveDots = p.WaveDots;
            _waveThick = p.WaveThick;

            // Apply per-frame equations (dynamic modulation based on audio)
            if (p.PerFrameActions != null)
            {
                foreach (var action in p.PerFrameActions)
                {
                    action(this);
                }
            }

            // Apply any C# per-frame code from the preset
            p.PerFrameCode?.Invoke(new MilkdropFrameContext
            {
                Time = _time,
                Frame = _frame,
                FPS = _fps,
                Progress = _progress,
                Bass = _bass,
                Mid = _mid,
                Treb = _treb,
                BassAtt = _bassAtt,
                MidAtt = _midAtt,
                TrebAtt = _trebAtt,
                Vol = _vol,
                IsBeat = _isBeatDetected,
                Engine = this
            });
        }

        // Expose setters for per-frame equation delegates
        public void SetZoom(double v) => _zoom = v;
        public void SetRotation(double v) => _rot = v;
        public void SetWarp(double v) => _warp = v;
        public void SetCenterX(double v) => _cx = v;
        public void SetCenterY(double v) => _cy = v;
        public void SetTranslateX(double v) => _dx = v;
        public void SetTranslateY(double v) => _dy = v;
        public void SetDecay(double v) => _decay = v;
        public void SetWaveR(double v) => _wave_r = v;
        public void SetWaveG(double v) => _wave_g = v;
        public void SetWaveB(double v) => _wave_b = v;
        public void SetWaveA(double v) => _wave_a = v;
        public void SetQ(int index, double v) { if (index >= 0 && index < 32) _q[index] = v; }
        public double GetQ(int index) => index >= 0 && index < 32 ? _q[index] : 0;
        public double Time => _time;
        public int Frame => _frame;
        public double Bass => _bass;
        public double Mid => _mid;
        public double Treb => _treb;
        public double BassAtt => _bassAtt;
        public double MidAtt => _midAtt;
        public double TrebAtt => _trebAtt;
        public double Vol => _vol;
        public bool IsBeat => _isBeatDetected;

        #endregion

        #region Motion (Grid Warping)

        private void ApplyMotion()
        {
            // Compute per-vertex warped coordinates
            double zoomFactor = Math.Pow(_zoom, Math.Pow(_zoomExp, (_fps < 15 ? 15 : _fps) / _fps));
            double cosRot = Math.Cos(_rot);
            double sinRot = Math.Sin(_rot);
            double aspectRatio = (double)_width / _height;

            for (int j = 0; j <= _gridHeight; j++)
            {
                for (int i = 0; i <= _gridWidth; i++)
                {
                    // Normalized coordinates (0-1)
                    double u = (double)i / _gridWidth;
                    double v = (double)j / _gridHeight;

                    // Center and apply aspect ratio
                    double x = (u - _cx) * 2.0;
                    double y = (v - _cy) * 2.0;
                    y *= aspectRatio;

                    // Apply zoom
                    x /= zoomFactor;
                    y /= zoomFactor;

                    // Apply rotation
                    double x2 = x * cosRot - y * sinRot;
                    double y2 = x * sinRot + y * cosRot;
                    x = x2;
                    y = y2;

                    // Apply stretch
                    x *= _sx;
                    y *= _sy;

                    // Apply warp
                    if (Math.Abs(_warp) > 0.001)
                    {
                        double radius = Math.Sqrt(x * x + y * y);
                        double angle = Math.Atan2(y, x);
                        double warpFactor = 1.0 + _warp * 0.15 * Math.Sin(radius * 5.0 - _time * 2.0);
                        x = radius * warpFactor * Math.Cos(angle);
                        y = radius * warpFactor * Math.Sin(angle);
                    }

                    // Undo aspect ratio
                    y /= aspectRatio;

                    // Apply translation
                    x = (x / 2.0 + _cx) + _dx;
                    y = (y / 2.0 + _cy) + _dy;

                    // Run per-vertex equations if present
                    if (_preset.PerVertexCode != null)
                    {
                        var ctx = new MilkdropVertexContext
                        {
                            X = x, Y = y, U = u, V = v,
                            Rad = Math.Sqrt((u - 0.5) * (u - 0.5) + (v - 0.5) * (v - 0.5)) * 2.0,
                            Ang = Math.Atan2(v - 0.5, u - 0.5)
                        };
                        _preset.PerVertexCode(ctx);
                        x = ctx.X;
                        y = ctx.Y;
                    }

                    _gridX[j, i] = x;
                    _gridY[j, i] = y;
                }
            }

            // Now warp the previous frame pixels using the grid
            Array.Clear(_motionOutput, 0, _motionOutput.Length);

            for (int j = 0; j < _gridHeight; j++)
            {
                for (int i = 0; i < _gridWidth; i++)
                {
                    // Get the four corners of this grid cell (in texture coords)
                    double u00 = _gridX[j, i], v00 = _gridY[j, i];
                    double u10 = _gridX[j, i + 1], v10 = _gridY[j, i + 1];
                    double u01 = _gridX[j + 1, i], v01 = _gridY[j + 1, i];
                    double u11 = _gridX[j + 1, i + 1], v11 = _gridY[j + 1, i + 1];

                    // Screen pixel range for this grid cell
                    int px0 = (int)((double)i / _gridWidth * _width);
                    int py0 = (int)((double)j / _gridHeight * _height);
                    int px1 = (int)((double)(i + 1) / _gridWidth * _width);
                    int py1 = (int)((double)(j + 1) / _gridHeight * _height);

                    for (int py = py0; py < py1; py++)
                    {
                        double fy = (double)(py - py0) / Math.Max(py1 - py0, 1);
                        for (int px = px0; px < px1; px++)
                        {
                            double fx = (double)(px - px0) / Math.Max(px1 - px0, 1);

                            // Bilinear interpolation of texture coordinates
                            double su = u00 * (1 - fx) * (1 - fy) + u10 * fx * (1 - fy) +
                                        u01 * (1 - fx) * fy + u11 * fx * fy;
                            double sv = v00 * (1 - fx) * (1 - fy) + v10 * fx * (1 - fy) +
                                        v01 * (1 - fx) * fy + v11 * fx * fy;

                            // Sample the previous frame with wrapping
                            int srcX = (int)(su * _width) % _width;
                            int srcY = (int)(sv * _height) % _height;
                            if (srcX < 0) srcX += _width;
                            if (srcY < 0) srcY += _height;

                            int dstIdx = py * _width + px;
                            int srcIdx = srcY * _width + srcX;

                            if (dstIdx >= 0 && dstIdx < _motionOutput.Length && srcIdx >= 0 && srcIdx < _prevFrame.Length)
                            {
                                _motionOutput[dstIdx] = _prevFrame[srcIdx];
                            }
                        }
                    }
                }
            }

            Array.Copy(_motionOutput, _pixels, _pixels.Length);
        }

        #endregion

        #region Decay

        private void ApplyDecay()
        {
            double decayFactor = _decay;
            for (int i = 0; i < _pixels.Length; i++)
            {
                uint pixel = _pixels[i];
                byte b = (byte)(pixel & 0xFF);
                byte g = (byte)((pixel >> 8) & 0xFF);
                byte r = (byte)((pixel >> 16) & 0xFF);

                r = (byte)(r * decayFactor);
                g = (byte)(g * decayFactor);
                b = (byte)(b * decayFactor);

                _pixels[i] = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }

        #endregion

        #region Color Enhancement

        /// <summary>
        /// Applies a subtle color tint wash based on wave colors and audio intensity.
        /// This injects color into the background so it's not just fading to black.
        /// </summary>
        private void ApplyColorTint()
        {
            // Only apply when there's audio activity
            double intensity = (_bass + _mid + _treb) / 3.0;
            if (intensity < 0.02) return;

            // Tint strength: subtle enough to not overpower, strong enough to add color
            double tintStrength = Math.Min(intensity * 0.08, 0.12);
            byte tintR = (byte)Math.Clamp(_wave_r * 255 * tintStrength, 0, 30);
            byte tintG = (byte)Math.Clamp(_wave_g * 255 * tintStrength, 0, 30);
            byte tintB = (byte)Math.Clamp(_wave_b * 255 * tintStrength, 0, 30);

            if (tintR == 0 && tintG == 0 && tintB == 0) return;

            for (int i = 0; i < _pixels.Length; i++)
            {
                uint pixel = _pixels[i];
                int r = Math.Min(255, (int)((pixel >> 16) & 0xFF) + tintR);
                int g = Math.Min(255, (int)((pixel >> 8) & 0xFF) + tintG);
                int b = Math.Min(255, (int)(pixel & 0xFF) + tintB);
                _pixels[i] = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
            }
        }

        /// <summary>
        /// Flash of bright color on beat detection for punch.
        /// </summary>
        private void ApplyBeatFlash()
        {
            double flashStrength = Math.Min(_bass * 0.15, 0.3);
            byte flashR = (byte)Math.Clamp(_wave_r * 255 * flashStrength, 0, 60);
            byte flashG = (byte)Math.Clamp(_wave_g * 255 * flashStrength, 0, 60);
            byte flashB = (byte)Math.Clamp(_wave_b * 255 * flashStrength, 0, 60);

            if (flashR == 0 && flashG == 0 && flashB == 0) return;

            for (int i = 0; i < _pixels.Length; i++)
            {
                uint pixel = _pixels[i];
                int r = Math.Min(255, (int)((pixel >> 16) & 0xFF) + flashR);
                int g = Math.Min(255, (int)((pixel >> 8) & 0xFF) + flashG);
                int b = Math.Min(255, (int)(pixel & 0xFF) + flashB);
                _pixels[i] = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
            }
        }

        #endregion

        #region Borders

        private void DrawBorders()
        {
            // Outer border
            if (_ob_a > 0.01 && _ob_size > 0)
            {
                int borderSize = (int)(_ob_size * Math.Min(_width, _height) * 0.05);
                uint borderColor = MakeColor(_ob_r, _ob_g, _ob_b, _ob_a);

                for (int y = 0; y < _height; y++)
                {
                    for (int x = 0; x < _width; x++)
                    {
                        if (x < borderSize || x >= _width - borderSize ||
                            y < borderSize || y >= _height - borderSize)
                        {
                            BlendPixel(x, y, borderColor);
                        }
                    }
                }
            }

            // Inner border
            if (_ib_a > 0.01 && _ib_size > 0)
            {
                int ob = (int)(_ob_size * Math.Min(_width, _height) * 0.05);
                int ib = (int)(_ib_size * Math.Min(_width, _height) * 0.03);
                uint borderColor = MakeColor(_ib_r, _ib_g, _ib_b, _ib_a);

                for (int y = ob; y < _height - ob; y++)
                {
                    for (int x = ob; x < _width - ob; x++)
                    {
                        if (x < ob + ib || x >= _width - ob - ib ||
                            y < ob + ib || y >= _height - ob - ib)
                        {
                            BlendPixel(x, y, borderColor);
                        }
                    }
                }
            }
        }

        #endregion

        #region Motion Vectors

        private void DrawMotionVectors()
        {
            if (_mv_a < 0.01) return;

            int nx = (int)_mv_x;
            int ny = (int)_mv_y;
            if (nx < 1 || ny < 1) return;

            uint color = MakeColor(_mv_r, _mv_g, _mv_b, _mv_a);

            for (int j = 0; j < ny; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    double u = (i + 0.5) / nx;
                    double v2 = (j + 0.5) / ny;

                    int px = (int)(u * _width);
                    int py = (int)(v2 * _height);

                    // Draw small line indicating motion direction
                    int ex = (int)((u + _mv_dx * _mv_l) * _width);
                    int ey = (int)((v2 + _mv_dy * _mv_l) * _height);

                    DrawLine(px, py, ex, ey, color);
                }
            }
        }

        #endregion

        #region Waveform

        private void DrawWaveform()
        {
            if (_wave_a < 0.01) return;

            uint waveColor = MakeColor(_wave_r, _wave_g, _wave_b, _wave_a);
            int samples = 256;

            switch (_waveMode)
            {
                case 0: DrawWaveformCircular(waveColor, samples); break;
                case 1: DrawWaveformHorizontal(waveColor, samples, false); break;
                case 2: DrawWaveformHorizontal(waveColor, samples, true); break;
                case 3: DrawWaveformCenterSpike(waveColor, samples); break;
                case 4: DrawWaveformXY(waveColor, samples); break;
                case 5: DrawWaveformDoubleLR(waveColor, samples); break;
                case 6: DrawWaveformSpectrum(waveColor); break;
                case 7: DrawWaveformDotPlot(waveColor, samples); break;
                default: DrawWaveformHorizontal(waveColor, samples, false); break;
            }
        }

        private void DrawWaveformCircular(uint color, int samples)
        {
            double cx = _width * 0.5;
            double cy = _height * 0.5;
            double baseRadius = Math.Min(_width, _height) * 0.25;

            int prevX = -1, prevY = -1;
            for (int i = 0; i <= samples; i++)
            {
                int idx = i % samples;
                double angle = (double)i / samples * Math.PI * 2;
                double val = (_waveformLeft[idx % 512] + _waveformRight[idx % 512]) * 0.5;
                double radius = baseRadius + val * baseRadius * 0.8;

                int px = (int)(cx + Math.Cos(angle) * radius);
                int py = (int)(cy + Math.Sin(angle) * radius);

                if (prevX >= 0 && !_waveDots)
                {
                    if (_waveThick)
                    {
                        DrawLineThick(prevX, prevY, px, py, color, 3);
                        // Additive glow pass for thickness
                        if (_additiveWaves)
                        {
                            uint glowColor = MakeColor(_wave_r * 0.6, _wave_g * 0.6, _wave_b * 0.6, _wave_a * 0.4);
                            DrawLineThick(prevX, prevY, px, py, glowColor, 6);
                        }
                    }
                    else
                    {
                        DrawLine(prevX, prevY, px, py, color);
                    }
                }
                else if (_waveDots)
                {
                    SetPixelSafe(px, py, color);
                }

                prevX = px;
                prevY = py;
            }
        }

        private void DrawWaveformHorizontal(uint color, int samples, bool centered)
        {
            int prevX = -1, prevY = -1;
            for (int i = 0; i < samples; i++)
            {
                double t = (double)i / samples;
                int px = (int)(t * _width);
                double val = _waveformLeft[i % 512];
                int py;

                if (centered)
                {
                    py = (int)(_height * 0.5 + val * _height * 0.4);
                }
                else
                {
                    py = (int)(_height * 0.5 + val * _height * 0.35);
                }

                if (prevX >= 0 && !_waveDots)
                {
                    if (_waveThick) DrawLineThick(prevX, prevY, px, py, color, 3);
                    else DrawLine(prevX, prevY, px, py, color);
                }
                else if (_waveDots)
                {
                    SetPixelSafe(px, py, color);
                }

                prevX = px;
                prevY = py;
            }
        }

        private void DrawWaveformCenterSpike(uint color, int samples)
        {
            double cx = _width * 0.5;
            double cy = _height * 0.5;

            for (int i = 0; i < samples; i++)
            {
                double angle = (double)i / samples * Math.PI * 2;
                double val = Math.Abs(_waveformLeft[i % 512]);
                double length = val * Math.Min(_width, _height) * 0.4;

                int ex = (int)(cx + Math.Cos(angle) * length);
                int ey = (int)(cy + Math.Sin(angle) * length);

                DrawLine((int)cx, (int)cy, ex, ey, color);
            }
        }

        private void DrawWaveformXY(uint color, int samples)
        {
            int prevX = -1, prevY = -1;
            for (int i = 0; i < samples; i++)
            {
                int px = (int)((_waveformLeft[i % 512] * 0.4 + 0.5) * _width);
                int py = (int)((_waveformRight[i % 512] * 0.4 + 0.5) * _height);

                if (prevX >= 0 && !_waveDots)
                {
                    DrawLine(prevX, prevY, px, py, color);
                }
                else if (_waveDots)
                {
                    SetPixelSafe(px, py, color);
                }

                prevX = px;
                prevY = py;
            }
        }

        private void DrawWaveformDoubleLR(uint color, int samples)
        {
            // Top half: left channel, Bottom half: right channel
            DrawChannelWave(color, samples, _waveformLeft, _height * 0.25);
            DrawChannelWave(color, samples, _waveformRight, _height * 0.75);
        }

        private void DrawChannelWave(uint color, int samples, float[] waveData, double yCenter)
        {
            int prevX = -1, prevY = -1;
            for (int i = 0; i < samples; i++)
            {
                int px = (int)((double)i / samples * _width);
                int py = (int)(yCenter + waveData[i % 512] * _height * 0.2);

                if (prevX >= 0 && !_waveDots)
                {
                    DrawLine(prevX, prevY, px, py, color);
                }
                prevX = px;
                prevY = py;
            }
        }

        private void DrawWaveformSpectrum(uint color)
        {
            int bands = Math.Min(128, _width);
            for (int i = 0; i < bands; i++)
            {
                int specIdx = (int)((double)i / bands * 256);
                if (specIdx >= 512) specIdx = 511;
                double val = _spectrumLeft[specIdx];

                int px = (int)((double)i / bands * _width);
                int barWidth = Math.Max(1, _width / bands - 1);
                int barHeight = (int)(val * _height * 0.8);

                for (int bx = 0; bx < barWidth; bx++)
                {
                    for (int by = 0; by < barHeight; by++)
                    {
                        BlendPixel(px + bx, _height - 1 - by, color);
                    }
                }
            }
        }

        private void DrawWaveformDotPlot(uint color, int samples)
        {
            for (int i = 0; i < samples; i++)
            {
                int px = (int)((double)i / samples * _width);
                int py = (int)(_height * 0.5 + _waveformLeft[i % 512] * _height * 0.4);
                SetPixelSafe(px, py, color);
                SetPixelSafe(px + 1, py, color);
                SetPixelSafe(px, py + 1, color);
            }
        }

        #endregion

        #region Darken Center

        private void DrawDarkenCenter()
        {
            if (!_preset.DarkenCenter) return;

            double cx = _width * 0.5;
            double cy = _height * 0.5;
            double maxRadius = Math.Min(_width, _height) * 0.15;

            for (int y = (int)(cy - maxRadius); y <= (int)(cy + maxRadius); y++)
            {
                for (int x = (int)(cx - maxRadius); x <= (int)(cx + maxRadius); x++)
                {
                    double dist = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                    if (dist < maxRadius)
                    {
                        double factor = 1.0 - (1.0 - dist / maxRadius) * 0.3;
                        DarkenPixel(x, y, factor);
                    }
                }
            }
        }

        #endregion

        #region Pixel Operations

        private static uint MakeColor(double r, double g, double b, double a)
        {
            byte br = (byte)(Math.Clamp(r, 0, 1) * 255);
            byte bg = (byte)(Math.Clamp(g, 0, 1) * 255);
            byte bb = (byte)(Math.Clamp(b, 0, 1) * 255);
            byte ba = (byte)(Math.Clamp(a, 0, 1) * 255);
            return ((uint)ba << 24) | ((uint)br << 16) | ((uint)bg << 8) | bb;
        }

        private void SetPixelSafe(int x, int y, uint color)
        {
            if (x >= 0 && x < _width && y >= 0 && y < _height)
            {
                int idx = y * _width + x;
                _pixels[idx] = color;
            }
        }

        private void BlendPixel(int x, int y, uint color)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;

            int idx = y * _width + x;
            byte sa = (byte)((color >> 24) & 0xFF);
            if (sa == 255)
            {
                _pixels[idx] = color;
                return;
            }
            if (sa == 0) return;

            uint dst = _pixels[idx];
            double alpha = sa / 255.0;
            double invAlpha = 1.0 - alpha;

            byte dr = (byte)(((dst >> 16) & 0xFF) * invAlpha + ((color >> 16) & 0xFF) * alpha);
            byte dg = (byte)(((dst >> 8) & 0xFF) * invAlpha + ((color >> 8) & 0xFF) * alpha);
            byte db = (byte)((dst & 0xFF) * invAlpha + (color & 0xFF) * alpha);

            _pixels[idx] = 0xFF000000 | ((uint)dr << 16) | ((uint)dg << 8) | db;
        }

        private void DarkenPixel(int x, int y, double factor)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;

            int idx = y * _width + x;
            uint pixel = _pixels[idx];

            byte r = (byte)(((pixel >> 16) & 0xFF) * factor);
            byte g = (byte)(((pixel >> 8) & 0xFF) * factor);
            byte b = (byte)((pixel & 0xFF) * factor);

            _pixels[idx] = 0xFF000000 | ((uint)r << 16) | ((uint)g << 8) | b;
        }

        private void DrawLine(int x0, int y0, int x1, int y1, uint color)
        {
            // Bresenham's line algorithm
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            int maxSteps = dx + dy + 1;
            for (int step = 0; step < maxSteps; step++)
            {
                if (_additiveWaves)
                    AdditivePixel(x0, y0, color);
                else
                    BlendPixel(x0, y0, color);

                if (x0 == x1 && y0 == y1) break;

                int e2 = 2 * err;
                if (e2 > -dy) { err -= dy; x0 += sx; }
                if (e2 < dx) { err += dx; y0 += sy; }
            }
        }

        private void DrawLineThick(int x0, int y0, int x1, int y1, uint color, int thickness)
        {
            for (int t = -thickness / 2; t <= thickness / 2; t++)
            {
                DrawLine(x0, y0 + t, x1, y1 + t, color);
                DrawLine(x0 + t, y0, x1 + t, y1, color);
            }
        }

        private void AdditivePixel(int x, int y, uint color)
        {
            if (x < 0 || x >= _width || y < 0 || y >= _height) return;

            int idx = y * _width + x;
            uint dst = _pixels[idx];

            byte sa = (byte)((color >> 24) & 0xFF);
            double alpha = sa / 255.0;

            int r = (int)(((dst >> 16) & 0xFF) + ((color >> 16) & 0xFF) * alpha);
            int g = (int)(((dst >> 8) & 0xFF) + ((color >> 8) & 0xFF) * alpha);
            int b = (int)((dst & 0xFF) + (color & 0xFF) * alpha);

            _pixels[idx] = 0xFF000000 | ((uint)Math.Min(r, 255) << 16) | ((uint)Math.Min(g, 255) << 8) | (uint)Math.Min(b, 255);
        }

        #endregion

        public void Dispose()
        {
            _pixels = Array.Empty<uint>();
            _prevFrame = Array.Empty<uint>();
        }
    }

    /// <summary>
    /// Context passed to per-frame code delegates.
    /// </summary>
    public class MilkdropFrameContext
    {
        public double Time { get; init; }
        public int Frame { get; init; }
        public double FPS { get; init; }
        public double Progress { get; init; }
        public double Bass { get; init; }
        public double Mid { get; init; }
        public double Treb { get; init; }
        public double BassAtt { get; init; }
        public double MidAtt { get; init; }
        public double TrebAtt { get; init; }
        public double Vol { get; init; }
        public bool IsBeat { get; init; }
        public MilkdropEngine Engine { get; init; } = null!;
    }

    /// <summary>
    /// Context passed to per-vertex code delegates.
    /// </summary>
    public class MilkdropVertexContext
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double U { get; set; }
        public double V { get; set; }
        public double Rad { get; set; }
        public double Ang { get; set; }
    }
}
