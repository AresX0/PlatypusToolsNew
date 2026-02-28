using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PlatypusTools.UI.Services;

namespace PlatypusTools.UI.ViewModels;

/// <summary>
/// ViewModel for the Remote Desktop CLIENT — connects to a remote PlatypusTools
/// instance over WebSocket and displays the remote screen with mouse/keyboard forwarding.
/// </summary>
public class RemoteDesktopViewModel : BindableBase
{
    #region Fields

    private string _hostAddress = "";
    private int _hostPort = 47392;
    private bool _useHttps = true;
    private bool _isConnected;
    private bool _isConnecting;
    private string _statusMessage = "Not connected";
    private string _connectionInfo = "";
    private int _fps;
    private int _jpegQuality = 50;
    private int _maxFps = 15;
    private int _selectedMonitorIndex = -1;
    private double _latencyMs;
    private long _bytesReceived;
    private long _framesReceived;
    private int _remoteScreenWidth;
    private int _remoteScreenHeight;
    private bool _isInputEnabled = true;
    private bool _isViewOnly;
    private ImageSource? _frameImage;

    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private readonly object _frameLock = new();

    #endregion

    #region Properties

    public string HostAddress
    {
        get => _hostAddress;
        set => SetProperty(ref _hostAddress, value);
    }

    public int HostPort
    {
        get => _hostPort;
        set => SetProperty(ref _hostPort, value);
    }

    public bool UseHttps
    {
        get => _useHttps;
        set => SetProperty(ref _useHttps, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            SetProperty(ref _isConnected, value);
            RaisePropertyChanged(nameof(CanConnect));
            RaisePropertyChanged(nameof(ConnectButtonText));
        }
    }

    public bool IsConnecting
    {
        get => _isConnecting;
        set
        {
            SetProperty(ref _isConnecting, value);
            RaisePropertyChanged(nameof(CanConnect));
            RaisePropertyChanged(nameof(ConnectButtonText));
        }
    }

    public bool CanConnect => !IsConnecting;

    public string ConnectButtonText => IsConnected ? "Disconnect" : IsConnecting ? "Connecting..." : "Connect";

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string ConnectionInfo
    {
        get => _connectionInfo;
        set => SetProperty(ref _connectionInfo, value);
    }

    public int Fps
    {
        get => _fps;
        set => SetProperty(ref _fps, value);
    }

    public int JpegQuality
    {
        get => _jpegQuality;
        set
        {
            if (SetProperty(ref _jpegQuality, Math.Clamp(value, 1, 100)))
                SendQualitySettingsAsync();
        }
    }

    public int MaxFps
    {
        get => _maxFps;
        set
        {
            if (SetProperty(ref _maxFps, Math.Clamp(value, 1, 60)))
                SendQualitySettingsAsync();
        }
    }

    public int SelectedMonitorIndex
    {
        get => _selectedMonitorIndex;
        set
        {
            if (SetProperty(ref _selectedMonitorIndex, value))
                SendQualitySettingsAsync();
        }
    }

    public double LatencyMs
    {
        get => _latencyMs;
        set => SetProperty(ref _latencyMs, value);
    }

    public long BytesReceived
    {
        get => _bytesReceived;
        set
        {
            SetProperty(ref _bytesReceived, value);
            RaisePropertyChanged(nameof(BytesReceivedDisplay));
        }
    }

    public string BytesReceivedDisplay => BytesReceived < 1_000_000
        ? $"{BytesReceived / 1024.0:F0} KB"
        : $"{BytesReceived / 1_048_576.0:F1} MB";

    public long FramesReceived
    {
        get => _framesReceived;
        set => SetProperty(ref _framesReceived, value);
    }

    public int RemoteScreenWidth
    {
        get => _remoteScreenWidth;
        set => SetProperty(ref _remoteScreenWidth, value);
    }

    public int RemoteScreenHeight
    {
        get => _remoteScreenHeight;
        set => SetProperty(ref _remoteScreenHeight, value);
    }

    public bool IsInputEnabled
    {
        get => _isInputEnabled;
        set => SetProperty(ref _isInputEnabled, value);
    }

    public bool IsViewOnly
    {
        get => _isViewOnly;
        set
        {
            SetProperty(ref _isViewOnly, value);
            IsInputEnabled = !value;
        }
    }

    /// <summary>
    /// The current frame image. Bound to the Image control in the view.
    /// </summary>
    public ImageSource? FrameImage
    {
        get => _frameImage;
        set => SetProperty(ref _frameImage, value);
    }

    public List<Services.RemoteDesktop.MonitorInfo> RemoteMonitors { get; } = new();

    #endregion

    #region Commands

    public ICommand ConnectCommand { get; }
    public ICommand ToggleViewOnlyCommand { get; }
    public ICommand SendCtrlAltDelCommand { get; }
    public ICommand FitToWindowCommand { get; }

    #endregion

    public RemoteDesktopViewModel()
    {
        ConnectCommand = new RelayCommand(_ => ToggleConnection());
        ToggleViewOnlyCommand = new RelayCommand(_ => IsViewOnly = !IsViewOnly);
        SendCtrlAltDelCommand = new RelayCommand(_ => SendSpecialKey("ctrl_alt_del"), _ => IsConnected);
        FitToWindowCommand = new RelayCommand(_ => { /* handled by view */ });

        // Load last-used connection settings
        LoadSettings();
    }

    #region Connection Management

    private async void ToggleConnection()
    {
        if (IsConnected)
        {
            await DisconnectAsync();
        }
        else
        {
            await ConnectAsync();
        }
    }

    public async Task ConnectAsync()
    {
        if (IsConnected || IsConnecting) return;

        IsConnecting = true;
        StatusMessage = "Connecting...";

        try
        {
            _cts = new CancellationTokenSource();
            _ws = new ClientWebSocket();

            // Build WebSocket URL
            var scheme = UseHttps ? "wss" : "ws";
            var uri = new Uri($"{scheme}://{HostAddress}:{HostPort}/ws/remote-desktop");

            // For self-signed certs, skip validation
            if (UseHttps)
            {
                _ws.Options.RemoteCertificateValidationCallback = (sender, cert, chain, errors) => true;
            }

            await _ws.ConnectAsync(uri, _cts.Token);

            IsConnected = true;
            IsConnecting = false;
            StatusMessage = $"Connected to {HostAddress}:{HostPort}";
            ConnectionInfo = $"{scheme}://{HostAddress}:{HostPort}";

            // Save settings for next time
            SaveSettings();

            // Start receiving frames
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }
        catch (Exception ex)
        {
            IsConnecting = false;
            StatusMessage = $"Connection failed: {ex.Message}";
            Debug.WriteLine($"[RemoteDesktop Client] Connect error: {ex}");
            await DisconnectAsync();
        }
    }

    public async Task DisconnectAsync()
    {
        _cts?.Cancel();

        if (_ws != null)
        {
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "User disconnect",
                        CancellationToken.None);
                }
            }
            catch { }
            finally
            {
                _ws.Dispose();
                _ws = null;
            }
        }

        IsConnected = false;
        IsConnecting = false;
        StatusMessage = "Disconnected";
        ConnectionInfo = "";
        Fps = 0;
        FrameImage = null;
    }

    #endregion

    #region Frame Receiving

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[4 * 1024 * 1024]; // 4 MB buffer for large frames
        var fpsCounter = 0;
        var fpsTimer = Stopwatch.StartNew();

        while (!ct.IsCancellationRequested && _ws?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = "Remote host closed connection";
                        IsConnected = false;
                    });
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Binary = JPEG frame
                    var frameData = new byte[result.Count];
                    Buffer.BlockCopy(buffer, 0, frameData, 0, result.Count);
                    
                    BytesReceived += result.Count;
                    FramesReceived++;
                    fpsCounter++;

                    // Update FPS every second
                    if (fpsTimer.ElapsedMilliseconds >= 1000)
                    {
                        var currentFps = fpsCounter;
                        fpsCounter = 0;
                        fpsTimer.Restart();

                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            Fps = currentFps;
                        });
                    }

                    // Decode JPEG and update the frame on UI thread
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            using var ms = new MemoryStream(frameData);
                            var decoder = new JpegBitmapDecoder(ms, BitmapCreateOptions.None,
                                BitmapCacheOption.OnLoad);
                            var frame = decoder.Frames[0];

                            // Freeze the frame so it can be used across threads
                            if (frame.CanFreeze) frame.Freeze();
                            FrameImage = frame;
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[RemoteDesktop Client] Frame decode error: {ex.Message}");
                        }
                    });
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    // JSON metadata message
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    ProcessServerMessage(json);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (WebSocketException wex)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    StatusMessage = $"Connection lost: {wex.Message}";
                    IsConnected = false;
                });
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RemoteDesktop Client] Receive error: {ex.Message}");
            }
        }
    }

    private void ProcessServerMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "screen_info":
                    var width = doc.RootElement.GetProperty("width").GetInt32();
                    var height = doc.RootElement.GetProperty("height").GetInt32();

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        RemoteScreenWidth = width;
                        RemoteScreenHeight = height;
                        StatusMessage = $"Connected — {width}×{height}";
                    });

                    // Parse monitors
                    if (doc.RootElement.TryGetProperty("monitors", out var monitorsElement))
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            RemoteMonitors.Clear();
                            foreach (var m in monitorsElement.EnumerateArray())
                            {
                                RemoteMonitors.Add(new Services.RemoteDesktop.MonitorInfo
                                {
                                    Index = m.GetProperty("index").GetInt32(),
                                    Name = m.GetProperty("name").GetString() ?? "",
                                    Width = m.GetProperty("width").GetInt32(),
                                    Height = m.GetProperty("height").GetInt32(),
                                    IsPrimary = m.GetProperty("isPrimary").GetBoolean()
                                });
                            }
                            RaisePropertyChanged(nameof(RemoteMonitors));
                        });
                    }
                    break;

                case "error":
                    var errorMsg = doc.RootElement.GetProperty("message").GetString() ?? "Unknown error";
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        StatusMessage = $"Host error: {errorMsg}";
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteDesktop Client] Message parse error: {ex.Message}");
        }
    }

    #endregion

    #region Input Sending

    /// <summary>
    /// Sends a mouse move event with normalized coordinates (0–1).
    /// Called from the view's mouse handler.
    /// </summary>
    public async void SendMouseMove(double normalizedX, double normalizedY)
    {
        if (!IsConnected || !IsInputEnabled || _ws == null) return;

        var json = JsonSerializer.Serialize(new { type = "mouse_move", x = normalizedX, y = normalizedY });
        await SendTextAsync(json);
    }

    /// <summary>
    /// Sends a mouse button event.
    /// </summary>
    public async void SendMouseButton(double normalizedX, double normalizedY, string button, bool isDown)
    {
        if (!IsConnected || !IsInputEnabled || _ws == null) return;

        var type = isDown ? "mouse_down" : "mouse_up";
        var json = JsonSerializer.Serialize(new { type, x = normalizedX, y = normalizedY, button });
        await SendTextAsync(json);
    }

    /// <summary>
    /// Sends a mouse scroll event.
    /// </summary>
    public async void SendMouseScroll(double normalizedX, double normalizedY, int delta)
    {
        if (!IsConnected || !IsInputEnabled || _ws == null) return;

        var json = JsonSerializer.Serialize(new { type = "mouse_scroll", x = normalizedX, y = normalizedY, delta });
        await SendTextAsync(json);
    }

    /// <summary>
    /// Sends a key down/up event.
    /// </summary>
    public async void SendKeyEvent(int virtualKeyCode, bool isDown)
    {
        if (!IsConnected || !IsInputEnabled || _ws == null) return;

        var type = isDown ? "key_down" : "key_up";
        var json = JsonSerializer.Serialize(new { type, keyCode = virtualKeyCode });
        await SendTextAsync(json);
    }

    /// <summary>
    /// Sends a special key sequence (like Ctrl+Alt+Del).
    /// </summary>
    public async void SendSpecialKey(string key)
    {
        if (!IsConnected || _ws == null) return;

        var json = JsonSerializer.Serialize(new { type = key });
        await SendTextAsync(json);
    }

    private async void SendQualitySettingsAsync()
    {
        if (!IsConnected || _ws == null) return;

        var json = JsonSerializer.Serialize(new
        {
            type = "quality",
            jpegQuality = JpegQuality,
            maxFps = MaxFps,
            monitorIndex = SelectedMonitorIndex
        });
        await SendTextAsync(json);
    }

    private async Task SendTextAsync(string json)
    {
        if (_ws?.State != WebSocketState.Open) return;

        try
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                _cts?.Token ?? CancellationToken.None);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteDesktop Client] Send error: {ex.Message}");
        }
    }

    #endregion

    #region Settings Persistence

    private void LoadSettings()
    {
        try
        {
            var settings = SettingsManager.Current;
            HostAddress = settings.RemoteDesktopHost ?? "";
            HostPort = settings.RemoteDesktopPort > 0 ? settings.RemoteDesktopPort : 47392;
            UseHttps = settings.RemoteDesktopUseHttps;
            JpegQuality = settings.RemoteDesktopQuality > 0 ? settings.RemoteDesktopQuality : 50;
            MaxFps = settings.RemoteDesktopMaxFps > 0 ? settings.RemoteDesktopMaxFps : 15;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteDesktop Client] LoadSettings error: {ex.Message}");
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = SettingsManager.Current;
            settings.RemoteDesktopHost = HostAddress;
            settings.RemoteDesktopPort = HostPort;
            settings.RemoteDesktopUseHttps = UseHttps;
            settings.RemoteDesktopQuality = JpegQuality;
            settings.RemoteDesktopMaxFps = MaxFps;
            SettingsManager.SaveCurrent();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[RemoteDesktop Client] SaveSettings error: {ex.Message}");
        }
    }

    #endregion
}
