using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PlatypusTools.UI.Services.RemoteDesktop;

/// <summary>
/// Simulates mouse and keyboard input on the host machine using the Windows SendInput API.
/// Receives normalized coordinates (0–1) and maps them to absolute screen coordinates.
/// </summary>
public sealed class InputSimulatorService
{
    // ── P/Invoke declarations ──

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    // INPUT structure for SendInput
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Input types
    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;

    // Mouse event flags
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    // Keyboard event flags
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

    /// <summary>Whether input simulation is enabled. Can be toggled mid-session.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The screen bounds that the host is capturing (needed to map normalized coords → absolute).
    /// Must be set before processing input events.
    /// </summary>
    public System.Drawing.Rectangle CaptureBounds { get; set; }

    /// <summary>
    /// Moves the mouse to the specified normalized position (0–1).
    /// </summary>
    public void MoveMouse(double normalizedX, double normalizedY)
    {
        if (!Enabled) return;

        var (absX, absY) = NormalizedToAbsolute(normalizedX, normalizedY);
        SetCursorPos(absX, absY);
    }

    /// <summary>
    /// Presses or releases a mouse button at the specified normalized position.
    /// </summary>
    public void MouseButton(double normalizedX, double normalizedY, string button, bool isDown)
    {
        if (!Enabled) return;

        var (absX, absY) = NormalizedToAbsolute(normalizedX, normalizedY);
        SetCursorPos(absX, absY);

        uint flags = (button.ToLowerInvariant(), isDown) switch
        {
            ("left", true) => MOUSEEVENTF_LEFTDOWN,
            ("left", false) => MOUSEEVENTF_LEFTUP,
            ("right", true) => MOUSEEVENTF_RIGHTDOWN,
            ("right", false) => MOUSEEVENTF_RIGHTUP,
            ("middle", true) => MOUSEEVENTF_MIDDLEDOWN,
            ("middle", false) => MOUSEEVENTF_MIDDLEUP,
            _ => 0
        };

        if (flags == 0) return;

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = flags,
                    dx = 0,
                    dy = 0
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Scrolls the mouse wheel at the specified normalized position.
    /// </summary>
    public void MouseScroll(double normalizedX, double normalizedY, int delta)
    {
        if (!Enabled) return;

        var (absX, absY) = NormalizedToAbsolute(normalizedX, normalizedY);
        SetCursorPos(absX, absY);

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dwFlags = MOUSEEVENTF_WHEEL,
                    mouseData = delta
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Presses a key. virtualKeyCode is a Windows VK_ code.
    /// </summary>
    public void KeyDown(int virtualKeyCode)
    {
        if (!Enabled) return;

        uint flags = IsExtendedKey(virtualKeyCode) ? KEYEVENTF_EXTENDEDKEY : 0;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKeyCode,
                    dwFlags = flags
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Releases a key.
    /// </summary>
    public void KeyUp(int virtualKeyCode)
    {
        if (!Enabled) return;

        uint flags = KEYEVENTF_KEYUP;
        if (IsExtendedKey(virtualKeyCode))
            flags |= KEYEVENTF_EXTENDEDKEY;

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)virtualKeyCode,
                    dwFlags = flags
                }
            }
        };

        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// Sends Ctrl+Alt+Del (simulated with individual key events — note: only works in certain contexts).
    /// </summary>
    public void SendCtrlAltDel()
    {
        if (!Enabled) return;

        KeyDown(0xA2); // VK_LCONTROL
        KeyDown(0xA4); // VK_LMENU (Alt)
        KeyDown(0x2E); // VK_DELETE
        KeyUp(0x2E);
        KeyUp(0xA4);
        KeyUp(0xA2);
    }

    /// <summary>
    /// Converts normalized coordinates (0–1) to absolute screen coordinates.
    /// </summary>
    private (int x, int y) NormalizedToAbsolute(double nx, double ny)
    {
        nx = Math.Clamp(nx, 0.0, 1.0);
        ny = Math.Clamp(ny, 0.0, 1.0);

        int x = CaptureBounds.X + (int)(nx * CaptureBounds.Width);
        int y = CaptureBounds.Y + (int)(ny * CaptureBounds.Height);

        return (x, y);
    }

    /// <summary>
    /// Extended keys are function keys, navigation keys, Ins/Del, arrow keys, etc.
    /// </summary>
    private static bool IsExtendedKey(int vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true, // PageUp, PageDown, End, Home
        0x25 or 0x26 or 0x27 or 0x28 => true, // Arrow keys
        0x2D or 0x2E => true,                   // Insert, Delete
        0x5B or 0x5C => true,                   // Win keys
        0x5D => true,                            // Apps key
        0x6F => true,                            // Divide (numpad /)
        0x0D when false => true,                 // NumPad Enter (handled separately if needed)
        _ => false
    };
}
