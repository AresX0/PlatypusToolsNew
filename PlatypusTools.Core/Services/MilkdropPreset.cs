using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace PlatypusTools.Core.Services
{
    /// <summary>
    /// Represents a Milkdrop2 preset with per-frame/per-vertex parameters.
    /// Can be constructed programmatically or parsed from .milk files.
    /// </summary>
    public class MilkdropPreset
    {
        public string Name { get; set; } = "Default";
        public string Author { get; set; } = "";

        // --- Motion parameters ---
        public double Decay { get; set; } = 0.98;
        public double Zoom { get; set; } = 1.0;
        public double ZoomExponent { get; set; } = 1.0;
        public double Rotation { get; set; } = 0.0;
        public double WarpAmount { get; set; } = 0.0;
        public double CenterX { get; set; } = 0.5;
        public double CenterY { get; set; } = 0.5;
        public double TranslateX { get; set; } = 0.0;
        public double TranslateY { get; set; } = 0.0;
        public double StretchX { get; set; } = 1.0;
        public double StretchY { get; set; } = 1.0;

        // --- Wave parameters ---
        public int WaveMode { get; set; } = 0;
        public double WaveR { get; set; } = 1.0;
        public double WaveG { get; set; } = 1.0;
        public double WaveB { get; set; } = 1.0;
        public double WaveA { get; set; } = 0.8;
        public bool AdditiveWaves { get; set; } = false;
        public bool WaveDots { get; set; } = false;
        public bool WaveThick { get; set; } = true;

        // --- Outer border ---
        public double OuterBorderR { get; set; } = 0.0;
        public double OuterBorderG { get; set; } = 0.0;
        public double OuterBorderB { get; set; } = 0.0;
        public double OuterBorderA { get; set; } = 0.0;
        public double OuterBorderSize { get; set; } = 0.0;

        // --- Inner border ---
        public double InnerBorderR { get; set; } = 0.0;
        public double InnerBorderG { get; set; } = 0.0;
        public double InnerBorderB { get; set; } = 0.0;
        public double InnerBorderA { get; set; } = 0.0;
        public double InnerBorderSize { get; set; } = 0.0;

        // --- Motion vectors ---
        public double MotionVectorsX { get; set; } = 0;
        public double MotionVectorsY { get; set; } = 0;
        public double MotionVectorsDX { get; set; } = 0;
        public double MotionVectorsDY { get; set; } = 0;
        public double MotionVectorsLength { get; set; } = 0;
        public double MotionVectorsR { get; set; } = 1;
        public double MotionVectorsG { get; set; } = 1;
        public double MotionVectorsB { get; set; } = 1;
        public double MotionVectorsA { get; set; } = 0;

        // --- Misc ---
        public bool DarkenCenter { get; set; } = false;
        public bool Brighten { get; set; } = false;
        public bool Solarize { get; set; } = false;
        public bool Invert { get; set; } = false;
        public double GammaAdj { get; set; } = 1.0;

        // --- Per-frame code (C# delegates) ---
        public List<Action<MilkdropEngine>>? PerFrameActions { get; set; }
        public Action<MilkdropFrameContext>? PerFrameCode { get; set; }
        public Action<MilkdropVertexContext>? PerVertexCode { get; set; }

        // --- Raw equations from .milk file ---
        public string? PerFrameEquations { get; set; }
        public string? PerVertexEquations { get; set; }
        public string? PerFrameInitEquations { get; set; }

        /// <summary>
        /// Create the default preset.
        /// </summary>
        public static MilkdropPreset CreateDefault()
        {
            return new MilkdropPreset
            {
                Name = "Vivid Flow",
                Decay = 0.985,
                Zoom = 1.02,
                Rotation = 0.015,
                WarpAmount = 0.8,
                WaveMode = 0, // Circular
                WaveR = 1.0,
                WaveG = 0.2,
                WaveB = 0.8,
                WaveA = 1.0,
                WaveThick = true,
                AdditiveWaves = true,
                DarkenCenter = true,
                PerFrameCode = ctx =>
                {
                    // Cycle through vivid rainbow colors
                    ctx.Engine.SetWaveR(Math.Sin(ctx.Time * 1.3) * 0.5 + 0.5);
                    ctx.Engine.SetWaveG(Math.Sin(ctx.Time * 1.3 + 2.094) * 0.5 + 0.5);
                    ctx.Engine.SetWaveB(Math.Sin(ctx.Time * 1.3 + 4.189) * 0.5 + 0.5);
                    ctx.Engine.SetZoom(1.02 + ctx.Bass * 0.06);
                    ctx.Engine.SetRotation(0.015 + ctx.Treb * 0.015);
                    ctx.Engine.SetWarp(0.8 + ctx.Mid * 0.8);
                    ctx.Engine.SetDecay(0.985 - ctx.Bass * 0.02);
                }
            };
        }

        /// <summary>
        /// Generate a random preset using built-in C# equation presets.
        /// </summary>
        public static MilkdropPreset CreateRandom(Random rng)
        {
            var presets = GetBuiltInPresets();
            return presets[rng.Next(presets.Count)];
        }

        /// <summary>
        /// All built-in C# presets.
        /// </summary>
        public static List<MilkdropPreset> GetBuiltInPresets()
        {
            return new List<MilkdropPreset>
            {
                // 1. Classic Flow
                new MilkdropPreset
                {
                    Name = "Classic Flow",
                    Decay = 0.98, Zoom = 1.03, Rotation = 0.02, WarpAmount = 1.0,
                    WaveMode = 0, WaveR = 0.0, WaveG = 1.0, WaveB = 0.8, WaveA = 1.0,
                    WaveThick = true, DarkenCenter = true, AdditiveWaves = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.03 + ctx.Bass * 0.08);
                        ctx.Engine.SetRotation(0.02 + Math.Sin(ctx.Time * 0.3) * 0.015 + ctx.Treb * 0.015);
                        ctx.Engine.SetWarp(1.0 + ctx.Mid * 1.2);
                        ctx.Engine.SetWaveR(Math.Sin(ctx.Time * 0.8 + 1) * 0.4 + 0.4);
                        ctx.Engine.SetWaveG(Math.Sin(ctx.Time * 0.6 + 3) * 0.4 + 0.6);
                        ctx.Engine.SetWaveB(Math.Sin(ctx.Time * 1.0 + 5) * 0.4 + 0.6);
                    }
                },

                // 2. Cosmic Tunnel
                new MilkdropPreset
                {
                    Name = "Cosmic Tunnel",
                    Decay = 0.95, Zoom = 1.10, Rotation = 0.04, WarpAmount = 2.0,
                    WaveMode = 1, WaveR = 1.0, WaveG = 0.3, WaveB = 0.8, WaveA = 0.7,
                    AdditiveWaves = true, WaveThick = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.10 + ctx.Bass * 0.10);
                        ctx.Engine.SetRotation(0.04 + Math.Sin(ctx.Time) * 0.03);
                        ctx.Engine.SetWarp(2.0 + ctx.Treb * 2.0);
                        ctx.Engine.SetDecay(0.95 - ctx.Bass * 0.04);
                    }
                },

                // 3. Bass Reactor
                new MilkdropPreset
                {
                    Name = "Bass Reactor",
                    Decay = 0.97, Zoom = 1.0, Rotation = 0.0, WarpAmount = 0.5,
                    WaveMode = 3, WaveR = 1.0, WaveG = 0.2, WaveB = 0.1, WaveA = 1.0,
                    WaveThick = true, AdditiveWaves = true,
                    PerFrameCode = ctx =>
                    {
                        double beat = ctx.IsBeat ? 1.0 : 0.0;
                        ctx.Engine.SetZoom(1.0 + ctx.Bass * 0.15 + beat * 0.10);
                        ctx.Engine.SetRotation(ctx.Treb * 0.04);
                        ctx.Engine.SetWarp(0.5 + ctx.Mid * 1.5);
                        ctx.Engine.SetWaveR(0.8 + ctx.Bass * 0.5);
                        ctx.Engine.SetWaveG(0.1 + ctx.Mid * 0.5);
                        ctx.Engine.SetWaveB(0.3 + ctx.Treb * 0.5);
                    }
                },

                // 4. Kaleidoscope Dream
                new MilkdropPreset
                {
                    Name = "Kaleidoscope Dream",
                    Decay = 0.93, Zoom = 0.97, Rotation = 0.05, WarpAmount = 2.5,
                    WaveMode = 4, WaveR = 0.5, WaveG = 0.5, WaveB = 1.0, WaveA = 0.6,
                    DarkenCenter = true, WaveDots = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(0.97 + Math.Sin(ctx.Time * 0.5) * 0.03 + ctx.Bass * 0.05);
                        ctx.Engine.SetRotation(0.05 + ctx.Treb * 0.04 + Math.Cos(ctx.Time * 0.7) * 0.02);
                        ctx.Engine.SetWarp(2.5 + ctx.Mid * 2.0 + Math.Sin(ctx.Time * 0.3) * 0.8);
                        ctx.Engine.SetCenterX(0.5 + Math.Sin(ctx.Time * 0.4) * 0.15);
                        ctx.Engine.SetCenterY(0.5 + Math.Cos(ctx.Time * 0.3) * 0.15);
                    },
                    PerVertexCode = vtx =>
                    {
                        vtx.X += Math.Sin(vtx.Rad * 10 + vtx.Ang * 3) * 0.005;
                        vtx.Y += Math.Cos(vtx.Rad * 10 + vtx.Ang * 3) * 0.005;
                    }
                },

                // 5. Neon Pulse
                new MilkdropPreset
                {
                    Name = "Neon Pulse",
                    Decay = 0.95, Zoom = 1.05, Rotation = -0.02, WarpAmount = 0.6,
                    WaveMode = 2, WaveR = 0.0, WaveG = 1.0, WaveB = 1.0, WaveA = 1.0,
                    AdditiveWaves = true, WaveThick = true,
                    OuterBorderR = 0, OuterBorderG = 0.5, OuterBorderB = 1, OuterBorderA = 0.3, OuterBorderSize = 0.5,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.05 + ctx.Bass * 0.04);
                        ctx.Engine.SetRotation(-0.02 + ctx.Mid * 0.01);
                        ctx.Engine.SetWaveR(Math.Sin(ctx.Time * 1.5) * 0.5 + 0.5);
                        ctx.Engine.SetWaveG(Math.Sin(ctx.Time * 1.2 + 2) * 0.5 + 0.5);
                        ctx.Engine.SetWaveB(Math.Sin(ctx.Time * 0.9 + 4) * 0.5 + 0.5);
                    }
                },

                // 6. Spiral Galaxy
                new MilkdropPreset
                {
                    Name = "Spiral Galaxy",
                    Decay = 0.97, Zoom = 1.01, Rotation = 0.05, WarpAmount = 1.2,
                    WaveMode = 0, WaveR = 1.0, WaveG = 0.8, WaveB = 0.3, WaveA = 0.5,
                    WaveThick = true, DarkenCenter = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.01 + ctx.Bass * 0.015);
                        ctx.Engine.SetRotation(0.05 + ctx.Mid * 0.01);
                        ctx.Engine.SetWarp(1.2 + Math.Sin(ctx.Time * 0.5) * 0.3 + ctx.Treb * 0.5);
                        ctx.Engine.SetDecay(0.97 + ctx.Vol * 0.01);
                    },
                    PerVertexCode = vtx =>
                    {
                        double spiral = Math.Sin(vtx.Rad * 8 - vtx.Ang * 2) * 0.003;
                        vtx.X += spiral;
                        vtx.Y += spiral;
                    }
                },

                // 7. Electric Storm
                new MilkdropPreset
                {
                    Name = "Electric Storm",
                    Decay = 0.93, Zoom = 1.03, Rotation = 0.0, WarpAmount = 3.0,
                    WaveMode = 5, WaveR = 0.8, WaveG = 0.8, WaveB = 1.0, WaveA = 0.9,
                    AdditiveWaves = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.03 + ctx.Bass * 0.06);
                        ctx.Engine.SetWarp(3.0 + ctx.Treb * 2.0);
                        ctx.Engine.SetRotation(Math.Sin(ctx.Time * 0.7) * 0.03);
                        ctx.Engine.SetCenterX(0.5 + Math.Sin(ctx.Time * 0.5) * 0.15);
                        ctx.Engine.SetCenterY(0.5 + Math.Cos(ctx.Time * 0.4) * 0.15);
                        if (ctx.IsBeat)
                        {
                            ctx.Engine.SetDecay(0.85);
                        }
                        else
                        {
                            ctx.Engine.SetDecay(0.93);
                        }
                    }
                },

                // 8. Calm Waves
                new MilkdropPreset
                {
                    Name = "Calm Waves",
                    Decay = 0.99, Zoom = 1.005, Rotation = 0.003, WarpAmount = 0.2,
                    WaveMode = 1, WaveR = 0.4, WaveG = 0.6, WaveB = 1.0, WaveA = 0.9,
                    WaveThick = true, AdditiveWaves = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.005 + ctx.Bass * 0.005);
                        ctx.Engine.SetWarp(0.2 + Math.Sin(ctx.Time * 0.2) * 0.1);
                        ctx.Engine.SetRotation(0.003 + Math.Sin(ctx.Time * 0.15) * 0.002);
                        ctx.Engine.SetWaveR(Math.Sin(ctx.Time * 0.4) * 0.3 + 0.4);
                        ctx.Engine.SetWaveG(Math.Sin(ctx.Time * 0.3 + 2) * 0.3 + 0.6);
                        ctx.Engine.SetWaveB(Math.Sin(ctx.Time * 0.5 + 4) * 0.3 + 0.7);
                    },
                    PerVertexCode = vtx =>
                    {
                        vtx.Y += Math.Sin(vtx.U * Math.PI * 4) * 0.002;
                    }
                },

                // 9. Fire Vortex
                new MilkdropPreset
                {
                    Name = "Fire Vortex",
                    Decay = 0.95, Zoom = 1.06, Rotation = 0.06, WarpAmount = 1.0,
                    WaveMode = 3, WaveR = 1.0, WaveG = 0.5, WaveB = 0.0, WaveA = 1.0,
                    AdditiveWaves = true, WaveThick = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.06 + ctx.Bass * 0.04);
                        ctx.Engine.SetRotation(0.06 + ctx.Treb * 0.03);
                        ctx.Engine.SetWaveR(1.0);
                        ctx.Engine.SetWaveG(0.3 + ctx.Bass * 0.3);
                        ctx.Engine.SetWaveB(ctx.Treb * 0.3);
                        ctx.Engine.SetDecay(0.95 - ctx.Vol * 0.03);
                    }
                },

                // 10. Hypnotic Rings
                new MilkdropPreset
                {
                    Name = "Hypnotic Rings",
                    Decay = 0.96, Zoom = 0.97, Rotation = -0.01, WarpAmount = 0.0,
                    WaveMode = 0, WaveR = 1.0, WaveG = 1.0, WaveB = 1.0, WaveA = 0.9,
                    DarkenCenter = true, WaveThick = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(0.97 + Math.Sin(ctx.Time * 0.3) * 0.02 + ctx.Bass * 0.01);
                        ctx.Engine.SetRotation(-0.01 + Math.Cos(ctx.Time * 0.5) * 0.01);
                        ctx.Engine.SetCenterX(0.5 + Math.Sin(ctx.Time * 0.2) * 0.05);
                        ctx.Engine.SetCenterY(0.5 + Math.Cos(ctx.Time * 0.3) * 0.05);
                    },
                    PerVertexCode = vtx =>
                    {
                        double ring = Math.Sin(vtx.Rad * 15) * 0.003;
                        vtx.X += ring * Math.Cos(vtx.Ang);
                        vtx.Y += ring * Math.Sin(vtx.Ang);
                    }
                },

                // 11. Deep Space
                new MilkdropPreset
                {
                    Name = "Deep Space",
                    Decay = 0.985, Zoom = 1.12, Rotation = 0.008, WarpAmount = 0.4,
                    WaveMode = 7, WaveR = 0.6, WaveG = 0.8, WaveB = 1.0, WaveA = 0.5,
                    WaveDots = true, DarkenCenter = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.12 + ctx.Bass * 0.02);
                        ctx.Engine.SetRotation(0.008 + ctx.Treb * 0.002);
                        ctx.Engine.SetWarp(0.4 + Math.Sin(ctx.Time * 0.15) * 0.2);
                    }
                },

                // 12. Acid Trip
                new MilkdropPreset
                {
                    Name = "Acid Trip",
                    Decay = 0.93, Zoom = 1.01, Rotation = 0.0, WarpAmount = 4.0,
                    WaveMode = 4, WaveR = 0.0, WaveG = 1.0, WaveB = 0.0, WaveA = 0.8,
                    AdditiveWaves = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetWarp(4.0 + ctx.Mid * 3.0);
                        ctx.Engine.SetZoom(1.01 + ctx.Bass * 0.03);
                        ctx.Engine.SetRotation(Math.Sin(ctx.Time * 0.3) * 0.05 + ctx.Treb * 0.02);
                        ctx.Engine.SetCenterX(0.5 + Math.Sin(ctx.Time * 0.7) * 0.2);
                        ctx.Engine.SetCenterY(0.5 + Math.Cos(ctx.Time * 0.5) * 0.2);
                        ctx.Engine.SetWaveR(Math.Abs(Math.Sin(ctx.Time * 2)));
                        ctx.Engine.SetWaveG(Math.Abs(Math.Sin(ctx.Time * 2 + 2)));
                        ctx.Engine.SetWaveB(Math.Abs(Math.Sin(ctx.Time * 2 + 4)));
                    },
                    PerVertexCode = vtx =>
                    {
                        vtx.X += Math.Sin(vtx.Y * 20 + vtx.Rad * 5) * 0.008;
                        vtx.Y += Math.Cos(vtx.X * 20 + vtx.Ang * 3) * 0.008;
                    }
                },

                // 13. Morphing Plasma  
                new MilkdropPreset
                {
                    Name = "Morphing Plasma",
                    Decay = 0.96, Zoom = 1.0, Rotation = 0.02, WarpAmount = 2.5,
                    WaveMode = 6, WaveR = 0.7, WaveG = 0.4, WaveB = 1.0, WaveA = 0.4,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetWarp(2.5 + Math.Sin(ctx.Time * 0.4) * 1.0 + ctx.Bass * 0.5);
                        ctx.Engine.SetRotation(0.02 + Math.Sin(ctx.Time * 0.6) * 0.02);
                        ctx.Engine.SetZoom(1.0 + ctx.Bass * 0.015);
                    },
                    PerVertexCode = vtx =>
                    {
                        double plasma = Math.Sin(vtx.U * 10 + vtx.V * 10) * 0.004;
                        vtx.X += plasma;
                        vtx.Y += Math.Cos(vtx.U * 8 - vtx.V * 12) * 0.004;
                    }
                },

                // 14. Crystalline
                new MilkdropPreset
                {
                    Name = "Crystalline",
                    Decay = 0.97, Zoom = 1.03, Rotation = -0.02, WarpAmount = 0.1,
                    WaveMode = 2, WaveR = 0.8, WaveG = 0.9, WaveB = 1.0, WaveA = 0.7,
                    WaveThick = true,
                    OuterBorderR = 0.3, OuterBorderG = 0.5, OuterBorderB = 0.8, OuterBorderA = 0.5, OuterBorderSize = 0.3,
                    InnerBorderR = 0.1, InnerBorderG = 0.2, InnerBorderB = 0.5, InnerBorderA = 0.3, InnerBorderSize = 0.2,
                    MotionVectorsX = 12, MotionVectorsY = 9, MotionVectorsLength = 0.5,
                    MotionVectorsR = 0.3, MotionVectorsG = 0.5, MotionVectorsB = 0.8, MotionVectorsA = 0.15,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetZoom(1.03 + ctx.Bass * 0.01);
                        ctx.Engine.SetRotation(-0.02 + ctx.Treb * 0.01);
                    }
                },

                // 15. Breathing Nebula
                new MilkdropPreset
                {
                    Name = "Breathing Nebula",
                    Decay = 0.98, Zoom = 1.0, Rotation = 0.005, WarpAmount = 1.0,
                    WaveMode = 0, WaveR = 0.8, WaveG = 0.4, WaveB = 1.0, WaveA = 0.6,
                    WaveThick = true, DarkenCenter = true,
                    PerFrameCode = ctx =>
                    {
                        double breath = Math.Sin(ctx.Time * 0.5) * 0.5 + 0.5;
                        ctx.Engine.SetZoom(0.99 + breath * 0.02 + ctx.Bass * 0.01);
                        ctx.Engine.SetWarp(1.0 + breath * 0.5 + ctx.Mid * 0.3);
                        ctx.Engine.SetRotation(0.005 + breath * 0.005);
                        ctx.Engine.SetWaveR(0.5 + breath * 0.3);
                        ctx.Engine.SetWaveB(1.0 - breath * 0.3);
                    }
                },

                // 16. Quantum Foam
                new MilkdropPreset
                {
                    Name = "Quantum Foam",
                    Decay = 0.92, Zoom = 1.0, Rotation = 0.0, WarpAmount = 5.0,
                    WaveMode = 4, WaveR = 0.5, WaveG = 1.0, WaveB = 0.5, WaveA = 0.5,
                    AdditiveWaves = true, WaveDots = true,
                    PerFrameCode = ctx =>
                    {
                        ctx.Engine.SetWarp(5.0 + ctx.Vol * 3.0);
                        ctx.Engine.SetZoom(1.0 + Math.Sin(ctx.Time * 0.8) * 0.02);
                        ctx.Engine.SetRotation(Math.Sin(ctx.Time * 0.3) * 0.03);
                        ctx.Engine.SetCenterX(0.5 + Math.Sin(ctx.Time * 0.6) * 0.1);
                        ctx.Engine.SetCenterY(0.5 + Math.Cos(ctx.Time * 0.7) * 0.1);
                    },
                    PerVertexCode = vtx =>
                    {
                        vtx.X += Math.Sin(vtx.Rad * 20 + vtx.Ang * 5) * 0.01;
                        vtx.Y += Math.Cos(vtx.Rad * 20 - vtx.Ang * 3) * 0.01;
                    }
                },
            };
        }

        /// <summary>
        /// Parse a .milk preset file into a MilkdropPreset.
        /// Handles the INI-style key=value format used by Milkdrop2.
        /// Note: Per-frame/per-vertex equations are stored as strings; only static parameters are fully parsed.
        /// </summary>
        public static MilkdropPreset ParseFromFile(string filePath)
        {
            var preset = new MilkdropPreset();
            preset.Name = Path.GetFileNameWithoutExtension(filePath);

            if (!File.Exists(filePath)) return preset;

            var lines = File.ReadAllLines(filePath);
            var perFrameLines = new List<string>();
            var perVertexLines = new List<string>();
            var perFrameInitLines = new List<string>();

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("//")) continue;

                // Collect per-frame equations
                if (line.StartsWith("per_frame_", StringComparison.OrdinalIgnoreCase))
                {
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        // per_frame_1=zoom = zoom + 0.1*bass
                        string eqLine = line[(eqIdx + 1)..].Trim();
                        perFrameLines.Add(eqLine);
                    }
                    continue;
                }

                if (line.StartsWith("per_pixel_", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("per_vertex_", StringComparison.OrdinalIgnoreCase))
                {
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string eqLine = line[(eqIdx + 1)..].Trim();
                        perVertexLines.Add(eqLine);
                    }
                    continue;
                }

                if (line.StartsWith("per_frame_init_", StringComparison.OrdinalIgnoreCase))
                {
                    int eqIdx = line.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        string eqLine = line[(eqIdx + 1)..].Trim();
                        perFrameInitLines.Add(eqLine);
                    }
                    continue;
                }

                // Parse key=value pairs
                int equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0) continue;

                string key = line[..equalsIndex].Trim().ToLowerInvariant();
                string value = line[(equalsIndex + 1)..].Trim();

                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double dval))
                {
                    // Try integer
                    if (int.TryParse(value, out int ival))
                        dval = ival;
                    else
                        continue; // Skip non-numeric values
                }

                switch (key)
                {
                    // Motion
                    case "fdecay" or "decay": preset.Decay = dval; break;
                    case "fzoom" or "zoom": preset.Zoom = dval; break;
                    case "fzoomexponent" or "zoomexp": preset.ZoomExponent = dval; break;
                    case "frot" or "rot": preset.Rotation = dval; break;
                    case "fwarpamount" or "warp": preset.WarpAmount = dval; break;
                    case "fwarpanimspeed": break; // Ignored for now
                    case "fwarpscale": break;
                    case "fcx" or "cx": preset.CenterX = dval; break;
                    case "fcy" or "cy": preset.CenterY = dval; break;
                    case "fdx" or "dx": preset.TranslateX = dval; break;
                    case "fdy" or "dy": preset.TranslateY = dval; break;
                    case "fsx" or "sx": preset.StretchX = dval; break;
                    case "fsy" or "sy": preset.StretchY = dval; break;

                    // Wave
                    case "nwavemode": preset.WaveMode = (int)dval; break;
                    case "badditivewaves": preset.AdditiveWaves = dval > 0.5; break;
                    case "bwavedots": preset.WaveDots = dval > 0.5; break;
                    case "bwavethick": preset.WaveThick = dval > 0.5; break;
                    case "wave_r": preset.WaveR = dval; break;
                    case "wave_g": preset.WaveG = dval; break;
                    case "wave_b": preset.WaveB = dval; break;
                    case "wave_a": preset.WaveA = dval; break;
                    case "fwavealpha": preset.WaveA = dval; break;

                    // Outer border
                    case "ob_r": preset.OuterBorderR = dval; break;
                    case "ob_g": preset.OuterBorderG = dval; break;
                    case "ob_b": preset.OuterBorderB = dval; break;
                    case "ob_a": preset.OuterBorderA = dval; break;
                    case "ob_size": preset.OuterBorderSize = dval; break;

                    // Inner border
                    case "ib_r": preset.InnerBorderR = dval; break;
                    case "ib_g": preset.InnerBorderG = dval; break;
                    case "ib_b": preset.InnerBorderB = dval; break;
                    case "ib_a": preset.InnerBorderA = dval; break;
                    case "ib_size": preset.InnerBorderSize = dval; break;

                    // Motion vectors
                    case "nmotionvectorsx": preset.MotionVectorsX = dval; break;
                    case "nmotionvectorsy": preset.MotionVectorsY = dval; break;
                    case "mv_dx": preset.MotionVectorsDX = dval; break;
                    case "mv_dy": preset.MotionVectorsDY = dval; break;
                    case "mv_l": preset.MotionVectorsLength = dval; break;
                    case "mv_r": preset.MotionVectorsR = dval; break;
                    case "mv_g": preset.MotionVectorsG = dval; break;
                    case "mv_b": preset.MotionVectorsB = dval; break;
                    case "mv_a": preset.MotionVectorsA = dval; break;

                    // Misc
                    case "bdarkencenter": preset.DarkenCenter = dval > 0.5; break;
                    case "bbrighten": preset.Brighten = dval > 0.5; break;
                    case "bsolarize": preset.Solarize = dval > 0.5; break;
                    case "binvert": preset.Invert = dval > 0.5; break;
                    case "fgammaadjustment" or "fgammaadj": preset.GammaAdj = dval; break;
                }
            }

            if (perFrameLines.Count > 0)
                preset.PerFrameEquations = string.Join(";", perFrameLines);
            if (perVertexLines.Count > 0)
                preset.PerVertexEquations = string.Join(";", perVertexLines);
            if (perFrameInitLines.Count > 0)
                preset.PerFrameInitEquations = string.Join(";", perFrameInitLines);

            return preset;
        }

        /// <summary>
        /// Get preset names for UI display.
        /// </summary>
        public static List<string> GetBuiltInPresetNames()
        {
            return GetBuiltInPresets().Select(p => p.Name).ToList();
        }
    }
}
