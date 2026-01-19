using System;
using System.Collections.Generic;
using System.Linq;
using PlatypusTools.Core.Models.Video;

namespace PlatypusTools.Core.Services.Video
{
    /// <summary>
    /// Service for interpolating keyframe values with various easing functions.
    /// Supports Bezier curve interpolation for smooth animations.
    /// </summary>
    public class KeyframeInterpolator
    {
        /// <summary>
        /// Gets the interpolated value at a specific time.
        /// </summary>
        /// <param name="keyframes">List of keyframes (must be sorted by time).</param>
        /// <param name="time">Time to evaluate.</param>
        /// <param name="defaultValue">Value if no keyframes exist.</param>
        /// <returns>Interpolated value.</returns>
        public static double GetValue(IList<AnimatedKeyframe> keyframes, TimeSpan time, double defaultValue = 0)
        {
            if (keyframes == null || keyframes.Count == 0)
                return defaultValue;

            if (keyframes.Count == 1)
                return keyframes[0].Value;

            // Find surrounding keyframes
            AnimatedKeyframe? before = null;
            AnimatedKeyframe? after = null;

            for (int i = 0; i < keyframes.Count; i++)
            {
                if (keyframes[i].Time <= time)
                {
                    before = keyframes[i];
                }
                else
                {
                    after = keyframes[i];
                    break;
                }
            }

            // Before first keyframe
            if (before == null)
                return keyframes[0].Value;

            // After last keyframe
            if (after == null)
                return keyframes[^1].Value;

            // Interpolate between before and after
            double t = (time - before.Time).TotalMilliseconds / (after.Time - before.Time).TotalMilliseconds;
            t = Math.Clamp(t, 0, 1);

            double easedT = ApplyEasing(t, before.Easing, before.ControlPoint1, before.ControlPoint2);

            return Lerp(before.Value, after.Value, easedT);
        }

        /// <summary>
        /// Gets all property values at a specific time for a clip.
        /// </summary>
        public static Dictionary<string, double> GetAllValues(IList<KeyframeTrack> tracks, TimeSpan time)
        {
            var result = new Dictionary<string, double>();

            foreach (var track in tracks)
            {
                result[track.Property] = GetValue(track.Keyframes, time, track.DefaultValue);
            }

            return result;
        }

        /// <summary>
        /// Gets the interpolated transform at a specific time.
        /// </summary>
        public static ClipTransform GetTransform(IList<KeyframeTrack> tracks, TimeSpan time)
        {
            var values = GetAllValues(tracks, time);
            
            return new ClipTransform
            {
                PositionX = values.GetValueOrDefault("Position.X", 0),
                PositionY = values.GetValueOrDefault("Position.Y", 0),
                Scale = values.GetValueOrDefault("Scale", 1),
                ScaleX = values.GetValueOrDefault("Scale.X", 1),
                ScaleY = values.GetValueOrDefault("Scale.Y", 1),
                Rotation = values.GetValueOrDefault("Rotation", 0),
                AnchorX = values.GetValueOrDefault("Anchor.X", 0.5),
                AnchorY = values.GetValueOrDefault("Anchor.Y", 0.5)
            };
        }

        #region Instance Methods (for dependency injection)

        /// <summary>
        /// Instance wrapper for InterpolateTransform (calls static GetTransform).
        /// </summary>
        public ClipTransform InterpolateTransform(List<KeyframeTrack> tracks, double timeSeconds)
        {
            return GetTransform(tracks, TimeSpan.FromSeconds(timeSeconds));
        }

        /// <summary>
        /// Instance wrapper for GetValue.
        /// </summary>
        public double GetValue(IList<AnimatedKeyframe> keyframes, double timeProgress, double defaultValue = 0)
        {
            return GetValue(keyframes, TimeSpan.FromSeconds(timeProgress), defaultValue);
        }

        #endregion

        /// <summary>
        /// Applies easing function to a linear t value.
        /// </summary>
        public static double ApplyEasing(double t, KeyframeEasing easing, BezierPoint cp1, BezierPoint cp2)
        {
            return easing switch
            {
                KeyframeEasing.Linear => t,
                KeyframeEasing.EaseIn => EaseInQuad(t),
                KeyframeEasing.EaseOut => EaseOutQuad(t),
                KeyframeEasing.EaseInOut => EaseInOutQuad(t),
                KeyframeEasing.EaseInQuad => EaseInQuad(t),
                KeyframeEasing.EaseOutQuad => EaseOutQuad(t),
                KeyframeEasing.EaseInOutQuad => EaseInOutQuad(t),
                KeyframeEasing.EaseInCubic => EaseInCubic(t),
                KeyframeEasing.EaseOutCubic => EaseOutCubic(t),
                KeyframeEasing.EaseInOutCubic => EaseInOutCubic(t),
                KeyframeEasing.EaseInBack => EaseInBack(t),
                KeyframeEasing.EaseOutBack => EaseOutBack(t),
                KeyframeEasing.EaseInOutBack => EaseInOutBack(t),
                KeyframeEasing.EaseInElastic => EaseInElastic(t),
                KeyframeEasing.EaseOutElastic => EaseOutElastic(t),
                KeyframeEasing.EaseInBounce => EaseInBounce(t),
                KeyframeEasing.EaseOutBounce => EaseOutBounce(t),
                KeyframeEasing.Bezier => CubicBezier(t, cp1.X, cp1.Y, cp2.X, cp2.Y),
                _ => t
            };
        }

        /// <summary>
        /// Adds a keyframe to a track, maintaining sort order.
        /// </summary>
        public static void AddKeyframe(KeyframeTrack track, AnimatedKeyframe keyframe)
        {
            // Find insertion point
            int index = 0;
            while (index < track.Keyframes.Count && track.Keyframes[index].Time < keyframe.Time)
            {
                index++;
            }

            // Replace if same time exists
            if (index < track.Keyframes.Count && track.Keyframes[index].Time == keyframe.Time)
            {
                track.Keyframes[index] = keyframe;
            }
            else
            {
                track.Keyframes.Insert(index, keyframe);
            }
        }

        /// <summary>
        /// Removes a keyframe from a track.
        /// </summary>
        public static bool RemoveKeyframe(KeyframeTrack track, string keyframeId)
        {
            var kf = track.Keyframes.FirstOrDefault(k => k.Id == keyframeId);
            if (kf != null)
            {
                return track.Keyframes.Remove(kf);
            }
            return false;
        }

        /// <summary>
        /// Creates standard keyframe tracks for transform animation.
        /// </summary>
        public static List<KeyframeTrack> CreateTransformTracks()
        {
            return new List<KeyframeTrack>
            {
                new() { Property = "Position.X", DisplayName = "Position X", DefaultValue = 0, MinValue = -2, MaxValue = 2, CurveColor = "#E74C3C" },
                new() { Property = "Position.Y", DisplayName = "Position Y", DefaultValue = 0, MinValue = -2, MaxValue = 2, CurveColor = "#2ECC71" },
                new() { Property = "Scale", DisplayName = "Scale", DefaultValue = 1, MinValue = 0, MaxValue = 10, CurveColor = "#3498DB" },
                new() { Property = "Scale.X", DisplayName = "Scale X", DefaultValue = 1, MinValue = 0, MaxValue = 10, CurveColor = "#9B59B6" },
                new() { Property = "Scale.Y", DisplayName = "Scale Y", DefaultValue = 1, MinValue = 0, MaxValue = 10, CurveColor = "#1ABC9C" },
                new() { Property = "Rotation", DisplayName = "Rotation", DefaultValue = 0, MinValue = -360, MaxValue = 360, CurveColor = "#F39C12" },
                new() { Property = "Opacity", DisplayName = "Opacity", DefaultValue = 1, MinValue = 0, MaxValue = 1, CurveColor = "#95A5A6" },
                new() { Property = "Speed", DisplayName = "Speed", DefaultValue = 1, MinValue = 0.1, MaxValue = 100, CurveColor = "#E91E63" }
            };
        }

        #region Easing Functions

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;

        private static double EaseInQuad(double t) => t * t;
        private static double EaseOutQuad(double t) => t * (2 - t);
        private static double EaseInOutQuad(double t) => t < 0.5 ? 2 * t * t : -1 + (4 - 2 * t) * t;

        private static double EaseInCubic(double t) => t * t * t;
        private static double EaseOutCubic(double t) { t--; return t * t * t + 1; }
        private static double EaseInOutCubic(double t) => t < 0.5 ? 4 * t * t * t : (t - 1) * (2 * t - 2) * (2 * t - 2) + 1;

        private static double EaseInBack(double t)
        {
            const double s = 1.70158;
            return t * t * ((s + 1) * t - s);
        }

        private static double EaseOutBack(double t)
        {
            const double s = 1.70158;
            t--;
            return t * t * ((s + 1) * t + s) + 1;
        }

        private static double EaseInOutBack(double t)
        {
            const double s = 1.70158 * 1.525;
            t *= 2;
            if (t < 1) return 0.5 * (t * t * ((s + 1) * t - s));
            t -= 2;
            return 0.5 * (t * t * ((s + 1) * t + s) + 2);
        }

        private static double EaseInElastic(double t)
        {
            if (t == 0 || t == 1) return t;
            const double p = 0.3;
            const double s = p / 4;
            t--;
            return -(Math.Pow(2, 10 * t) * Math.Sin((t - s) * (2 * Math.PI) / p));
        }

        private static double EaseOutElastic(double t)
        {
            if (t == 0 || t == 1) return t;
            const double p = 0.3;
            const double s = p / 4;
            return Math.Pow(2, -10 * t) * Math.Sin((t - s) * (2 * Math.PI) / p) + 1;
        }

        private static double EaseInBounce(double t) => 1 - EaseOutBounce(1 - t);

        private static double EaseOutBounce(double t)
        {
            if (t < 1 / 2.75)
                return 7.5625 * t * t;
            if (t < 2 / 2.75)
            {
                t -= 1.5 / 2.75;
                return 7.5625 * t * t + 0.75;
            }
            if (t < 2.5 / 2.75)
            {
                t -= 2.25 / 2.75;
                return 7.5625 * t * t + 0.9375;
            }
            t -= 2.625 / 2.75;
            return 7.5625 * t * t + 0.984375;
        }

        /// <summary>
        /// Evaluates a cubic Bezier curve at parameter t.
        /// Control points: P0=(0,0), P1=(x1,y1), P2=(x2,y2), P3=(1,1)
        /// </summary>
        private static double CubicBezier(double t, double x1, double y1, double x2, double y2)
        {
            // Use Newton-Raphson to solve for t given x
            // This is needed because we know x (the time progress) and want y (the value progress)
            
            double epsilon = 0.0001;
            double tx = t;

            // Newton-Raphson iteration
            for (int i = 0; i < 10; i++)
            {
                double xCalc = BezierX(tx, x1, x2);
                double xDeriv = BezierXDerivative(tx, x1, x2);

                if (Math.Abs(xDeriv) < epsilon) break;

                double xDiff = xCalc - t;
                if (Math.Abs(xDiff) < epsilon) break;

                tx -= xDiff / xDeriv;
                tx = Math.Clamp(tx, 0, 1);
            }

            return BezierY(tx, y1, y2);
        }

        private static double BezierX(double t, double x1, double x2)
        {
            double tm = 1 - t;
            return 3 * tm * tm * t * x1 + 3 * tm * t * t * x2 + t * t * t;
        }

        private static double BezierY(double t, double y1, double y2)
        {
            double tm = 1 - t;
            return 3 * tm * tm * t * y1 + 3 * tm * t * t * y2 + t * t * t;
        }

        private static double BezierXDerivative(double t, double x1, double x2)
        {
            double tm = 1 - t;
            return 3 * tm * tm * x1 + 6 * tm * t * (x2 - x1) + 3 * t * t * (1 - x2);
        }

        #endregion
    }
}
