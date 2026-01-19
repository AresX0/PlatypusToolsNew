using System;
using System.Collections.Generic;
using System.Linq;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Library of video and audio filters matching Shotcut's filter set.
    /// Each filter includes FFmpeg implementation details.
    /// </summary>
    public static class FilterLibrary
    {
        private static List<Filter>? _allFilters;

        /// <summary>
        /// Gets all available filters.
        /// </summary>
        public static IReadOnlyList<Filter> GetAllFilters()
        {
            _allFilters ??= CreateAllFilters();
            return _allFilters;
        }

        /// <summary>
        /// Gets filters by category.
        /// </summary>
        public static IEnumerable<Filter> GetFiltersByCategory(FilterCategory category)
        {
            return GetAllFilters().Where(f => f.Category == category);
        }

        /// <summary>
        /// Gets filters by media type.
        /// </summary>
        public static IEnumerable<Filter> GetFiltersByMediaType(FilterMediaType mediaType)
        {
            return GetAllFilters().Where(f => f.MediaType == mediaType || f.MediaType == FilterMediaType.Both);
        }

        /// <summary>
        /// Creates a copy of a filter for applying to a clip.
        /// </summary>
        public static Filter CreateInstance(string filterName)
        {
            var template = GetAllFilters().FirstOrDefault(f => f.Name == filterName);
            return template?.Clone() ?? throw new ArgumentException($"Filter not found: {filterName}");
        }

        private static List<Filter> CreateAllFilters()
        {
            var filters = new List<Filter>();

            // === COLOR CORRECTION FILTERS ===
            filters.Add(CreateBrightnessFilter());
            filters.Add(CreateContrastFilter());
            filters.Add(CreateSaturationFilter());
            filters.Add(CreateHueShiftFilter());
            filters.Add(CreateGammaFilter());
            filters.Add(CreateWhiteBalanceFilter());
            filters.Add(CreateColorBalanceFilter());
            filters.Add(CreateExposureFilter());
            filters.Add(CreateHighlightsShadowsFilter());
            filters.Add(CreateLevelsFilter());
            filters.Add(CreateCurvesFilter());

            // === COLOR GRADING FILTERS ===
            filters.Add(CreateLUTFilter());
            filters.Add(CreateVignetteFilter());
            filters.Add(CreateFilmGrainFilter());
            filters.Add(CreateCinematicFilter());
            filters.Add(CreateVintageFilter());
            filters.Add(CreateBleachBypassFilter());
            filters.Add(CreateCrossProcessFilter());
            filters.Add(CreateTealOrangeFilter());
            filters.Add(CreateBlackAndWhiteFilter());
            filters.Add(CreateSepiaFilter());
            filters.Add(CreateColorizeFilter());
            filters.Add(CreateGradientMapFilter());

            // === BLUR FILTERS ===
            filters.Add(CreateGaussianBlurFilter());
            filters.Add(CreateBoxBlurFilter());
            filters.Add(CreateMotionBlurFilter());
            filters.Add(CreateRadialBlurFilter());
            filters.Add(CreateZoomBlurFilter());
            filters.Add(CreateDirectionalBlurFilter());
            filters.Add(CreateLensBlurFilter());
            filters.Add(CreateTiltShiftFilter());

            // === SHARPEN FILTERS ===
            filters.Add(CreateSharpenFilter());
            filters.Add(CreateUnsharpMaskFilter());
            filters.Add(CreateHighPassFilter());

            // === DISTORT FILTERS ===
            filters.Add(CreateLensDistortionFilter());
            filters.Add(CreateFisheyeFilter());
            filters.Add(CreatePerspectiveFilter());
            filters.Add(CreateCornerPinFilter());
            filters.Add(CreateMirrorFilter());
            filters.Add(CreateFlipFilter());
            filters.Add(CreateWaveFilter());
            filters.Add(CreateTwirlFilter());
            filters.Add(CreateBulgeFilter());
            filters.Add(CreatePixelateFilter());

            // === STYLIZE FILTERS ===
            filters.Add(CreateEdgeDetectFilter());
            filters.Add(CreateEmbossFilter());
            filters.Add(CreatePosterizeFilter());
            filters.Add(CreateNegativeFilter());
            filters.Add(CreateSolarizeFilter());
            filters.Add(CreateSketchFilter());
            filters.Add(CreateOilPaintFilter());
            filters.Add(CreateCartoonFilter());
            filters.Add(CreateGlowFilter());
            filters.Add(CreateHalftoneFilter());
            filters.Add(CreateDotPatternFilter());

            // === GENERATE FILTERS ===
            filters.Add(CreateColorFilter());
            filters.Add(CreateGradientFilter());
            filters.Add(CreateNoiseFilter());
            filters.Add(CreateCheckerboardFilter());
            filters.Add(CreateGridFilter());

            // === TIME FILTERS ===
            filters.Add(CreateFreezeFrameFilter());
            filters.Add(CreateReverseFilter());
            filters.Add(CreateSpeedFilter());
            filters.Add(CreateEchoFilter());
            filters.Add(CreateStrobeFilter());

            // === TRANSFORM FILTERS ===
            filters.Add(CreatePositionFilter());
            filters.Add(CreateScaleFilter());
            filters.Add(CreateRotationFilter());
            filters.Add(CreateCropFilter());
            filters.Add(CreatePanZoomFilter());
            filters.Add(CreateStabilizeFilter());

            // === OVERLAY FILTERS ===
            filters.Add(CreateOpacityFilter());
            filters.Add(CreateBlendModeFilter());
            filters.Add(CreateChromaKeyFilter());
            filters.Add(CreateLumaKeyFilter());
            filters.Add(CreateMaskFilter());

            // === AUDIO FILTERS ===
            filters.Add(CreateVolumeFilter());
            filters.Add(CreateFadeAudioFilter());
            filters.Add(CreateNormalizeFilter());
            filters.Add(CreateCompressorFilter());
            filters.Add(CreateLimiterFilter());
            filters.Add(CreateNoiseGateFilter());
            filters.Add(CreateEqualizerFilter());
            filters.Add(CreateBassBoostFilter());
            filters.Add(CreateTrebleFilter());
            filters.Add(CreateReverbFilter());
            filters.Add(CreateDelayFilter());
            filters.Add(CreateChorusFilter());
            filters.Add(CreatePitchShiftFilter());
            filters.Add(CreatePanFilter());
            filters.Add(CreateBalanceFilter());

            return filters;
        }

        #region Color Correction Filters

        private static Filter CreateBrightnessFilter() => new()
        {
            Name = "brightness",
            DisplayName = "Brightness",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "eq",
            Icon = "☀️",
            Description = "Adjusts the overall brightness of the image",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "brightness",
                    DisplayName = "Brightness",
                    Type = FilterParameterType.Double,
                    DefaultValue = 0.0,
                    MinValue = -1.0,
                    MaxValue = 1.0,
                    Step = 0.01
                }
            }
        };

        private static Filter CreateContrastFilter() => new()
        {
            Name = "contrast",
            DisplayName = "Contrast",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "eq",
            Icon = "◐",
            Description = "Adjusts the contrast between light and dark areas",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "contrast",
                    DisplayName = "Contrast",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.0,
                    MaxValue = 3.0,
                    Step = 0.01
                }
            }
        };

        private static Filter CreateSaturationFilter() => new()
        {
            Name = "saturation",
            DisplayName = "Saturation",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "eq",
            Icon = "🎨",
            Description = "Adjusts color intensity",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "saturation",
                    DisplayName = "Saturation",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.0,
                    MaxValue = 3.0,
                    Step = 0.01
                }
            }
        };

        private static Filter CreateHueShiftFilter() => new()
        {
            Name = "hue",
            DisplayName = "Hue / Lightness / Saturation",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "hue",
            Icon = "🌈",
            Description = "Shifts colors around the color wheel",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "h",
                    DisplayName = "Hue",
                    Type = FilterParameterType.Double,
                    DefaultValue = 0.0,
                    MinValue = -180.0,
                    MaxValue = 180.0,
                    Step = 1,
                    Unit = "°"
                },
                new()
                {
                    Name = "s",
                    DisplayName = "Saturation",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.0,
                    MaxValue = 2.0,
                    Step = 0.01
                },
                new()
                {
                    Name = "b",
                    DisplayName = "Brightness",
                    Type = FilterParameterType.Double,
                    DefaultValue = 0.0,
                    MinValue = -1.0,
                    MaxValue = 1.0,
                    Step = 0.01
                }
            }
        };

        private static Filter CreateGammaFilter() => new()
        {
            Name = "gamma",
            DisplayName = "Gamma",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "eq",
            Icon = "📈",
            Description = "Adjusts gamma curve for midtones",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "gamma",
                    DisplayName = "Gamma",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.1,
                    MaxValue = 3.0,
                    Step = 0.01
                },
                new()
                {
                    Name = "gamma_r",
                    DisplayName = "Red Gamma",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.1,
                    MaxValue = 3.0,
                    Step = 0.01
                },
                new()
                {
                    Name = "gamma_g",
                    DisplayName = "Green Gamma",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.1,
                    MaxValue = 3.0,
                    Step = 0.01
                },
                new()
                {
                    Name = "gamma_b",
                    DisplayName = "Blue Gamma",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.1,
                    MaxValue = 3.0,
                    Step = 0.01
                }
            }
        };

        private static Filter CreateWhiteBalanceFilter() => new()
        {
            Name = "white_balance",
            DisplayName = "White Balance",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "colortemperature",
            Icon = "🌡️",
            Description = "Adjusts color temperature and tint",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "temperature",
                    DisplayName = "Temperature",
                    Type = FilterParameterType.Double,
                    DefaultValue = 6500,
                    MinValue = 1000,
                    MaxValue = 15000,
                    Step = 100,
                    Unit = "K"
                },
                new()
                {
                    Name = "mix",
                    DisplayName = "Mix",
                    Type = FilterParameterType.Double,
                    DefaultValue = 1.0,
                    MinValue = 0.0,
                    MaxValue = 1.0,
                    Step = 0.01
                }
            }
        };

        private static Filter CreateColorBalanceFilter() => new()
        {
            Name = "color_balance",
            DisplayName = "Color Balance",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "colorbalance",
            Icon = "⚖️",
            Description = "Adjusts color balance for shadows, midtones, and highlights",
            Parameters = new List<FilterParameter>
            {
                // Shadows
                new() { Name = "rs", DisplayName = "Shadows Red-Cyan", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "gs", DisplayName = "Shadows Green-Magenta", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "bs", DisplayName = "Shadows Blue-Yellow", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                // Midtones
                new() { Name = "rm", DisplayName = "Midtones Red-Cyan", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "gm", DisplayName = "Midtones Green-Magenta", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "bm", DisplayName = "Midtones Blue-Yellow", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                // Highlights
                new() { Name = "rh", DisplayName = "Highlights Red-Cyan", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "gh", DisplayName = "Highlights Green-Magenta", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "bh", DisplayName = "Highlights Blue-Yellow", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateExposureFilter() => new()
        {
            Name = "exposure",
            DisplayName = "Exposure",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "exposure",
            Icon = "📷",
            Description = "Adjusts exposure like a camera",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "exposure",
                    DisplayName = "Exposure",
                    Type = FilterParameterType.Double,
                    DefaultValue = 0.0,
                    MinValue = -3.0,
                    MaxValue = 3.0,
                    Step = 0.1,
                    Unit = "EV"
                },
                new()
                {
                    Name = "black",
                    DisplayName = "Black Level",
                    Type = FilterParameterType.Double,
                    DefaultValue = 0.0,
                    MinValue = -1.0,
                    MaxValue = 1.0,
                    Step = 0.01
                }
            }
        };

        private static Filter CreateHighlightsShadowsFilter() => new()
        {
            Name = "highlights_shadows",
            DisplayName = "Highlights & Shadows",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "eq",
            Icon = "🔆",
            Description = "Separately adjusts highlights and shadows",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "highlights", DisplayName = "Highlights", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "shadows", DisplayName = "Shadows", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateLevelsFilter() => new()
        {
            Name = "levels",
            DisplayName = "Levels",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "colorlevels",
            Icon = "📊",
            Description = "Adjusts input and output levels",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "rimin", DisplayName = "Red In Min", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "rimax", DisplayName = "Red In Max", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "gimin", DisplayName = "Green In Min", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "gimax", DisplayName = "Green In Max", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "bimin", DisplayName = "Blue In Min", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "bimax", DisplayName = "Blue In Max", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateCurvesFilter() => new()
        {
            Name = "curves",
            DisplayName = "Color Curves",
            Category = FilterCategory.ColorCorrection,
            FFmpegFilterName = "curves",
            Icon = "〰️",
            Description = "Advanced color correction using curves"
        };

        #endregion

        #region Color Grading Filters

        private static Filter CreateLUTFilter() => new()
        {
            Name = "lut",
            DisplayName = "LUT (3D)",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "lut3d",
            Icon = "🎬",
            Description = "Apply a 3D LUT file for color grading",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "file", DisplayName = "LUT File", Type = FilterParameterType.File },
                new() { Name = "interp", DisplayName = "Interpolation", Type = FilterParameterType.Enum, Options = new List<string> { "nearest", "trilinear", "tetrahedral" }, DefaultValue = "trilinear" }
            }
        };

        private static Filter CreateVignetteFilter() => new()
        {
            Name = "vignette",
            DisplayName = "Vignette",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "vignette",
            Icon = "🔲",
            Description = "Darkens the edges of the frame",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "angle", DisplayName = "Angle", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "x0", DisplayName = "Center X", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "y0", DisplayName = "Center Y", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateFilmGrainFilter() => new()
        {
            Name = "film_grain",
            DisplayName = "Film Grain",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "noise",
            Icon = "🎞️",
            Description = "Adds film-like grain texture",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "alls", DisplayName = "Grain Amount", Type = FilterParameterType.Integer, DefaultValue = 10, MinValue = 0, MaxValue = 100 },
                new() { Name = "allf", DisplayName = "Grain Type", Type = FilterParameterType.Enum, Options = new List<string> { "a", "p", "t", "u" }, DefaultValue = "t" }
            }
        };

        private static Filter CreateCinematicFilter() => new()
        {
            Name = "cinematic",
            DisplayName = "Cinematic Look",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "eq",
            Icon = "🎥",
            Description = "Hollywood-style color grading",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "intensity", DisplayName = "Intensity", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "shadows_blue", DisplayName = "Blue Shadows", Type = FilterParameterType.Double, DefaultValue = 0.3, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "highlights_orange", DisplayName = "Orange Highlights", Type = FilterParameterType.Double, DefaultValue = 0.3, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateVintageFilter() => new()
        {
            Name = "vintage",
            DisplayName = "Vintage",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "curves",
            Icon = "📻",
            Description = "Retro/vintage color look"
        };

        private static Filter CreateBleachBypassFilter() => new()
        {
            Name = "bleach_bypass",
            DisplayName = "Bleach Bypass",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "colorchannelmixer",
            Icon = "🧪",
            Description = "Film processing technique for desaturated high-contrast look"
        };

        private static Filter CreateCrossProcessFilter() => new()
        {
            Name = "cross_process",
            DisplayName = "Cross Process",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "curves",
            Icon = "🔄",
            Description = "Film cross-processing effect"
        };

        private static Filter CreateTealOrangeFilter() => new()
        {
            Name = "teal_orange",
            DisplayName = "Teal & Orange",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "colorbalance",
            Icon = "🎭",
            Description = "Popular Hollywood color grading style"
        };

        private static Filter CreateBlackAndWhiteFilter() => new()
        {
            Name = "black_white",
            DisplayName = "Black & White",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "hue",
            Icon = "⬛",
            Description = "Converts to black and white with channel mixing",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "red", DisplayName = "Red", Type = FilterParameterType.Double, DefaultValue = 0.33, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "green", DisplayName = "Green", Type = FilterParameterType.Double, DefaultValue = 0.33, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "blue", DisplayName = "Blue", Type = FilterParameterType.Double, DefaultValue = 0.33, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateSepiaFilter() => new()
        {
            Name = "sepia",
            DisplayName = "Sepia",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "colorchannelmixer",
            Icon = "🟤",
            Description = "Classic sepia tone effect"
        };

        private static Filter CreateColorizeFilter() => new()
        {
            Name = "colorize",
            DisplayName = "Colorize",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "colorize",
            Icon = "🖌️",
            Description = "Tints the image with a color",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "hue", DisplayName = "Hue", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 360.0, Unit = "°" },
                new() { Name = "saturation", DisplayName = "Saturation", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "lightness", DisplayName = "Lightness", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateGradientMapFilter() => new()
        {
            Name = "gradient_map",
            DisplayName = "Gradient Map",
            Category = FilterCategory.ColorGrading,
            FFmpegFilterName = "pseudocolor",
            Icon = "🌈",
            Description = "Maps luminance to a color gradient"
        };

        #endregion

        #region Blur Filters

        private static Filter CreateGaussianBlurFilter() => new()
        {
            Name = "gaussian_blur",
            DisplayName = "Gaussian Blur",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "gblur",
            Icon = "💨",
            Description = "Smooth blur effect",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "sigma", DisplayName = "Blur Amount", Type = FilterParameterType.Double, DefaultValue = 5.0, MinValue = 0.0, MaxValue = 50.0 }
            }
        };

        private static Filter CreateBoxBlurFilter() => new()
        {
            Name = "box_blur",
            DisplayName = "Box Blur",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "boxblur",
            Icon = "📦",
            Description = "Fast box blur",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "luma_radius", DisplayName = "Radius", Type = FilterParameterType.Integer, DefaultValue = 5, MinValue = 0, MaxValue = 100 }
            }
        };

        private static Filter CreateMotionBlurFilter() => new()
        {
            Name = "motion_blur",
            DisplayName = "Motion Blur",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "tblend",
            Icon = "🏃",
            Description = "Simulates motion blur"
        };

        private static Filter CreateRadialBlurFilter() => new()
        {
            Name = "radial_blur",
            DisplayName = "Radial Blur",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "v360",
            Icon = "🌀",
            Description = "Blur radiating from center"
        };

        private static Filter CreateZoomBlurFilter() => new()
        {
            Name = "zoom_blur",
            DisplayName = "Zoom Blur",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "v360",
            Icon = "🔍",
            Description = "Zoom/speed lines blur effect"
        };

        private static Filter CreateDirectionalBlurFilter() => new()
        {
            Name = "directional_blur",
            DisplayName = "Directional Blur",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "avgblur",
            Icon = "➡️",
            Description = "Blur in a specific direction"
        };

        private static Filter CreateLensBlurFilter() => new()
        {
            Name = "lens_blur",
            DisplayName = "Lens Blur (Bokeh)",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "gblur",
            Icon = "📷",
            Description = "Simulates camera lens bokeh"
        };

        private static Filter CreateTiltShiftFilter() => new()
        {
            Name = "tilt_shift",
            DisplayName = "Tilt-Shift",
            Category = FilterCategory.Blur,
            FFmpegFilterName = "gblur",
            Icon = "🏙️",
            Description = "Miniature/diorama effect",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "focus_position", DisplayName = "Focus Position", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "focus_size", DisplayName = "Focus Size", Type = FilterParameterType.Double, DefaultValue = 0.2, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "blur_amount", DisplayName = "Blur Amount", Type = FilterParameterType.Double, DefaultValue = 10.0, MinValue = 0.0, MaxValue = 50.0 }
            }
        };

        #endregion

        #region Sharpen Filters

        private static Filter CreateSharpenFilter() => new()
        {
            Name = "sharpen",
            DisplayName = "Sharpen",
            Category = FilterCategory.Sharpen,
            FFmpegFilterName = "unsharp",
            Icon = "🔪",
            Description = "Sharpens the image",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "luma_amount", DisplayName = "Amount", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.0, MaxValue = 5.0 }
            }
        };

        private static Filter CreateUnsharpMaskFilter() => new()
        {
            Name = "unsharp_mask",
            DisplayName = "Unsharp Mask",
            Category = FilterCategory.Sharpen,
            FFmpegFilterName = "unsharp",
            Icon = "🔬",
            Description = "Professional unsharp mask"
        };

        private static Filter CreateHighPassFilter() => new()
        {
            Name = "high_pass",
            DisplayName = "High Pass",
            Category = FilterCategory.Sharpen,
            FFmpegFilterName = "highpass",
            Icon = "📶",
            Description = "High-pass frequency filter"
        };

        #endregion

        #region Distort Filters

        private static Filter CreateLensDistortionFilter() => new()
        {
            Name = "lens_distortion",
            DisplayName = "Lens Distortion",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "lenscorrection",
            Icon = "🔭",
            Description = "Barrel/pincushion distortion correction"
        };

        private static Filter CreateFisheyeFilter() => new()
        {
            Name = "fisheye",
            DisplayName = "Fisheye",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "v360",
            Icon = "🐟",
            Description = "Fisheye lens effect"
        };

        private static Filter CreatePerspectiveFilter() => new()
        {
            Name = "perspective",
            DisplayName = "Perspective",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "perspective",
            Icon = "📐",
            Description = "3D perspective transform"
        };

        private static Filter CreateCornerPinFilter() => new()
        {
            Name = "corner_pin",
            DisplayName = "Corner Pin",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "perspective",
            Icon = "📍",
            Description = "Four-corner distortion for screen replacement"
        };

        private static Filter CreateMirrorFilter() => new()
        {
            Name = "mirror",
            DisplayName = "Mirror",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "hflip,split[main][flip];[flip]crop=iw/2:ih:iw/2:0[right];[main]crop=iw/2:ih:0:0[left];[right][left]hstack",
            Icon = "🪞",
            Description = "Mirror effect"
        };

        private static Filter CreateFlipFilter() => new()
        {
            Name = "flip",
            DisplayName = "Flip",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "hflip",
            Icon = "↔️",
            Description = "Flip horizontally or vertically",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "horizontal", DisplayName = "Horizontal", Type = FilterParameterType.Boolean, DefaultValue = true },
                new() { Name = "vertical", DisplayName = "Vertical", Type = FilterParameterType.Boolean, DefaultValue = false }
            }
        };

        private static Filter CreateWaveFilter() => new()
        {
            Name = "wave",
            DisplayName = "Wave",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "sine",
            Icon = "🌊",
            Description = "Wave distortion effect"
        };

        private static Filter CreateTwirlFilter() => new()
        {
            Name = "twirl",
            DisplayName = "Twirl",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "v360",
            Icon = "🌀",
            Description = "Twirl/spiral distortion"
        };

        private static Filter CreateBulgeFilter() => new()
        {
            Name = "bulge",
            DisplayName = "Bulge",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "lenscorrection",
            Icon = "🎈",
            Description = "Bulge/pinch distortion"
        };

        private static Filter CreatePixelateFilter() => new()
        {
            Name = "pixelate",
            DisplayName = "Pixelate",
            Category = FilterCategory.Distort,
            FFmpegFilterName = "scale",
            Icon = "🟩",
            Description = "Pixelates the image",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "size", DisplayName = "Pixel Size", Type = FilterParameterType.Integer, DefaultValue = 10, MinValue = 2, MaxValue = 100 }
            }
        };

        #endregion

        #region Stylize Filters

        private static Filter CreateEdgeDetectFilter() => new()
        {
            Name = "edge_detect",
            DisplayName = "Edge Detect",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "edgedetect",
            Icon = "📝",
            Description = "Detects and highlights edges"
        };

        private static Filter CreateEmbossFilter() => new()
        {
            Name = "emboss",
            DisplayName = "Emboss",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "convolution",
            Icon = "🏛️",
            Description = "3D embossed effect"
        };

        private static Filter CreatePosterizeFilter() => new()
        {
            Name = "posterize",
            DisplayName = "Posterize",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "posterize",
            Icon = "🖼️",
            Description = "Reduces color levels",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "levels", DisplayName = "Color Levels", Type = FilterParameterType.Integer, DefaultValue = 4, MinValue = 2, MaxValue = 32 }
            }
        };

        private static Filter CreateNegativeFilter() => new()
        {
            Name = "negative",
            DisplayName = "Negative",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "negate",
            Icon = "🔄",
            Description = "Inverts colors"
        };

        private static Filter CreateSolarizeFilter() => new()
        {
            Name = "solarize",
            DisplayName = "Solarize",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "solarize",
            Icon = "☀️",
            Description = "Solarization effect"
        };

        private static Filter CreateSketchFilter() => new()
        {
            Name = "sketch",
            DisplayName = "Sketch",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "edgedetect",
            Icon = "✏️",
            Description = "Pencil sketch effect"
        };

        private static Filter CreateOilPaintFilter() => new()
        {
            Name = "oil_paint",
            DisplayName = "Oil Paint",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "convolution",
            Icon = "🎨",
            Description = "Oil painting effect"
        };

        private static Filter CreateCartoonFilter() => new()
        {
            Name = "cartoon",
            DisplayName = "Cartoon",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "edgedetect",
            Icon = "📺",
            Description = "Cartoon/cel-shading effect"
        };

        private static Filter CreateGlowFilter() => new()
        {
            Name = "glow",
            DisplayName = "Glow",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "gblur",
            Icon = "✨",
            Description = "Soft glow effect",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "intensity", DisplayName = "Intensity", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "radius", DisplayName = "Radius", Type = FilterParameterType.Double, DefaultValue = 10.0, MinValue = 0.0, MaxValue = 50.0 }
            }
        };

        private static Filter CreateHalftoneFilter() => new()
        {
            Name = "halftone",
            DisplayName = "Halftone",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "format",
            Icon = "📰",
            Description = "Newspaper halftone effect"
        };

        private static Filter CreateDotPatternFilter() => new()
        {
            Name = "dot_pattern",
            DisplayName = "Dot Pattern",
            Category = FilterCategory.Stylize,
            FFmpegFilterName = "format",
            Icon = "⚫",
            Description = "Dot pattern overlay"
        };

        #endregion

        #region Generate Filters

        private static Filter CreateColorFilter() => new()
        {
            Name = "color",
            DisplayName = "Solid Color",
            Category = FilterCategory.Generate,
            FFmpegFilterName = "color",
            Icon = "🟥",
            Description = "Generates a solid color",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "color", DisplayName = "Color", Type = FilterParameterType.Color, DefaultValue = "#000000" }
            }
        };

        private static Filter CreateGradientFilter() => new()
        {
            Name = "gradient",
            DisplayName = "Gradient",
            Category = FilterCategory.Generate,
            FFmpegFilterName = "gradients",
            Icon = "🌈",
            Description = "Generates a color gradient"
        };

        private static Filter CreateNoiseFilter() => new()
        {
            Name = "noise",
            DisplayName = "Noise",
            Category = FilterCategory.Generate,
            FFmpegFilterName = "geq",
            Icon = "📺",
            Description = "Generates noise pattern"
        };

        private static Filter CreateCheckerboardFilter() => new()
        {
            Name = "checkerboard",
            DisplayName = "Checkerboard",
            Category = FilterCategory.Generate,
            FFmpegFilterName = "testsrc",
            Icon = "♟️",
            Description = "Generates a checkerboard pattern"
        };

        private static Filter CreateGridFilter() => new()
        {
            Name = "grid",
            DisplayName = "Grid",
            Category = FilterCategory.Generate,
            FFmpegFilterName = "drawgrid",
            Icon = "🔲",
            Description = "Draws a grid overlay"
        };

        #endregion

        #region Time Filters

        private static Filter CreateFreezeFrameFilter() => new()
        {
            Name = "freeze_frame",
            DisplayName = "Freeze Frame",
            Category = FilterCategory.Time,
            FFmpegFilterName = "tpad",
            Icon = "⏸️",
            Description = "Freezes a specific frame"
        };

        private static Filter CreateReverseFilter() => new()
        {
            Name = "reverse",
            DisplayName = "Reverse",
            Category = FilterCategory.Time,
            FFmpegFilterName = "reverse",
            Icon = "⏪",
            Description = "Plays the clip in reverse"
        };

        private static Filter CreateSpeedFilter() => new()
        {
            Name = "speed",
            DisplayName = "Speed",
            Category = FilterCategory.Time,
            FFmpegFilterName = "setpts",
            Icon = "⏩",
            Description = "Changes playback speed",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "speed", DisplayName = "Speed", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.1, MaxValue = 10.0 }
            }
        };

        private static Filter CreateEchoFilter() => new()
        {
            Name = "echo",
            DisplayName = "Echo / Trail",
            Category = FilterCategory.Time,
            FFmpegFilterName = "tblend",
            Icon = "👻",
            Description = "Creates ghosting/trail effect"
        };

        private static Filter CreateStrobeFilter() => new()
        {
            Name = "strobe",
            DisplayName = "Strobe",
            Category = FilterCategory.Time,
            FFmpegFilterName = "select",
            Icon = "💡",
            Description = "Strobe/flash effect"
        };

        #endregion

        #region Transform Filters

        private static Filter CreatePositionFilter() => new()
        {
            Name = "position",
            DisplayName = "Position",
            Category = FilterCategory.Transform,
            FFmpegFilterName = "overlay",
            Icon = "📍",
            Description = "Moves the clip position",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "x", DisplayName = "X Position", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 },
                new() { Name = "y", DisplayName = "Y Position", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -1.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateScaleFilter() => new()
        {
            Name = "scale",
            DisplayName = "Scale",
            Category = FilterCategory.Transform,
            FFmpegFilterName = "scale",
            Icon = "🔍",
            Description = "Scales the clip size",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "scale_x", DisplayName = "Scale X", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0 },
                new() { Name = "scale_y", DisplayName = "Scale Y", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.1, MaxValue = 5.0 }
            }
        };

        private static Filter CreateRotationFilter() => new()
        {
            Name = "rotation",
            DisplayName = "Rotation",
            Category = FilterCategory.Transform,
            FFmpegFilterName = "rotate",
            Icon = "🔄",
            Description = "Rotates the clip",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "angle", DisplayName = "Angle", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = -360.0, MaxValue = 360.0, Unit = "°" }
            }
        };

        private static Filter CreateCropFilter() => new()
        {
            Name = "crop",
            DisplayName = "Crop",
            Category = FilterCategory.Transform,
            FFmpegFilterName = "crop",
            Icon = "✂️",
            Description = "Crops the frame",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "left", DisplayName = "Left", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 0.5 },
                new() { Name = "right", DisplayName = "Right", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 0.5 },
                new() { Name = "top", DisplayName = "Top", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 0.5 },
                new() { Name = "bottom", DisplayName = "Bottom", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 0.5 }
            }
        };

        private static Filter CreatePanZoomFilter() => new()
        {
            Name = "pan_zoom",
            DisplayName = "Size, Position & Rotate",
            Category = FilterCategory.Transform,
            FFmpegFilterName = "zoompan",
            Icon = "🎯",
            Description = "Ken Burns style pan and zoom",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "zoom", DisplayName = "Zoom", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.5, MaxValue = 5.0 },
                new() { Name = "x", DisplayName = "X", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "y", DisplayName = "Y", Type = FilterParameterType.Double, DefaultValue = 0.5, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateStabilizeFilter() => new()
        {
            Name = "stabilize",
            DisplayName = "Stabilize",
            Category = FilterCategory.Transform,
            FFmpegFilterName = "vidstabdetect",
            Icon = "📹",
            Description = "Stabilizes shaky footage"
        };

        #endregion

        #region Overlay Filters

        private static Filter CreateOpacityFilter() => new()
        {
            Name = "opacity",
            DisplayName = "Opacity",
            Category = FilterCategory.Overlay,
            FFmpegFilterName = "format",
            Icon = "👁️",
            Description = "Adjusts clip transparency",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "opacity", DisplayName = "Opacity", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateBlendModeFilter() => new()
        {
            Name = "blend_mode",
            DisplayName = "Blend Mode",
            Category = FilterCategory.Overlay,
            FFmpegFilterName = "blend",
            Icon = "🎭",
            Description = "Changes how layers blend",
            Parameters = new List<FilterParameter>
            {
                new()
                {
                    Name = "mode",
                    DisplayName = "Mode",
                    Type = FilterParameterType.Enum,
                    Options = new List<string>
                    {
                        "normal", "multiply", "screen", "overlay", "darken", "lighten",
                        "color-dodge", "color-burn", "hard-light", "soft-light",
                        "difference", "exclusion", "hue", "saturation", "color", "luminosity"
                    },
                    DefaultValue = "normal"
                }
            }
        };

        private static Filter CreateChromaKeyFilter() => new()
        {
            Name = "chroma_key",
            DisplayName = "Chroma Key (Green Screen)",
            Category = FilterCategory.Overlay,
            FFmpegFilterName = "chromakey",
            Icon = "🟢",
            Description = "Removes a color (green screen)",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "color", DisplayName = "Key Color", Type = FilterParameterType.Color, DefaultValue = "#00FF00" },
                new() { Name = "similarity", DisplayName = "Similarity", Type = FilterParameterType.Double, DefaultValue = 0.1, MinValue = 0.0, MaxValue = 1.0 },
                new() { Name = "blend", DisplayName = "Blend", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 1.0 }
            }
        };

        private static Filter CreateLumaKeyFilter() => new()
        {
            Name = "luma_key",
            DisplayName = "Luma Key",
            Category = FilterCategory.Overlay,
            FFmpegFilterName = "lumakey",
            Icon = "⬛",
            Description = "Removes based on brightness"
        };

        private static Filter CreateMaskFilter() => new()
        {
            Name = "mask",
            DisplayName = "Mask",
            Category = FilterCategory.Overlay,
            FFmpegFilterName = "alphaextract",
            Icon = "🎭",
            Description = "Applies a mask to the clip"
        };

        #endregion

        #region Audio Filters

        private static Filter CreateVolumeFilter() => new()
        {
            Name = "volume",
            DisplayName = "Volume",
            Category = FilterCategory.AudioLevel,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "volume",
            Icon = "🔊",
            Description = "Adjusts audio volume",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "volume", DisplayName = "Volume", Type = FilterParameterType.Double, DefaultValue = 1.0, MinValue = 0.0, MaxValue = 3.0 }
            }
        };

        private static Filter CreateFadeAudioFilter() => new()
        {
            Name = "audio_fade",
            DisplayName = "Fade In/Out",
            Category = FilterCategory.AudioLevel,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "afade",
            Icon = "📉",
            Description = "Audio fade in/out",
            Parameters = new List<FilterParameter>
            {
                new() { Name = "fade_in", DisplayName = "Fade In (s)", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 10.0 },
                new() { Name = "fade_out", DisplayName = "Fade Out (s)", Type = FilterParameterType.Double, DefaultValue = 0.0, MinValue = 0.0, MaxValue = 10.0 }
            }
        };

        private static Filter CreateNormalizeFilter() => new()
        {
            Name = "normalize",
            DisplayName = "Normalize",
            Category = FilterCategory.AudioLevel,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "loudnorm",
            Icon = "📊",
            Description = "Normalizes audio levels"
        };

        private static Filter CreateCompressorFilter() => new()
        {
            Name = "compressor",
            DisplayName = "Compressor",
            Category = FilterCategory.AudioDynamics,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "acompressor",
            Icon = "📈",
            Description = "Dynamic range compression"
        };

        private static Filter CreateLimiterFilter() => new()
        {
            Name = "limiter",
            DisplayName = "Limiter",
            Category = FilterCategory.AudioDynamics,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "alimiter",
            Icon = "🛑",
            Description = "Prevents audio clipping"
        };

        private static Filter CreateNoiseGateFilter() => new()
        {
            Name = "noise_gate",
            DisplayName = "Noise Gate",
            Category = FilterCategory.AudioDynamics,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "agate",
            Icon = "🚪",
            Description = "Reduces background noise"
        };

        private static Filter CreateEqualizerFilter() => new()
        {
            Name = "equalizer",
            DisplayName = "Equalizer",
            Category = FilterCategory.AudioEQ,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "equalizer",
            Icon = "🎚️",
            Description = "Multi-band equalizer"
        };

        private static Filter CreateBassBoostFilter() => new()
        {
            Name = "bass_boost",
            DisplayName = "Bass",
            Category = FilterCategory.AudioEQ,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "bass",
            Icon = "🔈",
            Description = "Boost or cut bass frequencies"
        };

        private static Filter CreateTrebleFilter() => new()
        {
            Name = "treble",
            DisplayName = "Treble",
            Category = FilterCategory.AudioEQ,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "treble",
            Icon = "🔔",
            Description = "Boost or cut treble frequencies"
        };

        private static Filter CreateReverbFilter() => new()
        {
            Name = "reverb",
            DisplayName = "Reverb",
            Category = FilterCategory.AudioEffect,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "aecho",
            Icon = "🏛️",
            Description = "Adds room reverb"
        };

        private static Filter CreateDelayFilter() => new()
        {
            Name = "delay",
            DisplayName = "Delay / Echo",
            Category = FilterCategory.AudioEffect,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "aecho",
            Icon = "🔁",
            Description = "Delay/echo effect"
        };

        private static Filter CreateChorusFilter() => new()
        {
            Name = "chorus",
            DisplayName = "Chorus",
            Category = FilterCategory.AudioEffect,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "chorus",
            Icon = "🎤",
            Description = "Chorus effect"
        };

        private static Filter CreatePitchShiftFilter() => new()
        {
            Name = "pitch_shift",
            DisplayName = "Pitch Shift",
            Category = FilterCategory.AudioEffect,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "asetrate",
            Icon = "🎵",
            Description = "Changes audio pitch"
        };

        private static Filter CreatePanFilter() => new()
        {
            Name = "pan",
            DisplayName = "Pan",
            Category = FilterCategory.AudioSpatial,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "pan",
            Icon = "↔️",
            Description = "Stereo panning"
        };

        private static Filter CreateBalanceFilter() => new()
        {
            Name = "balance",
            DisplayName = "Balance",
            Category = FilterCategory.AudioSpatial,
            MediaType = FilterMediaType.Audio,
            FFmpegFilterName = "stereotools",
            Icon = "⚖️",
            Description = "Left/right balance"
        };

        #endregion
    }
}
