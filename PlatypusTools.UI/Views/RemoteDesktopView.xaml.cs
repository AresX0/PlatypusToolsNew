using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PlatypusTools.UI.ViewModels;

namespace PlatypusTools.UI.Views;

/// <summary>
/// Code-behind for RemoteDesktopView — handles mouse/keyboard events on the desktop image
/// and forwards them to the ViewModel for WebSocket transmission.
/// </summary>
public partial class RemoteDesktopView : UserControl
{
    private RemoteDesktopViewModel? ViewModel => DataContext as RemoteDesktopViewModel;

    public RemoteDesktopView()
    {
        InitializeComponent();
    }

    // ── Mouse events on the remote desktop image ──

    private void DesktopImage_MouseMove(object sender, MouseEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || !vm.IsConnected || vm.IsViewOnly) return;

        var (nx, ny) = GetNormalizedPosition(e);
        vm.SendMouseMove(nx, ny);
    }

    private void DesktopImage_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || !vm.IsConnected || vm.IsViewOnly) return;

        // Capture mouse to keep receiving events even if cursor leaves the image
        DesktopImage.CaptureMouse();

        var (nx, ny) = GetNormalizedPosition(e);
        var button = GetButtonName(e.ChangedButton);
        vm.SendMouseButton(nx, ny, button, isDown: true);

        // Give keyboard focus to the control for key forwarding
        this.Focus();
    }

    private void DesktopImage_MouseUp(object sender, MouseButtonEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || !vm.IsConnected || vm.IsViewOnly) return;

        DesktopImage.ReleaseMouseCapture();

        var (nx, ny) = GetNormalizedPosition(e);
        var button = GetButtonName(e.ChangedButton);
        vm.SendMouseButton(nx, ny, button, isDown: false);
    }

    private void DesktopImage_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || !vm.IsConnected || vm.IsViewOnly) return;

        var (nx, ny) = GetNormalizedPosition(e);
        vm.SendMouseScroll(nx, ny, e.Delta);
    }

    // ── Keyboard events ──

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || !vm.IsConnected || vm.IsViewOnly) return;

        var vk = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (vk != 0)
        {
            vm.SendKeyEvent(vk, isDown: true);
            e.Handled = true; // Prevent local processing
        }
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || !vm.IsConnected || vm.IsViewOnly) return;

        var vk = KeyInterop.VirtualKeyFromKey(e.Key == Key.System ? e.SystemKey : e.Key);
        if (vk != 0)
        {
            vm.SendKeyEvent(vk, isDown: false);
            e.Handled = true;
        }
    }

    // ── Monitor combo selection ──

    private void MonitorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var vm = ViewModel;
        if (vm == null || MonitorCombo.SelectedItem is not ComboBoxItem item) return;

        if (item.Tag is string tagStr && int.TryParse(tagStr, out var idx))
        {
            vm.SelectedMonitorIndex = idx;
        }
        else if (item.Tag is int tagInt)
        {
            vm.SelectedMonitorIndex = tagInt;
        }
    }

    // ── Helpers ──

    /// <summary>
    /// Gets normalized (0–1) coordinates relative to the image's actual rendered size.
    /// </summary>
    private (double nx, double ny) GetNormalizedPosition(MouseEventArgs e)
    {
        var pos = e.GetPosition(DesktopImage);
        var imgWidth = DesktopImage.ActualWidth;
        var imgHeight = DesktopImage.ActualHeight;

        if (imgWidth <= 0 || imgHeight <= 0)
            return (0, 0);

        var nx = Math.Clamp(pos.X / imgWidth, 0.0, 1.0);
        var ny = Math.Clamp(pos.Y / imgHeight, 0.0, 1.0);
        return (nx, ny);
    }

    private static string GetButtonName(MouseButton button) => button switch
    {
        MouseButton.Left => "left",
        MouseButton.Right => "right",
        MouseButton.Middle => "middle",
        _ => "left"
    };
}
