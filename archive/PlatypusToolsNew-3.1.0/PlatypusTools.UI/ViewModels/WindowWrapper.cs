using System;
using System.Windows.Forms;

namespace PlatypusTools.UI.ViewModels
{
    /// <summary>
    /// Wrapper for Win32 window handles to use with WinForms dialogs.
    /// Allows WinForms dialogs to be properly parented to WPF windows.
    /// </summary>
    internal class WindowWrapper : IWin32Window
    {
        public IntPtr Handle { get; }

        public WindowWrapper(IntPtr handle)
        {
            Handle = handle;
        }
    }
}
