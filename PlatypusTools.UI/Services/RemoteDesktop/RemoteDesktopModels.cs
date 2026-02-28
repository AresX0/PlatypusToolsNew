using System.Text.Json.Serialization;

namespace PlatypusTools.UI.Services.RemoteDesktop;

/// <summary>
/// Message types for the remote desktop WebSocket protocol.
/// Host → Client: Binary JPEG frames + JSON metadata.
/// Client → Host: JSON mouse/keyboard events.
/// </summary>
/// 
// ── Host → Client messages ──

public class ScreenInfoMessage
{
    [JsonPropertyName("type")] public string Type => "screen_info";
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("monitors")] public List<MonitorInfo> Monitors { get; set; } = new();
}

public class MonitorInfo
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("x")] public int X { get; set; }
    [JsonPropertyName("y")] public int Y { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("isPrimary")] public bool IsPrimary { get; set; }
}

public class ClipboardMessage
{
    [JsonPropertyName("type")] public string Type => "clipboard";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}

public class HostErrorMessage
{
    [JsonPropertyName("type")] public string Type => "error";
    [JsonPropertyName("message")] public string Message { get; set; } = "";
}

// ── Client → Host messages ──

public class InputMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
}

public class MouseMoveMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "mouse_move";
    /// <summary>Normalized X coordinate (0.0 – 1.0)</summary>
    [JsonPropertyName("x")] public double X { get; set; }
    /// <summary>Normalized Y coordinate (0.0 – 1.0)</summary>
    [JsonPropertyName("y")] public double Y { get; set; }
}

public class MouseButtonMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    /// <summary>"left", "right", "middle"</summary>
    [JsonPropertyName("button")] public string Button { get; set; } = "left";
}

public class MouseScrollMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "mouse_scroll";
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    /// <summary>Scroll delta (positive = up, negative = down)</summary>
    [JsonPropertyName("delta")] public int Delta { get; set; }
}

public class KeyMessage
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    /// <summary>Windows virtual key code</summary>
    [JsonPropertyName("keyCode")] public int KeyCode { get; set; }
    /// <summary>Active modifier keys: "ctrl", "shift", "alt", "win"</summary>
    [JsonPropertyName("modifiers")] public List<string> Modifiers { get; set; } = new();
}

// ── Quality / settings ──

public class QualityMessage
{
    [JsonPropertyName("type")] public string Type => "quality";
    [JsonPropertyName("jpegQuality")] public int JpegQuality { get; set; } = 50;
    [JsonPropertyName("maxFps")] public int MaxFps { get; set; } = 15;
    [JsonPropertyName("monitorIndex")] public int MonitorIndex { get; set; } = 0;
}

/// <summary>
/// Settings for the remote desktop session (persisted per-connection).
/// </summary>
public class RemoteDesktopSessionSettings
{
    public int JpegQuality { get; set; } = 50;
    public int MaxFps { get; set; } = 15;
    public int MonitorIndex { get; set; } = 0;
    public bool ShowCursor { get; set; } = true;
    public bool AllowInput { get; set; } = true;
}
