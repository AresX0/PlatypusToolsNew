using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using PlatypusTools.Core.Services;

namespace PlatypusTools.UI.Interop
{
    /// <summary>
    /// Provides DWM (Desktop Window Manager) interop for glass/blur effects.
    /// Supports Windows 10 (Acrylic via AccentPolicy) and Windows 11 (Mica/Acrylic).
    /// </summary>
    public static class DwmGlassHelper
    {
        #region Native Imports

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmIsCompositionEnabled(out bool enabled);

        [DllImport("user32.dll")]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS
        {
            public int Left;
            public int Right;
            public int Top;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WindowCompositionAttributeData
        {
            public WindowCompositionAttribute Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public AccentState AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        private enum WindowCompositionAttribute
        {
            WCA_ACCENT_POLICY = 19
        }

        private enum AccentState
        {
            ACCENT_DISABLED = 0,
            ACCENT_ENABLE_GRADIENT = 1,
            ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
            ACCENT_ENABLE_BLURBEHIND = 3,
            ACCENT_ENABLE_ACRYLICBLURBEHIND = 4,
            ACCENT_ENABLE_HOSTBACKDROP = 5,  // Windows 11 Mica
            ACCENT_INVALID_STATE = 6
        }

        // DWM Window Attributes for Windows 11
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_MICA_EFFECT = 1029;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        // Backdrop types for Windows 11
        private const int DWMSBT_AUTO = 0;
        private const int DWMSBT_NONE = 1;
        private const int DWMSBT_MAINWINDOW = 2;  // Mica
        private const int DWMSBT_TRANSIENTWINDOW = 3;  // Acrylic
        private const int DWMSBT_TABBEDWINDOW = 4;  // Tabbed Mica

        #endregion

        #region Public API

        /// <summary>
        /// Detects if glass effects are supported on the current system.
        /// </summary>
        public static bool IsGlassSupported
        {
            get
            {
                try
                {
                    if (DwmIsCompositionEnabled(out bool enabled) == 0)
                        return enabled && !IsTransparencyDisabled;
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Detects if user has disabled transparency effects in Windows settings.
        /// </summary>
        public static bool IsTransparencyDisabled
        {
            get
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                    if (key != null)
                    {
                        var value = key.GetValue("EnableTransparency");
                        if (value is int intValue)
                            return intValue == 0;
                    }
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Gets the Windows build number to determine feature support.
        /// </summary>
        public static int WindowsBuildNumber
        {
            get
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                    if (key != null)
                    {
                        var value = key.GetValue("CurrentBuildNumber");
                        if (value is string buildStr && int.TryParse(buildStr, out int build))
                            return build;
                    }
                }
                catch { }
                return 0;
            }
        }

        /// <summary>
        /// True if running on Windows 11 (build 22000+).
        /// </summary>
        public static bool IsWindows11 => WindowsBuildNumber >= 22000;

        /// <summary>
        /// True if running on Windows 10 with acrylic support (build 17763+).
        /// </summary>
        public static bool IsWindows10WithAcrylic => WindowsBuildNumber >= 17763 && !IsWindows11;

        /// <summary>
        /// Enables the glass effect on the specified window.
        /// </summary>
        /// <param name="window">The window to apply glass effect to.</param>
        /// <param name="level">The glass effect level.</param>
        /// <param name="tintColor">Optional tint color (ARGB).</param>
        /// <returns>True if effect was applied successfully.</returns>
        public static bool EnableGlass(Window window, GlassLevel level, Color? tintColor = null)
        {
            SimpleLogger.Debug($"DwmGlassHelper.EnableGlass: level={level}, isSupported={IsGlassSupported}, build={WindowsBuildNumber}");
            
            if (window == null || !IsGlassSupported)
            {
                SimpleLogger.Warn($"DwmGlassHelper.EnableGlass: Early return - window null={window == null}, isSupported={IsGlassSupported}");
                return false;
            }

            if (level == GlassLevel.Off)
            {
                SimpleLogger.Debug("DwmGlassHelper.EnableGlass: Level is Off, disabling glass");
                DisableGlass(window);
                return true;
            }

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                SimpleLogger.Warn("DwmGlassHelper.EnableGlass: Window handle is IntPtr.Zero - window not shown yet?");
                return false;
            }

            SimpleLogger.Debug($"DwmGlassHelper.EnableGlass: hwnd={hwnd}, IsWindows11={IsWindows11}, IsWindows10WithAcrylic={IsWindows10WithAcrylic}");

            try
            {
                // Extend frame into client area (required for glass)
                var margins = new MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                bool result;
                if (IsWindows11)
                {
                    result = ApplyWindows11Backdrop(hwnd, level, tintColor);
                    SimpleLogger.Debug($"DwmGlassHelper.EnableGlass: ApplyWindows11Backdrop returned {result}");
                }
                else if (IsWindows10WithAcrylic)
                {
                    result = ApplyWindows10Acrylic(hwnd, level, tintColor);
                    SimpleLogger.Debug($"DwmGlassHelper.EnableGlass: ApplyWindows10Acrylic returned {result}");
                }
                else
                {
                    // Fallback: basic blur (Windows 7/8 style)
                    result = ApplyBasicBlur(hwnd);
                    SimpleLogger.Debug($"DwmGlassHelper.EnableGlass: ApplyBasicBlur returned {result}");
                }
                return result;
            }
            catch (Exception ex)
            {
                SimpleLogger.Error($"DwmGlassHelper.EnableGlass: Exception - {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disables glass effects on the specified window.
        /// </summary>
        public static void DisableGlass(Window window)
        {
            if (window == null)
                return;

            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                // Reset margins
                var margins = new MARGINS { Left = 0, Right = 0, Top = 0, Bottom = 0 };
                DwmExtendFrameIntoClientArea(hwnd, ref margins);

                if (IsWindows11)
                {
                    int value = DWMSBT_NONE;
                    DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int));
                }
                else
                {
                    // Disable accent
                    var accent = new AccentPolicy { AccentState = AccentState.ACCENT_DISABLED };
                    SetAccentPolicy(hwnd, accent);
                }
            }
            catch { }
        }

        /// <summary>
        /// Sets dark mode for window title bar (Windows 10 1809+).
        /// </summary>
        public static void SetDarkMode(Window window, bool isDark)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;

            try
            {
                int value = isDark ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
            }
            catch { }
        }

        #endregion

        #region Private Methods

        private static bool ApplyWindows11Backdrop(IntPtr hwnd, GlassLevel level, Color? tintColor)
        {
            // Try using SetWindowCompositionAttribute for better Acrylic effect on Windows 11
            // DWMWA_SYSTEMBACKDROP_TYPE is more subtle and may not show well in all cases
            
            var accentState = level switch
            {
                GlassLevel.High => AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GlassLevel.Medium => AccentState.ACCENT_ENABLE_BLURBEHIND,
                GlassLevel.Low => AccentState.ACCENT_ENABLE_TRANSPARENTGRADIENT,
                _ => AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
            };

            // Calculate tint color (ABGR format)
            uint gradientColor = 0x80000000; // Default: semi-transparent black
            if (tintColor.HasValue)
            {
                var c = tintColor.Value;
                byte opacity = level switch
                {
                    GlassLevel.High => 0x40,   // ~25% opacity for more blur visibility
                    GlassLevel.Medium => 0x80, // ~50%
                    GlassLevel.Low => 0xCC,    // ~80%
                    _ => 0x60
                };
                gradientColor = (uint)((opacity << 24) | (c.B << 16) | (c.G << 8) | c.R);
            }

            SimpleLogger.Debug($"ApplyWindows11Backdrop: level={level}, accentState={accentState}, gradientColor={gradientColor:X8}");

            var accent = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = 2,  // Enable gradient
                GradientColor = gradientColor
            };

            return SetAccentPolicy(hwnd, accent);
        }

        private static bool ApplyWindows10Acrylic(IntPtr hwnd, GlassLevel level, Color? tintColor)
        {
            var accentState = level switch
            {
                GlassLevel.High => AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GlassLevel.Medium => AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                GlassLevel.Low => AccentState.ACCENT_ENABLE_BLURBEHIND,
                _ => AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND
            };

            // Calculate tint color (ABGR format for AccentPolicy)
            uint gradientColor = 0x99000000; // Default: transparent black with some opacity
            if (tintColor.HasValue)
            {
                var c = tintColor.Value;
                byte opacity = level switch
                {
                    GlassLevel.High => 0x66,   // ~40%
                    GlassLevel.Medium => 0x99, // ~60%
                    GlassLevel.Low => 0xCC,    // ~80%
                    _ => 0x99
                };
                gradientColor = (uint)((opacity << 24) | (c.B << 16) | (c.G << 8) | c.R);
            }

            var accent = new AccentPolicy
            {
                AccentState = accentState,
                AccentFlags = 2,  // Enable gradient
                GradientColor = gradientColor
            };

            return SetAccentPolicy(hwnd, accent);
        }

        private static bool ApplyBasicBlur(IntPtr hwnd)
        {
            var accent = new AccentPolicy
            {
                AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND
            };

            return SetAccentPolicy(hwnd, accent);
        }

        private static bool SetAccentPolicy(IntPtr hwnd, AccentPolicy accent)
        {
            int accentSize = Marshal.SizeOf(accent);
            IntPtr accentPtr = Marshal.AllocHGlobal(accentSize);

            try
            {
                Marshal.StructureToPtr(accent, accentPtr, false);

                var data = new WindowCompositionAttributeData
                {
                    Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                    Data = accentPtr,
                    SizeOfData = accentSize
                };

                return SetWindowCompositionAttribute(hwnd, ref data) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(accentPtr);
            }
        }

        #endregion
    }

    /// <summary>
    /// Glass effect intensity levels.
    /// </summary>
    public enum GlassLevel
    {
        /// <summary>No glass effect (solid background).</summary>
        Off = 0,
        /// <summary>Subtle glass effect with higher opacity.</summary>
        Low = 1,
        /// <summary>Balanced glass effect (default).</summary>
        Medium = 2,
        /// <summary>Strong glass effect with more transparency.</summary>
        High = 3,
        /// <summary>Automatically choose based on system settings.</summary>
        Auto = 4
    }
}
