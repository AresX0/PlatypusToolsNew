using System;
using System.Collections.Generic;

namespace PlatypusTools.Core.Models.Video
{
    /// <summary>
    /// Represents a transition between two clips.
    /// </summary>
    public class Transition
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        /// <summary>
        /// Display name of the transition.
        /// </summary>
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Category of the transition.
        /// </summary>
        public TransitionCategory Category { get; set; } = TransitionCategory.Fade;
        
        /// <summary>
        /// Specific type of transition.
        /// </summary>
        public TransitionType Type { get; set; } = TransitionType.CrossDissolve;
        
        /// <summary>
        /// Duration of the transition.
        /// </summary>
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(0.5);
        
        /// <summary>
        /// Easing function for the transition.
        /// </summary>
        public EasingType Easing { get; set; } = EasingType.EaseInOut;
        
        /// <summary>
        /// Transition-specific parameters.
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; } = new();
        
        /// <summary>
        /// FFmpeg filter string for this transition.
        /// </summary>
        public string FFmpegFilter { get; set; } = string.Empty;
        
        /// <summary>
        /// Preview thumbnail path.
        /// </summary>
        public string PreviewPath { get; set; } = string.Empty;
    }

    /// <summary>
    /// Categories of transitions.
    /// </summary>
    public enum TransitionCategory
    {
        Fade,
        Wipe,
        Slide,
        Zoom,
        ThreeD,
        Blur,
        Custom
    }

    /// <summary>
    /// Specific transition types.
    /// </summary>
    public enum TransitionType
    {
        // Fade
        FadeIn,
        FadeOut,
        CrossDissolve,
        DipToBlack,
        DipToWhite,
        
        // Wipe
        WipeLeft,
        WipeRight,
        WipeUp,
        WipeDown,
        WipeDiagonal,
        WipeCircle,
        WipeHeart,
        WipeStar,
        
        // Slide
        SlideLeft,
        SlideRight,
        SlideUp,
        SlideDown,
        Push,
        Cover,
        Reveal,
        
        // Zoom
        ZoomIn,
        ZoomOut,
        CrossZoom,
        
        // 3D
        Cube,
        Flip,
        PageCurl,
        Rotate,
        
        // Blur
        GaussianBlur,
        MotionBlur,
        RadialBlur,
        
        // Custom
        Custom
    }

    /// <summary>
    /// Factory for creating transitions with proper FFmpeg filters.
    /// </summary>
    public static class TransitionFactory
    {
        /// <summary>
        /// Creates a transition with default settings.
        /// </summary>
        public static Transition Create(TransitionType type, TimeSpan? duration = null)
        {
            var transition = new Transition
            {
                Type = type,
                Duration = duration ?? TimeSpan.FromSeconds(0.5),
                Name = GetTransitionName(type),
                Category = GetCategory(type),
                FFmpegFilter = GetFFmpegFilter(type)
            };
            return transition;
        }

        private static string GetTransitionName(TransitionType type) => type switch
        {
            TransitionType.FadeIn => "Fade In",
            TransitionType.FadeOut => "Fade Out",
            TransitionType.CrossDissolve => "Cross Dissolve",
            TransitionType.DipToBlack => "Dip to Black",
            TransitionType.DipToWhite => "Dip to White",
            TransitionType.WipeLeft => "Wipe Left",
            TransitionType.WipeRight => "Wipe Right",
            TransitionType.WipeUp => "Wipe Up",
            TransitionType.WipeDown => "Wipe Down",
            TransitionType.SlideLeft => "Slide Left",
            TransitionType.SlideRight => "Slide Right",
            TransitionType.Push => "Push",
            TransitionType.ZoomIn => "Zoom In",
            TransitionType.ZoomOut => "Zoom Out",
            TransitionType.Cube => "3D Cube",
            TransitionType.Flip => "3D Flip",
            TransitionType.PageCurl => "Page Curl",
            _ => type.ToString()
        };

        private static TransitionCategory GetCategory(TransitionType type) => type switch
        {
            TransitionType.FadeIn or TransitionType.FadeOut or TransitionType.CrossDissolve or
            TransitionType.DipToBlack or TransitionType.DipToWhite => TransitionCategory.Fade,
            
            TransitionType.WipeLeft or TransitionType.WipeRight or TransitionType.WipeUp or
            TransitionType.WipeDown or TransitionType.WipeDiagonal or TransitionType.WipeCircle or
            TransitionType.WipeHeart or TransitionType.WipeStar => TransitionCategory.Wipe,
            
            TransitionType.SlideLeft or TransitionType.SlideRight or TransitionType.SlideUp or
            TransitionType.SlideDown or TransitionType.Push or TransitionType.Cover or
            TransitionType.Reveal => TransitionCategory.Slide,
            
            TransitionType.ZoomIn or TransitionType.ZoomOut or TransitionType.CrossZoom => TransitionCategory.Zoom,
            
            TransitionType.Cube or TransitionType.Flip or TransitionType.PageCurl or
            TransitionType.Rotate => TransitionCategory.ThreeD,
            
            TransitionType.GaussianBlur or TransitionType.MotionBlur or
            TransitionType.RadialBlur => TransitionCategory.Blur,
            
            _ => TransitionCategory.Custom
        };

        private static string GetFFmpegFilter(TransitionType type) => type switch
        {
            TransitionType.CrossDissolve => "xfade=transition=fade:duration={duration}:offset={offset}",
            TransitionType.WipeLeft => "xfade=transition=wipeleft:duration={duration}:offset={offset}",
            TransitionType.WipeRight => "xfade=transition=wiperight:duration={duration}:offset={offset}",
            TransitionType.WipeUp => "xfade=transition=wipeup:duration={duration}:offset={offset}",
            TransitionType.WipeDown => "xfade=transition=wipedown:duration={duration}:offset={offset}",
            TransitionType.SlideLeft => "xfade=transition=slideleft:duration={duration}:offset={offset}",
            TransitionType.SlideRight => "xfade=transition=slideright:duration={duration}:offset={offset}",
            TransitionType.SlideUp => "xfade=transition=slideup:duration={duration}:offset={offset}",
            TransitionType.SlideDown => "xfade=transition=slidedown:duration={duration}:offset={offset}",
            TransitionType.WipeCircle => "xfade=transition=circleopen:duration={duration}:offset={offset}",
            TransitionType.ZoomIn => "xfade=transition=zoomin:duration={duration}:offset={offset}",
            TransitionType.DipToBlack => "xfade=transition=fadeblack:duration={duration}:offset={offset}",
            TransitionType.DipToWhite => "xfade=transition=fadewhite:duration={duration}:offset={offset}",
            TransitionType.FadeIn => "fade=t=in:st=0:d={duration}",
            TransitionType.FadeOut => "fade=t=out:st={offset}:d={duration}",
            _ => "xfade=transition=fade:duration={duration}:offset={offset}"
        };

        /// <summary>
        /// Gets all available transitions grouped by category.
        /// </summary>
        public static Dictionary<TransitionCategory, List<Transition>> GetAllTransitions()
        {
            var result = new Dictionary<TransitionCategory, List<Transition>>();
            
            foreach (TransitionType type in Enum.GetValues(typeof(TransitionType)))
            {
                var transition = Create(type);
                if (!result.ContainsKey(transition.Category))
                    result[transition.Category] = new List<Transition>();
                result[transition.Category].Add(transition);
            }
            
            return result;
        }
    }
}
