using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace PlatypusTools.UI.Services.RemoteDesktop;

/// <summary>
/// Handles a remote desktop WebSocket session on the HOST side.
/// Captures screen frames →  sends as binary JPEG blobs.
/// Receives mouse/keyboard events → feeds to InputSimulatorService.
/// </summary>
public sealed class RemoteDesktopWebSocketHandler : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ScreenCaptureService _capture = new();
    private readonly InputSimulatorService _input = new();
    private CancellationTokenSource? _cts;
    private volatile bool _running;

    /// <summary>Current session settings.</summary>
    public RemoteDesktopSessionSettings Settings { get; } = new();

    /// <summary>
    /// Entry point called from the ASP.NET middleware when a WebSocket connects to /ws/remote-desktop.
    /// </summary>
    public static async Task HandleSessionAsync(WebSocket ws, CancellationToken requestAborted)
    {
        using var handler = new RemoteDesktopWebSocketHandler();
        await handler.RunAsync(ws, requestAborted);
    }

    private async Task RunAsync(WebSocket ws, CancellationToken requestAborted)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(requestAborted);
        _running = true;

        // Apply default settings
        _capture.Quality = Settings.JpegQuality;
        _capture.ShowCursor = Settings.ShowCursor;
        _capture.MonitorIndex = -1; // All monitors
        _input.CaptureBounds = _capture.GetCaptureBounds();
        _input.Enabled = Settings.AllowInput;

        try
        {
            // Send initial screen info
            await SendScreenInfoAsync(ws, _cts.Token);

            // Start two tasks: frame sender + input receiver
            var sendTask = FrameSenderLoopAsync(ws, _cts.Token);
            var recvTask = InputReceiverLoopAsync(ws, _cts.Token);

            // Wait for either to complete (client disconnect, error, or cancellation)
            await Task.WhenAny(sendTask, recvTask);
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteDesktop] Session error: {ex.Message}");
        }
        finally
        {
            _running = false;
            _cts?.Cancel();

            if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended",
                        CancellationToken.None);
                }
                catch { }
            }

            Debug.WriteLine("[RemoteDesktop] Session ended");
        }
    }

    /// <summary>
    /// Continuously captures and sends JPEG frames.
    /// </summary>
    private async Task FrameSenderLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        long frameCount = 0;

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            var targetInterval = 1000.0 / Settings.MaxFps;
            var frameStart = sw.ElapsedMilliseconds;

            try
            {
                // Capture screen
                var jpegBytes = _capture.CaptureFrame();

                // Send as binary WebSocket message
                await ws.SendAsync(
                    new ArraySegment<byte>(jpegBytes),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    cancellationToken: ct);

                frameCount++;
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteDesktop] Frame capture error: {ex.Message}");
                // Skip this frame, continue
            }

            // Sleep to maintain target FPS
            var elapsed = sw.ElapsedMilliseconds - frameStart;
            var sleepMs = (int)(targetInterval - elapsed);
            if (sleepMs > 0)
            {
                await Task.Delay(sleepMs, ct);
            }
        }
    }

    /// <summary>
    /// Receives and processes input events from the client.
    /// </summary>
    private async Task InputReceiverLoopAsync(WebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[4096];

        while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.WriteLine("[RemoteDesktop] Client requested close");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessInputMessage(json);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException) { break; }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteDesktop] Input receive error: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Parses and dispatches a JSON input message.
    /// </summary>
    private void ProcessInputMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "mouse_move":
                {
                    var x = root.GetProperty("x").GetDouble();
                    var y = root.GetProperty("y").GetDouble();
                    _input.MoveMouse(x, y);
                    break;
                }
                case "mouse_down":
                {
                    var x = root.GetProperty("x").GetDouble();
                    var y = root.GetProperty("y").GetDouble();
                    var btn = root.GetProperty("button").GetString() ?? "left";
                    _input.MouseButton(x, y, btn, isDown: true);
                    break;
                }
                case "mouse_up":
                {
                    var x = root.GetProperty("x").GetDouble();
                    var y = root.GetProperty("y").GetDouble();
                    var btn = root.GetProperty("button").GetString() ?? "left";
                    _input.MouseButton(x, y, btn, isDown: false);
                    break;
                }
                case "mouse_scroll":
                {
                    var x = root.GetProperty("x").GetDouble();
                    var y = root.GetProperty("y").GetDouble();
                    var delta = root.GetProperty("delta").GetInt32();
                    _input.MouseScroll(x, y, delta);
                    break;
                }
                case "key_down":
                {
                    var keyCode = root.GetProperty("keyCode").GetInt32();
                    _input.KeyDown(keyCode);
                    break;
                }
                case "key_up":
                {
                    var keyCode = root.GetProperty("keyCode").GetInt32();
                    _input.KeyUp(keyCode);
                    break;
                }
                case "quality":
                {
                    // Client requesting quality/fps change
                    if (root.TryGetProperty("jpegQuality", out var q))
                    {
                        Settings.JpegQuality = q.GetInt32();
                        _capture.Quality = Settings.JpegQuality;
                    }
                    if (root.TryGetProperty("maxFps", out var f))
                    {
                        Settings.MaxFps = Math.Clamp(f.GetInt32(), 1, 60);
                    }
                    if (root.TryGetProperty("monitorIndex", out var m))
                    {
                        var idx = m.GetInt32();
                        _capture.MonitorIndex = idx;
                        _input.CaptureBounds = _capture.GetCaptureBounds();
                        Settings.MonitorIndex = idx;
                    }
                    break;
                }
                case "ctrl_alt_del":
                {
                    _input.SendCtrlAltDel();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteDesktop] Failed to process input: {ex.Message}");
        }
    }

    /// <summary>
    /// Sends the initial screen_info message to the client.
    /// </summary>
    private async Task SendScreenInfoAsync(WebSocket ws, CancellationToken ct)
    {
        var bounds = _capture.GetCaptureBounds();
        var msg = new ScreenInfoMessage
        {
            Width = bounds.Width,
            Height = bounds.Height,
            Monitors = _capture.GetMonitors()
        };

        var json = JsonSerializer.Serialize(msg, _jsonOpts);
        var bytes = Encoding.UTF8.GetBytes(json);
        await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    public void Dispose()
    {
        _running = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _capture.Dispose();
    }
}
