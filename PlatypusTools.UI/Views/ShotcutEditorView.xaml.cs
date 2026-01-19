using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Embeds Shotcut video editor inside the WPF application.
    /// </summary>
    public partial class ShotcutEditorView : UserControl
    {
        private Process? _shotcutProcess;
        private IntPtr _shotcutHwnd = IntPtr.Zero;
        private IntPtr _originalParent = IntPtr.Zero;
        private DispatcherTimer? _embedTimer;
        private DispatcherTimer? _resizeTimer;
        private bool _isEmbedded;
        private bool _isDetached;
        private System.Windows.Forms.Panel? _hostPanel;

        private readonly string _shotcutPath;

        #region Win32 API

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool UpdateWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetParent(IntPtr hWnd);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_BORDER = 0x00800000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_CLIPCHILDREN = 0x02000000;
        private const int WS_CLIPSIBLINGS = 0x04000000;

        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_WINDOWEDGE = 0x00000100;
        private const int WS_EX_CLIENTEDGE = 0x00000200;
        private const int WS_EX_DLGMODALFRAME = 0x00000001;

        private const int SW_SHOW = 5;
        private const int SW_HIDE = 0;
        private const int SW_RESTORE = 9;
        private const int SW_MAXIMIZE = 3;
        private const int SW_SHOWNOACTIVATE = 4;

        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_DRAWFRAME = SWP_FRAMECHANGED;

        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_UPDATENOW = 0x0100;
        private const uint RDW_ALLCHILDREN = 0x0080;

        #endregion

        public ShotcutEditorView()
        {
            InitializeComponent();

            // Find Shotcut path - prefer local copy in project
            var possiblePaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Shotcut\shotcut.exe"),
                @"C:\Projects\PlatypusToolsNew\PlatypusTools.UI\Shotcut\shotcut.exe",
                @"C:\Projects\PlatypusToolsNew\archive\shotcutsource\ShotcutWindowsCode\shotcut.exe",
                @"C:\Program Files\Shotcut\shotcut.exe",
                @"C:\Program Files (x86)\Shotcut\shotcut.exe"
            };

            _shotcutPath = "";
            foreach (var path in possiblePaths)
            {
                try
                {
                    var fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                    {
                        _shotcutPath = fullPath;
                        break;
                    }
                }
                catch { }
            }

            // Create host panel
            _hostPanel = new System.Windows.Forms.Panel
            {
                Dock = System.Windows.Forms.DockStyle.Fill,
                BackColor = System.Drawing.Color.Black
            };
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            if (_hostPanel != null)
            {
                WinFormsHost.Child = _hostPanel;
            }

            // Setup resize timer (debounced)
            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _resizeTimer.Tick += (s, args) =>
            {
                _resizeTimer.Stop();
                ResizeEmbeddedWindow();
            };
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            CloseShotcut();
            _embedTimer?.Stop();
            _resizeTimer?.Stop();
        }

        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Debounce resize
            _resizeTimer?.Stop();
            _resizeTimer?.Start();
        }

        private void LaunchShotcut_Click(object sender, RoutedEventArgs e)
        {
            LaunchAndEmbed();
        }

        private void RestartShotcut_Click(object sender, RoutedEventArgs e)
        {
            CloseShotcut();
            LaunchAndEmbed();
        }

        private void CloseShotcut_Click(object sender, RoutedEventArgs e)
        {
            CloseShotcut();
        }

        private void DetachShotcut_Click(object sender, RoutedEventArgs e)
        {
            DetachWindow();
        }

        private void ReattachShotcut_Click(object sender, RoutedEventArgs e)
        {
            ReattachWindow();
        }

        private void LaunchAndEmbed()
        {
            if (string.IsNullOrEmpty(_shotcutPath) || !File.Exists(_shotcutPath))
            {
                MessageBox.Show($"Shotcut not found.\n\nExpected at:\n{_shotcutPath}", 
                    "Shotcut Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Kill existing process
            if (_shotcutProcess != null && !_shotcutProcess.HasExited)
            {
                _shotcutProcess.Kill();
                _shotcutProcess.WaitForExit(1000);
            }

            StatusText.Text = "Launching Shotcut...";
            PlaceholderPanel.Visibility = Visibility.Collapsed;
            WinFormsHost.Visibility = Visibility.Visible;

            try
            {
                // Get the host panel handle to pass to Qt
                var hostHandle = _hostPanel?.Handle ?? IntPtr.Zero;
                
                var startInfo = new ProcessStartInfo
                {
                    FileName = _shotcutPath,
                    WorkingDirectory = Path.GetDirectoryName(_shotcutPath),
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden, // Start hidden
                    CreateNoWindow = false
                };

                _shotcutProcess = Process.Start(startInfo);

                if (_shotcutProcess != null)
                {
                    // Wait longer for Qt app to fully initialize (splash screen, etc.)
                    _embedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                    var attempts = 0;
                    _embedTimer.Tick += async (s, args) =>
                    {
                        attempts++;
                        if (attempts > 30) // 15 second timeout
                        {
                            _embedTimer.Stop();
                            StatusText.Text = "Timeout - opening in separate window";
                            // Show window normally if embedding fails
                            if (_shotcutHwnd != IntPtr.Zero)
                            {
                                ShowWindow(_shotcutHwnd, SW_SHOW);
                                SetForegroundWindow(_shotcutHwnd);
                            }
                            return;
                        }

                        StatusText.Text = $"Waiting for Shotcut... ({attempts})";
                        
                        if (TryGetShotcutWindow())
                        {
                            // Wait a bit more for window to be fully ready
                            await Task.Delay(500);
                            _embedTimer.Stop();
                            EmbedWindow();
                        }
                    };
                    _embedTimer.Start();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed: {ex.Message}";
                PlaceholderPanel.Visibility = Visibility.Visible;
                WinFormsHost.Visibility = Visibility.Collapsed;
            }
        }

        private bool TryGetShotcutWindow()
        {
            if (_shotcutProcess == null || _shotcutProcess.HasExited)
                return false;

            _shotcutProcess.Refresh();
            
            // First try MainWindowHandle
            _shotcutHwnd = _shotcutProcess.MainWindowHandle;
            
            if (_shotcutHwnd != IntPtr.Zero && IsWindow(_shotcutHwnd))
            {
                // Check if it's the main Shotcut window (not splash screen)
                var title = GetWindowTitle(_shotcutHwnd);
                if (title.Contains("Shotcut") && !title.Contains("Loading"))
                {
                    return true;
                }
            }
            
            // Enumerate all windows for this process to find main window
            var processId = (uint)_shotcutProcess.Id;
            IntPtr foundHwnd = IntPtr.Zero;
            
            EnumWindows((hWnd, lParam) =>
            {
                GetWindowThreadProcessId(hWnd, out uint windowPid);
                if (windowPid == processId && IsWindowVisible(hWnd))
                {
                    var title = GetWindowTitle(hWnd);
                    // Look for main Shotcut window (has "Shotcut" in title, not splash)
                    if (title.Contains("Shotcut") && !title.Contains("Loading") && !title.Contains("Splash"))
                    {
                        // Check if it's a main window (has caption)
                        int style = GetWindowLong(hWnd, GWL_STYLE);
                        if ((style & WS_CAPTION) != 0)
                        {
                            foundHwnd = hWnd;
                            return false; // Stop enumeration
                        }
                    }
                }
                return true; // Continue enumeration
            }, IntPtr.Zero);
            
            if (foundHwnd != IntPtr.Zero)
            {
                _shotcutHwnd = foundHwnd;
                return true;
            }
            
            return false;
        }

        private string GetWindowTitle(IntPtr hWnd)
        {
            int length = GetWindowTextLength(hWnd);
            if (length == 0) return string.Empty;
            
            var sb = new System.Text.StringBuilder(length + 1);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }

        private void EmbedWindow()
        {
            if (_shotcutHwnd == IntPtr.Zero || _hostPanel == null)
                return;

            try
            {
                StatusText.Text = "Embedding Shotcut window...";

                // Step 1: Hide the window first
                ShowWindow(_shotcutHwnd, SW_HIDE);

                // Step 2: Remove extended styles that prevent embedding
                int exStyle = GetWindowLong(_shotcutHwnd, GWL_EXSTYLE);
                exStyle &= ~WS_EX_APPWINDOW;  // Remove from taskbar
                exStyle |= WS_EX_TOOLWINDOW;  // Tool window (no taskbar entry)
                exStyle &= ~WS_EX_WINDOWEDGE;
                exStyle &= ~WS_EX_CLIENTEDGE;
                exStyle &= ~WS_EX_DLGMODALFRAME;
                SetWindowLong(_shotcutHwnd, GWL_EXSTYLE, exStyle);

                // Step 3: Remove window chrome and make it a child window
                int style = GetWindowLong(_shotcutHwnd, GWL_STYLE);
                style &= ~WS_POPUP;
                style &= ~WS_CAPTION;
                style &= ~WS_THICKFRAME;
                style &= ~WS_BORDER;
                style &= ~WS_SYSMENU;
                style &= ~WS_MINIMIZEBOX;
                style &= ~WS_MAXIMIZEBOX;
                style |= WS_CHILD;
                style |= WS_VISIBLE;
                style |= WS_CLIPCHILDREN;
                style |= WS_CLIPSIBLINGS;
                SetWindowLong(_shotcutHwnd, GWL_STYLE, style);

                // Step 4: Set parent to our host panel
                var result = SetParent(_shotcutHwnd, _hostPanel.Handle);
                if (result == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    StatusText.Text = $"SetParent failed with error: {error}";
                    // Still try to show it
                    ShowWindow(_shotcutHwnd, SW_SHOW);
                    return;
                }

                // Step 5: Resize to fill container
                var width = _hostPanel.Width;
                var height = _hostPanel.Height;
                if (width > 0 && height > 0)
                {
                    MoveWindow(_shotcutHwnd, 0, 0, width, height, true);
                }

                // Step 6: Force frame change and show
                SetWindowPos(_shotcutHwnd, IntPtr.Zero, 0, 0, width, height, 
                    SWP_NOZORDER | SWP_FRAMECHANGED);
                
                ShowWindow(_shotcutHwnd, SW_SHOW);

                // Step 7: Force redraw
                RedrawWindow(_shotcutHwnd, IntPtr.Zero, IntPtr.Zero, 
                    RDW_INVALIDATE | RDW_UPDATENOW | RDW_ALLCHILDREN);
                UpdateWindow(_shotcutHwnd);

                // Verify embedding
                var newParent = GetParent(_shotcutHwnd);
                if (newParent == _hostPanel.Handle)
                {
                    _isEmbedded = true;
                    _isDetached = false;
                    UpdateButtonStates();
                    StatusText.Text = "Shotcut embedded - ready to edit";
                }
                else
                {
                    StatusText.Text = $"Embedding partial - parent mismatch";
                    _isEmbedded = true;
                    _isDetached = false;
                    UpdateButtonStates();
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Embed failed: {ex.Message}";
                // Show window if embedding fails
                ShowWindow(_shotcutHwnd, SW_SHOW);
            }
        }

        private void ResizeEmbeddedWindow()
        {
            if (!_isEmbedded || _isDetached || _shotcutHwnd == IntPtr.Zero || _hostPanel == null)
                return;

            if (!IsWindow(_shotcutHwnd))
            {
                _isEmbedded = false;
                UpdateButtonStates();
                PlaceholderPanel.Visibility = Visibility.Visible;
                WinFormsHost.Visibility = Visibility.Collapsed;
                StatusText.Text = "Shotcut closed";
                return;
            }

            var width = _hostPanel.Width;
            var height = _hostPanel.Height;

            if (width > 0 && height > 0)
            {
                MoveWindow(_shotcutHwnd, 0, 0, width, height, true);
            }
        }

        private void DetachWindow()
        {
            if (!_isEmbedded || _shotcutHwnd == IntPtr.Zero)
                return;

            try
            {
                // Hide first
                ShowWindow(_shotcutHwnd, SW_HIDE);

                // Remove parent (make top-level)
                SetParent(_shotcutHwnd, IntPtr.Zero);

                // Restore extended style
                int exStyle = GetWindowLong(_shotcutHwnd, GWL_EXSTYLE);
                exStyle |= WS_EX_APPWINDOW;
                exStyle &= ~WS_EX_TOOLWINDOW;
                SetWindowLong(_shotcutHwnd, GWL_EXSTYLE, exStyle);

                // Restore window style
                int style = GetWindowLong(_shotcutHwnd, GWL_STYLE);
                style &= ~WS_CHILD;
                style |= WS_POPUP;
                style |= WS_CAPTION;
                style |= WS_THICKFRAME;
                style |= WS_SYSMENU;
                style |= WS_MINIMIZEBOX;
                style |= WS_MAXIMIZEBOX;
                SetWindowLong(_shotcutHwnd, GWL_STYLE, style);

                // Force frame change
                SetWindowPos(_shotcutHwnd, IntPtr.Zero, 100, 100, 1280, 720, 
                    SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

                // Show and restore
                ShowWindow(_shotcutHwnd, SW_RESTORE);
                SetForegroundWindow(_shotcutHwnd);

                _isDetached = true;
                UpdateButtonStates();
                StatusText.Text = "Shotcut detached - running in separate window";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Detach failed: {ex.Message}";
            }
        }

        private void ReattachWindow()
        {
            if (!_isDetached || _shotcutHwnd == IntPtr.Zero || _hostPanel == null)
                return;

            if (!IsWindow(_shotcutHwnd))
            {
                _isEmbedded = false;
                _isDetached = false;
                UpdateButtonStates();
                return;
            }

            // Re-embed using the full embed logic
            EmbedWindow();
        }

        private void CloseShotcut()
        {
            if (_shotcutProcess != null && !_shotcutProcess.HasExited)
            {
                try
                {
                    _shotcutProcess.CloseMainWindow();
                    if (!_shotcutProcess.WaitForExit(2000))
                    {
                        _shotcutProcess.Kill();
                    }
                }
                catch { }
            }

            _shotcutProcess = null;
            _shotcutHwnd = IntPtr.Zero;
            _isEmbedded = false;
            _isDetached = false;

            PlaceholderPanel.Visibility = Visibility.Visible;
            WinFormsHost.Visibility = Visibility.Collapsed;
            UpdateButtonStates();
            StatusText.Text = "Ready";
        }

        private void UpdateButtonStates()
        {
            bool running = _shotcutProcess != null && !_shotcutProcess.HasExited;
            
            LaunchButton.IsEnabled = !running;
            RestartButton.IsEnabled = running;
            CloseButton.IsEnabled = running;
            DetachButton.IsEnabled = running && _isEmbedded && !_isDetached;
            ReattachButton.Visibility = running && _isDetached ? Visibility.Visible : Visibility.Collapsed;
            DetachButton.Visibility = running && !_isDetached ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
