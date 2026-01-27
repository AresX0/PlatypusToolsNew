using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views
{
    /// <summary>
    /// Interaction logic for ScreenRecorderView.xaml
    /// </summary>
    public partial class ScreenRecorderView : UserControl
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_START_ID = 9001;
        private const int HOTKEY_STOP_ID = 9002;
        
        // Modifier keys
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_NOREPEAT = 0x4000;
        
        // Virtual key codes
        private const uint VK_R = 0x52;
        private const uint VK_S = 0x53;
        
        private HwndSource? _hwndSource;
        private bool _hotkeysRegistered;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public ScreenRecorderView()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RegisterHotkeys();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            UnregisterHotkeys();
        }

        private void RegisterHotkeys()
        {
            if (_hotkeysRegistered) return;

            var window = Window.GetWindow(this);
            if (window == null) return;

            var helper = new WindowInteropHelper(window);
            _hwndSource = HwndSource.FromHwnd(helper.Handle);
            _hwndSource?.AddHook(HwndHook);

            var handle = helper.Handle;

            // Register Ctrl+Shift+R for Start Recording
            if (RegisterHotKey(handle, HOTKEY_START_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_R))
            {
                LogMessage("Hotkey registered: Ctrl+Shift+R (Start Recording)");
            }
            else
            {
                LogMessage("Failed to register hotkey Ctrl+Shift+R - may be in use by another application");
            }

            // Register Ctrl+Shift+S for Stop Recording
            if (RegisterHotKey(handle, HOTKEY_STOP_ID, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, VK_S))
            {
                LogMessage("Hotkey registered: Ctrl+Shift+S (Stop Recording)");
            }
            else
            {
                LogMessage("Failed to register hotkey Ctrl+Shift+S - may be in use by another application");
            }

            _hotkeysRegistered = true;
        }

        private void UnregisterHotkeys()
        {
            if (!_hotkeysRegistered) return;

            var window = Window.GetWindow(this);
            if (window == null) return;

            var helper = new WindowInteropHelper(window);
            var handle = helper.Handle;

            UnregisterHotKey(handle, HOTKEY_START_ID);
            UnregisterHotKey(handle, HOTKEY_STOP_ID);

            _hwndSource?.RemoveHook(HwndHook);
            _hotkeysRegistered = false;
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int hotkeyId = wParam.ToInt32();
                
                if (hotkeyId == HOTKEY_START_ID)
                {
                    LogMessage("Hotkey Ctrl+Shift+R pressed - starting recording");
                    if (DataContext is ScreenRecorderViewModel vm && vm.StartRecordingCommand.CanExecute(null))
                    {
                        vm.StartRecordingCommand.Execute(null);
                    }
                    handled = true;
                }
                else if (hotkeyId == HOTKEY_STOP_ID)
                {
                    LogMessage("Hotkey Ctrl+Shift+S pressed - stopping recording");
                    if (DataContext is ScreenRecorderViewModel vm)
                    {
                        vm.StopRecordingCommand.Execute(null);
                    }
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        private void LogMessage(string message)
        {
            if (DataContext is ScreenRecorderViewModel vm)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    vm.LogMessages.Add($"[{DateTime.Now:HH:mm:ss}] [HOTKEY] {message}");
                });
            }
        }

        private void StopRecording_Click(object sender, RoutedEventArgs e)
        {
            // Fallback click handler in case command doesn't fire
            if (DataContext is ScreenRecorderViewModel vm)
            {
                vm.StopRecordingCommand.Execute(null);
            }
        }

        private void CancelRecording_Click(object sender, RoutedEventArgs e)
        {
            // Fallback click handler in case command doesn't fire
            if (DataContext is ScreenRecorderViewModel vm)
            {
                vm.CancelRecordingCommand.Execute(null);
            }
        }
    }
}
